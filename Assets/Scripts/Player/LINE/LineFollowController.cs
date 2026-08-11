using UnityEngine;

/// <summary>
/// Character controller that constrains horizontal movement to a single LinePath at a time.
/// The player can only move forward/backward along the line's length; any input perpendicular
/// to the line is ignored. If the player's actual position drifts further from the line than
/// snapTolerance (e.g. right after a LineSwitcher move, or from external physics), it is pulled
/// back onto the line rather than being allowed to walk off it.
///
/// This is a standalone replacement for the movement half of PlayerMovementController — it owns
/// gravity/jump/CharacterController.Move itself, drives the Animator (isRunning/Velocity/
/// isJumping/isFalling), and keeps the sprite's plane perpendicular to the line's own tangent at
/// the player's distance (rotating the transform as the line's direction changes, not just
/// flipping), with flipX layered on top to show forward vs backward travel along that tangent.
/// Camera rotation etc. from your existing controller can still be layered on top by reading
/// GetDistanceAlongLine() / IsOnLine.
/// </summary>
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

  [Header("Sprite Facing")]
  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

  [Tooltip("The direction of travel the player starts facing, before any movement — resolves which of the two possible perpendicular orientations the transform starts rotated to. Only the horizontal (X/Z) component is used.")]
  public Vector3 initialFacingDirection = Vector3.right;

  [Tooltip("Swap which way flipX points when moving backward along the line.")]
  public bool invertFacing = false;

  [Tooltip("Along-line speed below this (units/second) is treated as idle — keeps the last rotation/flip instead of updating on tiny drift.")]
  public float facingSpeedDeadzone = 0.05f;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logSnapWarnings = true;

  private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
  private static readonly int VelocityHash = Animator.StringToHash("Velocity");
  private static readonly int IsJumpingHash = Animator.StringToHash("isJumping");
  private static readonly int IsFallingHash = Animator.StringToHash("isFalling");

  private CharacterController _cc;
  private float _distanceAlongLine;
  private float _alongLineSpeed;
  private float _moveInput;
  private float _verticalVelocity;
  private bool _jumpRequested;

  // The resolved perpendicular-to-travel normal the transform is rotated to face. Tracked frame
  // to frame (rather than re-derived from a bare cross product each time) because Cross(up, T)
  // has two valid solutions 180° apart — without continuity, a line whose tangent crosses the
  // ambiguity boundary would snap the sprite to face the wrong way instead of turning smoothly.
  private Vector3 _facingNormal;

  /// <summary>True while movement is being driven by this controller (set false during a LineSwitcher move or vision mode).</summary>
  public bool movementEnabled = true;

  /// <summary>Current distance along currentLine, in world units from the line's start.</summary>
  public float GetDistanceAlongLine() => _distanceAlongLine;

  /// <summary>True when the player's actual position is within snapTolerance of currentLine.</summary>
  public bool IsOnLine { get; private set; } = true;

  private void Awake() {
    _cc = GetComponent<CharacterController>();
    if (animator == null) animator = GetComponent<Animator>();
    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

    // Preserve the orientation authored in the Inspector. UpdateFacing() uses this
    // direction to choose the closest perpendicular orientation once movement begins.
    Vector3 initialForward = transform.forward;
    initialForward.y = 0f;
    _facingNormal = initialForward.sqrMagnitude > 0.0001f
      ? initialForward.normalized
      : Vector3.forward;
  }

  private void Start() {
    if (currentLine != null) {
      var dist = currentLine.FindClosestDistance(transform.position, out _, out _, out int strand);
      SetLine(currentLine, strand, dist);
    }
  }

#pragma warning disable IDE0051
  // Wire these up to your Input Actions the same way PlayerMovementController does,
  // or delete these and drive _moveInput / RequestJump() from your own input script.
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

    UpdateAlongLineSpeed();
    var horizontalDelta = ComputeHorizontalDelta();
    ApplyGravityAndMove(horizontalDelta);
    UpdateSnapState();
    UpdateFacing();
    UpdateAnimator();
  }

  /// <summary>
  /// Builds the line's tangent at the player's current distance and rotates the transform so the
  /// sprite plane stays perpendicular to it — a line that bends from running along X to running
  /// along Z (or anything in between, not just a clean 90°) turns the character to match instead
  /// of leaving it facing a fixed world axis. flipX on top of that rotation shows walking forward
  /// vs backward along the tangent, instead of spinning the transform 180°. Holds the last
  /// rotation/flip while along-line speed is inside the deadzone (idle, or pinned at an end).
  /// </summary>
  private void UpdateFacing() {
    if (Mathf.Abs(_alongLineSpeed) < facingSpeedDeadzone) return;

    Vector3 tangent = currentLine.GetDirectionAtDistance(currentStrand, _distanceAlongLine);
    tangent.y = 0f;
    if (tangent.sqrMagnitude < 0.0001f) return;
    tangent.Normalize();

    Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
    if (Vector3.Dot(normal, _facingNormal) < 0f) normal = -normal; // stick with the closer of the two perpendicular solutions
    _facingNormal = normal;

    transform.rotation = Quaternion.LookRotation(_facingNormal, Vector3.up);

    if (spriteRenderer != null) {
      bool movingBackward = _alongLineSpeed < 0f;
      spriteRenderer.flipX = invertFacing ? !movingBackward : movingBackward;
    }
  }

  private void UpdateAnimator() {
    if (animator == null) return;

    bool grounded = _cc.isGrounded;
    animator.SetBool(IsRunningHash, Mathf.Abs(_moveInput) > 0.01f);
    animator.SetFloat(VelocityHash, Mathf.Abs(_alongLineSpeed));
    animator.SetBool(IsJumpingHash, !grounded && _verticalVelocity > 0f);
    animator.SetBool(IsFallingHash, !grounded && _verticalVelocity < 0f);
  }

  private void UpdateAlongLineSpeed() {
    var target = _moveInput * moveSpeed;
    var rate = (_moveInput != 0f) ? acceleration : deceleration;
    _alongLineSpeed = Mathf.MoveTowards(_alongLineSpeed, target, rate * Time.deltaTime);
  }

  /// <summary>
  /// The actual world point the player should be resting at for a given distance along the
  /// current strand — the line's own point, lifted by heightAboveLine. Every piece of this
  /// controller that needs to know "where the line is" goes through this, and LineSwitcher
  /// reads the same heightAboveLine value, so walking and switching always agree on where
  /// "on the line" actually is. Without that agreement, a switch that lands the player at a
  /// different height than walking expects gets immediately yanked around by snap correction.
  /// </summary>
  private Vector3 GetHuggedPoint(float distance) {
    return currentLine.GetPointAtDistance(currentStrand, distance) + Vector3.up * heightAboveLine;
  }

  /// <summary>
  /// Advances distance-along-line by the current speed and returns the world-space horizontal
  /// move delta needed to get the player from its current position to the new point on the line.
  /// </summary>
  private Vector3 ComputeHorizontalDelta() {
    Vector3 beforePos = GetHuggedPoint(_distanceAlongLine);
    var wantedDistance = _distanceAlongLine + _alongLineSpeed * Time.deltaTime;

    if (currentLine.IsStrandClosedLoop(currentStrand)) {
      _distanceAlongLine = wantedDistance; // GetPointAtDistance wraps closed-loop strands internally
    } else {
      var strandLength = currentLine.GetStrandLength(currentStrand);
      _distanceAlongLine = Mathf.Clamp(wantedDistance, 0f, strandLength);
      if (!Mathf.Approximately(_distanceAlongLine, wantedDistance)) {
        // Hit an end — zero speed so it doesn't build up while pinned there.
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

    // Pull the player back onto the line before adding this frame's move, so drift doesn't compound.
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

    // Keep the player's own tracked distance-along-line in sync with where the snap is pulling toward,
    // so ComputeHorizontalDelta doesn't fight the correction next frame.
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

  /// <summary>
  /// Switches the player onto a new line at a given distance along it, resetting along-line speed.
  /// Call this after a LineSwitcher move completes, or to place the player on a line at start.
  /// Does NOT move the player's transform — the caller (LineSwitcher, or Start()) is responsible
  /// for that; this just changes which line subsequent movement is measured against.
  /// </summary>
  public void SetLine(LinePath newLine, int strandIndex, float distanceAlongLine) {
    currentLine = newLine;
    currentStrand = strandIndex;
    _distanceAlongLine = distanceAlongLine;
    _alongLineSpeed = 0f;
  }

  /// <summary>Clears vertical/along-line momentum, used after scripted repositioning.</summary>
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
