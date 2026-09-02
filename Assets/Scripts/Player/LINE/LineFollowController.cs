using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
public class LineFollowController : MonoBehaviour {
  public static LineFollowController ActivePlayer { get; private set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void ResetActivePlayer() => ActivePlayer = null;

  [Header("Line")]
  [Tooltip("The line and strand the player currently follows.")]
  public LinePath currentLine;
  public int currentStrand;

  [Header("Feet")]
  [Tooltip("Place this child transform at the soles of the player's feet.")]
  public Transform feetAnchor;

  [Header("Movement")]
  [Min(0f)] public float moveSpeed = 2f;
  [Min(0f)] public float acceleration = 10f;
  [Min(0f)] public float deceleration = 25f;

  [FormerlySerializedAs("movementEnabled")]
  [SerializeField, Tooltip("Controls manual player movement without disabling scripted path positioning.")]
  private bool manualMovementEnabled = true;

  public bool movementEnabled {
    get => manualMovementEnabled;
    set {
      if (manualMovementEnabled == value) return;
      manualMovementEnabled = value;
      if (!manualMovementEnabled) StopManualMovement();
    }
  }

  [Header("Corner Assist")]
  [Tooltip("How far ahead the controller looks for an outer corner before committing to it. Inner corners never trigger assist.")]
  [Min(0f)] public float cornerEntryDistance = 0.25f;

  [Tooltip("How far onto the new segment the player is carried before normal deceleration resumes.")]
  [Min(0f)] public float cornerExitDistance = 0.2f;

  [Tooltip("Minimum movement speed maintained while carrying the player through a corner.")]
  [Min(0f)] public float cornerAssistSpeed = 1f;

  [Tooltip("Small direction changes below this angle are not treated as corners.")]
  [Range(0f, 180f)] public float minimumCornerAngle = 45f;

  [Header("Path Connections")]
  [Tooltip("Maximum world-space separation between authored endpoints that should connect.")]
  [Min(0.001f)] public float endpointConnectionTolerance = 0.03f;

  [Header("Facing")]
  [Min(0f)] public float facingRotationSpeed = 200f;
  [Tooltip("Camera used to translate left/right input and sprite facing into screen space. Assign the player's gameplay camera.")]
  public Camera movementCamera;
  public PlayerFlipController flipController;
  public SpriteRenderer spriteRenderer;

  [Header("Animation")]
  public Animator animator;

  [Header("Debug")]
  public bool drawDebugGizmos = true;

  private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
  private static readonly int VelocityHash = Animator.StringToHash("Velocity");

  private CharacterController _characterController;
  private float _distanceAlongLine;
  private float _input;
  private float _speed;
  private float _actualSignedSpeed;
  private float _facingSideSign = 1f;
  private bool _hasFacingSide;
  private bool _lastFlipState;
  private bool _cornerAssistActive;
  private bool _cornerWasPassed;
  private float _cornerDirectionSign;
  private float _cornerExitStartDistance;
  private Vector3 _cornerEntryTangent;
  private bool _connectionAssistActive;
  private float _connectionDirectionSign;
  private float _connectionExitStartDistance;
  private bool _hasConnectionFacingNormal;
  private Vector3 _connectionFacingNormal;
  private bool _facingTurnActive;

  public float DistanceAlongLine => _distanceAlongLine;
  public Vector3 FeetPosition => feetAnchor != null ? feetAnchor.position : transform.position;
  public bool IsTurning => _cornerAssistActive
                           || _connectionAssistActive
                           || _facingTurnActive
                           || (flipController != null && flipController.IsFlipping);

  private void Awake() {
    _characterController = GetComponent<CharacterController>();
    if (animator == null) animator = GetComponent<Animator>();
    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    if (flipController == null) flipController = GetComponent<PlayerFlipController>();
    if (movementCamera == null) movementCamera = GetComponentInChildren<Camera>(true);
  }

  private void OnEnable() {
    ActivePlayer = this;
  }

  private void Start() {
    if (currentLine == null) return;

    float distance = currentLine.FindClosestDistance(
      FeetPosition,
      out _,
      out _,
      out int strand);

    SetLine(currentLine, strand, distance);
  }

  private void OnDisable() {
    if (ActivePlayer == this) ActivePlayer = null;
    _input = 0f;
    _speed = 0f;
    _actualSignedSpeed = 0f;
    CancelCornerAssist();
    CancelConnectionAssist();
    _hasConnectionFacingNormal = false;
    _facingTurnActive = false;
    UpdateAnimator();
  }

#pragma warning disable IDE0051
  private void OnMove(InputValue value) {
    _input = manualMovementEnabled ? value.Get<float>() : 0f;
  }
#pragma warning restore IDE0051

  private void Update() {
    if (!manualMovementEnabled) return;

    if (currentLine == null || currentLine.StrandCount == 0) {
      StopMoving();
      return;
    }

    UpdateCornerAssist();
    UpdateSpeed();
    FollowLine();
    UpdateCornerAssistProgress();
    UpdateFacing();
    UpdateConnectionAssistProgress();
    UpdateSpriteFlip();
    UpdateAnimator();
  }

  private void UpdateSpeed() {
    float targetSpeed = GetPathRelativeInput() * moveSpeed;

    if (_cornerAssistActive && Mathf.Abs(targetSpeed) < cornerAssistSpeed) {
      targetSpeed = _cornerDirectionSign * cornerAssistSpeed;
    }

    if (_connectionAssistActive) {
      float committedSpeed = Mathf.Max(Mathf.Abs(targetSpeed), cornerAssistSpeed);
      targetSpeed = _connectionDirectionSign * committedSpeed;
    }

    float rate = Mathf.Abs(targetSpeed) > 0.001f ? acceleration : deceleration;
    _speed = Mathf.MoveTowards(_speed, targetSpeed, rate * Time.deltaTime);
  }

  private void UpdateCornerAssist() {
    if (_connectionAssistActive) return;
    float pathRelativeInput = GetPathRelativeInput();
    if (_cornerAssistActive) {
      if (pathRelativeInput * _cornerDirectionSign < -0.001f) CancelCornerAssist();
      else if (!_cornerWasPassed && !HasOuterCornerAhead(_cornerDirectionSign)) CancelCornerAssist();
      return;
    }

    if (Mathf.Abs(pathRelativeInput) < 0.001f || cornerEntryDistance <= 0f) return;

    float directionSign = Mathf.Sign(pathRelativeInput);
    if (!TryGetCornerAhead(directionSign, out Vector3 currentTangent, out _)) return;

    _cornerAssistActive = true;
    _cornerWasPassed = false;
    _cornerDirectionSign = directionSign;
    _cornerEntryTangent = currentTangent;
  }

  private bool HasOuterCornerAhead(float directionSign) =>
    TryGetCornerAhead(directionSign, out _, out _);

  private bool TryGetCornerAhead(
    float directionSign,
    out Vector3 currentTangent,
    out Vector3 futureTangent) {
    currentTangent = Vector3.zero;
    futureTangent = Vector3.zero;
    if (currentLine == null || currentLine.StrandCount == 0 || cornerEntryDistance <= 0f)
      return false;

    float lookAheadDistance = _distanceAlongLine + directionSign * cornerEntryDistance;

    if (!currentLine.IsStrandClosedLoop(currentStrand)) {
      float length = currentLine.GetStrandLength(currentStrand);
      bool reachesStart = lookAheadDistance < 0f;
      bool reachesEnd = lookAheadDistance > length;
      if (reachesStart || reachesEnd) {
        float endpointDistance = reachesEnd ? length : 0f;
        Vector3 endpoint = currentLine.GetPointAtDistance(currentStrand, endpointDistance);
        if (!LinePath.TryFindConnectedEndpoint(
              currentLine,
              currentStrand,
              endpoint,
              GetPreferredConnectionDirection(),
              endpointConnectionTolerance,
              out LinePath.EndpointTransition transition)) return false;

        futureTangent = transition.Path.GetDirectionAtDistance(
          transition.Strand,
          transition.EndpointDistance) * transition.InwardDirection;
        futureTangent.y = 0f;
      } else {
        futureTangent = GetHorizontalTangent(lookAheadDistance) * directionSign;
      }
    } else {
      futureTangent = GetHorizontalTangent(lookAheadDistance) * directionSign;
    }

    currentTangent = GetHorizontalTangent(_distanceAlongLine) * directionSign;
    if (currentTangent == Vector3.zero || futureTangent == Vector3.zero) return false;
    if (Vector3.Angle(currentTangent, futureTangent) < minimumCornerAngle) return false;
    return IsOuterCorner(futureTangent);
  }

  private void UpdateCornerAssistProgress() {
    if (!_cornerAssistActive) return;

    Vector3 currentTangent = GetHorizontalTangent(_distanceAlongLine) * _cornerDirectionSign;
    if (!_cornerWasPassed) {
      if (currentTangent == Vector3.zero ||
          Vector3.Angle(_cornerEntryTangent, currentTangent) < minimumCornerAngle) {
        return;
      }

      _cornerWasPassed = true;
      _cornerExitStartDistance = _distanceAlongLine;
    }

    if (GetDirectedDistance(_cornerExitStartDistance, _distanceAlongLine, _cornerDirectionSign) >= cornerExitDistance) {
      CancelCornerAssist();
    }
  }

  private Vector3 GetHorizontalTangent(float distance) {
    Vector3 tangent = currentLine.GetDirectionAtDistance(currentStrand, distance);
    tangent.y = 0f;
    return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.zero;
  }

  private float GetDirectedDistance(float from, float to, float directionSign) {
    float directedDistance = (to - from) * directionSign;
    if (currentLine.IsStrandClosedLoop(currentStrand) && directedDistance < 0f) {
      directedDistance += currentLine.GetStrandLength(currentStrand);
    }
    return Mathf.Max(0f, directedDistance);
  }

  private void CancelCornerAssist() {
    _cornerAssistActive = false;
    _cornerWasPassed = false;
  }

  private void BeginConnectionAssist(float inwardDirection, float endpointDistance) {
    _connectionAssistActive = true;
    _connectionDirectionSign = inwardDirection;
    _connectionExitStartDistance = endpointDistance;
  }

  private void UpdateConnectionAssistProgress() {
    if (!_connectionAssistActive) return;
    float travelled = GetDirectedDistance(
      _connectionExitStartDistance,
      _distanceAlongLine,
      _connectionDirectionSign);
    if (travelled >= cornerExitDistance && !_facingTurnActive &&
        (flipController == null || !flipController.IsFlipping)) {
      CancelConnectionAssist();
    }
  }

  private void CancelConnectionAssist() => _connectionAssistActive = false;

  private void FollowLine() {
    Vector3 previousFeetPosition = FeetPosition;
    float previousDistance = _distanceAlongLine;
    float wantedDistance = previousDistance + _speed * Time.deltaTime;
    bool transferredPath = false;
    float transferredDirection = 0f;

    if (!currentLine.IsStrandClosedLoop(currentStrand)) {
      float length = currentLine.GetStrandLength(currentStrand);
      bool crossedStart = wantedDistance < 0f;
      bool crossedEnd = wantedDistance > length;

      if (crossedStart || crossedEnd) {
        Vector3 incomingTravelDirection = GetHorizontalTangent(previousDistance) * Mathf.Sign(_speed);
        float overflow = crossedEnd ? wantedDistance - length : -wantedDistance;
        Vector3 endpointPoint = currentLine.GetPointAtDistance(
          currentStrand,
          crossedEnd ? length : 0f);

        if (LinePath.TryFindConnectedEndpoint(
              currentLine,
              currentStrand,
              endpointPoint,
              GetPreferredConnectionDirection(),
              endpointConnectionTolerance,
              out LinePath.EndpointTransition transition)) {
          currentLine = transition.Path;
          currentStrand = transition.Strand;
          transferredDirection = transition.InwardDirection;
          Vector3 outgoingTravelDirection = GetHorizontalTangent(transition.EndpointDistance) * transferredDirection;
          bool isOuterCorner = IsOuterCorner(outgoingTravelDirection);
          SetConnectionFacingNormal(incomingTravelDirection, outgoingTravelDirection);
          wantedDistance = transition.EndpointDistance + overflow * transferredDirection;
          float targetLength = currentLine.GetStrandLength(currentStrand);
          wantedDistance = Mathf.Clamp(wantedDistance, 0f, targetLength);
          _speed = Mathf.Abs(_speed) * transferredDirection;
          _hasFacingSide = false;
          _facingTurnActive = true;
          CancelCornerAssist();
          if (isOuterCorner) BeginConnectionAssist(transferredDirection, transition.EndpointDistance);
          else CancelConnectionAssist();
          transferredPath = true;
        } else {
          CancelConnectionAssist();
          wantedDistance = Mathf.Clamp(wantedDistance, 0f, length);
          if (Mathf.Approximately(wantedDistance, previousDistance) && Mathf.Abs(_speed) > 0f)
            _speed = 0f;
        }
      }
    }

    Vector3 targetFeetPosition = currentLine.GetPointAtDistance(currentStrand, wantedDistance);
    _characterController.Move(targetFeetPosition - FeetPosition);

    _distanceAlongLine = currentLine.FindClosestDistanceOnStrand(
      currentStrand,
      FeetPosition,
      out _,
      out _);

    float signedDistance;
    if (transferredPath) {
      signedDistance = Vector3.Distance(previousFeetPosition, FeetPosition) * transferredDirection;
    } else {
      signedDistance = _distanceAlongLine - previousDistance;
    }

    if (!transferredPath && currentLine.IsStrandClosedLoop(currentStrand)) {
      float length = currentLine.GetStrandLength(currentStrand);
      if (length > 0f && Mathf.Abs(signedDistance) > length * 0.5f) {
        signedDistance -= Mathf.Sign(signedDistance) * length;
      }
    }

    _actualSignedSpeed = Time.deltaTime > 0f ? signedDistance / Time.deltaTime : 0f;
  }

  private Vector3 GetPreferredConnectionDirection() {
    if (movementCamera != null && Mathf.Abs(_input) > 0.001f) {
      Vector3 cameraRight = movementCamera.transform.right;
      cameraRight.y = 0f;
      if (cameraRight.sqrMagnitude > 0.0001f) return cameraRight.normalized * Mathf.Sign(_input);
    }

    return GetHorizontalTangent(_distanceAlongLine) * Mathf.Sign(_speed);
  }

  private bool IsOuterCorner(Vector3 outgoingTravelDirection) {
    outgoingTravelDirection.y = 0f;
    if (outgoingTravelDirection.sqrMagnitude <= 0.0001f) return false;

    Vector3 directionTowardSupportingWall = transform.forward;
    directionTowardSupportingWall.y = 0f;
    if (directionTowardSupportingWall.sqrMagnitude <= 0.0001f) return false;

    // The player root faces toward the wall supporting the current path. At a convex corner the
    // outgoing segment travels toward that wall's plane; at a concave corner it travels away from
    // it. This test is independent of which direction the junction is traversed.
    return Vector3.Dot(
      outgoingTravelDirection.normalized,
      directionTowardSupportingWall.normalized) > 0.001f;
  }

  private void SetConnectionFacingNormal(Vector3 incomingTravelDirection, Vector3 outgoingTravelDirection) {
    incomingTravelDirection.y = 0f;
    outgoingTravelDirection.y = 0f;
    if (incomingTravelDirection.sqrMagnitude < 0.0001f || outgoingTravelDirection.sqrMagnitude < 0.0001f) {
      _hasConnectionFacingNormal = false;
      return;
    }

    // At a right-angle connection both normals of the destination path are equally
    // close to the current view. Carrying the supporting-wall normal through the
    // same signed turn as travel removes that ambiguity and keeps the camera outside.
    Quaternion travelTurn = Quaternion.FromToRotation(
      incomingTravelDirection.normalized,
      outgoingTravelDirection.normalized);
    _connectionFacingNormal = travelTurn * transform.forward;
    _connectionFacingNormal.y = 0f;
    _hasConnectionFacingNormal = _connectionFacingNormal.sqrMagnitude > 0.0001f;
    if (_hasConnectionFacingNormal) _connectionFacingNormal.Normalize();
  }

  private void UpdateFacing() {
    if (flipController != null && flipController.IsFlipping) {
      _facingTurnActive = true;
      return;
    }

    Vector3 tangent = currentLine.GetDirectionAtDistance(currentStrand, _distanceAlongLine);
    tangent.y = 0f;
    if (tangent.sqrMagnitude < 0.0001f) return;

    Vector3 rawNormal = Vector3.Cross(Vector3.up, tangent.normalized);
    bool flipState = flipController != null && flipController.IsFlipped;

    if (!_hasFacingSide) {
      Vector3 referenceNormal = _hasConnectionFacingNormal
        ? _connectionFacingNormal
        : transform.forward;
      _facingSideSign = Vector3.Dot(rawNormal, referenceNormal) >= 0f ? 1f : -1f;
      _hasConnectionFacingNormal = false;
      _lastFlipState = flipState;
      _hasFacingSide = true;
    } else if (flipState != _lastFlipState) {
      _facingSideSign = -_facingSideSign;
      _lastFlipState = flipState;
    }

    Vector3 normal = rawNormal * _facingSideSign;

    Quaternion targetRotation = Quaternion.LookRotation(normal, Vector3.up);
    _facingTurnActive = Quaternion.Angle(transform.rotation, targetRotation) > 0.5f;
    transform.rotation = Quaternion.RotateTowards(
      transform.rotation,
      targetRotation,
      facingRotationSpeed * Time.deltaTime);
    _facingTurnActive = Quaternion.Angle(transform.rotation, targetRotation) > 0.5f;
  }

  private void UpdateSpriteFlip() {
    if (spriteRenderer == null || Mathf.Abs(_actualSignedSpeed) < 0.001f) return;

    Vector3 movementDirection = GetHorizontalTangent(_distanceAlongLine) * Mathf.Sign(_actualSignedSpeed);
    if (movementDirection == Vector3.zero) {
      spriteRenderer.flipX = _actualSignedSpeed < 0f;
      return;
    }

    Vector3 spriteRight = spriteRenderer.transform.right;
    spriteRight.y = 0f;
    spriteRenderer.flipX = spriteRight.sqrMagnitude > 0.0001f &&
      Vector3.Dot(movementDirection, spriteRight.normalized) < 0f;
  }

  private float GetPathRelativeInput() {
    if (Mathf.Abs(_input) < 0.001f || movementCamera == null) return _input;

    Vector3 tangent = GetHorizontalTangent(_distanceAlongLine);
    Vector3 cameraRight = movementCamera.transform.right;
    cameraRight.y = 0f;
    if (tangent == Vector3.zero || cameraRight.sqrMagnitude < 0.0001f) return _input;

    // Positive input always means screen-right, independently of path point ordering or
    // which side of the wall the camera currently occupies.
    float increasingPathScreenDirection = Vector3.Dot(tangent, cameraRight.normalized) >= 0f ? 1f : -1f;
    return _input * increasingPathScreenDirection;
  }

  private void UpdateAnimator() {
    if (animator == null) return;

    float speed = Mathf.Abs(_actualSignedSpeed);
    animator.SetBool(IsRunningHash, speed > 0.01f);
    animator.SetFloat(VelocityHash, speed);
  }

  private void StopMoving() {
    _speed = 0f;
    _actualSignedSpeed = 0f;
    UpdateAnimator();
  }

  private void StopManualMovement() {
    _input = 0f;
    CancelCornerAssist();
    CancelConnectionAssist();
    _hasConnectionFacingNormal = false;
    _facingTurnActive = false;
    StopMoving();
  }

  public void SetLine(LinePath line, int strand, float distanceAlongLine) {
    currentLine = line;
    currentStrand = strand;
    _distanceAlongLine = distanceAlongLine;
    _speed = 0f;
    _actualSignedSpeed = 0f;
    _hasFacingSide = false;
    _hasConnectionFacingNormal = false;
    _facingTurnActive = false;
    CancelCornerAssist();
    CancelConnectionAssist();
  }

  /// <summary>
  /// Starts a scripted leg on a connected path while preserving the side of the supporting
  /// wall through the corner. The regular facing update then turns the player and its camera.
  /// </summary>
  public void SetScriptedConnectedLine(
    LinePath line,
    int strand,
    float distanceAlongLine,
    Vector3 incomingTravelDirection,
    Vector3 outgoingTravelDirection) {
    SetLine(line, strand, distanceAlongLine);
    SetConnectionFacingNormal(incomingTravelDirection, outgoingTravelDirection);
    _facingTurnActive = true;
    UpdateFacing();
  }

  public Vector3 GetRootPositionForFeetAt(Vector3 feetPosition) {
    return transform.position + feetPosition - FeetPosition;
  }

  /// <summary>Moves the player to an authored distance while normal input movement is disabled.</summary>
  public void SetScriptedPathPosition(int strand, float distanceAlongLine, float signedSpeed) {
    if (currentLine == null || currentLine.StrandCount == 0) return;

    currentStrand = Mathf.Clamp(strand, 0, currentLine.StrandCount - 1);
    float length = currentLine.GetStrandLength(currentStrand);
    if (currentLine.IsStrandClosedLoop(currentStrand) && length > 0f)
      distanceAlongLine = Mathf.Repeat(distanceAlongLine, length);
    else
      distanceAlongLine = Mathf.Clamp(distanceAlongLine, 0f, length);

    Vector3 targetFeetPosition = currentLine.GetPointAtDistance(currentStrand, distanceAlongLine);
    transform.position = GetRootPositionForFeetAt(targetFeetPosition);
    _distanceAlongLine = distanceAlongLine;
    _speed = signedSpeed;
    _actualSignedSpeed = signedSpeed;
    UpdateFacing();
    UpdateSpriteFlip();
    UpdateAnimator();
  }

  /// <summary>Ends an externally driven path movement without changing its final path distance.</summary>
  public void FinishScriptedPathMovement() {
    _speed = 0f;
    _actualSignedSpeed = 0f;
    UpdateAnimator();
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    if (!drawDebugGizmos) return;

    Gizmos.color = Color.green;
    Gizmos.DrawWireSphere(
      feetAnchor != null ? feetAnchor.position : transform.position,
      0.06f);
  }
#endif
}
