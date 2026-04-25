using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GuardSpriteFacing : MonoBehaviour {
  [Header("References")]
  [SerializeField] private Camera gameCamera;
  [SerializeField] private Transform spriteVisual;
  [SerializeField] private SpriteRenderer spriteRenderer;

  [Header("Front Frames")]
  [SerializeField] private Sprite[] frontFrames;

  [Header("Back Frames")]
  [SerializeField] private Sprite[] backFrames;

  [Header("Left Frames")]
  [SerializeField] private Sprite[] leftFrames;

  [Header("Right Frames")]
  [SerializeField] private Sprite[] rightFrames;

  [Header("Animation")]
  [SerializeField] private float framesPerSecond = 6f;
  [SerializeField] private float minimumMoveSpeed = 0.05f;
  [SerializeField] private bool rotateSpriteToFaceCamera = true;

  private NavMeshAgent agent;

  private Vector3 lastMoveDirection = Vector3.forward;
  private Sprite[] currentFrames;
  private int frameIndex;
  private float frameTimer;

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
    Sprite[] wantedFrames = GetFramesForCameraRelativeDirection();

    Animate(wantedFrames, isMoving);
  }

  private bool UpdateLastMoveDirection() {
    Vector3 velocity = agent.velocity;
    velocity.y = 0f;

    if (velocity.magnitude >= minimumMoveSpeed) {
      lastMoveDirection = velocity.normalized;
      return true;
    }

    return false;
  }

  private Sprite[] GetFramesForCameraRelativeDirection() {
    Vector3 cameraForward = gameCamera.transform.forward;
    cameraForward.y = 0f;
    cameraForward.Normalize();

    Vector3 cameraRight = gameCamera.transform.right;
    cameraRight.y = 0f;
    cameraRight.Normalize();

    var forwardDot = Vector3.Dot(lastMoveDirection, cameraForward);
    var rightDot = Vector3.Dot(lastMoveDirection, cameraRight);

    if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot)) {
      if (forwardDot > 0f) {
        return backFrames;
      } else {
        return frontFrames;
      }
    } else {
      if (rightDot > 0f) {
        return rightFrames;
      } else {
        return leftFrames;
      }
    }
  }

  private void Animate(Sprite[] wantedFrames, bool isMoving) {
    if (wantedFrames == null || wantedFrames.Length == 0) {
      return;
    }

    if (currentFrames != wantedFrames) {
      currentFrames = wantedFrames;
      frameIndex = 0;
      frameTimer = 0f;
    }

    if (isMoving && currentFrames.Length > 1) {
      frameTimer += Time.deltaTime;

      var secondsPerFrame = 1f / framesPerSecond;

      while (frameTimer >= secondsPerFrame) {
        frameTimer -= secondsPerFrame;
        frameIndex = (frameIndex + 1) % currentFrames.Length;
      }
    } else if (!isMoving) {
      frameIndex = 0;
    }

    spriteRenderer.sprite = currentFrames[frameIndex];
  }

  private void RotateVisualTowardCamera() {
    Vector3 cameraEuler = gameCamera.transform.eulerAngles;

    spriteVisual.rotation = Quaternion.Euler(0f, cameraEuler.y, 0f);
  }
}
