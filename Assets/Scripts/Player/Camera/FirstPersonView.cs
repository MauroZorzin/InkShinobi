using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the camera while active: moves it to a first-person position inside the player, takes
/// exclusive control of look direction from mouse delta, and disables OrbitCameraController for
/// the duration so the two don't fight over the same Transform. AimSwitch calls Enter()/Exit()
/// instead of moving the camera itself.
/// </summary>
[RequireComponent(typeof(LineFollowController))]
public class FirstPersonView : MonoBehaviour {
  [Header("References")]
  [Tooltip("Camera moved into first-person view. Defaults to Camera.main if left empty.")]
  public Camera viewCamera;

  public LineFollowController followController;

  [Tooltip("Disabled while this view is active, re-enabled on exit. Defaults to viewCamera's own OrbitCameraController if left empty.")]
  public OrbitCameraController orbitCameraController;

  [Header("Camera")]
  [Tooltip("Local position (relative to the player) the camera moves to, e.g. Vector3.zero to place it inside the player's body.")]
  public Vector3 viewLocalPosition = Vector3.zero;

  [Tooltip("How fast the camera moves to/from the first-person position when entering/exiting.")]
  public float cameraMoveSpeed = 10f;

  [Header("Look")]
  [Tooltip("Degrees of yaw/pitch applied per pixel of mouse delta.")]
  public Vector2 lookSensitivity = new Vector2(0.15f, 0.15f);

  [Tooltip("Clamps how far the camera can pitch up/down, in degrees. X = down limit, Y = up limit.")]
  public Vector2 pitchLimits = new Vector2(-60f, 60f);

  public bool invertY = false;

  [Header("Cursor")]
  [Tooltip("If true, locks and hides the OS cursor while active, restoring the previous state on exit.")]
  public bool lockCursorWhileActive = true;

  private bool _isActive;
  private float _yaw;
  private float _pitch;

  private Coroutine _moveRoutine;
  private Vector3 _returnLocalPosition;
  private Quaternion _returnLocalRotation;
  private bool _wasOrbitEnabled;

  // True from the first Enter() until MoveOutOfView() actually finishes (not just starts) —
  // guards the return-state capture below so a second Enter(), fired while still mid-exit
  // transition, doesn't re-capture from an in-flight (not-at-rest) camera position/rotation.
  private bool _hasStoredReturnState;

  private CursorLockMode _prevLockState;
  private bool _prevCursorVisible;

  public bool IsActive => _isActive;

  /// <summary>Fired once the camera has finished moving into first-person position.</summary>
  public event System.Action Entered;

  /// <summary>Fired once the camera has finished returning to its previous position.</summary>
  public event System.Action Exited;

  private void Awake() {
    if (followController == null) followController = GetComponent<LineFollowController>();
    if (viewCamera == null) viewCamera = Camera.main;
    if (orbitCameraController == null && viewCamera != null) orbitCameraController = viewCamera.GetComponent<OrbitCameraController>();
  }

  public void Enter() {
    if (_isActive || viewCamera == null) {
      return;
    }

    _isActive = true;

    if (followController != null) {
      followController.movementEnabled = false;
    }

    if (!_hasStoredReturnState) {
      if (orbitCameraController != null) {
        _wasOrbitEnabled = orbitCameraController.orbitEnabled;
      }

      Transform pivot = followController != null ? followController.transform : transform;
      _returnLocalPosition = pivot.InverseTransformPoint(viewCamera.transform.position);
      _returnLocalRotation = Quaternion.Inverse(pivot.rotation) * viewCamera.transform.rotation;

      _hasStoredReturnState = true;
    }

    if (orbitCameraController != null) {
      orbitCameraController.orbitEnabled = false;
    }

    Vector3 startEuler = viewCamera.transform.eulerAngles;
    _yaw = startEuler.y;
    _pitch = startEuler.x > 180f ? startEuler.x - 360f : startEuler.x;

    if (_moveRoutine != null) StopCoroutine(_moveRoutine);
    _moveRoutine = StartCoroutine(MoveIntoView());

    if (lockCursorWhileActive) {
      _prevLockState = Cursor.lockState;
      _prevCursorVisible = Cursor.visible;
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }
  }

  public void Exit() {
    if (!_isActive) {
      return;
    }

    _isActive = false;

    if (followController != null) {
      followController.movementEnabled = true;
    }

    if (orbitCameraController != null) {
      orbitCameraController.orbitEnabled = _wasOrbitEnabled;
      orbitCameraController.SyncFromCurrentTransform();
    }

    if (_moveRoutine != null) StopCoroutine(_moveRoutine);
    _moveRoutine = StartCoroutine(MoveOutOfView());

    if (lockCursorWhileActive) {
      Cursor.lockState = _prevLockState;
      Cursor.visible = _prevCursorVisible;
    }
  }

  private void LateUpdate() {
    if (!_isActive || Mouse.current == null) {
      return;
    }

    Vector2 delta = Mouse.current.delta.ReadValue();
    float verticalInput = invertY ? delta.y : -delta.y;
    _yaw += delta.x * lookSensitivity.x;
    _pitch = Mathf.Clamp(_pitch + verticalInput * lookSensitivity.y, pitchLimits.x, pitchLimits.y);

    viewCamera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
  }

  private IEnumerator MoveIntoView() {
    Transform pivot = followController != null ? followController.transform : transform;

    while (true) {
      Vector3 targetPos = pivot.TransformPoint(viewLocalPosition);

      if (Vector3.Distance(viewCamera.transform.position, targetPos) < 0.001f) {
        viewCamera.transform.position = targetPos;
        break;
      }

      viewCamera.transform.position = Vector3.Lerp(viewCamera.transform.position, targetPos, cameraMoveSpeed * Time.deltaTime);
      yield return null;
    }

    _moveRoutine = null;
    Entered?.Invoke();
  }

  private IEnumerator MoveOutOfView() {
    Transform pivot = followController != null ? followController.transform : transform;

    while (true) {
      Vector3 targetPos = pivot.TransformPoint(_returnLocalPosition);
      Quaternion targetRot = pivot.rotation * _returnLocalRotation;

      if (Vector3.Distance(viewCamera.transform.position, targetPos) < 0.001f
          && Quaternion.Angle(viewCamera.transform.rotation, targetRot) < 0.05f) {
        viewCamera.transform.SetPositionAndRotation(targetPos, targetRot);
        break;
      }

      viewCamera.transform.SetPositionAndRotation(
        Vector3.Lerp(viewCamera.transform.position, targetPos, cameraMoveSpeed * Time.deltaTime),
        Quaternion.Slerp(viewCamera.transform.rotation, targetRot, cameraMoveSpeed * Time.deltaTime));
      yield return null;
    }

    _moveRoutine = null;
    _hasStoredReturnState = false;
    Exited?.Invoke();
  }
}
