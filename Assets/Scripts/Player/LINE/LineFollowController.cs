using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class LineFollowController : MonoBehaviour {
  [Header("Line")]
  [Tooltip("The LinePath the player currently walks along. Assign a starting line here, or leave empty and call SetLine() at runtime.")]
  public LinePath currentLine;

  [Tooltip("Which strand (disjoint sub-path) of currentLine the player is on. Most LinePaths only have strand 0 unless authored with multiple groups — see LinePath's summary.")]
  public int currentStrand = 0;

  [Header("Movement")]
  [Tooltip("Units per second moved along the line's length at full input.")]
  public float moveSpeed = 4f;

  [Tooltip("Rate at which along-line speed approaches moveSpeed while input is held.")]
  public float acceleration = 20f;

  [Tooltip("Rate at which along-line speed returns to zero when input is released.")]
  public float deceleration = 25f;

  [Header("Snapping")]
  [Tooltip("How far (world units) the player may drift off the line before being pulled back. Keep small — this is a tolerance for float drift/collision jitter, not a lane width.")]
  public float snapTolerance = 0.15f;

  [Tooltip("How fast the player is pulled back onto the line once outside snapTolerance, in units/second.")]
  public float snapPullSpeed = 10f;

  [Tooltip("Height above the line's own Y the player sits at — e.g. so a ground-level line doesn't visually clip into the player's feet. LineSwitcher reads this same value so a switch lands the player at exactly the height normal walking will then treat as 'on the line', instead of the two disagreeing and fighting each other.")]
  public float heightAboveLine = 0.05f;

  [Header("Gravity")]
  public float gravity = -20f;

  [Tooltip("Peak jump height used to calculate initial jump velocity.")]
  public float jumpHeight = 2.5f;

  [Header("Animation")]
  [Tooltip("Drives isRunning/Velocity/isJumping/isFalling on PlayerAnimatorController. Defaults to this GameObject's Animator if left empty.")]
  public Animator animator;

  [Header("Facing")]
  [Tooltip("Degrees per second the whole-body facing rotation turns to catch up to its target orientation.")]
  public float facingRotationSpeed = 540f;

  [Tooltip("Degrees per second the facing turns to catch up right after a line switch (SnapFacingToLine), independent of movement input.")]
  public float switchFacingSnapSpeed = 1080f;

  [Tooltip("Defaults to this GameObject's PlayerFlipController if left empty. IsFlipped reverses which side of the path the player faces (and with it, move input and flipX). While IsFlipping is true, PlayerFlipController has exclusive control of transform.rotation and this script's own facing rotation is skipped.")]
  public PlayerFlipController flipController;

  [Header("Sprite Flip")]
  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

  [Tooltip("Swap which way flipX points when moving backward along the line.")]
  public bool invertFlip = false;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logSnapWarnings = true;

  private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
  private static readonly int VelocityHash = Animator.StringToHash("Velocity");
  private static readonly int IsJumpingHash = Animator.StringToHash("isJumping");
  private static readonly int IsFallingHash = Animator.StringToHash("isFalling");

  private CharacterController _cc;
  private Vector3 _facingNormal;
  private Vector3 _rawNormal = Vector3.forward;
  private bool _lastFlipped;
  private float _distanceAlongLine;
  private float _alongLineSpeed;
  private float _moveInput;
  private float _verticalVelocity;
  private bool _jumpRequested;
  private bool _isSnappingFacing;

  public bool movementEnabled = true;

  public float GetDistanceAlongLine() => _distanceAlongLine;

  public bool IsOnLine { get; private set; } = true;

  private void Awake() {
    _cc = GetComponent<CharacterController>();
    if (animator == null) animator = GetComponent<Animator>();
    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    if (flipController == null) flipController = GetComponent<PlayerFlipController>();

    Vector3 initialForward = transform.forward;
    initialForward.y = 0f;
    _facingNormal = initialForward.sqrMagnitude > 0.0001f ? initialForward.normalized : Vector3.forward;
  }

  private void OnDisable() {
    _moveInput = 0f;
    _alongLineSpeed = 0f;
    _jumpRequested = false;

    if (animator == null) return;

    animator.SetBool(IsRunningHash, false);
    animator.SetFloat(VelocityHash, 0f);
    animator.SetBool(IsJumpingHash, false);
    animator.SetBool(IsFallingHash, false);
  }

  private void Start() {
    if (currentLine != null) {
      var dist = currentLine.FindClosestDistance(transform.position, out _, out _, out int strand);
      SetLine(currentLine, strand, dist);
    }
    _lastFlipped = flipController != null && flipController.IsFlipped;
  }

#pragma warning disable IDE0051
  private void OnMove(UnityEngine.InputSystem.InputValue value) {
    _moveInput = value.Get<float>();
  }

  private void OnJump(UnityEngine.InputSystem.InputValue value) {
    if (value.isPressed) {
      RequestJump();
    }
  }
#pragma warning restore IDE0051

  public void RequestJump() {
    _jumpRequested = true;
  }

  private void Update() {
    if (!movementEnabled || currentLine == null) {
      return;
    }

    UpdateFacingSide();
    UpdateAlongLineSpeed();
    var horizontalDelta = ComputeHorizontalDelta();
    ApplyGravityAndMove(horizontalDelta);
    UpdateSnapState();
    UpdateAnimator();
    UpdateFacing();
    UpdateSpriteFlip();
  }

  private void UpdateFacingSide() {
    Vector3 tangent = currentLine.GetDirectionAtDistance(currentStrand, _distanceAlongLine);
    tangent.y = 0f;
    if (tangent.sqrMagnitude < 0.0001f) return;
    tangent.Normalize();

    _rawNormal = Vector3.Cross(Vector3.up, tangent);
    _facingNormal = Vector3.Dot(_rawNormal, _facingNormal) < 0f ? -_rawNormal : _rawNormal;

    bool flipped = flipController != null && flipController.IsFlipped;
    if (flipped != _lastFlipped) {
      _facingNormal = -_facingNormal;
      _lastFlipped = flipped;
    }
  }

  private void UpdateFacing() {
    if (flipController != null && flipController.IsFlipping) return;

    float targetYaw = Mathf.Atan2(_facingNormal.x, _facingNormal.z) * Mathf.Rad2Deg;

    if (_isSnappingFacing) {
      float snappedYaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, switchFacingSnapSpeed * Time.deltaTime);
      transform.eulerAngles = new Vector3(0f, snappedYaw, 0f);
      if (Mathf.Approximately(snappedYaw, targetYaw)) _isSnappingFacing = false;
      return;
    }

    if (Mathf.Abs(_alongLineSpeed) < 0.01f) return;

    float newYaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, facingRotationSpeed * Time.deltaTime);
    transform.eulerAngles = new Vector3(0f, newYaw, 0f);
  }

  private void UpdateSpriteFlip() {
    // Input becomes zero before deceleration finishes. Do not treat that release frame as
    // rightward movement, otherwise the sprite always flips back to its default when it idles.
    if (spriteRenderer == null || Mathf.Abs(_moveInput) < 0.01f) return;
    bool movingBackward = _moveInput < 0f;
    spriteRenderer.flipX = invertFlip ? !movingBackward : movingBackward;
  }

  private void UpdateAnimator() {
    if (animator == null) return;

    bool grounded = _cc.isGrounded;
    // Held input at an open line endpoint does not mean the character is moving.
    animator.SetBool(IsRunningHash, Mathf.Abs(_alongLineSpeed) > 0.01f);
    animator.SetFloat(VelocityHash, Mathf.Abs(_alongLineSpeed));
    animator.SetBool(IsJumpingHash, !grounded && _verticalVelocity > 0f);
    animator.SetBool(IsFallingHash, !grounded && _verticalVelocity < 0f);
  }

  private void UpdateAlongLineSpeed() {
    float sign = Vector3.Dot(_facingNormal, _rawNormal) < 0f ? 1f : -1f;
    float input = _moveInput * sign;
    var target = input * moveSpeed;
    var rate = (input != 0f) ? acceleration : deceleration;
    _alongLineSpeed = Mathf.MoveTowards(_alongLineSpeed, target, rate * Time.deltaTime);
  }

  private Vector3 GetHuggedPoint(float distance) {
    return currentLine.GetPointAtDistance(currentStrand, distance) + Vector3.up * heightAboveLine;
  }

  private Vector3 ComputeHorizontalDelta() {
    Vector3 beforePos = GetHuggedPoint(_distanceAlongLine);
    var wantedDistance = _distanceAlongLine + _alongLineSpeed * Time.deltaTime;

    if (currentLine.IsStrandClosedLoop(currentStrand)) {
      _distanceAlongLine = wantedDistance;
    } else {
      var strandLength = currentLine.GetStrandLength(currentStrand);
      _distanceAlongLine = Mathf.Clamp(wantedDistance, 0f, strandLength);
      if (!Mathf.Approximately(_distanceAlongLine, wantedDistance)) {
        _alongLineSpeed = 0f;
      }
    }

    Vector3 afterPos = GetHuggedPoint(_distanceAlongLine);
    return afterPos - beforePos;
  }

  private void ApplyGravityAndMove(Vector3 horizontalDelta) {
    if (_cc.isGrounded && _verticalVelocity < 0f) {
      _verticalVelocity = -2f;
    }

    if (_jumpRequested) {
      _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
      _jumpRequested = false;
    }

    _verticalVelocity += gravity * Time.deltaTime;

    Vector3 correction = ComputeSnapCorrection();

    Vector3 move = horizontalDelta + correction + Vector3.up * (_verticalVelocity * Time.deltaTime);
    _cc.Move(move);
  }

  private Vector3 ComputeSnapCorrection() {
    if (currentLine == null) return Vector3.zero;

    var distAlong = currentLine.FindClosestDistanceOnStrand(currentStrand, transform.position, out Vector3 closestPoint, out _);
    Vector3 huggedPoint = closestPoint + Vector3.up * heightAboveLine;
    float distToHugged = Vector3.Distance(transform.position, huggedPoint);

    if (distToHugged <= snapTolerance) {
      return Vector3.zero;
    }

    if (logSnapWarnings) {
      Debug.LogWarning($"[LineFollowController] Off line by {distToHugged:F2}m (tolerance {snapTolerance:F2}m) — snapping back.");
    }

    _distanceAlongLine = distAlong;

    Vector3 toLine = huggedPoint - transform.position;
    Vector3 step = Vector3.ClampMagnitude(toLine * snapPullSpeed * Time.deltaTime, toLine.magnitude);
    return step;
  }

  private void UpdateSnapState() {
    if (currentLine == null) {
      IsOnLine = false;
      return;
    }

    currentLine.FindClosestDistanceOnStrand(currentStrand, transform.position, out Vector3 closestPoint, out _);
    float distToHugged = Vector3.Distance(transform.position, closestPoint + Vector3.up * heightAboveLine);
    IsOnLine = distToHugged <= snapTolerance;
  }

  public void SetLine(LinePath newLine, int strandIndex, float distanceAlongLine) {
    currentLine = newLine;
    currentStrand = strandIndex;
    _distanceAlongLine = distanceAlongLine;
    _alongLineSpeed = 0f;
  }

  public void SnapFacingToLine() {
    if (currentLine == null) return;

    UpdateFacingSide();
    _isSnappingFacing = true;
  }

  public void ResetVelocity() {
    _alongLineSpeed = 0f;
    _verticalVelocity = 0f;
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    if (!drawDebugGizmos || currentLine == null) return;
    Gizmos.color = IsOnLine ? Color.green : Color.red;
    Gizmos.DrawWireSphere(transform.position, snapTolerance);
  }
#endif
}
