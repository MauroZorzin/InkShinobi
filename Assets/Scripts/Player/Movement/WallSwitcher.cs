using System.Collections;
using UnityEngine;

/// <summary>
/// Handles scripted 180-degree wall switches when the player requests a transition to the wall ahead.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerMovementController))]
[RequireComponent(typeof(RightAngleWallTurner))]
public class WallSwitcher : MonoBehaviour {
  [Header("References")]
  [Tooltip("Pivot that rotates the camera around the player (defaults to Camera.main parent).")]
  public Transform camPivot;

  [Tooltip("Movement controller to disable during a wall switch.")]
  public PlayerMovementController movementController;

  [Tooltip("Corner turn script to pause while the switch is running.")]
  public RightAngleWallTurner rightAngleWallTurner;

  [Header("Wall Detection")]
  [Tooltip("Layer(s) used for wall detection.")]
  public LayerMask wallLayer;

  [Tooltip("Maximum front distance to detect a switchable wall.")]
  public float frontRayLength = 1f;

  [Tooltip("Horizontal offset from the player center for the two front rays.")]
  public float frontRayCenterOffset = 0.2f;

  [Header("Switch Transition")]
  [Tooltip("Seconds spent rotating the camera the first 90 degrees before the player begins moving to the target wall.")]
  public float firstNinetyRotationDuration = 0.25f;

  [Tooltip("Seconds spent moving the player from the current wall position to the target wall position while the camera remains turned sideways.")]
  public float switchObservationDuration = 0.5f;

  [Tooltip("Seconds spent rotating the camera the final 90 degrees after the player reaches the target wall.")]
  public float finalNinetyRotationDuration = 0.25f;

  [Header("Player snap")]
  [Tooltip("Distance from the target wall after switching.")]
  public float wallHugDistance = 0.25f;

  [Header("Debug")]
  [Tooltip("Draws wall-switch detection rays and target points in the Scene view.")]
  public bool drawDebugGizmos = true;

  [Tooltip("Writes wall-switch ray hit diagnostics to the console.")]
  public bool logRayHits = true;

  private const float SWITCH_COOLDOWN = 1f;

  private CharacterController _cc;
  private bool _isSwitching;
  private float _lastRequestTime = -999f;

  private bool _hasLastFrontHit;
  private RaycastHit _lastFrontHit;
  private bool _lastFrontLeftHit;
  private bool _lastFrontRightHit;
  private RaycastHit _lastFrontLeftHitInfo;
  private RaycastHit _lastFrontRightHitInfo;

  /// <summary>
  /// Returns whether a wall switch transition is currently being animated.
  /// </summary>
  /// <value>True while the switch coroutine owns camera rotation and player positioning.</value>
  public bool IsSwitching => _isSwitching;

  private void Awake() {
    _cc = GetComponent<CharacterController>();

    if (movementController == null) {
      movementController = GetComponent<PlayerMovementController>();
    }

    if (rightAngleWallTurner == null) {
      rightAngleWallTurner = GetComponent<RightAngleWallTurner>();
    }

    if (camPivot == null && Camera.main != null) {
      camPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
    }
  }

  /// <summary>
  /// Attempts to start a wall switch from player input.
  /// </summary>
  /// <returns>True when a valid front wall was found and the switch animation was started.</returns>
  public bool RequestSwitch() {
    if (!enabled || _isSwitching || Time.time < _lastRequestTime + SWITCH_COOLDOWN) {
      return false;
    }

    _lastRequestTime = Time.time;

    if (movementController != null && movementController.IsRotating()) {
      if (logRayHits) {
        Debug.Log("[WallSwitcher] Request denied: movement controller is rotating.");
      }
      return false;
    }

    if (rightAngleWallTurner != null && rightAngleWallTurner.IsTurning) {
      if (logRayHits) {
        Debug.Log("[WallSwitcher] Request denied: right-angle turn in progress.");
      }
      return false;
    }

    if (!TryDetectFrontWall(out var frontHit)) {
      if (logRayHits) {
        Debug.Log("[WallSwitcher] Request denied: no front wall in range.");
      }
      return false;
    }

    _lastFrontHit = frontHit;
    _hasLastFrontHit = true;

    if (logRayHits) {
      Debug.Log($"[WallSwitcher] Switch started. frontNormal={frontHit.normal.ToString("F3")} frontPoint={frontHit.point.ToString("F3")}");
    }

    StartCoroutine(DoSwitch(frontHit));
    return true;
  }

  /// <summary>
  /// Requires both offset front rays to hit, preventing partial or edge-only wall switches.
  /// </summary>
  /// <param name="hit">The closest of the two front-ray wall hits when detection succeeds.</param>
  /// <returns>True when both front rays hit a switchable wall.</returns>
  private bool TryDetectFrontWall(out RaycastHit hit) {
    Vector3 forward = -GetCameraPlanarForward();
    if (forward.sqrMagnitude < 0.0001f) {
      hit = default;
      _lastFrontLeftHit = false;
      _lastFrontRightHit = false;
      return false;
    }

    Vector3 lateral = GetCameraPlanarRight();
    if (lateral.sqrMagnitude < 0.0001f) {
      lateral = transform.right;
      lateral.y = 0f;
      lateral = lateral.sqrMagnitude > 0.0001f ? lateral.normalized : Vector3.right;
    }

    var offset = Mathf.Max(0f, frontRayCenterOffset);
    var length = Mathf.Max(0.05f, frontRayLength);

    Vector3 leftOrigin = transform.position - lateral * offset;
    Vector3 rightOrigin = transform.position + lateral * offset;

    _lastFrontLeftHit = Physics.Raycast(leftOrigin, forward, out _lastFrontLeftHitInfo, length, wallLayer, QueryTriggerInteraction.Ignore);
    _lastFrontRightHit = Physics.Raycast(rightOrigin, forward, out _lastFrontRightHitInfo, length, wallLayer, QueryTriggerInteraction.Ignore);

    if (!(_lastFrontLeftHit && _lastFrontRightHit)) {
      hit = default;
      return false;
    }

    hit = _lastFrontLeftHitInfo.distance <= _lastFrontRightHitInfo.distance ? _lastFrontLeftHitInfo : _lastFrontRightHitInfo;
    return true;
  }

  /// <summary>
  /// Rotates the camera, moves the player to the opposite wall, then restores movement control.
  /// </summary>
  /// <param name="targetWall">The wall hit selected as the target for the switch.</param>
  /// <returns>Coroutine enumerator used by Unity while the switch sequence is active.</returns>
  private IEnumerator DoSwitch(RaycastHit targetWall) {
    _isSwitching = true;

    if (rightAngleWallTurner != null) {
      rightAngleWallTurner.enabled = false;
    }

    if (movementController != null) {
      movementController.enabled = false;
    }

    if (camPivot == null && Camera.main != null) {
      camPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
    }

    if (camPivot == null) {
      FinishSwitch();
      yield break;
    }

    Vector3 startPos = transform.position;
    Vector3 targetPos = ComputeHuggedPosition(targetWall, startPos);

    var startYaw = camPivot.eulerAngles.y;
    var sideYaw = startYaw + 90f;
    var targetYaw = startYaw + 180f;

    // 1. Rotate the camera 90 degrees first.
    yield return AnimateCameraYaw(startYaw, sideYaw, firstNinetyRotationDuration);

    // 2. Move the player while the camera stays sideways.
    yield return AnimatePlayerMove(startPos, targetPos, sideYaw, switchObservationDuration);

    // 3. Rotate the camera the remaining 90 degrees.
    yield return AnimateCameraYaw(sideYaw, targetYaw, finalNinetyRotationDuration);

    // Final exact placement.
    camPivot.eulerAngles = new Vector3(0f, targetYaw, 0f);
    SetPlayerPosition(targetPos);
    camPivot.position = transform.position;

    if (movementController != null) {
      movementController.ResetHorizontalVelocity();
    }

    if (rightAngleWallTurner != null) {
      rightAngleWallTurner.NotifyWallSwitchCompleted(targetWall.normal);
    }

    if (logRayHits) {
      Debug.Log($"[WallSwitcher] Switch completed. targetPos={targetPos.ToString("F3")}");
    }

    FinishSwitch();
  }

  /// <summary>
  /// Interpolates the camera pivot yaw while keeping the pivot centered on the player.
  /// </summary>
  /// <param name="fromYaw">Starting yaw angle in degrees.</param>
  /// <param name="toYaw">Target yaw angle in degrees.</param>
  /// <param name="duration">Animation duration in seconds before clamping to a minimum value.</param>
  /// <returns>Coroutine enumerator used by Unity while the yaw animation is active.</returns>
  private IEnumerator AnimateCameraYaw(float fromYaw, float toYaw, float duration) {
    var elapsed = 0f;
    duration = Mathf.Max(0.01f, duration);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;

      var t = Mathf.Clamp01(elapsed / duration);
      var eased = Mathf.SmoothStep(0f, 1f, t);

      var yaw = Mathf.LerpAngle(fromYaw, toYaw, eased);
      camPivot.eulerAngles = new Vector3(0f, yaw, 0f);
      camPivot.position = transform.position;

      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, toYaw, 0f);
    camPivot.position = transform.position;
  }

  /// <summary>
  /// Moves the player between two wall-hug positions while holding the camera at a fixed yaw.
  /// </summary>
  /// <param name="fromPos">Starting world position.</param>
  /// <param name="toPos">Target world position.</param>
  /// <param name="yaw">Camera yaw to hold during the move.</param>
  /// <param name="duration">Animation duration in seconds before clamping to a minimum value.</param>
  /// <returns>Coroutine enumerator used by Unity while the move animation is active.</returns>
  private IEnumerator AnimatePlayerMove(Vector3 fromPos, Vector3 toPos, float yaw, float duration) {
    var elapsed = 0f;
    duration = Mathf.Max(0.01f, duration);

    camPivot.eulerAngles = new Vector3(0f, yaw, 0f);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;

      var t = Mathf.Clamp01(elapsed / duration);
      var eased = Mathf.SmoothStep(0f, 1f, t);

      Vector3 pos = Vector3.Lerp(fromPos, toPos, eased);
      SetPlayerPosition(pos);

      camPivot.eulerAngles = new Vector3(0f, yaw, 0f);
      camPivot.position = transform.position;

      yield return null;
    }

    SetPlayerPosition(toPos);
    camPivot.eulerAngles = new Vector3(0f, yaw, 0f);
    camPivot.position = transform.position;
  }

  private void FinishSwitch() {
    if (movementController != null) {
      movementController.enabled = true;
    }

    if (rightAngleWallTurner != null) {
      rightAngleWallTurner.enabled = true;
    }

    _isSwitching = false;
  }

  /// <summary>
  /// Computes the target wall-hug position while preserving the player's along-wall coordinate.
  /// </summary>
  /// <param name="wallHit">Raycast hit on the target wall.</param>
  /// <param name="fromPosition">Current player position before the switch begins.</param>
  /// <returns>A world position offset from the wall by the configured hug distance.</returns>
  private Vector3 ComputeHuggedPosition(RaycastHit wallHit, Vector3 fromPosition) {
    var standoff = Mathf.Max(0.01f, wallHugDistance);
    Vector3 targetPos = wallHit.point + wallHit.normal * standoff;

    Vector3 flatNormal = wallHit.normal;
    flatNormal.y = 0f;

    if (flatNormal.sqrMagnitude > 0.0001f) {
      flatNormal.Normalize();
      Vector3 flatTangent = Vector3.Cross(Vector3.up, flatNormal);

      if (flatTangent.sqrMagnitude > 0.0001f) {
        flatTangent.Normalize();

        // Keep the same along-wall coordinate to avoid lateral drift during the switch.
        var sourceAlongWall = Vector3.Dot(new Vector3(fromPosition.x, 0f, fromPosition.z), flatTangent);
        var targetAlongWall = Vector3.Dot(new Vector3(targetPos.x, 0f, targetPos.z), flatTangent);
        var alongWallDelta = sourceAlongWall - targetAlongWall;
        targetPos += flatTangent * alongWallDelta;
      }
    }

    targetPos.y = fromPosition.y;
    return targetPos;
  }

  /// <summary>
  /// Temporarily disables the character controller so scripted placement is not blocked by collision resolution.
  /// </summary>
  /// <param name="worldPos">World position to assign to the player transform.</param>
  private void SetPlayerPosition(Vector3 worldPos) {
    if (_cc == null) {
      _cc = GetComponent<CharacterController>();
    }

    if (_cc == null) {
      transform.position = worldPos;
      return;
    }

    var wasEnabled = _cc.enabled;
    _cc.enabled = false;
    transform.position = worldPos;
    _cc.enabled = wasEnabled;
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

    Vector3 right = Vector3.Cross(Vector3.up, forward);
    return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.zero;
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    DrawDebug();
  }

  private void OnDrawGizmosSelected() {
    DrawDebug();
  }

  private void DrawDebug() {
    if (!drawDebugGizmos) {
      return;
    }

    Vector3 forward = -GetCameraPlanarForward();
    if (forward.sqrMagnitude < 0.0001f) {
      return;
    }

    Vector3 lateral = GetCameraPlanarRight();
    if (lateral.sqrMagnitude < 0.0001f) {
      lateral = transform.right;
      lateral.y = 0f;
      lateral = lateral.sqrMagnitude > 0.0001f ? lateral.normalized : Vector3.right;
    }

    var checkLength = Mathf.Max(0.05f, frontRayLength);
    var offset = Mathf.Max(0f, frontRayCenterOffset);
    Vector3 leftOrigin = transform.position - lateral * offset;
    Vector3 rightOrigin = transform.position + lateral * offset;

    Gizmos.color = _lastFrontLeftHit ? Color.green : Color.yellow;
    Gizmos.DrawRay(leftOrigin, forward * checkLength);
    Gizmos.color = _lastFrontRightHit ? Color.green : Color.yellow;
    Gizmos.DrawRay(rightOrigin, forward * checkLength);

    if (_hasLastFrontHit) {
      Gizmos.color = Color.cyan;
      Gizmos.DrawSphere(_lastFrontHit.point, 0.05f);
      Gizmos.DrawRay(_lastFrontHit.point, _lastFrontHit.normal * 0.4f);
    }
  }
#endif
}
