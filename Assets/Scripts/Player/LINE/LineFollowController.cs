using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class LineFollowController : MonoBehaviour {
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

  [Header("Corner Assist")]
  [Tooltip("How far ahead the controller looks for a corner before committing to it.")]
  [Min(0f)] public float cornerEntryDistance = 0.25f;

  [Tooltip("How far onto the new segment the player is carried before normal deceleration resumes.")]
  [Min(0f)] public float cornerExitDistance = 0.2f;

  [Tooltip("Minimum movement speed maintained while carrying the player through a corner.")]
  [Min(0f)] public float cornerAssistSpeed = 1f;

  [Tooltip("Small direction changes below this angle are not treated as corners.")]
  [Range(0f, 180f)] public float minimumCornerAngle = 45f;

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
  private bool _facingTurnActive;

  public float DistanceAlongLine => _distanceAlongLine;
  public Vector3 FeetPosition => feetAnchor != null ? feetAnchor.position : transform.position;
  public bool IsTurning => _cornerAssistActive
                           || _facingTurnActive
                           || (flipController != null && flipController.IsFlipping);

  private void Awake() {
    _characterController = GetComponent<CharacterController>();
    if (animator == null) animator = GetComponent<Animator>();
    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    if (flipController == null) flipController = GetComponent<PlayerFlipController>();
    if (movementCamera == null) movementCamera = GetComponentInChildren<Camera>(true);
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
    _input = 0f;
    _speed = 0f;
    _actualSignedSpeed = 0f;
    CancelCornerAssist();
    _facingTurnActive = false;
    UpdateAnimator();
  }

#pragma warning disable IDE0051
  private void OnMove(InputValue value) {
    _input = value.Get<float>();
  }
#pragma warning restore IDE0051

  private void Update() {
    if (currentLine == null || currentLine.StrandCount == 0) {
      StopMoving();
      return;
    }

    UpdateCornerAssist();
    UpdateSpeed();
    FollowLine();
    UpdateCornerAssistProgress();
    UpdateFacing();
    UpdateSpriteFlip();
    UpdateAnimator();
  }

  private void UpdateSpeed() {
    float targetSpeed = GetPathRelativeInput() * moveSpeed;

    if (_cornerAssistActive && Mathf.Abs(targetSpeed) < cornerAssistSpeed) {
      targetSpeed = _cornerDirectionSign * cornerAssistSpeed;
    }

    float rate = Mathf.Abs(targetSpeed) > 0.001f ? acceleration : deceleration;
    _speed = Mathf.MoveTowards(_speed, targetSpeed, rate * Time.deltaTime);
  }

  private void UpdateCornerAssist() {
    float pathRelativeInput = GetPathRelativeInput();
    if (_cornerAssistActive) {
      if (pathRelativeInput * _cornerDirectionSign < -0.001f) CancelCornerAssist();
      return;
    }

    if (Mathf.Abs(pathRelativeInput) < 0.001f || cornerEntryDistance <= 0f) return;

    float directionSign = Mathf.Sign(pathRelativeInput);
    float lookAheadDistance = _distanceAlongLine + directionSign * cornerEntryDistance;

    if (!currentLine.IsStrandClosedLoop(currentStrand)) {
      lookAheadDistance = Mathf.Clamp(
        lookAheadDistance,
        0f,
        currentLine.GetStrandLength(currentStrand));
    }

    Vector3 currentTangent = GetHorizontalTangent(_distanceAlongLine);
    Vector3 futureTangent = GetHorizontalTangent(lookAheadDistance);
    if (currentTangent == Vector3.zero || futureTangent == Vector3.zero) return;
    if (Vector3.Angle(currentTangent, futureTangent) < minimumCornerAngle) return;

    _cornerAssistActive = true;
    _cornerWasPassed = false;
    _cornerDirectionSign = directionSign;
    _cornerEntryTangent = currentTangent;
  }

  private void UpdateCornerAssistProgress() {
    if (!_cornerAssistActive) return;

    Vector3 currentTangent = GetHorizontalTangent(_distanceAlongLine);
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

  private void FollowLine() {
    float previousDistance = _distanceAlongLine;
    float wantedDistance = previousDistance + _speed * Time.deltaTime;

    if (!currentLine.IsStrandClosedLoop(currentStrand)) {
      float length = currentLine.GetStrandLength(currentStrand);
      wantedDistance = Mathf.Clamp(wantedDistance, 0f, length);
      if (Mathf.Approximately(wantedDistance, previousDistance) && Mathf.Abs(_speed) > 0f) {
        _speed = 0f;
      }
    }

    Vector3 targetFeetPosition = currentLine.GetPointAtDistance(currentStrand, wantedDistance);
    _characterController.Move(targetFeetPosition - FeetPosition);

    _distanceAlongLine = currentLine.FindClosestDistanceOnStrand(
      currentStrand,
      FeetPosition,
      out _,
      out _);

    float signedDistance = _distanceAlongLine - previousDistance;
    if (currentLine.IsStrandClosedLoop(currentStrand)) {
      float length = currentLine.GetStrandLength(currentStrand);
      if (length > 0f && Mathf.Abs(signedDistance) > length * 0.5f) {
        signedDistance -= Mathf.Sign(signedDistance) * length;
      }
    }

    _actualSignedSpeed = Time.deltaTime > 0f ? signedDistance / Time.deltaTime : 0f;
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
      _facingSideSign = Vector3.Dot(rawNormal, transform.forward) >= 0f ? 1f : -1f;
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

  public void SetLine(LinePath line, int strand, float distanceAlongLine) {
    currentLine = line;
    currentStrand = strand;
    _distanceAlongLine = distanceAlongLine;
    _speed = 0f;
    _actualSignedSpeed = 0f;
    _hasFacingSide = false;
    _facingTurnActive = false;
    CancelCornerAssist();
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
