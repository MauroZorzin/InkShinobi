using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GuardSpriteFacing : MonoBehaviour {
  private enum FacingDirection {
    Front,
    Back,
    Left,
    Right
  }

  [Header("References")]
  [SerializeField] private Camera gameCamera;
  [SerializeField] private Transform spriteVisual;
  [SerializeField] private SpriteRenderer spriteRenderer;

  [Header("Front")]
  [SerializeField] private Sprite[] frontIdleFrames;
  [SerializeField] private Sprite[] frontFrames;

  [Header("Back")]
  [SerializeField] private Sprite[] backIdleFrames;
  [SerializeField] private Sprite[] backFrames;

  [Header("Left")]
  [SerializeField] private Sprite[] leftIdleFrames;
  [SerializeField] private Sprite[] leftFrames;

  [Header("Right")]
  [SerializeField] private Sprite[] rightIdleFrames;
  [SerializeField] private Sprite[] rightFrames;

  [Header("Animation")]
  [SerializeField] private float walkFramesPerSecond = 6f;
  [SerializeField] private float idleFramesPerSecond = 3f;
  [SerializeField] private float minimumMoveSpeed = 0.05f;
  [SerializeField] private float idleDelay = 0.1f;
  [SerializeField] private bool rotateSpriteToFaceCamera = true;

  private NavMeshAgent agent;

  private Vector3 lastMoveDirection = Vector3.forward;
  private Sprite[] currentFrames;
  private int frameIndex;
  private float frameTimer;
  private float lastMovingTime;

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();

    if (gameCamera == null) {
      gameCamera = Camera.main;
    }

    if (spriteRenderer == null && spriteVisual != null) {
      spriteRenderer = spriteVisual.GetComponent<SpriteRenderer>();
    }
  }

  private void Update() {
    if (gameCamera == null || spriteRenderer == null) {
      return;
    }

    if (rotateSpriteToFaceCamera && spriteVisual != null) {
      RotateVisualTowardCamera();
    }

    var isMoving = UpdateLastMoveDirection();
    FacingDirection facingDirection = GetCameraRelativeDirection();

    var shouldUseWalkAnimation = isMoving || Time.time < lastMovingTime + idleDelay;

    Sprite[] wantedFrames;
    bool shouldAdvanceFrames;
    float wantedFps;

    if (shouldUseWalkAnimation) {
      wantedFrames = GetWalkFrames(facingDirection);
      shouldAdvanceFrames = true;
      wantedFps = walkFramesPerSecond;
    } else {
      wantedFrames = GetIdleFrames(facingDirection);

      if (wantedFrames == null || wantedFrames.Length == 0) {
        wantedFrames = GetWalkFrames(facingDirection);
        shouldAdvanceFrames = false;
      } else {
        shouldAdvanceFrames = true;
      }

      wantedFps = idleFramesPerSecond;
    }

    Animate(wantedFrames, shouldAdvanceFrames, wantedFps);
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

  private Sprite[] GetWalkFrames(FacingDirection direction) {
    switch (direction) {
      case FacingDirection.Front:
        return frontFrames;

      case FacingDirection.Back:
        return backFrames;

      case FacingDirection.Left:
        return leftFrames;

      case FacingDirection.Right:
        return rightFrames;

      default:
        return frontFrames;
    }
  }

  private Sprite[] GetIdleFrames(FacingDirection direction) {
    return direction switch {
      FacingDirection.Front => frontIdleFrames,
      FacingDirection.Back => backIdleFrames,
      FacingDirection.Left => leftIdleFrames,
      FacingDirection.Right => rightIdleFrames,
      _ => frontIdleFrames,
    };
  }

  private void Animate(Sprite[] wantedFrames, bool shouldAdvanceFrames, float framesPerSecond) {
    if (wantedFrames == null || wantedFrames.Length == 0) {
      return;
    }

    if (currentFrames != wantedFrames) {
      currentFrames = wantedFrames;
      frameIndex = 0;
      frameTimer = 0f;
    }

    if (shouldAdvanceFrames && currentFrames.Length > 1) {
      frameTimer += Time.deltaTime;

      var safeFps = Mathf.Max(0.01f, framesPerSecond);
      var secondsPerFrame = 1f / safeFps;

      while (frameTimer >= secondsPerFrame) {
        frameTimer -= secondsPerFrame;
        frameIndex = (frameIndex + 1) % currentFrames.Length;
      }
    } else {
      frameIndex = 0;
      frameTimer = 0f;
    }

    spriteRenderer.sprite = currentFrames[frameIndex];
  }

  private void RotateVisualTowardCamera() {
    Vector3 cameraEuler = gameCamera.transform.eulerAngles;
    spriteVisual.rotation = Quaternion.Euler(0f, cameraEuler.y, 0f);
  }
}
