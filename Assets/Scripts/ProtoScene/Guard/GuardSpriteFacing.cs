using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GuardSpriteFacing : MonoBehaviour {
  private enum FacingDirection {
    Front = 0,
    Back = 1,
    Left = 2,
    Right = 3
  }

  [Header("References")]
  [SerializeField] private Camera gameCamera;
  [SerializeField] private Transform spriteVisual;
  [SerializeField] private Animator spriteAnimator;

  [Header("Movement Detection")]
  [SerializeField] private float minimumMoveSpeed = 0.05f;
  [SerializeField] private float idleDelay = 0.1f;

  [Header("Billboard")]
  [SerializeField] private bool rotateSpriteToFaceCamera = true;

  private NavMeshAgent agent;
  private Vector3 lastMoveDirection = Vector3.forward;
  private float lastMovingTime = -999f;

  private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
  private static readonly int FacingHash = Animator.StringToHash("Facing");

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();

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

    // Small delay prevents flickering between walk and idle when the agent slows down.
    var shouldUseWalkAnimation = isCurrentlyMoving || Time.time < lastMovingTime + idleDelay;

    FacingDirection facingDirection = GetCameraRelativeDirection();

    spriteAnimator.SetBool(IsMovingHash, shouldUseWalkAnimation);
    spriteAnimator.SetInteger(FacingHash, (int)facingDirection);
  }

  private bool UpdateLastMoveDirection() {
    Vector3 velocity = agent.velocity;
    velocity.y = 0f;

    if (velocity.magnitude >= minimumMoveSpeed) {
      lastMoveDirection = velocity.normalized;
      lastMovingTime = Time.time;
      return true;
    }

    return false;
  }

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

  private void RotateVisualTowardCamera() {
    Vector3 cameraEuler = gameCamera.transform.eulerAngles;
    spriteVisual.rotation = Quaternion.Euler(0f, cameraEuler.y, 0f);
  }
}
