using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the actual mechanics of a wall switch: validating a candidate target wall
/// and moving the player onto it. Has no knowledge of the camera or of input — those
/// are owned by WallVisionController, which calls into this component.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WallSwitcher : MonoBehaviour {
  [Header("References")]
  [Tooltip("Movement controller notified once the switch move completes (velocity reset).")]
  public PlayerMovementController movementController;

  [Tooltip("Corner turn script notified once the switch completes, so it can resume from the new wall.")]
  public RightAngleWallTurner rightAngleWallTurner;

  [Header("Wall Detection")]
  [Tooltip("Layer(s) used for wall detection.")]
  public LayerMask wallLayer;

  [Tooltip("Ray length used to find the wall the player is currently standing on.")]
  public float currentWallProbeLength = 1.5f;

  [Header("Switch Transition")]
  [Tooltip("Seconds spent moving the player from the current wall position to the target wall position.")]
  public float switchObservationDuration = 0.5f;

  [Tooltip("Distance from the target wall after switching.")]
  public float wallHugDistance = 0.25f;

  [Header("Debug")]
  [Tooltip("Draws the current-wall probe ray in the Scene view.")]
  public bool drawDebugGizmos = true;

  [Tooltip("Writes wall-switch diagnostics to the console.")]
  public bool logRayHits = true;

  private const float SWITCH_COOLDOWN = 0.5f;

  private CharacterController _cc;
  private bool _isSwitching;
  private float _lastSwitchTime = -999f;
  private Collider _currentWallCollider;

  /// <summary>True while the player is being moved onto the target wall.</summary>
  public bool IsSwitching => _isSwitching;

  /// <summary>The wall collider the player is currently standing on, if known.</summary>
  public Collider CurrentWallCollider => _currentWallCollider;

  private void Awake() {
    _cc = GetComponent<CharacterController>();

    if (movementController == null) {
      movementController = GetComponent<PlayerMovementController>();
    }

    if (rightAngleWallTurner == null) {
      rightAngleWallTurner = GetComponent<RightAngleWallTurner>();
    }
  }

  /// <summary>
  /// Re-probes the wall directly behind the player (along -transform.up) and caches it as
  /// the "current" wall, so it can be excluded from valid switch targets. Call this when
  /// entering vision mode, before the player starts aiming.
  /// </summary>
  public void RefreshCurrentWall() {
    _currentWallCollider = null;
    Vector3 origin = transform.position;
    Vector3 dir = -transform.up; // player's "up" points off the wall in a wall-walker setup

    if (Physics.Raycast(origin, dir, out RaycastHit hit, currentWallProbeLength, wallLayer, QueryTriggerInteraction.Ignore)) {
      _currentWallCollider = hit.collider;
    }
  }

  /// <summary>
  /// Checks whether a raycast hit is a legal switch target: it must be a real hit and not
  /// the wall the player is already standing on.
  /// </summary>
  /// <param name="hit">Candidate raycast hit, expected to already be filtered to wallLayer by the caller.</param>
  /// <returns>True when the hit is a valid switch target.</returns>
  public bool IsValidSwitchTarget(RaycastHit hit) {
    return hit.collider != null && hit.collider != _currentWallCollider;
  }

  /// <summary>
  /// Starts moving the player onto the target wall if the target is valid and no switch
  /// is already in progress or on cooldown.
  /// </summary>
  /// <param name="targetWall">The aimed raycast hit selected as the switch target.</param>
  /// <param name="onComplete">Optional callback invoked with the target wall once the move finishes.</param>
  /// <returns>True if a switch was started.</returns>
  public bool TrySwitchToWall(RaycastHit targetWall, Action<RaycastHit> onComplete = null) {
    if (!enabled || _isSwitching || Time.time < _lastSwitchTime + SWITCH_COOLDOWN) {
      return false;
    }

    if (!IsValidSwitchTarget(targetWall)) {
      if (logRayHits) {
        Debug.Log("[WallSwitcher] Switch denied: invalid target wall.");
      }
      return false;
    }

    if (logRayHits) {
      Debug.Log($"[WallSwitcher] Switch started. targetNormal={targetWall.normal:F3} targetPoint={targetWall.point:F3}");
    }

    StartCoroutine(SwitchRoutine(targetWall, onComplete));
    return true;
  }

  /// <summary>
  /// Moves the player from its current position to the hugged position on the target wall.
  /// </summary>
  /// <param name="targetWall">The wall hit selected as the target for the switch.</param>
  /// <param name="onComplete">Optional callback invoked with the target wall once the move finishes.</param>
  /// <returns>Coroutine enumerator used by Unity while the move is active.</returns>
  private IEnumerator SwitchRoutine(RaycastHit targetWall, Action<RaycastHit> onComplete) {
    _isSwitching = true;

    Vector3 startPos = transform.position;
    Vector3 targetPos = ComputeHuggedPosition(targetWall, startPos);

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, switchObservationDuration);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      SetPlayerPosition(Vector3.Lerp(startPos, targetPos, t));
      yield return null;
    }

    SetPlayerPosition(targetPos);

    if (movementController != null) {
      movementController.ResetHorizontalVelocity();
    }

    if (rightAngleWallTurner != null) {
      rightAngleWallTurner.NotifyWallSwitchCompleted(targetWall.normal);
    }

    _currentWallCollider = targetWall.collider;
    _lastSwitchTime = Time.time;
    _isSwitching = false;

    if (logRayHits) {
      Debug.Log($"[WallSwitcher] Switch completed. targetPos={targetPos:F3}");
    }

    onComplete?.Invoke(targetWall);
  }

  /// <summary>
  /// Computes the target wall-hug position from the aimed point's X/Z only — the player's
  /// height (Y) never changes during a switch, regardless of where vertically they aimed.
  /// This method only ever returns a position — it must never touch player rotation. Discrete
  /// 90/180/270/360-degree world rotation is owned exclusively by
  /// PlayerMovementController.RotateWorld / RightAngleWallTurner; nothing here should set
  /// transform.rotation, ever.
  /// </summary>
  /// <param name="wallHit">Raycast hit on the target wall — the exact point the player aimed at.</param>
  /// <param name="fromPosition">Current player position before the switch begins; its Y is preserved.</param>
  /// <returns>A world position at the aimed X/Z, offset off the wall by the configured hug distance, at the player's current height.</returns>
  private Vector3 ComputeHuggedPosition(RaycastHit wallHit, Vector3 fromPosition) {
    var standoff = Mathf.Max(0.01f, wallHugDistance);
    Vector3 targetPos = wallHit.point + wallHit.normal * standoff;
    targetPos.y = fromPosition.y;
    return targetPos;
  }

  /// <summary>
  /// Temporarily disables the character controller so scripted placement is not blocked by collision resolution.
  /// Intentionally position-only — never assigns transform.rotation. Player facing only ever changes
  /// in discrete 90-degree steps, owned by PlayerMovementController.RotateWorld / RightAngleWallTurner.
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

    Gizmos.color = Color.magenta;
    Gizmos.DrawRay(transform.position, -transform.up * currentWallProbeLength);
  }
#endif
}