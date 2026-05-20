using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
/// <summary>
/// Detects 90-degree wall corners around the player and orchestrates the camera rotation,
/// player snap, and post-turn wall re-hugging needed to transition cleanly onto the next wall.
/// </summary>
public class RightAngleWallTurner : MonoBehaviour {
  [Header("References")]
  [Tooltip("Pivot that rotates the camera around the player (defaults to Camera.main parent).")]
  public Transform camPivot;

  [Tooltip("Movement controller to pause during the corner transition.")]
  public PlayerMovementController movementController;

  [Header("Wall")]
  [Tooltip("Layer(s) that are considered turnable walls.")]
  public LayerMask wallLayer;

  [Header("Passageways")]
  [Tooltip("Layer used by back rays to treat closed passageways like walls.")]
  public LayerMask passagewayLayer;

  [Header("Corner Rays")]
  [Tooltip("Length of the two lateral rays used to detect the next wall.")]
  public float lateralRayLength = 0.25f;

  [Tooltip("Length of the back ray used to classify outer corners.")]
  public float backRayLength = 0.5f;

  [Header("Camera Turn")]
  [Tooltip("Duration of the 90-degree camera turn.")]
  public float cameraTurnDuration = 0.25f;

  [Header("Player Snap")]
  [Tooltip("How far to advance on the new wall while turning the corner.")]
  public float nextWallAdvance = 0.25f;

  [Tooltip("Distance from the wall while hugging it (player center to wall surface).")]
  public float wallHugDistance = 0.25f;

  [Header("Post-Turn Correction")]
  [Tooltip("How many frames to try re-hugging when both back rays are false after a corner turn.")]
  public int postTurnCorrectionFrames = 4;

  [Tooltip("Backtracking step used by post-turn correction while searching for both back-ray hits.")]
  public float postTurnBacktrackStep = 0.05f;

  [Tooltip("Maximum backtracking distance used by post-turn correction.")]
  public float postTurnBacktrackMaxDistance = 1.5f;

  [Tooltip("Maximum allowed error between current wall distance and configured hug distance during post-turn correction.")]
  public float postTurnHugDistanceTolerance = 0.02f;

  [Tooltip("Maximum wall-normal search range used by strict post-turn wall distance restoration.")]
  public float postTurnWallSearchDistance = 2.5f;

  [Header("Debug")]
  public bool drawRayGizmos = true;
  public bool logRayHits = true;

  private const float MIN_MOVE_MAGNITUDE = 0.03f;
  private const float RETRIGGER_COOLDOWN = 0.1f;
  private const float RAY_START_OFFSET = 0.03f;
  private const float SPHERECAST_RADIUS = 0.2f;

  private CharacterController _cc;
  private float _moveInput;
  private bool _movementInputLocked;
  private bool _isTurning;
  private float _lastTurnTime = -999f;
  private bool _awaitingPostTurnReady;
  private bool _awaitingPostSwitchInputDecision;

  private Vector3 _cachedWallNormal;
  private bool _hasCachedWall;

  private bool _hasDebugProbe;
  private Vector3 _debugProbeOrigin;
  private Vector3 _debugMoveDir;
  private Vector3 _debugLateralDir;
  private Vector3 _debugLeftTip;
  private Vector3 _debugRightTip;
  private bool _debugLeftHit;
  private bool _debugRightHit;
  private bool _debugLeftBackHit;
  private bool _debugRightBackHit;
  private RaycastHit _debugLeftHitInfo;
  private RaycastHit _debugRightHitInfo;
  private RaycastHit _debugLeftBackHitInfo;
  private RaycastHit _debugRightBackHitInfo;
  private bool _hasLoggedRayHitState;
  private bool _lastLoggedLeftHit;
  private bool _lastLoggedRightHit;
  private bool _lastLoggedLeftBackHit;
  private bool _lastLoggedRightBackHit;

  /// <summary>
  /// Returns whether a corner turn is currently being animated.
  /// </summary>
  public bool IsTurning => _isTurning;

  /// <summary>
  /// Clears the transient turn state after an external wall-switch completes and stores the
  /// new wall normal so the next detection pass can continue from the new wall face.
  /// </summary>
  /// <param name="switchedWallNormal">The normal of the wall the player has just switched onto.</param>
  public void NotifyWallSwitchCompleted(Vector3 switchedWallNormal) {
    _isTurning = false;
    _movementInputLocked = false;
    _moveInput = 0f;

    _awaitingPostTurnReady = false;
    _awaitingPostSwitchInputDecision = true;

    Vector3 flatNormal = Flatten(switchedWallNormal);
    _hasCachedWall = flatNormal.sqrMagnitude > 0.0001f;
    if (_hasCachedWall) {
      _cachedWallNormal = flatNormal;
    }

    _lastTurnTime = Time.time;
  }

  private void Awake() {
    _cc = GetController();

    if (movementController == null) {
      movementController = GetComponent<PlayerMovementController>();
    }

    if (camPivot == null && Camera.main != null) {
      camPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
    }
  }

  private CharacterController GetController() {
    if (_cc == null) {
      _cc = GetComponent<CharacterController>();
    }

    return _cc;
  }

#pragma warning disable IDE0051
  private void OnMove(InputValue value) {
    var input = value.Get<float>();

    if (_movementInputLocked) {
      _moveInput = 0f;
      return;
    }

    _moveInput = input;
  }
#pragma warning restore IDE0051

  /// <summary>
  /// Samples the current wall context each frame and triggers a corner turn when the current
  /// ray pattern indicates an inner or outer right-angle transition.
  /// </summary>
  private void LateUpdate() {
    if (_isTurning || Time.time < _lastTurnTime + RETRIGGER_COOLDOWN || camPivot == null) {
      return;
    }

    Vector3 cameraForward = GetCameraPlanarForward();
    if (cameraForward.sqrMagnitude < 0.0001f) {
      return;
    }

    Vector3 rightDir = GetCameraPlanarRight();
    if (rightDir.sqrMagnitude < 0.0001f) {
      return;
    }

    GetLogicalTurnProbeDirections(rightDir, out Vector3 logicalLeftDir, out Vector3 logicalRightDir);

    Vector3 probeOrigin = transform.position;
    var lateralLen = Mathf.Max(0.05f, lateralRayLength);
    var backLen = Mathf.Max(0.05f, backRayLength);

    _hasDebugProbe = true;
    _debugProbeOrigin = probeOrigin;
    _debugMoveDir = cameraForward;
    _debugLateralDir = logicalRightDir;
    _debugLeftTip = probeOrigin + logicalLeftDir * lateralLen;
    _debugRightTip = probeOrigin + logicalRightDir * lateralLen;

    _debugLeftHit = Physics.Raycast(probeOrigin, logicalLeftDir, out _debugLeftHitInfo, lateralLen, wallLayer, QueryTriggerInteraction.Ignore);
    _debugRightHit = Physics.Raycast(probeOrigin, logicalRightDir, out _debugRightHitInfo, lateralLen, wallLayer, QueryTriggerInteraction.Ignore);
    _debugLeftBackHit = RaycastBackRay(_debugLeftTip, cameraForward, out _debugLeftBackHitInfo, backLen);
    _debugRightBackHit = RaycastBackRay(_debugRightTip, cameraForward, out _debugRightBackHitInfo, backLen);

    if (drawRayGizmos) {
      DrawRuntimeRays(probeOrigin, logicalLeftDir, logicalRightDir, cameraForward, lateralLen, backLen);
    }

    if (logRayHits) {
      LogRayHits();
    }

    if (_awaitingPostSwitchInputDecision) {
      if (_debugLeftBackHit && _debugRightBackHit) {
        ClearPostSwitchDecision();
        return;
      }

      Vector3 postSwitchMoveDir = GetHorizontalMoveDirection();
      if (postSwitchMoveDir.sqrMagnitude < MIN_MOVE_MAGNITUDE * MIN_MOVE_MAGNITUDE) {
        return;
      }

      if (TryTriggerCornerTurnFromMovement(postSwitchMoveDir, logicalLeftDir, logicalRightDir, cameraForward, true, out var hadCornerCandidate)) {
        return;
      }

      if (hadCornerCandidate) {
        return;
      }

      return;
    }

    if (_awaitingPostTurnReady) {
      if (_debugLeftBackHit && _debugRightBackHit) {
        _awaitingPostTurnReady = false;
      }
      return;
    }

    Vector3 alongWall = GetHorizontalMoveDirection();
    if (alongWall.sqrMagnitude < MIN_MOVE_MAGNITUDE * MIN_MOVE_MAGNITUDE) {
      return;
    }

    if (TryGetCurrentWall(rightDir, out _, out Vector3 currentNormal)) {
      _cachedWallNormal = currentNormal;
      _hasCachedWall = true;
    } else {
      _hasCachedWall = false;
    }

    if (TryTriggerCornerTurnFromMovement(alongWall, logicalLeftDir, logicalRightDir, cameraForward, false, out _)) {
      return;
    }
  }

  /// <summary>
  /// Evaluates the current probe state against the player's travel direction and starts the
  /// corner-turn coroutine when the detected corner is one the player is actually moving into.
  /// </summary>
  private bool TryTriggerCornerTurnFromMovement(Vector3 moveDir, Vector3 logicalLeftDir, Vector3 logicalRightDir, Vector3 cameraForward, bool requireTowardCornerCheck, out bool hadCornerCandidate) {
    hadCornerCandidate = false;

    if (!TryGetCornerTurnTarget(logicalLeftDir, logicalRightDir, cameraForward, moveDir, out Vector3 turnNormal, out Vector3 turnContact, out var turnLeft, out var turnCornerKind)) {
      return false;
    }

    hadCornerCandidate = true;

    if (requireTowardCornerCheck && !IsMoveTowardCorner(moveDir, turnContact)) {
      if (logRayHits) {
        Debug.Log("[RightAngleWallTurner] Post-switch decision: away from corner -> no turn.");
      }
      return false;
    }

    if (requireTowardCornerCheck && logRayHits) {
      Debug.Log($"[RightAngleWallTurner] Post-switch decision: toward corner -> trigger turn ({turnCornerKind}).");
    }

    ClearPostSwitchDecision();
    LogTurnTriggered(turnCornerKind, turnLeft ? "left" : "right", turnNormal, turnContact);
    StartCoroutine(DoCornerTurn(turnNormal, turnContact, moveDir, turnLeft));
    return true;
  }

  /// <summary>
  /// Converts camera-right space into the logical left/right probe directions used by the turn
  /// detector, which intentionally swaps world-left and world-right to match the wall-following
  /// convention used by the controller.
  /// </summary>
  private static void GetLogicalTurnProbeDirections(Vector3 rightDir, out Vector3 logicalLeftDir, out Vector3 logicalRightDir) {
    logicalLeftDir = rightDir;
    logicalRightDir = -rightDir;
  }

  private void ClearPostSwitchDecision() {
    _awaitingPostSwitchInputDecision = false;
  }

  private bool IsMoveTowardCorner(Vector3 moveDir, Vector3 cornerContact) {
    Vector3 toCorner = cornerContact - transform.position;
    toCorner.y = 0f;
    if (toCorner.sqrMagnitude < 0.0001f) {
      return false;
    }

    Vector3 flatMove = moveDir;
    flatMove.y = 0f;
    if (flatMove.sqrMagnitude < 0.0001f) {
      return false;
    }

    return Vector3.Dot(flatMove.normalized, toCorner.normalized) > 0f;
  }

  /// <summary>
  /// Resolves the next wall target for the active probe state, preferring inner-corner hits and
  /// falling back to outer-corner inference when the side rays do not directly hit a wall.
  /// </summary>
  private bool TryGetCornerTurnTarget(Vector3 logicalLeftDir, Vector3 logicalRightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out string cornerKind) {
    if (TryGetInnerTurnTarget(out nextNormal, out nextContact, out isLeftTurn)) {
      cornerKind = "inner";
      return true;
    }

    if (TryGetOuterTurnTarget(logicalLeftDir, logicalRightDir, cameraForward, moveDir, out nextNormal, out nextContact, out isLeftTurn)) {
      cornerKind = "outer";
      return true;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    isLeftTurn = false;
    cornerKind = string.Empty;
    return false;
  }

  private Vector3 GetHorizontalMoveDirection() {
    CharacterController cc = GetController();
    Vector3 velocity = cc != null ? cc.velocity : Vector3.zero;
    velocity.y = 0f;

    if (velocity.sqrMagnitude > MIN_MOVE_MAGNITUDE * MIN_MOVE_MAGNITUDE) {
      return velocity.normalized;
    }

    if (Mathf.Abs(_moveInput) < 0.001f) {
      return Vector3.zero;
    }

    Vector3 camRight = GetCameraPlanarRight();

    if (camRight.sqrMagnitude < 0.0001f) {
      return Vector3.zero;
    }

    return camRight.normalized * Mathf.Sign(_moveInput);
  }

  private bool TryGetCurrentWall(Vector3 lateralDir, out RaycastHit hit, out Vector3 wallNormal) {
    if (_hasCachedWall && TryFindWallAlongNormal(transform.position, _cachedWallNormal, out RaycastHit cachedHit)) {
      hit = cachedHit;
      wallNormal = Flatten(cachedHit.normal);
      return true;
    }

    var hasA = TryRayTowardNormal(transform.position, lateralDir, Mathf.Max(0.05f, lateralRayLength), out RaycastHit hitA);
    var hasB = TryRayTowardNormal(transform.position, -lateralDir, Mathf.Max(0.05f, lateralRayLength), out RaycastHit hitB);

    if (!hasA && !hasB) {
      if (TryFindWallBySphereCast(out RaycastHit sphereHit, out Vector3 sphereNormal)) {
        hit = sphereHit;
        wallNormal = sphereNormal;
        return true;
      }

      hit = default;
      wallNormal = Vector3.zero;
      return false;
    }

    if (hasA && !hasB) {
      hit = hitA;
      wallNormal = Flatten(hitA.normal);
      return true;
    }

    if (!hasA && hasB) {
      hit = hitB;
      wallNormal = Flatten(hitB.normal);
      return true;
    }

    var scoreA = ScoreCurrentWallCandidate(lateralDir, hitA);
    var scoreB = ScoreCurrentWallCandidate(-lateralDir, hitB);
    hit = scoreA >= scoreB ? hitA : hitB;
    wallNormal = Flatten(hit.normal);
    return true;
  }

  private bool TryFindWallBySphereCast(out RaycastHit hit, out Vector3 normal) {
    Vector3 origin = transform.position;
    Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    var bestScore = float.NegativeInfinity;
    var found = false;
    RaycastHit bestHit = default;

    foreach (Vector3 dir in dirs) {
      if (!Physics.SphereCast(origin, SPHERECAST_RADIUS, dir, out RaycastHit candidate, Mathf.Max(0.4f, lateralRayLength), wallLayer, QueryTriggerInteraction.Ignore)) {
        continue;
      }

      Vector3 candidateNormal = Flatten(candidate.normal);
      var score = -candidate.distance;
      if (_hasCachedWall) {
        score += Vector3.Dot(candidateNormal, _cachedWallNormal);
      }

      if (!found || score > bestScore) {
        found = true;
        bestScore = score;
        bestHit = candidate;
      }
    }

    if (!found) {
      hit = default;
      normal = Vector3.zero;
      return false;
    }

    hit = bestHit;
    normal = Flatten(bestHit.normal);
    return true;
  }

  private float ScoreCurrentWallCandidate(Vector3 expectedNormal, RaycastHit hit) {
    Vector3 candidateNormal = Flatten(hit.normal);
    var score = -hit.distance;
    score += Vector3.Dot(candidateNormal, expectedNormal) * 2f;

    if (_hasCachedWall) {
      score += Vector3.Dot(candidateNormal, _cachedWallNormal);
    }

    return score;
  }

  private void DrawRuntimeRays(Vector3 probeOrigin, Vector3 leftDir, Vector3 rightDir, Vector3 backDir, float lateralLen, float backLen) {
    Vector3 leftTip = probeOrigin + leftDir * lateralLen;
    Vector3 rightTip = probeOrigin + rightDir * lateralLen;

    Debug.DrawRay(probeOrigin, leftDir * lateralLen, _debugLeftHit ? Color.green : Color.blue);
    Debug.DrawRay(probeOrigin, rightDir * lateralLen, _debugRightHit ? Color.green : Color.magenta);
    Debug.DrawRay(leftTip, backDir * backLen, _debugLeftBackHit ? Color.red : Color.yellow);
    Debug.DrawRay(rightTip, backDir * backLen, _debugRightBackHit ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.6f, 0f));
  }

  private void LogRayHits() {
    var changed = !_hasLoggedRayHitState
      || _debugLeftHit != _lastLoggedLeftHit
      || _debugRightHit != _lastLoggedRightHit
      || _debugLeftBackHit != _lastLoggedLeftBackHit
      || _debugRightBackHit != _lastLoggedRightBackHit;

    if (!changed) {
      return;
    }

    _hasLoggedRayHitState = true;
    _lastLoggedLeftHit = _debugLeftHit;
    _lastLoggedRightHit = _debugRightHit;
    _lastLoggedLeftBackHit = _debugLeftBackHit;
    _lastLoggedRightBackHit = _debugRightBackHit;

    Debug.Log($"[RightAngleWallTurner] hits L:{_debugLeftHit} R:{_debugRightHit} LB:{_debugLeftBackHit} RB:{_debugRightBackHit}");
  }

  private bool TryGetInnerTurnTarget(out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn) {
    if (TryGetInnerCornerFromSideHit(_debugLeftHit, _debugLeftHitInfo, false, out nextNormal, out nextContact, out isLeftTurn)) {
      return true;
    }

    if (TryGetInnerCornerFromSideHit(_debugRightHit, _debugRightHitInfo, true, out nextNormal, out nextContact, out isLeftTurn)) {
      return true;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    isLeftTurn = false;
    return false;
  }

  /// <summary>
  /// Interprets a single lateral wall hit as an inner-corner target and translates the hit into
  /// the wall normal, contact point, and turn direction used by the corner-turn coroutine.
  /// </summary>
  private bool TryGetInnerCornerFromSideHit(bool hasHit, RaycastHit hitInfo, bool turnLeftOnHit, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn) {
    if (!hasHit) {
      nextNormal = Vector3.zero;
      nextContact = Vector3.zero;
      isLeftTurn = false;
      return false;
    }

    nextNormal = Flatten(hitInfo.normal);
    nextContact = hitInfo.point;
    isLeftTurn = turnLeftOnHit;
    return nextNormal.sqrMagnitude > 0.0001f;
  }

  /// <summary>
  /// Interprets the side and back ray pattern for exposed outside corners, including overshoot
  /// frames where the player has already moved past the one-hot back-ray state.
  /// </summary>
  private bool TryGetOuterTurnTarget(Vector3 leftDir, Vector3 rightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn) {
    var noSideHits = !_debugLeftHit && !_debugRightHit;
    var outwardLeft = noSideHits && !_debugLeftBackHit && _debugRightBackHit;
    var outwardRight = noSideHits && _debugLeftBackHit && !_debugRightBackHit;
    var overshotOuter = noSideHits && !_debugLeftBackHit && !_debugRightBackHit;

    if (!outwardLeft && !outwardRight && !overshotOuter) {
      nextNormal = Vector3.zero;
      nextContact = Vector3.zero;
      isLeftTurn = false;
      return false;
    }

    if (overshotOuter && !outwardLeft && !outwardRight) {
      return TryResolveOvershotOuterTurn(leftDir, rightDir, cameraForward, moveDir, out nextNormal, out nextContact, out isLeftTurn);
    }

    isLeftTurn = outwardLeft;

    Vector3 sideDir = outwardLeft ? leftDir : rightDir;
    Vector3 sideTip = outwardLeft ? _debugLeftTip : _debugRightTip;
    return TryResolveOuterTargetForSide(sideDir, sideTip, cameraForward, out nextNormal, out nextContact);
  }

  /// <summary>
  /// Resolves outer-corner frames where the player has already advanced beyond the one-hot back-ray
  /// pattern and the detector must infer the skipped corner side from the current movement vector.
  /// </summary>
  private bool TryResolveOvershotOuterTurn(Vector3 leftDir, Vector3 rightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn) {
    var preferLeft = Vector3.Dot(moveDir, leftDir) >= Vector3.Dot(moveDir, rightDir);

    if (TryResolveOuterTargetForSide(preferLeft ? leftDir : rightDir, preferLeft ? _debugLeftTip : _debugRightTip, cameraForward, out nextNormal, out nextContact)) {
      isLeftTurn = preferLeft;
      return true;
    }

    if (TryResolveOuterTargetForSide(preferLeft ? rightDir : leftDir, preferLeft ? _debugRightTip : _debugLeftTip, cameraForward, out nextNormal, out nextContact)) {
      isLeftTurn = !preferLeft;
      return true;
    }

    Vector3 inferredSideDir = preferLeft ? leftDir : rightDir;
    Vector3 inferredSideTip = preferLeft ? _debugLeftTip : _debugRightTip;
    Vector3 moveDirFlat = Flatten(moveDir);
    if (moveDirFlat.sqrMagnitude < 0.0001f) {
      moveDirFlat = GetHorizontalMoveDirection();
    }
    if (moveDirFlat.sqrMagnitude < 0.0001f) {
      moveDirFlat = preferLeft ? leftDir : rightDir;
    }

    nextNormal = inferredSideDir;
    nextContact = inferredSideTip + moveDirFlat.normalized * Mathf.Max(0.05f, backRayLength);
    isLeftTurn = preferLeft;
    return true;
  }

  private bool TryResolveOuterTargetForSide(Vector3 sideDir, Vector3 sideTip, Vector3 cameraForward, out Vector3 nextNormal, out Vector3 nextContact) {
    var backLen = Mathf.Max(0.05f, backRayLength);
    var lateralLen = Mathf.Max(0.05f, lateralRayLength);

    Vector3 searchOrigin = sideTip + cameraForward * backLen;
    if (Physics.Raycast(searchOrigin, -sideDir, out RaycastHit sideSearchHit, lateralLen * 2f, wallLayer, QueryTriggerInteraction.Ignore)) {
      nextNormal = Flatten(sideSearchHit.normal);
      nextContact = sideSearchHit.point;
      return nextNormal.sqrMagnitude > 0.0001f;
    }

    if (TryFindWallAlongNormal(searchOrigin, sideDir, out RaycastHit fallbackHit)) {
      nextNormal = Flatten(fallbackHit.normal);
      nextContact = fallbackHit.point;
      return nextNormal.sqrMagnitude > 0.0001f;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    return false;
  }

  private void LogTurnTriggered(string cornerKind, string direction, Vector3 normal, Vector3 contact) {
    if (logRayHits) {
      Debug.Log(
        $"[RightAngleWallTurner] Turn triggered kind={cornerKind} direction={direction} " +
        $"normal={normal.ToString("F3")} contact={contact.ToString("F3")} " +
        $"hits(L={_debugLeftHit},R={_debugRightHit},LB={_debugLeftBackHit},RB={_debugRightBackHit})"
      );
    }
  }

  /// <summary>
  /// Animates the camera yaw and player relocation needed to carry the player from the current
  /// wall onto the next wall while keeping the character hugged to the new surface.
  /// </summary>
  private IEnumerator DoCornerTurn(Vector3 nextNormal, Vector3 nextContactPoint, Vector3 alongWallBeforeTurn, bool isLeftTurn) {
    ClearPostSwitchDecision();

    _isTurning = true;
    _movementInputLocked = true;
    _moveInput = 0f;
    _lastTurnTime = Time.time;

    if (movementController != null) {
      movementController.enabled = false;
    }

    var alongNextWall = Vector3.ProjectOnPlane(alongWallBeforeTurn, nextNormal);
    if (alongNextWall.sqrMagnitude < 0.0001f) {
      Vector3 fallback = isLeftTurn ? -GetCameraPlanarForward() : GetCameraPlanarForward();
      alongNextWall = Vector3.ProjectOnPlane(fallback, nextNormal);
    }
    if (alongNextWall.sqrMagnitude < 0.0001f) {
      alongNextWall = Vector3.Cross(Vector3.up, nextNormal);
    }
    alongNextWall.Normalize();

    Vector3 reference = Quaternion.AngleAxis(isLeftTurn ? -90f : 90f, Vector3.up) * alongWallBeforeTurn;
    reference.y = 0f;
    if (reference.sqrMagnitude < 0.0001f) {
      reference = isLeftTurn ? -GetCameraPlanarForward() : GetCameraPlanarForward();
    }
    reference = Vector3.ProjectOnPlane(reference, nextNormal);
    if (reference.sqrMagnitude > 0.0001f && Vector3.Dot(alongNextWall, reference.normalized) < 0f) {
      alongNextWall = -alongNextWall;
    }

    var startYaw = camPivot.eulerAngles.y;
    var targetYaw = isLeftTurn ? startYaw - 90f : startYaw + 90f;

    CharacterController cc = GetController();
    var radius = cc != null ? cc.radius : 0.4f;
    var alongAdvance = Mathf.Max(nextWallAdvance, radius * 1.2f);
    Vector3 cornerExitAnchor = nextContactPoint + alongNextWall * alongAdvance;

    Vector3 startPos = transform.position;
    var elapsed = 0f;
    var safeDuration = Mathf.Max(0.05f, cameraTurnDuration);

    while (elapsed < safeDuration) {
      elapsed += Time.deltaTime;
      var t = Mathf.Clamp01(elapsed / safeDuration);
      var eased = Mathf.SmoothStep(0f, 1f, t);

      var yaw = Mathf.LerpAngle(startYaw, targetYaw, eased);
      camPivot.eulerAngles = new Vector3(0f, yaw, 0f);

      var travelAnchor = Vector3.Lerp(startPos, cornerExitAnchor, eased);
      Vector3 cornerPos = ComputeHuggedPosition(travelAnchor, nextNormal);
      SetPlayerPosition(cornerPos);
      camPivot.position = transform.position;

      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, targetYaw, 0f);
    SetPlayerPosition(ComputeHuggedPosition(cornerExitAnchor, nextNormal));
    camPivot.position = transform.position;

    yield return StartCoroutine(CorrectPostTurnHug(nextNormal));

    _cachedWallNormal = nextNormal;
    _hasCachedWall = true;

    if (movementController != null) {
      movementController.ReorientHorizontalVelocity(isLeftTurn ? -1 : 1);
    }

    if (movementController != null) {
      movementController.enabled = true;
    }

    _awaitingPostTurnReady = true;
    _movementInputLocked = false;
    _isTurning = false;
  }

  /// <summary>
  /// Runs a short corrective pass after a turn to restore the configured wall-hug distance when
  /// the animated turn path leaves the player slightly detached from the new wall.
  /// </summary>
  private IEnumerator CorrectPostTurnHug(Vector3 wallNormal) {
    var attempts = Mathf.Max(0, postTurnCorrectionFrames);
    for (var i = 0; i < attempts; i++) {
      if (IsHugDistanceRestored(transform.position, wallNormal, out var hugError)) {
        var bothBackHits = AreBackRaysBothHittingAtCurrentPose(out var leftDistance, out var rightDistance);
        if (logRayHits) {
          Debug.Log($"[RightAngleWallTurner] Post-turn correction settled. hugError={hugError:F4} backHitsBoth={bothBackHits} backDistances(L={leftDistance:F3},R={rightDistance:F3})");
        }
        yield break;
      }

      if (!RestoreHugDistance(wallNormal)) {
        break;
      }

      yield return null;
    }

    if (logRayHits) {
      var bothBackHits = AreBackRaysBothHittingAtCurrentPose(out var leftDistance, out var rightDistance);
      var hasHug = TryGetHugDistanceError(transform.position, wallNormal, out var remainingError);
      Debug.Log($"[RightAngleWallTurner] Post-turn correction ended without full restore. hasHug={hasHug} remainingError={remainingError:F4} backHitsBoth={bothBackHits} backDistances(L={leftDistance:F3},R={rightDistance:F3})");
    }
  }

  /// <summary>
  /// Steps the player back toward the target wall and reapplies strict wall-distance correction
  /// until the configured hug tolerance is restored or the search budget is exhausted.
  /// </summary>
  private bool RestoreHugDistance(Vector3 wallNormal) {
    if (wallNormal.sqrMagnitude < 0.0001f) {
      return false;
    }

    var step = Mathf.Max(0.01f, postTurnBacktrackStep);
    var maxDistance = Mathf.Max(step, postTurnBacktrackMaxDistance);
    var maxSteps = Mathf.Max(1, Mathf.CeilToInt(maxDistance / step));

    for (var i = 0; i < maxSteps; i++) {
      if (IsHugDistanceRestored(transform.position, wallNormal, out _)) {
        return true;
      }

      Vector3 towardWall = transform.position - wallNormal * step;
      Vector3 candidate = towardWall;

      if (TryComputeHuggedPositionStrict(candidate, wallNormal, out Vector3 corrected)) {
        SetPlayerPosition(corrected);
      } else {
        SetPlayerPosition(candidate);
      }

      if (camPivot != null) {
        camPivot.position = transform.position;
      }
    }

    return IsHugDistanceRestored(transform.position, wallNormal, out _);
  }

  private bool IsHugDistanceRestored(Vector3 position, Vector3 wallNormal, out float error) {
    if (!TryGetHugDistanceError(position, wallNormal, out error)) {
      return false;
    }

    return error <= Mathf.Max(0.0001f, postTurnHugDistanceTolerance);
  }

  private bool TryGetHugDistanceError(Vector3 position, Vector3 wallNormal, out float error) {
    error = 0f;

    if (!TryComputeHuggedPositionStrict(position, wallNormal, out Vector3 targetHuggedPosition)) {
      return false;
    }

    error = Mathf.Abs(Vector3.Dot(position - targetHuggedPosition, wallNormal));
    return true;
  }

  private bool AreBackRaysBothHittingAtCurrentPose(out float leftDistance, out float rightDistance) {
    leftDistance = 0f;
    rightDistance = 0f;

    Vector3 cameraForward = GetCameraPlanarForward();
    Vector3 rightDir = GetCameraPlanarRight();
    if (cameraForward.sqrMagnitude < 0.0001f || rightDir.sqrMagnitude < 0.0001f) {
      return false;
    }

    Vector3 logicalLeftDir = rightDir;
    Vector3 logicalRightDir = -rightDir;

    Vector3 probeOrigin = transform.position;
    var lateralLen = Mathf.Max(0.05f, lateralRayLength);
    var backLen = Mathf.Max(0.05f, backRayLength);

    Vector3 leftTip = probeOrigin + logicalLeftDir * lateralLen;
    Vector3 rightTip = probeOrigin + logicalRightDir * lateralLen;

    var leftBackHit = RaycastBackRay(leftTip, cameraForward, out RaycastHit leftHit, backLen);
    var rightBackHit = RaycastBackRay(rightTip, cameraForward, out RaycastHit rightHit, backLen);

    if (leftBackHit) {
      leftDistance = leftHit.distance;
    }

    if (rightBackHit) {
      rightDistance = rightHit.distance;
    }

    return leftBackHit && rightBackHit;
  }

  private bool RaycastBackRay(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance) {
    var backRayMask = wallLayer.value | passagewayLayer.value;
    return Physics.Raycast(origin, direction, out hit, distance, backRayMask, QueryTriggerInteraction.Ignore);
  }

  /// <summary>
  /// Computes a wall-aligned position by explicitly reacquiring the wall along the supplied normal
  /// instead of assuming the current position is already at the desired stand-off distance.
  /// </summary>
  private bool TryComputeHuggedPositionStrict(Vector3 nearPosition, Vector3 wallNormal, out Vector3 huggedPosition) {
    huggedPosition = Vector3.zero;

    var searchDistance = Mathf.Max(Mathf.Max(0.05f, lateralRayLength), postTurnWallSearchDistance);
    if (!TryFindWallAlongNormal(nearPosition, wallNormal, out RaycastHit hit, searchDistance)) {
      return false;
    }

    var standoff = Mathf.Max(0.01f, wallHugDistance);
    huggedPosition = hit.point + wallNormal * standoff;
    huggedPosition.y = transform.position.y;
    return true;
  }

  private Vector3 ComputeHuggedPosition(Vector3 nearPosition, Vector3 wallNormal) {
    var standoff = Mathf.Max(0.01f, wallHugDistance);

    if (TryFindWallAlongNormal(nearPosition, wallNormal, out RaycastHit hit)) {
      Vector3 hugged = hit.point + wallNormal * standoff;
      hugged.y = transform.position.y;
      return hugged;
    }

    Vector3 fallback = nearPosition + wallNormal * standoff;
    fallback.y = transform.position.y;
    return fallback;
  }

  private bool TryFindWallAlongNormal(Vector3 nearPosition, Vector3 wallNormal, out RaycastHit hit, float rayLengthOverride = -1f) {
    CharacterController cc = GetController();
    var radius = cc != null ? cc.radius : 0.4f;
    var rayLength = rayLengthOverride > 0f ? Mathf.Max(0.05f, rayLengthOverride) : Mathf.Max(0.05f, lateralRayLength);
    Vector3 origin = nearPosition + wallNormal * (rayLength + radius + RAY_START_OFFSET);
    return Physics.Raycast(origin, -wallNormal, out hit, rayLength * 2f, wallLayer, QueryTriggerInteraction.Ignore);
  }

  private bool TryRayTowardNormal(Vector3 nearPosition, Vector3 expectedWallNormal, float length, out RaycastHit hit) {
    Vector3 flatExpected = Flatten(expectedWallNormal);
    if (flatExpected.sqrMagnitude < 0.0001f) {
      hit = default;
      return false;
    }

    Vector3 origin = nearPosition + flatExpected * RAY_START_OFFSET;
    return Physics.Raycast(origin, -flatExpected, out hit, Mathf.Max(0.05f, length), wallLayer, QueryTriggerInteraction.Ignore);
  }

  private void SetPlayerPosition(Vector3 worldPos) {
    CharacterController cc = GetController();
    if (cc == null) {
      transform.position = worldPos;
      return;
    }

    var wasEnabled = cc.enabled;
    cc.enabled = false;
    transform.position = worldPos;
    cc.enabled = wasEnabled;
  }

  private static Vector3 Flatten(Vector3 v) {
    v.y = 0f;
    return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
  }

  private Vector3 GetCameraPlanarForward() {
    Vector3 forward;

    if (camPivot != null) {
      forward = camPivot.forward;
    } else if (Camera.main != null) {
      forward = Camera.main.transform.forward;
    } else {
      forward = transform.forward;
    }

    forward.y = 0f;
    return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
  }

  private Vector3 GetCameraPlanarRight() {
    Vector3 forward = GetCameraPlanarForward();
    if (forward.sqrMagnitude < 0.0001f) {
      return Vector3.zero;
    }

    var right = Vector3.Cross(Vector3.up, forward);
    return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.zero;
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    DrawDebugGizmos();
  }

  private void OnDrawGizmosSelected() {
    DrawDebugGizmos();
  }

  private void DrawDebugGizmos() {
    if (!drawRayGizmos) {
      return;
    }

    var lateralLen = Mathf.Max(0.05f, lateralRayLength);
    var backLen = Mathf.Max(0.05f, backRayLength);

    Vector3 backDir = _debugMoveDir;
    if (backDir.sqrMagnitude < 0.0001f) {
      backDir = GetCameraPlanarForward();
      if (backDir.sqrMagnitude < 0.0001f) {
        backDir = Vector3.forward;
      }
    }

    Vector3 lateralDir = _debugLateralDir;
    if (lateralDir.sqrMagnitude < 0.0001f) {
      lateralDir = -GetCameraPlanarRight();
      if (lateralDir.sqrMagnitude < 0.0001f) {
        lateralDir = Vector3.left;
      }
    }

    Vector3 probeOrigin = _hasDebugProbe ? _debugProbeOrigin : transform.position;

    Vector3 leftTip = _hasDebugProbe ? _debugLeftTip : probeOrigin + (-lateralDir) * lateralLen;
    Vector3 rightTip = _hasDebugProbe ? _debugRightTip : probeOrigin + lateralDir * lateralLen;

    Gizmos.color = Color.blue;
    Gizmos.DrawRay(probeOrigin, -lateralDir * lateralLen);
    Gizmos.color = Color.magenta;
    Gizmos.DrawRay(probeOrigin, lateralDir * lateralLen);

    Gizmos.color = Color.yellow;
    Gizmos.DrawRay(leftTip, backDir * backLen);
    Gizmos.color = new Color(1f, 0.6f, 0f);
    Gizmos.DrawRay(rightTip, backDir * backLen);

    if (_debugLeftHit) {
      Gizmos.color = Color.green;
      Gizmos.DrawSphere(_debugLeftHitInfo.point, 0.04f);
    }

    if (_debugRightHit) {
      Gizmos.color = Color.green;
      Gizmos.DrawSphere(_debugRightHitInfo.point, 0.04f);
    }

    if (_debugLeftBackHit) {
      Gizmos.color = Color.red;
      Gizmos.DrawSphere(_debugLeftBackHitInfo.point, 0.04f);
    }

    if (_debugRightBackHit) {
      Gizmos.color = new Color(1f, 0.2f, 0.2f);
      Gizmos.DrawSphere(_debugRightBackHitInfo.point, 0.04f);
    }

    if (_hasCachedWall) {
      Gizmos.color = Color.cyan;
      Gizmos.DrawRay(transform.position, _cachedWallNormal * Mathf.Max(0.1f, lateralRayLength));
    }
  }
#endif
}
