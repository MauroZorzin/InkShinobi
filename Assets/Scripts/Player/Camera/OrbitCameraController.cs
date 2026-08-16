using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player orbit this camera around a target at a fixed distance, always facing it.
/// Attach directly to the camera. Reads the mouse directly (Mouse.current) instead of going
/// through a PlayerInput action, so it works regardless of where any PlayerInput component lives
/// or how its action maps are set up. Nothing else — no zoom, no collision avoidance, no smoothing.
/// </summary>
public class OrbitCameraController : MonoBehaviour {
  [Header("Target")]
  [Tooltip("The point orbited around. Defaults to the GameObject tagged \"Player\" if left empty.")]
  public Transform target;

  [Tooltip("Local offset from target's position that is actually orbited/looked at, e.g. chest height instead of the feet.")]
  public Vector3 pivotOffset = Vector3.up * 1.5f;

  [Header("Orbit")]
  [Tooltip("Degrees of yaw/pitch applied per pixel of mouse delta.")]
  public Vector2 sensitivity = new Vector2(0.15f, 0.15f);

  [Tooltip("Clamps how far the camera can pitch above/below the pivot, in degrees. X = down limit, Y = up limit.")]
  public Vector2 pitchLimits = new Vector2(-40f, 80f);

  [Tooltip("Invert the vertical look axis.")]
  public bool invertY = false;

  public bool orbitEnabled = true;

  private float _distance;
  private float _yaw;
  private float _pitch;

  private void Awake() {
    if (target == null) {
      var player = GameObject.FindGameObjectWithTag("Player");
      if (player != null) target = player.transform;
    }
  }

  private void Start() {
    if (target == null) {
      Debug.LogWarning("[OrbitCameraController] No target assigned and no GameObject tagged \"Player\" found — orbit disabled.");
      return;
    }

    SyncFromCurrentTransform();
  }

  public void SyncFromCurrentTransform() {
    if (target == null) return;

    Vector3 fromPivot = transform.position - (target.position + pivotOffset);
    if (fromPivot.sqrMagnitude > 0.0001f) {
      Vector3 euler = Quaternion.LookRotation(-fromPivot.normalized, Vector3.up).eulerAngles;
      _yaw = euler.y;
      _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
      _distance = fromPivot.magnitude;
    }
  }

  private void LateUpdate() {
    if (!orbitEnabled || target == null || Mouse.current == null) return;

    Vector2 lookInput = Mouse.current.delta.ReadValue();

    float verticalInput = invertY ? lookInput.y : -lookInput.y;
    _yaw += lookInput.x * sensitivity.x;
    _pitch = Mathf.Clamp(_pitch + verticalInput * sensitivity.y, pitchLimits.x, pitchLimits.y);

    Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    Vector3 pivot = target.position + pivotOffset;

    transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * _distance, rotation);
  }
}
