using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the camera and input-facing side of a line switch: swinging the camera into
/// "vision mode", letting the player free-look with the mouse to aim at a candidate
/// LinePath, drawing the aim line to the closest point found, and confirming/cancelling
/// the switch. Delegates validation and player movement to LineSwitcher. Mirrors
/// WallVisionController.
///
/// Since LinePaths usually have no collider, aiming isn't a Physics.Raycast — instead this
/// samples points along the look direction and asks every LinePath in the scene how close it
/// passes to each sample (LinePath.FindClosestDistance), picking whichever line comes closest
/// within aimRadius.
///
/// Call from your input code:
///  - BeginVisionMode() on the vision key DOWN
///  - EndVisionMode() on the vision key UP (cancels if not yet confirmed)
///  - TryConfirmSwitch() on the confirm key DOWN, while vision is still held
/// </summary>
[RequireComponent(typeof(LineSwitcher))]
[RequireComponent(typeof(LineFollowController))]
public class LineVisionController : MonoBehaviour {
  [Header("References")]
  [Tooltip("Pivot that rotates the camera around the player (defaults to Camera.main parent).")]
  public Transform camPivot;

  [Tooltip("Camera used for aiming (defaults to Camera.main).")]
  public Camera aimCamera;

  public LineSwitcher lineSwitcher;
  public LineFollowController followController;

  [Header("Aiming")]
  [Tooltip("Max distance along the look direction searched for a candidate line.")]
  public float maxAimDistance = 25f;

  [Tooltip("How far apart (world units) sample points are taken along the look direction. Smaller = more accurate, more expensive.")]
  public float aimSampleStep = 0.25f;

  [Tooltip("Max distance from the look direction a line may be and still count as aimed-at.")]
  public float aimRadius = 0.75f;

  [Tooltip("Height above the player's feet the aim ray originates from.")]
  public float aimOriginHeight = 1.2f;

  [Header("Vision Mode")]
  public float visionEnterDuration = 0.3f;
  public float visionExitDuration = 0.25f;

  [Header("Mouse Look (while aiming)")]
  public float mouseSensitivity = 0.2f;
  public bool invertY = false;
  public float minPitch = -60f;
  public float maxPitch = 60f;
  public bool lockCursorDuringVision = true;

  [Header("Aim Line")]
  [Tooltip("LineRenderer used to draw the aim line. Auto-created if left empty.")]
  public LineRenderer aimLine;
  public Color validAimColor = Color.green;
  public Color invalidAimColor = Color.red;
  public float aimLineWidth = 0.03f;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logAimHits = true;

  private enum State { Normal, EnteringVision, Aiming, Switching, ExitingVision }

  private State _state = State.Normal;
  private float _normalYaw;
  private float _visionYaw;
  private float _yaw;
  private float _pitch;

  private bool _hasAimHit;
  private LinePath _aimLinePath;
  private int _aimStrand;
  private Vector3 _aimPoint;
  private float _aimDistance;
  private bool _aimValid;

  private CursorLockMode _prevLockState;
  private bool _prevCursorVisible;

  private Vector3 _pinnedPlayerPosition;
  private Quaternion _pinnedPlayerRotation;

  private CharacterController _cc;
  private bool _ccWasEnabled;

  public bool IsBusy => _state != State.Normal;
  public bool IsAiming => _state == State.Aiming;

  private void Awake() {
    if (lineSwitcher == null) lineSwitcher = GetComponent<LineSwitcher>();
    if (followController == null) followController = GetComponent<LineFollowController>();
    _cc = GetComponent<CharacterController>();

    if (camPivot == null && Camera.main != null) {
      camPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
    }

    if (camPivot == transform) {
      Debug.LogWarning("[LineVisionController] camPivot is the SAME Transform as the player. Free-look during " +
                        "vision mode will rotate the player itself (and its CharacterController capsule) along with " +
                        "the camera, which can corrupt the player's position once movement resumes after a switch. " +
                        "Give the camera its own separate empty-GameObject pivot instead, parented wherever your " +
                        "camera currently is, and assign that here.", this);
    }

    if (aimCamera == null) aimCamera = Camera.main;

    if (aimLine == null) {
      var lineObj = new GameObject("LineSwitchAimLine");
      lineObj.transform.SetParent(transform, false);
      aimLine = lineObj.AddComponent<LineRenderer>();
      aimLine.positionCount = 2;
      aimLine.material = new Material(Shader.Find("Sprites/Default"));
      aimLine.widthMultiplier = aimLineWidth;
    }

    aimLine.enabled = false;
  }

#pragma warning disable IDE0051
  private void OnVision(InputValue value) {
    if (value.isPressed) BeginVisionMode();
    else EndVisionMode();
  }

  private void OnConfirm(InputValue value) {
    if (value.isPressed) TryConfirmSwitch();
  }
#pragma warning restore IDE0051

  public bool BeginVisionMode() {
    if (!enabled || _state != State.Normal || lineSwitcher == null || lineSwitcher.IsSwitching || camPivot == null) {
      return false;
    }

    _pinnedPlayerPosition = transform.position;
    _pinnedPlayerRotation = transform.rotation;

    if (followController != null) followController.movementEnabled = false;

    // Disabled for the ENTIRE vision/switch sequence, not just the position-lerp — otherwise,
    // if look-around ends up rotating the player transform (see the camPivot warning in Awake),
    // the CharacterController capsule can end up misaligned with geometry while nothing is
    // calling .Move() to notice. The moment movement resumes, Unity shoves the capsule back out
    // of whatever it's now overlapping, which is what corrupts the player's position after a switch.
    if (_cc != null) {
      _ccWasEnabled = _cc.enabled;
      _cc.enabled = false;
    }

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

  public void EndVisionMode() {
    if (_state != State.Aiming && _state != State.EnteringVision) return;
    StopAllCoroutines();
    StartCoroutine(ExitVisionRoutine());
  }

  public bool TryConfirmSwitch() {
    if (lineSwitcher == null || _state != State.Aiming || !_aimValid) {
      if (logAimHits) Debug.Log($"[LineVisionController] Confirm denied. state={_state} aimValid={_aimValid}");
      return false;
    }

    aimLine.enabled = false;
    _state = State.Switching;

    var started = lineSwitcher.TrySwitchToLine(_aimLinePath, _aimStrand, _aimPoint, _aimDistance, OnSwitchMoveComplete);
    if (!started) {
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

  private void UpdateMouseLook() {
    Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    if (delta.sqrMagnitude < 0.000001f) return;

    _yaw += delta.x * mouseSensitivity;
    _pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
    _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

    camPivot.eulerAngles = new Vector3(_pitch, _yaw, 0f);
    camPivot.position = transform.position;
  }

  /// <summary>
  /// Samples points along the camera's look direction and finds the closest LinePath to any
  /// of them within aimRadius, updating the drawn aim line and cached switch target.
  /// </summary>
  private void UpdateAim() {
    Vector3 origin = transform.position + Vector3.up * aimOriginHeight;
    Vector3 direction = aimCamera != null ? aimCamera.transform.forward : camPivot.forward;

    var wasValid = _aimValid;
    _hasAimHit = false;
    _aimValid = false;
    float bestDist = float.MaxValue;

    int sampleCount = Mathf.Max(4, Mathf.CeilToInt(maxAimDistance / Mathf.Max(0.05f, aimSampleStep)));
    for (int s = 1; s <= sampleCount; s++) {
      Vector3 samplePos = origin + direction * (s * aimSampleStep);

      foreach (var line in LinePath.All) {
        if (line == null || !line.isActiveAndEnabled) continue;

        // Searches every strand on this LinePath at once (including disjoint ones) and
        // reports which strand actually produced the closest point.
        var distAlong = line.FindClosestDistance(samplePos, out Vector3 cp, out float distToLine, out int strand);
        if (strand < 0 || distToLine > aimRadius || distToLine >= bestDist) continue;
        if (!lineSwitcher.IsValidSwitchTarget(line, strand)) continue;

        bestDist = distToLine;
        _hasAimHit = true;
        _aimValid = true;
        _aimLinePath = line;
        _aimStrand = strand;
        _aimPoint = cp;
        _aimDistance = distAlong;
      }
    }

    aimLine.enabled = true;
    Vector3 endPoint = _hasAimHit ? _aimPoint : origin + direction * maxAimDistance;
    aimLine.SetPosition(0, origin);
    aimLine.SetPosition(1, endPoint);

    var color = _aimValid ? validAimColor : invalidAimColor;
    aimLine.startColor = color;
    aimLine.endColor = color;

    if (logAimHits && _aimValid != wasValid) {
      Debug.Log(_aimValid
        ? $"[LineVisionController] Aiming at '{_aimLinePath.name}' point={_aimPoint:F2}"
        : "[LineVisionController] No valid line within aimRadius.");
    }
  }

  private void OnSwitchMoveComplete() {
    ReturnControl();
  }

  private void ReturnControl() {
    transform.rotation = _pinnedPlayerRotation;
    if (followController != null) followController.movementEnabled = true;

    if (_cc != null) {
      _cc.enabled = _ccWasEnabled;
    }

    _hasAimHit = false;
    _aimValid = false;
    _state = State.Normal;
  }

  private static float NormalizePitch(float rawPitch) {
    return rawPitch > 180f ? rawPitch - 360f : rawPitch;
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || !_hasAimHit) return;
    Gizmos.color = _aimValid ? Color.green : Color.red;
    Gizmos.DrawSphere(_aimPoint, 0.05f);
  }
#endif
}
