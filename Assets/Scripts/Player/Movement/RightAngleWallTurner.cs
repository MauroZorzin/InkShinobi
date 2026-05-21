using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Detects 90-degree wall corners around the player and orchestrates the camera rotation,
/// player snap, and post-turn wall re-hugging needed to transition cleanly onto the next wall.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerMovementController))]
[RequireComponent(typeof(WallSwitcher))]
public class RightAngleWallTurner : MonoBehaviour {
  [Header("References")]
  [Tooltip("Pivot that rotates the camera around the player (defaults to Camera.main parent).")]
  public Transform camPivot;

  [Tooltip("Movement controller to pause during the corner transition.")]
  public PlayerMovementController movementController;

  [Tooltip("Wall switch script used to avoid overlapping transitions.")]
  public WallSwitcher wallSwitcher;

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

  [Tooltip("Maximum distance from the expected target contact for broad wall reacquisition when no target collider is known.")]
  public float targetContactTolerance = 0.25f;

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
  /// <value>True while the corner-turn coroutine owns camera rotation and player placement.</value>
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

    if (wallSwitcher == null) {
      wallSwitcher = GetComponent<WallSwitcher>();
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
    if (wallSwitcher != null && wallSwitcher.IsSwitching) {
      return;
    }

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
  /// <param name="moveDir">Current horizontal movement direction used to validate the corner candidate.</param>
  /// <param name="logicalLeftDir">Logical left probe direction for the current camera orientation.</param>
  /// <param name="logicalRightDir">Logical right probe direction for the current camera orientation.</param>
  /// <param name="cameraForward">Planar camera-forward direction used by back rays.</param>
  /// <param name="requireTowardCornerCheck">Whether the candidate must also be in front of the player's movement.</param>
  /// <param name="hadCornerCandidate">Set to true when the ray state found a candidate, even if it was rejected.</param>
  /// <returns>True when a corner-turn coroutine was started.</returns>
  private bool TryTriggerCornerTurnFromMovement(Vector3 moveDir, Vector3 logicalLeftDir, Vector3 logicalRightDir, Vector3 cameraForward, bool requireTowardCornerCheck, out bool hadCornerCandidate) {
    hadCornerCandidate = false;

    if (!TryGetCornerTurnTarget(logicalLeftDir, logicalRightDir, cameraForward, moveDir, out Vector3 turnNormal, out Vector3 turnContact, out var turnLeft, out var turnCornerKind, out var turnCollider)) {
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
    StartCoroutine(DoCornerTurn(turnNormal, turnContact, moveDir, turnLeft, turnCollider));
    return true;
  }

  /// <summary>
  /// Converts camera-right space into the logical left/right probe directions used by the turn
  /// detector, which intentionally swaps world-left and world-right to match the wall-following
  /// convention used by the controller.
  /// </summary>
  /// <param name="rightDir">Planar camera-right direction.</param>
  /// <param name="logicalLeftDir">Receives the logical left probe direction.</param>
  /// <param name="logicalRightDir">Receives the logical right probe direction.</param>
  private static void GetLogicalTurnProbeDirections(Vector3 rightDir, out Vector3 logicalLeftDir, out Vector3 logicalRightDir) {
    logicalLeftDir = rightDir;
    logicalRightDir = -rightDir;
  }

  private void ClearPostSwitchDecision() {
    _awaitingPostSwitchInputDecision = false;
  }

  /// <summary>
  /// Checks whether the current movement vector is carrying the player toward a detected corner.
  /// </summary>
  /// <param name="moveDir">Current horizontal movement direction.</param>
  /// <param name="cornerContact">Contact point used as the corner target.</param>
  /// <returns>True when the flattened move direction points toward the corner contact.</returns>
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
  /// <param name="logicalLeftDir">Logical left probe direction for the current camera orientation.</param>
  /// <param name="logicalRightDir">Logical right probe direction for the current camera orientation.</param>
  /// <param name="cameraForward">Planar camera-forward direction used by back rays.</param>
  /// <param name="moveDir">Current horizontal movement direction.</param>
  /// <param name="nextNormal">Receives the target wall normal when a corner is found.</param>
  /// <param name="nextContact">Receives the target wall contact point when a corner is found.</param>
  /// <param name="isLeftTurn">Receives whether the corner should be animated as a left turn.</param>
  /// <param name="cornerKind">Receives the detected corner classification.</param>
  /// <param name="targetCollider">Receives the exact target wall collider when the target came from a ray hit.</param>
  /// <returns>True when either inner- or outer-corner resolution found a target.</returns>
  private bool TryGetCornerTurnTarget(Vector3 logicalLeftDir, Vector3 logicalRightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out string cornerKind, out Collider targetCollider) {
    if (TryGetInnerTurnTarget(out nextNormal, out nextContact, out isLeftTurn, out targetCollider)) {
      cornerKind = "inner";
      return true;
    }

    if (TryGetOuterTurnTarget(logicalLeftDir, logicalRightDir, cameraForward, moveDir, out nextNormal, out nextContact, out isLeftTurn, out targetCollider)) {
      cornerKind = "outer";
      return true;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    isLeftTurn = false;
    cornerKind = string.Empty;
    targetCollider = null;
    return false;
  }

  /// <summary>
  /// Chooses the current horizontal travel direction from controller velocity, falling back to input.
  /// </summary>
  /// <returns>A normalized horizontal movement direction, or zero when the player is not moving.</returns>
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

  /// <summary>
  /// Finds the wall the player is currently hugging, favoring the cached wall normal when possible.
  /// </summary>
  /// <param name="lateralDir">One lateral search direction relative to the current camera orientation.</param>
  /// <param name="hit">Receives the selected wall hit.</param>
  /// <param name="wallNormal">Receives the flattened wall normal.</param>
  /// <returns>True when a current wall could be resolved.</returns>
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

  /// <summary>
  /// Searches nearby cardinal directions when lateral rays lost the current wall.
  /// </summary>
  /// <param name="hit">Receives the best nearby wall hit.</param>
  /// <param name="normal">Receives the flattened normal of the best wall hit.</param>
  /// <returns>True when the fallback sphere cast finds a wall candidate.</returns>
  private bool TryFindWallBySphereCast(out RaycastHit hit, out Vector3 normal) {
    Vector3 origin = transform.position;
    var dirs = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    var bestScore = float.NegativeInfinity;
    var found = false;
    RaycastHit bestHit = default;

    foreach (var dir in dirs) {
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

  /// <summary>
  /// Scores competing current-wall ray hits using distance, expected side, and cached wall continuity.
  /// </summary>
  /// <param name="expectedNormal">Expected wall normal direction for the candidate ray.</param>
  /// <param name="hit">Candidate raycast hit to score.</param>
  /// <returns>A higher score for candidates that are closer and better aligned with the expected wall.</returns>
  private float ScoreCurrentWallCandidate(Vector3 expectedNormal, RaycastHit hit) {
    Vector3 candidateNormal = Flatten(hit.normal);
    var score = -hit.distance;
    score += Vector3.Dot(candidateNormal, expectedNormal) * 2f;

    if (_hasCachedWall) {
      score += Vector3.Dot(candidateNormal, _cachedWallNormal);
    }

    return score;
  }

  /// <summary>
  /// Draws the runtime side and back rays using the latest hit state colors.
  /// </summary>
  /// <param name="probeOrigin">Origin shared by the two lateral side rays.</param>
  /// <param name="leftDir">Logical left lateral ray direction.</param>
  /// <param name="rightDir">Logical right lateral ray direction.</param>
  /// <param name="backDir">Direction used by the back rays from each side tip.</param>
  /// <param name="lateralLen">Length of each lateral ray.</param>
  /// <param name="backLen">Length of each back ray.</param>
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

  /// <summary>
  /// Attempts to resolve an inner-corner target from either lateral side hit.
  /// </summary>
  /// <param name="nextNormal">Receives the target wall normal.</param>
  /// <param name="nextContact">Receives the target wall contact point.</param>
  /// <param name="isLeftTurn">Receives whether the turn should animate left.</param>
  /// <param name="targetCollider">Receives the collider hit by the side ray.</param>
  /// <returns>True when one of the side hits describes a valid inner corner.</returns>
  private bool TryGetInnerTurnTarget(out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out Collider targetCollider) {
    if (TryGetInnerCornerFromSideHit(_debugLeftHit, _debugLeftHitInfo, false, out nextNormal, out nextContact, out isLeftTurn, out targetCollider)) {
      return true;
    }

    if (TryGetInnerCornerFromSideHit(_debugRightHit, _debugRightHitInfo, true, out nextNormal, out nextContact, out isLeftTurn, out targetCollider)) {
      return true;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    isLeftTurn = false;
    targetCollider = null;
    return false;
  }

  /// <summary>
  /// Interprets a single lateral wall hit as an inner-corner target and translates the hit into
  /// the wall normal, contact point, and turn direction used by the corner-turn coroutine.
  /// </summary>
  /// <param name="hasHit">Whether the side ray hit a wall.</param>
  /// <param name="hitInfo">Raycast hit from the side ray.</param>
  /// <param name="turnLeftOnHit">Turn direction to report if this hit is valid.</param>
  /// <param name="nextNormal">Receives the target wall normal.</param>
  /// <param name="nextContact">Receives the target wall contact point.</param>
  /// <param name="isLeftTurn">Receives whether the turn should animate left.</param>
  /// <param name="targetCollider">Receives the collider hit by the side ray.</param>
  /// <returns>True when the side hit provides a non-zero wall normal.</returns>
  private bool TryGetInnerCornerFromSideHit(bool hasHit, RaycastHit hitInfo, bool turnLeftOnHit, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out Collider targetCollider) {
    if (!hasHit) {
      nextNormal = Vector3.zero;
      nextContact = Vector3.zero;
      isLeftTurn = false;
      targetCollider = null;
      return false;
    }

    nextNormal = Flatten(hitInfo.normal);
    nextContact = hitInfo.point;
    isLeftTurn = turnLeftOnHit;
    targetCollider = hitInfo.collider;
    return nextNormal.sqrMagnitude > 0.0001f;
  }

  /// <summary>
  /// Interprets the side and back ray pattern for exposed outside corners, including overshoot
  /// frames where the player has already moved past the one-hot back-ray state.
  /// </summary>
  /// <param name="leftDir">Logical left lateral ray direction.</param>
  /// <param name="rightDir">Logical right lateral ray direction.</param>
  /// <param name="cameraForward">Planar camera-forward direction used by back rays.</param>
  /// <param name="moveDir">Current horizontal movement direction.</param>
  /// <param name="nextNormal">Receives the inferred target wall normal.</param>
  /// <param name="nextContact">Receives the inferred target wall contact point.</param>
  /// <param name="isLeftTurn">Receives whether the turn should animate left.</param>
  /// <param name="targetCollider">Receives the target wall collider when the target came from a ray hit.</param>
  /// <returns>True when the current side/back ray pattern describes an outer corner.</returns>
  private bool TryGetOuterTurnTarget(Vector3 leftDir, Vector3 rightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out Collider targetCollider) {
    var noSideHits = !_debugLeftHit && !_debugRightHit;
    var outwardLeft = noSideHits && !_debugLeftBackHit && _debugRightBackHit;
    var outwardRight = noSideHits && _debugLeftBackHit && !_debugRightBackHit;
    var overshotOuter = noSideHits && !_debugLeftBackHit && !_debugRightBackHit;

    if (!outwardLeft && !outwardRight && !overshotOuter) {
      nextNormal = Vector3.zero;
      nextContact = Vector3.zero;
      isLeftTurn = false;
      targetCollider = null;
      return false;
    }

    if (overshotOuter && !outwardLeft && !outwardRight) {
      return TryResolveOvershotOuterTurn(leftDir, rightDir, cameraForward, moveDir, out nextNormal, out nextContact, out isLeftTurn, out targetCollider);
    }

    isLeftTurn = outwardLeft;

    Vector3 sideDir = outwardLeft ? leftDir : rightDir;
    Vector3 sideTip = outwardLeft ? _debugLeftTip : _debugRightTip;
    return TryResolveOuterTargetForSide(sideDir, sideTip, cameraForward, out nextNormal, out nextContact, out targetCollider);
  }

  /// <summary>
  /// Resolves outer-corner frames where the player has already advanced beyond the one-hot back-ray
  /// pattern and the detector must infer the skipped corner side from the current movement vector.
  /// </summary>
  /// <param name="leftDir">Logical left lateral ray direction.</param>
  /// <param name="rightDir">Logical right lateral ray direction.</param>
  /// <param name="cameraForward">Planar camera-forward direction used for wall reacquisition.</param>
  /// <param name="moveDir">Current horizontal movement direction used to choose the preferred side.</param>
  /// <param name="nextNormal">Receives the inferred target wall normal.</param>
  /// <param name="nextContact">Receives the inferred target wall contact point.</param>
  /// <param name="isLeftTurn">Receives whether the turn should animate left.</param>
  /// <param name="targetCollider">Receives the target wall collider when the target came from a ray hit.</param>
  /// <returns>True when an overshot outer corner could be resolved or inferred.</returns>
  private bool TryResolveOvershotOuterTurn(Vector3 leftDir, Vector3 rightDir, Vector3 cameraForward, Vector3 moveDir, out Vector3 nextNormal, out Vector3 nextContact, out bool isLeftTurn, out Collider targetCollider) {
    var preferLeft = Vector3.Dot(moveDir, leftDir) >= Vector3.Dot(moveDir, rightDir);

    if (TryResolveOuterTargetForSide(preferLeft ? leftDir : rightDir, preferLeft ? _debugLeftTip : _debugRightTip, cameraForward, out nextNormal, out nextContact, out targetCollider)) {
      isLeftTurn = preferLeft;
      return true;
    }

    if (TryResolveOuterTargetForSide(preferLeft ? rightDir : leftDir, preferLeft ? _debugRightTip : _debugLeftTip, cameraForward, out nextNormal, out nextContact, out targetCollider)) {
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
    targetCollider = null;
    return true;
  }

  /// <summary>
  /// Reacquires the next wall for an outer corner from a side-tip search point.
  /// </summary>
  /// <param name="sideDir">Side direction that points away from the current wall edge.</param>
  /// <param name="sideTip">Tip position of the side ray for the chosen side.</param>
  /// <param name="cameraForward">Planar camera-forward direction used to advance the search origin.</param>
  /// <param name="nextNormal">Receives the target wall normal.</param>
  /// <param name="nextContact">Receives the target wall contact point.</param>
  /// <param name="targetCollider">Receives the target wall collider.</param>
  /// <returns>True when the next wall can be found from the side search.</returns>
  private bool TryResolveOuterTargetForSide(Vector3 sideDir, Vector3 sideTip, Vector3 cameraForward, out Vector3 nextNormal, out Vector3 nextContact, out Collider targetCollider) {
    var backLen = Mathf.Max(0.05f, backRayLength);
    var lateralLen = Mathf.Max(0.05f, lateralRayLength);

    Vector3 searchOrigin = sideTip + cameraForward * backLen;
    if (Physics.Raycast(searchOrigin, -sideDir, out RaycastHit sideSearchHit, lateralLen * 2f, wallLayer, QueryTriggerInteraction.Ignore)) {
      nextNormal = Flatten(sideSearchHit.normal);
      nextContact = sideSearchHit.point;
      targetCollider = sideSearchHit.collider;
      return nextNormal.sqrMagnitude > 0.0001f;
    }

    if (TryFindWallAlongNormal(searchOrigin, sideDir, out RaycastHit fallbackHit)) {
      nextNormal = Flatten(fallbackHit.normal);
      nextContact = fallbackHit.point;
      targetCollider = fallbackHit.collider;
      return nextNormal.sqrMagnitude > 0.0001f;
    }

    nextNormal = Vector3.zero;
    nextContact = Vector3.zero;
    targetCollider = null;
    return false;
  }

  private void LogTurnTriggered(string cornerKind, string direction, Vector3 normal, Vector3 contact) {
    if (logRayHits) {
      Debug.Log(
        $"[RightAngleWallTurner] Turn triggered kind={cornerKind} direction={direction} " +
        $"normal={normal:F3} contact={contact:F3} " +
        $"hits(L={_debugLeftHit},R={_debugRightHit},LB={_debugLeftBackHit},RB={_debugRightBackHit})"
      );
    }
  }

  /// <summary>
  /// Animates the camera yaw and player relocation needed to carry the player from the current
  /// wall onto the next wall while keeping the character hugged to the new surface.
  /// </summary>
  /// <param name="nextNormal">Normal of the wall the player is turning onto.</param>
  /// <param name="nextContactPoint">Contact point on the wall the player is turning onto.</param>
  /// <param name="alongWallBeforeTurn">Movement direction along the previous wall before the turn started.</param>
  /// <param name="isLeftTurn">Whether the camera should rotate left instead of right.</param>
  /// <param name="targetCollider">Exact wall collider to prefer during post-turn wall reacquisition.</param>
  /// <returns>Coroutine enumerator used by Unity while the corner turn is active.</returns>
  private IEnumerator DoCornerTurn(Vector3 nextNormal, Vector3 nextContactPoint, Vector3 alongWallBeforeTurn, bool isLeftTurn, Collider targetCollider) {
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
      var expectedContact = Vector3.Lerp(nextContactPoint, cornerExitAnchor, eased);
      Vector3 cornerPos = ComputeHuggedPosition(travelAnchor, nextNormal, targetCollider, expectedContact, false);
      SetPlayerPosition(cornerPos);
      camPivot.position = transform.position;

      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, targetYaw, 0f);
    SetPlayerPosition(ComputeHuggedPosition(cornerExitAnchor, nextNormal, targetCollider, cornerExitAnchor));
    camPivot.position = transform.position;

    yield return StartCoroutine(CorrectPostTurnHug(nextNormal, targetCollider, cornerExitAnchor));

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
  /// <param name="wallNormal">Normal of the wall the player should be hugging after the turn.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected final contact point on the target wall.</param>
  /// <returns>Coroutine enumerator used by Unity while post-turn correction is active.</returns>
  private IEnumerator CorrectPostTurnHug(Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact) {
    var attempts = Mathf.Max(0, postTurnCorrectionFrames);
    for (var i = 0; i < attempts; i++) {
      if (IsHugDistanceRestored(transform.position, wallNormal, targetCollider, expectedContact, out var hugError)) {
        var bothBackHits = AreBackRaysBothHittingAtCurrentPose(out var leftDistance, out var rightDistance);
        if (logRayHits) {
          Debug.Log($"[RightAngleWallTurner] Post-turn correction settled. hugError={hugError:F4} backHitsBoth={bothBackHits} backDistances(L={leftDistance:F3},R={rightDistance:F3})");
        }
        yield break;
      }

      if (!RestoreHugDistance(wallNormal, targetCollider, expectedContact)) {
        break;
      }

      yield return null;
    }

    if (logRayHits) {
      var bothBackHits = AreBackRaysBothHittingAtCurrentPose(out var leftDistance, out var rightDistance);
      var hasHug = TryGetHugDistanceError(transform.position, wallNormal, targetCollider, expectedContact, out var remainingError);
      Debug.Log($"[RightAngleWallTurner] Post-turn correction ended without full restore. hasHug={hasHug} remainingError={remainingError:F4} backHitsBoth={bothBackHits} backDistances(L={leftDistance:F3},R={rightDistance:F3})");
    }
  }

  /// <summary>
  /// Steps the player back toward the target wall and reapplies strict wall-distance correction
  /// until the configured hug tolerance is restored or the search budget is exhausted.
  /// </summary>
  /// <param name="wallNormal">Normal of the wall the player should be hugging.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected final contact point on the target wall.</param>
  /// <returns>True when the configured hug distance is restored within tolerance.</returns>
  private bool RestoreHugDistance(Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact) {
    if (wallNormal.sqrMagnitude < 0.0001f) {
      return false;
    }

    var step = Mathf.Max(0.01f, postTurnBacktrackStep);
    var maxDistance = Mathf.Max(step, postTurnBacktrackMaxDistance);
    var maxSteps = Mathf.Max(1, Mathf.CeilToInt(maxDistance / step));

    for (var i = 0; i < maxSteps; i++) {
      if (IsHugDistanceRestored(transform.position, wallNormal, targetCollider, expectedContact, out _)) {
        return true;
      }

      Vector3 towardWall = transform.position - wallNormal * step;
      Vector3 candidate = towardWall;

      if (TryComputeHuggedPositionStrict(candidate, wallNormal, targetCollider, expectedContact, out Vector3 corrected)) {
        SetPlayerPosition(corrected);
      } else {
        if (logRayHits) {
          Debug.Log($"[RightAngleWallTurner] Post-turn correction stopped: target wall not reacquired near expectedContact={expectedContact:F3}.");
        }
        return false;
      }

      if (camPivot != null) {
        camPivot.position = transform.position;
      }
    }

    return IsHugDistanceRestored(transform.position, wallNormal, targetCollider, expectedContact, out _);
  }

  /// <summary>
  /// Tests whether a position is within the configured wall-hug tolerance.
  /// </summary>
  /// <param name="position">World position to test.</param>
  /// <param name="wallNormal">Normal of the wall being hugged.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected contact point used to reject unrelated broad hits.</param>
  /// <param name="error">Receives the current signed-distance error as an absolute value.</param>
  /// <returns>True when the hug distance can be computed and is inside tolerance.</returns>
  private bool IsHugDistanceRestored(Vector3 position, Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact, out float error) {
    if (!TryGetHugDistanceError(position, wallNormal, targetCollider, expectedContact, out error)) {
      return false;
    }

    return error <= Mathf.Max(0.0001f, postTurnHugDistanceTolerance);
  }

  /// <summary>
  /// Computes how far a position is from the strict wall-hugged position.
  /// </summary>
  /// <param name="position">World position to compare against the wall-hugged target.</param>
  /// <param name="wallNormal">Normal of the wall being hugged.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected contact point used to reject unrelated broad hits.</param>
  /// <param name="error">Receives the absolute distance error along the wall normal.</param>
  /// <returns>True when the wall can be reacquired and the error can be computed.</returns>
  private bool TryGetHugDistanceError(Vector3 position, Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact, out float error) {
    error = 0f;

    if (!TryComputeHuggedPositionStrict(position, wallNormal, targetCollider, expectedContact, out Vector3 targetHuggedPosition)) {
      return false;
    }

    error = Mathf.Abs(Vector3.Dot(position - targetHuggedPosition, wallNormal));
    return true;
  }

  /// <summary>
  /// Re-samples the post-turn back rays to decide whether the player is aligned with the new wall.
  /// </summary>
  /// <param name="leftDistance">Receives the left back-ray hit distance, or zero when it misses.</param>
  /// <param name="rightDistance">Receives the right back-ray hit distance, or zero when it misses.</param>
  /// <returns>True when both back rays hit at the current pose.</returns>
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

  /// <summary>
  /// Casts a back ray against both wall and passageway layers.
  /// </summary>
  /// <param name="origin">Ray origin.</param>
  /// <param name="direction">Ray direction.</param>
  /// <param name="hit">Receives hit information when the ray succeeds.</param>
  /// <param name="distance">Maximum ray distance.</param>
  /// <returns>True when the ray hits either a wall or a passageway blocker.</returns>
  private bool RaycastBackRay(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance) {
    var backRayMask = wallLayer.value | passagewayLayer.value;
    return Physics.Raycast(origin, direction, out hit, distance, backRayMask, QueryTriggerInteraction.Ignore);
  }

  /// <summary>
  /// Computes a wall-aligned position by explicitly reacquiring the wall along the supplied normal
  /// instead of assuming the current position is already at the desired stand-off distance.
  /// </summary>
  /// <param name="nearPosition">Position near the expected wall.</param>
  /// <param name="wallNormal">Normal of the wall to reacquire.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected contact point used to reject unrelated broad hits.</param>
  /// <param name="huggedPosition">Receives the corrected wall-hugged position.</param>
  /// <returns>True when the wall is found within the strict search distance.</returns>
  private bool TryComputeHuggedPositionStrict(Vector3 nearPosition, Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact, out Vector3 huggedPosition) {
    huggedPosition = Vector3.zero;

    var searchDistance = Mathf.Max(Mathf.Max(0.05f, lateralRayLength), postTurnWallSearchDistance);
    if (!TryFindWallAlongNormal(nearPosition, wallNormal, out RaycastHit hit, searchDistance, targetCollider, true, expectedContact)) {
      return false;
    }

    var standoff = Mathf.Max(0.01f, wallHugDistance);
    huggedPosition = hit.point + wallNormal * standoff;
    huggedPosition.y = transform.position.y;
    return true;
  }

  /// <summary>
  /// Computes the standard wall-hugged position, falling back to an offset when the wall ray misses.
  /// </summary>
  /// <param name="nearPosition">Position near the expected wall.</param>
  /// <param name="wallNormal">Normal of the wall to hug.</param>
  /// <param name="targetCollider">Exact wall collider to prefer when reacquiring the wall.</param>
  /// <param name="expectedContact">Expected contact point used to reject unrelated broad hits.</param>
  /// <param name="constrainToExpectedContact">Whether reacquired hits must be near the expected contact point.</param>
  /// <returns>A corrected wall-hugged position, or an offset fallback if the wall ray misses.</returns>
  private Vector3 ComputeHuggedPosition(Vector3 nearPosition, Vector3 wallNormal, Collider targetCollider, Vector3 expectedContact, bool constrainToExpectedContact = true) {
    var standoff = Mathf.Max(0.01f, wallHugDistance);

    if (TryFindWallAlongNormal(nearPosition, wallNormal, out RaycastHit hit, -1f, targetCollider, constrainToExpectedContact, expectedContact)) {
      Vector3 hugged = hit.point + wallNormal * standoff;
      hugged.y = transform.position.y;
      return hugged;
    }

    Vector3 fallback = nearPosition + wallNormal * standoff;
    fallback.y = transform.position.y;
    return fallback;
  }

  /// <summary>
  /// Casts back toward a wall from beyond the player so wall distance can be restored from either side.
  /// </summary>
  /// <param name="nearPosition">Position near the expected wall.</param>
  /// <param name="wallNormal">Normal direction pointing away from the wall.</param>
  /// <param name="hit">Receives wall hit information when the ray succeeds.</param>
  /// <param name="rayLengthOverride">Optional search distance override; negative values use the lateral ray length.</param>
  /// <param name="targetCollider">Exact wall collider to raycast against when one is known.</param>
  /// <param name="constrainToExpectedContact">Whether broad wall hits must be near the expected contact point.</param>
  /// <param name="expectedContact">Expected contact point used to reject unrelated broad hits.</param>
  /// <returns>True when the wall is found along the supplied normal.</returns>
  private bool TryFindWallAlongNormal(Vector3 nearPosition, Vector3 wallNormal, out RaycastHit hit, float rayLengthOverride = -1f, Collider targetCollider = null, bool constrainToExpectedContact = false, Vector3 expectedContact = default) {
    CharacterController cc = GetController();
    var radius = cc != null ? cc.radius : 0.4f;
    var rayLength = rayLengthOverride > 0f ? Mathf.Max(0.05f, rayLengthOverride) : Mathf.Max(0.05f, lateralRayLength);
    Vector3 origin = nearPosition + wallNormal * (rayLength + radius + RAY_START_OFFSET);
    var ray = new Ray(origin, -wallNormal);
    var maxDistance = rayLength * 2f;

    if (targetCollider != null) {
      return targetCollider.Raycast(ray, out hit, maxDistance)
        && (!constrainToExpectedContact || IsHitNearExpectedContact(hit, expectedContact));
    }

    if (!constrainToExpectedContact) {
      return Physics.Raycast(ray, out hit, maxDistance, wallLayer, QueryTriggerInteraction.Ignore);
    }

    return TryFindExpectedWallHit(ray, maxDistance, expectedContact, out hit);
  }

  /// <summary>
  /// Selects the closest broad wall hit that remains near the expected target contact.
  /// </summary>
  /// <param name="ray">Ray used to reacquire the wall.</param>
  /// <param name="maxDistance">Maximum ray distance.</param>
  /// <param name="expectedContact">Expected contact point on the intended target wall.</param>
  /// <param name="hit">Receives the closest accepted wall hit.</param>
  /// <returns>True when a wall hit lies within the configured contact tolerance.</returns>
  private bool TryFindExpectedWallHit(Ray ray, float maxDistance, Vector3 expectedContact, out RaycastHit hit) {
    var hits = Physics.RaycastAll(ray, maxDistance, wallLayer, QueryTriggerInteraction.Ignore);
    var found = false;
    var bestDistance = float.PositiveInfinity;
    var bestHit = default(RaycastHit);

    foreach (var candidate in hits) {
      if (!IsHitNearExpectedContact(candidate, expectedContact) || candidate.distance >= bestDistance) {
        continue;
      }

      found = true;
      bestDistance = candidate.distance;
      bestHit = candidate;
    }

    hit = bestHit;
    return found;
  }

  /// <summary>
  /// Checks whether a wall hit belongs to the expected target-contact neighborhood.
  /// </summary>
  /// <param name="hit">Wall hit to test.</param>
  /// <param name="expectedContact">Expected contact point on the intended target wall.</param>
  /// <returns>True when the hit point is close enough to be considered the intended wall face.</returns>
  private bool IsHitNearExpectedContact(RaycastHit hit, Vector3 expectedContact) {
    var tolerance = Mathf.Max(0.01f, targetContactTolerance);
    return (hit.point - expectedContact).sqrMagnitude <= tolerance * tolerance;
  }

  /// <summary>
  /// Casts a short ray toward the expected wall normal after flattening invalid vertical components.
  /// </summary>
  /// <param name="nearPosition">Position to cast from.</param>
  /// <param name="expectedWallNormal">Expected wall normal before flattening.</param>
  /// <param name="length">Maximum ray distance before clamping to a minimum value.</param>
  /// <param name="hit">Receives wall hit information when the ray succeeds.</param>
  /// <returns>True when the flattened normal is valid and the ray hits a wall.</returns>
  private bool TryRayTowardNormal(Vector3 nearPosition, Vector3 expectedWallNormal, float length, out RaycastHit hit) {
    Vector3 flatExpected = Flatten(expectedWallNormal);
    if (flatExpected.sqrMagnitude < 0.0001f) {
      hit = default;
      return false;
    }

    Vector3 origin = nearPosition + flatExpected * RAY_START_OFFSET;
    return Physics.Raycast(origin, -flatExpected, out hit, Mathf.Max(0.05f, length), wallLayer, QueryTriggerInteraction.Ignore);
  }

  /// <summary>
  /// Temporarily disables the character controller so scripted placement is not blocked by collision resolution.
  /// </summary>
  /// <param name="worldPos">World position to assign to the player transform.</param>
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

  /// <summary>
  /// Removes vertical influence from a vector and normalizes it when it remains usable.
  /// </summary>
  /// <param name="v">Vector to flatten onto the horizontal plane.</param>
  /// <returns>A normalized horizontal vector, or zero when the flattened vector is too small.</returns>
  private static Vector3 Flatten(Vector3 v) {
    v.y = 0f;
    return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
  }

  /// <summary>
  /// Gets the camera-facing direction flattened onto the horizontal plane.
  /// </summary>
  /// <returns>A normalized planar forward vector, or zero when no stable direction is available.</returns>
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

  /// <summary>
  /// Gets the camera-right direction derived from the planar forward vector.
  /// </summary>
  /// <returns>A normalized planar right vector, or zero when no stable direction is available.</returns>
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
