using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player orbit the camera around this transform at a fixed distance, always facing it.
/// Attach to the PLAYER (same GameObject as the PlayerInput component) — PlayerInput's Send
/// Messages behavior only calls methods on components on its own GameObject, so this can't live
/// on the camera itself. Nothing else — no zoom, no collision avoidance, no smoothing.
/// </summary>
public class OrbitCameraController : MonoBehaviour {
  [Header("Camera")]
  [Tooltip("The camera being orbited. Defaults to Camera.main if left empty.")]
  public Transform cameraTransform;

  [Tooltip("Local offset from this transform's position that is actually orbited/looked at, e.g. chest height instead of the feet.")]
  public Vector3 pivotOffset = Vector3.up * 1.5f;

  [Header("Orbit")]
  [Tooltip("Degrees of yaw/pitch applied per unit of mouse delta.")]
  public Vector2 sensitivity = new Vector2(0.15f, 0.15f);

  [Tooltip("Clamps how far the camera can pitch above/below the pivot, in degrees. X = down limit, Y = up limit.")]
  public Vector2 pitchLimits = new Vector2(-40f, 80f);

  [Tooltip("Invert the vertical look axis.")]
  public bool invertY = false;

  private float _distance;
  private float _yaw;
  private float _pitch;
  private Vector2 _lookInput;

  private void Awake() {
    if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
  }

  private void Start() {
    if (cameraTransform == null) return;

    // Derive the starting distance/yaw/pitch from wherever the camera was placed in the scene,
    // so play mode doesn't snap it to some arbitrary default orbit position.
    Vector3 fromPivot = cameraTransform.position - (transform.position + pivotOffset);
    if (fromPivot.sqrMagnitude > 0.0001f) {
      Vector3 euler = Quaternion.LookRotation(-fromPivot.normalized, Vector3.up).eulerAngles;
      _yaw = euler.y;
      _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
      _distance = fromPivot.magnitude;
    }
  }

#pragma warning disable IDE0051
  // Wired to the "Look" Input Action (<Mouse>/delta) the same way LineFollowController wires
  // OnMove/OnJump — PlayerInput's Send Messages behavior calls this automatically.
  private void OnLook(InputValue value) {
    _lookInput = value.Get<Vector2>();
  }
#pragma warning restore IDE0051

  private void LateUpdate() {
    if (cameraTransform == null) return;

    // Mouse delta is already a per-frame amount, so it's applied directly — not scaled by
    // Time.deltaTime, which would double up frame-time scaling.
    float verticalInput = invertY ? _lookInput.y : -_lookInput.y;
    _yaw += _lookInput.x * sensitivity.x;
    _pitch = Mathf.Clamp(_pitch + verticalInput * sensitivity.y, pitchLimits.x, pitchLimits.y);

    Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    Vector3 pivot = transform.position + pivotOffset;

    cameraTransform.SetPositionAndRotation(pivot - rotation * Vector3.forward * _distance, rotation);
  }
}
