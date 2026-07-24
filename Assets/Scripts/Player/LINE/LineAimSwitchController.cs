using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Replaces LineVisionController. This component does not own or move the camera at all — it
/// just reads wherever the camera (aimCameraTransform, or Camera.main if left empty) is
/// currently pointed (AimOrigin/AimDirection) and checks whether that's close to a LinePath. On
/// confirm, hand off to LineSwitcher exactly as before.
///
/// Deliberately does NOT touch the player's position or rotation, and does NOT touch the
/// camera's position or rotation — this is purely a read-only consumer of wherever the camera
/// already is, so it never fights whatever script (if any) is driving the camera.
/// </summary>
[RequireComponent(typeof(LineSwitcher))]
[RequireComponent(typeof(LineFollowController))]
public class LineAimSwitchController : MonoBehaviour {
  [Header("References")]
  [Tooltip("Camera used for aiming (position + forward). Defaults to Camera.main if left empty.")]
  public Transform aimCameraTransform;

  public LineSwitcher lineSwitcher;
  public LineFollowController followController;

  [Header("Aiming")]
  [Tooltip("Max distance along the camera's look direction searched for a candidate line.")]
  public float maxAimDistance = 25f;

  [Tooltip("How far apart (world units) sample points are taken along the look direction. Smaller = more accurate, more expensive.")]
  public float aimSampleStep = 0.25f;

  [Tooltip("Max distance from the look direction a line may be and still count as aimed-at.")]
  public float aimRadius = 0.75f;

  [Header("Input")]
  [Tooltip("If true, locks and hides the OS cursor while aiming, so mouse delta reads cleanly.")]
  public bool lockCursorWhileAiming = true;

  [Header("Aim Line")]
  [Tooltip("LineRenderer used to draw the aim line. Auto-created if left empty.")]
  public LineRenderer aimLine;
  public Color validAimColor = Color.green;
  public Color invalidAimColor = Color.red;
  public float aimLineWidth = 0.03f;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logAimHits = true;

  private bool _isAiming;
  private bool _isSwitching;

  private bool _hasAimHit;
  private LinePath _aimLinePath;
  private int _aimStrand;
  private Vector3 _aimPoint;
  private float _aimDistance;
  private bool _aimValid;

  private CursorLockMode _prevLockState;
  private bool _prevCursorVisible;

  public bool IsAiming => _isAiming;

  /// <summary>Fired when aiming begins.</summary>
  public event Action AimStarted;

  /// <summary>Fired whenever aiming visually stops — either cancelled with no switch, or right after a switch's move finishes.</summary>
  public event Action AimEnded;

  /// <summary>Fired the instant a confirmed switch starts moving, with (player position before the move, aimed target point) — for systems that care what the switch's path crosses (e.g. TakedownController).</summary>
  public event Action<Vector3, Vector3> SwitchStarted;

  /// <summary>Fired when a switch's move finishes, before AimEnded.</summary>
  public event Action SwitchFinished;

  private void Awake() {
    if (lineSwitcher == null) lineSwitcher = GetComponent<LineSwitcher>();
    if (followController == null) followController = GetComponent<LineFollowController>();

    if (aimCameraTransform == null && Camera.main != null) aimCameraTransform = Camera.main.transform;

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
    if (value.isPressed) BeginAim();
    else EndAim();
  }

  private void OnConfirm(InputValue value) {
    if (value.isPressed) TryConfirmSwitch();
  }
#pragma warning restore IDE0051

  public void BeginAim() {
    if (!enabled || _isAiming || _isSwitching || lineSwitcher == null || lineSwitcher.IsSwitching
        || aimCameraTransform == null) {
      return;
    }

    _isAiming = true;

    if (followController != null) followController.movementEnabled = false;
    aimLine.enabled = true;

    if (lockCursorWhileAiming) {
      _prevLockState = Cursor.lockState;
      _prevCursorVisible = Cursor.visible;
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }

    AimStarted?.Invoke();
  }

  public void EndAim() {
    if (!_isAiming || _isSwitching) {
      return; // let an in-progress switch finish and call EndAim itself via OnSwitchMoveComplete
    }

    _isAiming = false;
    _hasAimHit = false;
    _aimValid = false;

    aimLine.enabled = false;

    if (followController != null) followController.movementEnabled = true;

    if (lockCursorWhileAiming) {
      Cursor.lockState = _prevLockState;
      Cursor.visible = _prevCursorVisible;
    }

    AimEnded?.Invoke();
  }

  public bool TryConfirmSwitch() {
    if (lineSwitcher == null || !_isAiming || !_aimValid) {
      if (logAimHits) Debug.Log($"[LineAimSwitchController] Confirm denied. isAiming={_isAiming} aimValid={_aimValid}");
      return false;
    }

    aimLine.enabled = false;
    Vector3 fromPosition = transform.position;
    _isSwitching = true;

    var started = lineSwitcher.TrySwitchToLine(_aimLinePath, _aimStrand, _aimPoint, _aimDistance, OnSwitchMoveComplete);
    if (!started) {
      aimLine.enabled = true;
      _isSwitching = false;
      return false;
    }

    SwitchStarted?.Invoke(fromPosition, _aimPoint);
    return true;
  }

  private void Update() {
    if (_isAiming && !_isSwitching) {
      UpdateAim();
    }
  }

  private void UpdateAim() {
    if (aimCameraTransform == null) return;

    Vector3 origin = aimCameraTransform.position;
    Vector3 direction = aimCameraTransform.forward;

    var wasValid = _aimValid;
    _hasAimHit = false;
    _aimValid = false;
    float bestDist = float.MaxValue;

    int sampleCount = Mathf.Max(4, Mathf.CeilToInt(maxAimDistance / Mathf.Max(0.05f, aimSampleStep)));
    for (int s = 1; s <= sampleCount; s++) {
      Vector3 samplePos = origin + direction * (s * aimSampleStep);

      foreach (var line in LinePath.All) {
        if (line == null || !line.isActiveAndEnabled) continue;

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

    // The SAMPLING above stays camera-based (origin/direction) since that's what the player is
    // actually looking at. The drawn line is purely visual and reads better anchored to the
    // player rather than floating from the camera, so it starts at the player instead.
    Vector3 lineOrigin = followController != null ? followController.transform.position : origin;

    aimLine.enabled = true;
    Vector3 endPoint = _hasAimHit ? _aimPoint : lineOrigin + direction * maxAimDistance;
    aimLine.SetPosition(0, lineOrigin);
    aimLine.SetPosition(1, endPoint);

    var color = _aimValid ? validAimColor : invalidAimColor;
    aimLine.startColor = color;
    aimLine.endColor = color;

    if (logAimHits && _aimValid != wasValid) {
      Debug.Log(_aimValid
        ? $"[LineAimSwitchController] Aiming at '{_aimLinePath.name}' strand={_aimStrand} point={_aimPoint:F2}"
        : "[LineAimSwitchController] No valid line within aimRadius.");
    }
  }

  private void OnSwitchMoveComplete() {
    _isSwitching = false;
    SwitchFinished?.Invoke();
    EndAim();
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || !_hasAimHit) return;
    Gizmos.color = _aimValid ? Color.green : Color.red;
    Gizmos.DrawSphere(_aimPoint, 0.05f);
  }
#endif
}
