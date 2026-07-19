using UnityEngine;

/// <summary>
/// Modular camera controller for 2.5D games where the player moves in 3D space
/// but the gameplay reads like 2D — including cases where the player's own
/// "up"/"forward" rotates (e.g. walking on walls, rotating gravity, etc).
///
/// Unlike a world-axis-locked camera, this rig follows the TARGET'S LOCAL AXES:
///  - target.forward  = depth (camera sits back along -forward, looks along +forward)
///  - target.right     = side/horizontal movement axis
///  - target.up        = vertical movement axis
/// As the player rotates, the camera rotates its reference frame with them,
/// so "behind and looking at the wall" always stays correct.
///
/// Features (each toggleable):
///  - Smooth follow with per-axis damping, all relative to target's local frame
///  - Look-ahead in the direction of local movement
///  - Vertical dead-zone (local "up" axis) to avoid jitter on small hops
///  - FOV speed kick
///  - Screen shake (via CameraShake component)
///  - Optional bounds clamp (in world space, for static levels)
///  - Smooth rotation matching (camera eases into the target's frame instead of snapping)
/// </summary>
[DisallowMultipleComponent]
public class Camera2_5DController : MonoBehaviour {
  [Header("Target")]
  [Tooltip("The transform the camera follows. Its local axes define the camera's reference frame.")]
  public Transform target;

  [Tooltip("Offset from the target in the TARGET'S LOCAL space (x = right, y = up, z = forward).")]
  public Vector3 localTargetOffset = new Vector3(0f, 1.5f, 0f);

  [Header("Depth (distance behind target along target.forward)")]
  public float depthDistance = 10f;
  public float zoomSmoothTime = 0.4f;

  [Header("Follow Smoothing (position, local space)")]
  [Tooltip("Time (seconds) for the camera to catch up on the target's local X (side) axis.")]
  public float horizontalSmoothTime = 0.15f;
  [Tooltip("Time (seconds) for the camera to catch up on the target's local Y (up) axis.")]
  public float verticalSmoothTime = 0.25f;
  [Tooltip("Max camera speed while smoothing (prevents huge teleport-catchups).")]
  public float maxFollowSpeed = 40f;

  [Header("Rotation Smoothing")]
  [Tooltip("If true, camera orientation eases toward the target's frame instead of snapping instantly. Important when the player rotates (e.g. walking around a corner onto another wall).")]
  public bool smoothRotation = true;
  [Tooltip("Degrees/second max turn speed when smoothRotation is enabled.")]
  public float rotationSpeedDegPerSec = 220f;

  [Header("Vertical Dead-Zone (local up axis)")]
  public bool useVerticalDeadZone = true;
  public float verticalDeadZoneSize = 1.0f;

  [Header("Look-Ahead")]
  [Tooltip("Push the camera in the direction of local movement to show more of what's ahead.")]
  public bool useLookAhead = true;
  public float lookAheadDistance = 2.5f;
  public float lookAheadSmoothTime = 0.3f;
  public float lookAheadMaxSpeed = 6f;

  [Header("FOV Speed Kick")]
  public bool useFovKick = true;
  public float baseFov = 60f;
  public float maxFovKick = 8f;
  public float fovKickMaxSpeed = 8f;
  public float fovSmoothTime = 0.3f;

  [Header("Bounds (optional, world space — for static levels only)")]
  public bool useBounds = false;
  public Bounds worldBounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));

  // --- internal state ---
  private Camera cam;
  private CameraShake shake;

  // local-space smoothed offsets (relative to target's frame)
  private float sideVel, upVel, depthVel, fovVel;
  private float currentSideLocal, currentUpLocal; // smoothed local x/y offset from target
  private float currentDepthDistance;

  private Vector3 lookAheadLocalOffset;
  private Vector3 lookAheadVelocity;

  private Vector3 lastTargetPos;
  private Vector3 targetVelocityWorld;

  private float lockedUpLocal;
  private bool verticalInitialized;

  private void Awake() {
    cam = GetComponent<Camera>();
    if (cam != null && cam.fieldOfView > 0f) baseFov = cam.fieldOfView;

    currentDepthDistance = depthDistance;

    shake = GetComponent<CameraShake>();
    if (shake == null) shake = gameObject.AddComponent<CameraShake>();

    if (target != null) {
      lastTargetPos = target.position;
      currentSideLocal = localTargetOffset.x;
      currentUpLocal = localTargetOffset.y;
      lockedUpLocal = localTargetOffset.y;
      verticalInitialized = true;
    }
  }

  private void LateUpdate() {
    if (target == null) return;
    float dt = Time.deltaTime;
    if (dt <= 0f) return;

    // --- Estimate target velocity in WORLD space, then convert to target's LOCAL space ---
    Vector3 rawVelWorld = (target.position - lastTargetPos) / dt;
    lastTargetPos = target.position;
    targetVelocityWorld = Vector3.Lerp(targetVelocityWorld, rawVelWorld, 0.25f);

    // Local velocity relative to the target's own rotation (so "right" is always target.right, etc.)
    Vector3 localVel = target.InverseTransformDirection(targetVelocityWorld);

    // --- Desired local offset (base offset + look-ahead) ---
    float desiredSideLocal = localTargetOffset.x;
    float desiredUpLocal = localTargetOffset.y;

    if (useLookAhead) {
      Vector2 horizLocalVel = new Vector2(localVel.x, localVel.y); // side + up movement, ignoring depth
      float speedFrac = Mathf.Clamp01(horizLocalVel.magnitude / Mathf.Max(0.01f, lookAheadMaxSpeed));
      Vector3 desiredLookAhead = horizLocalVel.magnitude > 0.05f
          ? (Vector3)(horizLocalVel.normalized * lookAheadDistance * speedFrac)
          : Vector3.zero;

      lookAheadLocalOffset = Vector3.SmoothDamp(lookAheadLocalOffset, desiredLookAhead, ref lookAheadVelocity, lookAheadSmoothTime);
      desiredSideLocal += lookAheadLocalOffset.x;
      desiredUpLocal += lookAheadLocalOffset.y;
    }

    // --- Vertical dead-zone (local up axis) ---
    if (useVerticalDeadZone) {
      if (!verticalInitialized) { lockedUpLocal = desiredUpLocal; verticalInitialized = true; }
      float diff = desiredUpLocal - lockedUpLocal;
      if (Mathf.Abs(diff) > verticalDeadZoneSize)
        lockedUpLocal += diff - Mathf.Sign(diff) * verticalDeadZoneSize;
      desiredUpLocal = lockedUpLocal;
    }

    // --- Smooth the local side/up offsets ---
    currentSideLocal = Mathf.SmoothDamp(currentSideLocal, desiredSideLocal, ref sideVel, horizontalSmoothTime, maxFollowSpeed);
    currentUpLocal = Mathf.SmoothDamp(currentUpLocal, desiredUpLocal, ref upVel, verticalSmoothTime, maxFollowSpeed);

    // --- Depth (zoom-capable) ---
    currentDepthDistance = Mathf.SmoothDamp(currentDepthDistance, depthDistance, ref depthVel, zoomSmoothTime);

    // --- Compose final world position from target's local frame ---
    Vector3 localOffset = new Vector3(currentSideLocal, currentUpLocal, localTargetOffset.z - currentDepthDistance);
    Vector3 desiredWorldPos = target.position
        + target.right * localOffset.x
        + target.up * localOffset.y
        + target.forward * localOffset.z;

    if (useBounds) {
      desiredWorldPos.x = Mathf.Clamp(desiredWorldPos.x, worldBounds.min.x, worldBounds.max.x);
      desiredWorldPos.y = Mathf.Clamp(desiredWorldPos.y, worldBounds.min.y, worldBounds.max.y);
      desiredWorldPos.z = Mathf.Clamp(desiredWorldPos.z, worldBounds.min.z, worldBounds.max.z);
    }

    Vector3 shakeOffset = shake != null ? shake.CurrentOffset : Vector3.zero;
    transform.position = desiredWorldPos + shakeOffset;

    // --- Orientation: look at target using target's own "up" as the reference up ---
    Vector3 lookPoint = target.position + target.right * localTargetOffset.x + target.up * localTargetOffset.y;
    Quaternion desiredRot = Quaternion.LookRotation((lookPoint - transform.position).normalized, target.up);

    if (smoothRotation)
      transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, rotationSpeedDegPerSec * dt);
    else
      transform.rotation = desiredRot;

    // --- FOV kick based on local (side+up) speed ---
    if (useFovKick && cam != null) {
      float speed = new Vector2(localVel.x, localVel.y).magnitude;
      float speedFrac = Mathf.Clamp01(speed / Mathf.Max(0.01f, fovKickMaxSpeed));
      float desiredFov = baseFov + maxFovKick * speedFrac;
      cam.fieldOfView = Mathf.SmoothDamp(cam.fieldOfView, desiredFov, ref fovVel, fovSmoothTime);
    }
  }

  /// <summary>Smoothly change the camera's distance behind the target (e.g. zoom for a cutscene).</summary>
  public void SetDepthDistance(float newDistance) => depthDistance = newDistance;

  /// <summary>Trigger a screen shake impulse.</summary>
  public void Shake(float amplitude = 0.3f, float duration = 0.25f, float frequency = 25f) {
    if (shake != null) shake.TriggerShake(amplitude, duration, frequency);
  }
}