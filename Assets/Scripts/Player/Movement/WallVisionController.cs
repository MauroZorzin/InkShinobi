using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the camera and input-facing side of a wall switch: swinging the camera into
/// "vision mode" around the player, letting the player free-look with the mouse to aim
/// at a candidate wall, drawing the aim line, and confirming/cancelling the switch.
/// Delegates the actual validation and player movement to WallSwitcher.
///
/// Call from your input code:
///  - BeginVisionMode() on the vision key DOWN
///  - EndVisionMode() on the vision key UP (cancels if not yet confirmed)
///  - TryConfirmSwitch() on the confirm key DOWN, while vision is still held
/// </summary>
[RequireComponent(typeof(WallSwitcher))]
public class WallVisionController : MonoBehaviour {
  [Header("References")]
  [Tooltip("Pivot that rotates the camera around the player (defaults to Camera.main parent).")]
  public Transform camPivot;

  [Tooltip("Camera used for aiming the switch-target line (defaults to Camera.main).")]
  public Camera aimCamera;

  [Tooltip("Logic component that validates and performs the actual wall switch.")]
  public WallSwitcher wallSwitcher;

  [Tooltip("Movement controller to disable during vision mode / a switch.")]
  public PlayerMovementController movementController;

  [Tooltip("Corner turn script to pause while vision mode / a switch is running.")]
  public RightAngleWallTurner rightAngleWallTurner;

  [Header("Wall Detection")]
  [Tooltip("Layer(s) considered valid switch targets by the aim ray.")]
  public LayerMask wallLayer;

  [Header("Vision Mode")]
  [Tooltip("Seconds spent swinging the camera 180 degrees into vision mode.")]
  public float visionEnterDuration = 0.3f;

  [Tooltip("Seconds spent swinging the camera back when vision mode is cancelled.")]
  public float visionExitDuration = 0.25f;

  [Tooltip("Max distance the aim line will search for a candidate wall.")]
  public float maxAimDistance = 25f;

  [Tooltip("Height above the player's feet the aim ray originates from (roughly eye/chest height).")]
  public float aimOriginHeight = 1.2f;

  [Header("Mouse Look (while aiming)")]
  [Tooltip("Degrees of camera rotation per unit of mouse delta.")]
  public float mouseSensitivity = 0.2f;

  [Tooltip("Inverts vertical mouse look.")]
  public bool invertY = false;

  [Tooltip("Minimum (most-downward) camera pitch in degrees while aiming.")]
  public float minPitch = -60f;

  [Tooltip("Maximum (most-upward) camera pitch in degrees while aiming.")]
  public float maxPitch = 60f;

  [Tooltip("Locks and hides the cursor while aiming, like a standard mouse-look camera.")]
  public bool lockCursorDuringVision = true;

  [Header("Aim Line")]
  [Tooltip("LineRenderer used to draw the aim line. Auto-created if left empty.")]
  public LineRenderer aimLine;
  public Color validAimColor = Color.green;
  public Color invalidAimColor = Color.red;
  public float aimLineWidth = 0.03f;

  [Header("Switch Camera")]
  [Tooltip("Seconds spent rotating the camera to settle facing the new wall after a confirmed switch.")]
  public float finalRotationDuration = 0.25f;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logRayHits = true;

  private enum State { Normal, EnteringVision, Aiming, Switching, ExitingVision }

  private State _state = State.Normal;
  private float _normalYaw;
  private float _visionYaw;
  private float _yaw;
  private float _pitch;

  private bool _hasAimHit;
  private RaycastHit _aimHit;
  private bool _aimValid;

  private CursorLockMode _prevLockState;
  private bool _prevCursorVisible;

  private Vector3 _pinnedPlayerPosition;
  private Quaternion _pinnedPlayerRotation;

  /// <summary>True while any part of the vision/switch sequence owns camera and movement control.</summary>
  public bool IsBusy => _state != State.Normal;

  /// <summary>True once the camera has finished swinging into vision mode and free-look aiming is active.</summary>
  public bool IsAiming => _state == State.Aiming;

  private void Awake() {
    if (wallSwitcher == null) {
      wallSwitcher = GetComponent<WallSwitcher>();
    }

    if (movementController == null) {
      movementController = GetComponent<PlayerMovementController>();
    }

    if (rightAngleWallTurner == null) {
      rightAngleWallTurner = GetComponent<RightAngleWallTurner>();
    }

    if (camPivot == null && Camera.main != null) {
      camPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
    }

    if (aimCamera == null) {
      aimCamera = Camera.main;
    }

    if (aimLine == null) {
      var lineObj = new GameObject("WallSwitchAimLine");
      lineObj.transform.SetParent(transform, false);
      aimLine = lineObj.AddComponent<LineRenderer>();
      aimLine.positionCount = 2;
      aimLine.material = new Material(Shader.Find("Sprites/Default"));
      aimLine.widthMultiplier = aimLineWidth;
    }

    aimLine.enabled = false;
  }

  /// <summary>
  /// Call on vision-key DOWN. Starts swinging the camera 180 degrees around the player;
  /// once the swing finishes, mouse movement takes over to freely aim at any wall.
  /// </summary>
  /// <returns>True if vision mode was started.</returns>
  public bool BeginVisionMode() {
    if (logRayHits) Debug.Log($"[WallVisionController] BeginVisionMode() called. state={_state} enabled={enabled} wallSwitcherBusy={(wallSwitcher != null && wallSwitcher.IsSwitching)}");

    if (!enabled || _state != State.Normal || wallSwitcher == null || wallSwitcher.IsSwitching) {
      return false;
    }

    if (movementController != null && movementController.IsRotating()) {
      if (logRayHits) Debug.Log("[WallVisionController] Vision denied: movement controller is rotating.");
      return false;
    }

    if (rightAngleWallTurner != null && rightAngleWallTurner.IsTurning) {
      if (logRayHits) Debug.Log("[WallVisionController] Vision denied: right-angle turn in progress.");
      return false;
    }

    if (camPivot == null) {
      if (logRayHits) Debug.Log("[WallVisionController] Vision denied: no camPivot available.");
      return false;
    }

    wallSwitcher.RefreshCurrentWall();

    _pinnedPlayerPosition = transform.position;
    _pinnedPlayerRotation = transform.rotation;

    if (movementController != null) movementController.enabled = false;
    if (rightAngleWallTurner != null) rightAngleWallTurner.enabled = false;

    _normalYaw = camPivot.eulerAngles.y;
    _visionYaw = _normalYaw + 180f;
    _pitch = NormalizePitch(camPivot.eulerAngles.x);

    if (lockCursorDuringVision) {
      _prevLockState = Cursor.lockState;
      _prevCursorVisible = Cursor.visible;
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }

    aimLine.enabled = true;

    StopAllCoroutines();
    StartCoroutine(EnterVisionRoutine());
    return true;
  }

  /// <summary>
  /// Call on vision-key UP. Cancels and swings the camera back if the switch hasn't
  /// been confirmed yet. No-op once a confirmed switch is already animating.
  /// </summary>
  public void EndVisionMode() {
    if (_state != State.Aiming && _state != State.EnteringVision) {
      return; // already switching/exiting, or already back to normal
    }

    StopAllCoroutines();
    StartCoroutine(ExitVisionRoutine());
  }

  /// <summary>
  /// Call on confirm-key DOWN while vision mode is held. Commits the switch if a
  /// valid wall is currently aimed at.
  /// </summary>
  /// <returns>True if a switch was started.</returns>
  public bool TryConfirmSwitch() {
    if (wallSwitcher == null) {
      if (logRayHits) Debug.Log("[WallVisionController] Confirm denied: no WallSwitcher reference.");
      return false;
    }

    if (_state != State.Aiming) {
      if (logRayHits) Debug.Log($"[WallVisionController] Confirm denied: not currently aiming (state={_state}). " +
                                 "This usually means BeginVisionMode()/the vision key-down was never registered, " +
                                 "or the camera swing into vision mode hasn't finished yet.");
      return false;
    }

    if (!_aimValid) {
      if (logRayHits) Debug.Log($"[WallVisionController] Confirm denied: current aim is not a valid target " +
                                 $"(hasHit={_hasAimHit}). Point the line at a green target before confirming.");
      return false;
    }

    aimLine.enabled = false;
    _state = State.Switching;

    var startedSwitch = wallSwitcher.TrySwitchToWall(_aimHit, OnSwitchMoveComplete);
    if (!startedSwitch) {
      // Target became invalid between last aim update and confirm (e.g. cooldown) — bail back to aiming.
      if (logRayHits) Debug.Log("[WallVisionController] Confirm denied: WallSwitcher.TrySwitchToWall rejected the target (cooldown or already switching?).");
      aimLine.enabled = true;
      _state = State.Aiming;
      return false;
    }

    return true;
  }

  private void Update() {
    if (_state == State.Aiming) {
      UpdateMouseLook();
      UpdateAim();
    }
  }

  private void LateUpdate() {
    // The camera orbits the player during vision mode. Only POSITION is pinned here —
    // rotation is intentionally left alone, because in setups where camPivot is the same
    // Transform as the player (camera parented directly under the player, no separate
    // pivot object), pinning rotation here would fight the camera's own free-look rotation.
    // Character facing is handled by SpriteRenderer.flipX in PlayerMovementController, not
    // by transform.rotation, so leaving rotation unpinned does not affect how the character
    // visually appears — it only lets the camera actually turn.
    //
    // If camPivot is a SEPARATE object from the player in your hierarchy, this is a non-issue
    // either way. If it is the SAME object, be aware that -transform.up (used by
    // WallSwitcher.RefreshCurrentWall to probe the current wall) will also rotate along with
    // the camera during vision mode — worth giving the camera its own dedicated pivot object
    // if that ever causes wall-detection to misbehave.
    if (_state == State.EnteringVision || _state == State.Aiming || _state == State.ExitingVision) {
      transform.position = _pinnedPlayerPosition;
    }
  }

  private IEnumerator EnterVisionRoutine() {
    _state = State.EnteringVision;

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, visionEnterDuration);
    var startYaw = camPivot.eulerAngles.y;
    var startPitch = NormalizePitch(camPivot.eulerAngles.x);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      camPivot.eulerAngles = new Vector3(Mathf.LerpAngle(startPitch, 0f, t), Mathf.LerpAngle(startYaw, _visionYaw, t), 0f);
      camPivot.position = transform.position;
      yield return null;
    }

    _yaw = _visionYaw;
    _pitch = 0f;
    camPivot.eulerAngles = new Vector3(_pitch, _yaw, 0f);
    camPivot.position = transform.position;

    _state = State.Aiming;
  }

  private IEnumerator ExitVisionRoutine() {
    _state = State.ExitingVision;
    aimLine.enabled = false;

    if (lockCursorDuringVision) {
      Cursor.lockState = _prevLockState;
      Cursor.visible = _prevCursorVisible;
    }

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, visionExitDuration);
    var startYaw = camPivot.eulerAngles.y;
    var startPitch = NormalizePitch(camPivot.eulerAngles.x);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      camPivot.eulerAngles = new Vector3(Mathf.LerpAngle(startPitch, 0f, t), Mathf.LerpAngle(startYaw, _normalYaw, t), 0f);
      camPivot.position = transform.position;
      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, _normalYaw, 0f);
    camPivot.position = transform.position;

    ReturnControl();
  }

  [Tooltip("If true, falls back to the legacy Input.GetAxis mouse look when the new Input System's Mouse.current is unavailable (e.g. Active Input Handling set to 'Input Manager (Old)' only).")]
  public bool useLegacyMouseFallback = true;

  private bool _warnedNoMouse;

  /// <summary>
  /// Reads mouse delta and orbits the camera pivot around the player, clamping pitch.
  /// The player's own transform is never touched here — see LateUpdate, which pins it
  /// every frame during vision mode regardless of how much the camera rotates.
  /// </summary>
  private void UpdateMouseLook() {
    Vector2 delta = Vector2.zero;

    if (Mouse.current != null) {
      delta = Mouse.current.delta.ReadValue();
    } else if (useLegacyMouseFallback) {
      delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f; // rough parity with Mouse.current.delta scale
      if (!_warnedNoMouse && logRayHits) {
        Debug.LogWarning("[WallVisionController] Mouse.current is null (new Input System not seeing the mouse) — " +
                          "falling back to legacy Input.GetAxis. Check Edit > Project Settings > Player > Active Input Handling " +
                          "is set to 'Both' or 'Input System Package' if you want camera rotation to work via the new system.");
        _warnedNoMouse = true;
      }
    } else if (!_warnedNoMouse && logRayHits) {
      Debug.LogWarning("[WallVisionController] Mouse.current is null and useLegacyMouseFallback is off — camera will not rotate.");
      _warnedNoMouse = true;
    }

    if (delta.sqrMagnitude < 0.000001f) {
      return;
    }

    _yaw += delta.x * mouseSensitivity;
    _pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
    _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

    camPivot.eulerAngles = new Vector3(_pitch, _yaw, 0f);
    camPivot.position = transform.position;
  }

  /// <summary>
  /// Casts the aim ray from the PLAYER (not the camera) along the camera's current look
  /// direction every frame while in Aiming state, updates the line renderer, and records
  /// the current hit for TryConfirmSwitch.
  /// </summary>
  private void UpdateAim() {
    if (wallSwitcher == null || camPivot == null) {
      _hasAimHit = false;
      _aimValid = false;
      aimLine.enabled = false;
      return;
    }

    Vector3 origin = transform.position + Vector3.up * aimOriginHeight;
    // Use the actual camera's forward (not camPivot's) so the line always matches what's
    // on screen, even if aimCamera has a local rotation offset from its pivot.
    Vector3 direction = aimCamera != null ? aimCamera.transform.forward : camPivot.forward;

    var wasValid = _aimValid;
    var hadHit = _hasAimHit;

    _hasAimHit = Physics.Raycast(origin, direction, out _aimHit, maxAimDistance, wallLayer, QueryTriggerInteraction.Ignore);
    _aimValid = _hasAimHit && wallSwitcher.IsValidSwitchTarget(_aimHit);

    aimLine.enabled = true;
    Vector3 endPoint = _hasAimHit ? _aimHit.point : origin + direction * maxAimDistance;
    aimLine.SetPosition(0, origin);
    aimLine.SetPosition(1, endPoint);

    var color = _aimValid ? validAimColor : invalidAimColor;
    aimLine.startColor = color;
    aimLine.endColor = color;

    if (logRayHits && (_hasAimHit != hadHit || _aimValid != wasValid)) {
      if (!_hasAimHit) {
        Debug.Log($"[WallVisionController] Aim: no hit within {maxAimDistance}m on wallLayer (mask={wallLayer.value}). " +
                  "If this never changes, check that Wall Layer is actually assigned in the Inspector.");
      } else {
        Debug.Log($"[WallVisionController] Aim hit '{_aimHit.collider.name}' valid={_aimValid} " +
                  $"(currentWall={(wallSwitcher.CurrentWallCollider != null ? wallSwitcher.CurrentWallCollider.name : "none")}) " +
                  $"point={_aimHit.point:F2}");
      }
    }
  }

  /// <summary>
  /// Called by WallSwitcher once the player has finished moving onto the target wall.
  /// Settles the camera to face the new wall, then returns control.
  /// </summary>
  /// <param name="targetWall">The wall the player just switched onto.</param>
  private void OnSwitchMoveComplete(RaycastHit targetWall) {
    StartCoroutine(SettleCameraRoutine());
  }

  private IEnumerator SettleCameraRoutine() {
    var holdYaw = camPivot.eulerAngles.y;
    var settleYaw = holdYaw - 180f; // face away from the wall again, i.e. behind the player

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, finalRotationDuration);
    var fromYaw = camPivot.eulerAngles.y;
    var fromPitch = NormalizePitch(camPivot.eulerAngles.x);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      camPivot.eulerAngles = new Vector3(Mathf.LerpAngle(fromPitch, 0f, t), Mathf.LerpAngle(fromYaw, settleYaw, t), 0f);
      camPivot.position = transform.position;
      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, settleYaw, 0f);
    camPivot.position = transform.position;
    _normalYaw = settleYaw;

    ReturnControl();
  }

  private void ReturnControl() {
    // Whatever the player's rotation did during aiming (free-look, sprite flip, etc.),
    // snap it back to exactly what it was the instant vision mode started. This runs once,
    // both on a cancelled vision mode and after a confirmed switch settles.
    transform.rotation = _pinnedPlayerRotation;

    if (movementController != null) movementController.enabled = true;
    if (rightAngleWallTurner != null) rightAngleWallTurner.enabled = true;

    _hasAimHit = false;
    _aimValid = false;
    _state = State.Normal;
  }

  /// <summary>Wraps a raw Unity euler-angle pitch (0-360) into a signed -180..180 range for lerping.</summary>
  private static float NormalizePitch(float rawPitch) {
    return rawPitch > 180f ? rawPitch - 360f : rawPitch;
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || !_hasAimHit) {
      return;
    }

    Gizmos.color = _aimValid ? Color.green : Color.red;
    Gizmos.DrawSphere(_aimHit.point, 0.05f);
    Gizmos.DrawRay(_aimHit.point, _aimHit.normal * 0.4f);
  }
#endif
}