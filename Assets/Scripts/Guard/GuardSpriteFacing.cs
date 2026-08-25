using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Maps a guard's NavMesh movement direction to camera-relative sprite facing and animation parameters.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DefaultExecutionOrder(100)]
public class GuardSpriteFacing : MonoBehaviour {
  private enum FacingDirection {
    Front = 0,
    Back = 1,
    Left = 2,
    Right = 3
  }

  [Header("References")]
  [Tooltip("Camera used to convert world movement into camera-relative facing.")]
  [SerializeField] private Camera gameCamera;

  [Tooltip("Transform that visually represents the guard sprite.")]
  [SerializeField] private Transform spriteVisual;

  [Tooltip("Animator that receives IsMoving and Facing parameters.")]
  [SerializeField] private Animator spriteAnimator;

  [Header("Movement Detection")]
  [Tooltip("Minimum horizontal NavMeshAgent speed required to refresh movement direction.")]
  [SerializeField] private float minimumMoveSpeed = 0.05f;

  [Tooltip("Seconds to keep walk animation active after movement falls below the threshold.")]
  [SerializeField] private float idleDelay = 0.1f;

  [Header("Billboard")]
  [Tooltip("Whether the sprite visual should rotate to match the camera yaw.")]
  [SerializeField] private bool rotateSpriteToFaceCamera = true;

  private NavMeshAgent agent;
  private Vector3 lastMoveDirection = Vector3.forward;
  private float lastMovingTime = -999f;
  private Vector3 previousPosition;

  private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
  private static readonly int FacingHash = Animator.StringToHash("Facing");

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();
    previousPosition = transform.position;

    if (gameCamera == null) {
      gameCamera = Camera.main;
    }

    if (spriteAnimator == null && spriteVisual != null) {
      spriteAnimator = spriteVisual.GetComponent<Animator>();
    }
  }

  private void Update() {
    if (gameCamera == null || spriteAnimator == null) {
      return;
    }

    if (rotateSpriteToFaceCamera && spriteVisual != null) {
      RotateVisualTowardCamera();
    }

    var isCurrentlyMoving = UpdateLastMoveDirection();
    var shouldUseWalkAnimation = isCurrentlyMoving || Time.time < lastMovingTime + idleDelay;
    FacingDirection facingDirection = GetCameraRelativeDirection();

    spriteAnimator.SetBool(IsMovingHash, shouldUseWalkAnimation);
    spriteAnimator.SetInteger(FacingHash, (int)facingDirection);
  }

  /// <summary>
  /// Updates the cached movement direction from the NavMeshAgent velocity.
  /// </summary>
  /// <returns>True when current velocity is above the movement threshold.</returns>
  private bool UpdateLastMoveDirection() {
    float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
    Vector3 displacementVelocity = (transform.position - previousPosition) / deltaTime;
    Vector3 velocity = displacementVelocity;

    if (agent != null && agent.enabled && agent.isOnNavMesh) {
      Vector3 agentVelocity = agent.velocity;
      // NavMesh-controlled guards report their true velocity here. Prototype motors such as
      // GuardSquarePatrol move the Transform directly, so use whichever signal is stronger.
      if (agentVelocity.sqrMagnitude > displacementVelocity.sqrMagnitude) velocity = agentVelocity;
    }
    previousPosition = transform.position;
    velocity.y = 0f;

    if (velocity.magnitude >= minimumMoveSpeed) {
      lastMoveDirection = velocity.normalized;
      lastMovingTime = Time.time;
      return true;
    }

    return false;
  }

  /// <summary>
  /// Converts the cached movement direction into a camera-relative facing enum.
  /// </summary>
  /// <returns>The camera-relative facing direction.</returns>
  private FacingDirection GetCameraRelativeDirection() {
    Vector3 cameraForward = gameCamera.transform.forward;
    cameraForward.y = 0f;

    if (cameraForward.sqrMagnitude < 0.0001f) {
      cameraForward = Vector3.forward;
    } else {
      cameraForward.Normalize();
    }

    Vector3 cameraRight = gameCamera.transform.right;
    cameraRight.y = 0f;

    if (cameraRight.sqrMagnitude < 0.0001f) {
      cameraRight = Vector3.right;
    } else {
      cameraRight.Normalize();
    }

    var forwardDot = Vector3.Dot(lastMoveDirection, cameraForward);
    var rightDot = Vector3.Dot(lastMoveDirection, cameraRight);

    if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot)) {
      if (forwardDot > 0f) {
        return FacingDirection.Back;
      }

      return FacingDirection.Front;
    }

    if (rightDot > 0f) {
      return FacingDirection.Right;
    }

    return FacingDirection.Left;
  }

  /// <summary>
  /// Rotates the sprite visual so it remains billboarded toward the camera yaw.
  /// </summary>
  private void RotateVisualTowardCamera() {
    Vector3 cameraEuler = gameCamera.transform.eulerAngles;
    spriteVisual.rotation = Quaternion.Euler(0f, cameraEuler.y, 0f);
  }
}
