using UnityEngine;

/// <summary>
/// Character controller that constrains horizontal movement to a single LinePath at a time.
/// The player can only move forward/backward along the line's length; any input perpendicular
/// to the line is ignored. If the player's actual position drifts further from the line than
/// snapTolerance (e.g. right after a LineSwitcher move, or from external physics), it is pulled
/// back onto the line rather than being allowed to walk off it.
///
/// This is a standalone replacement for the movement half of PlayerMovementController — it owns
/// gravity/jump/CharacterController.Move itself. Camera rotation, sprite flipping, etc. from your
/// existing controller can still be layered on top by reading GetDistanceAlongLine() / IsOnLine.
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

  [Header("Gravity")]
  public float gravity = -20f;

  [Tooltip("Peak jump height used to calculate initial jump velocity.")]
  public float jumpHeight = 2.5f;

  [Header("Facing (2D)")]
  [Tooltip("If true, the player flips to face the direction it's actually moving along the line. Since the camera is parallel to the line and looks perpendicular at it (a 2D-style view), this is just a left/right flip, not a 3D rotation.")]
  public bool faceDirectionOfTravel = true;

  [Tooltip("Camera used to decide which world direction counts as 'right' for the flip. Defaults to Camera.main if left empty.")]
  public Camera facingReferenceCamera;

  [Tooltip("If true and a SpriteRenderer is present, flips SpriteRenderer.flipX. Otherwise flips the object's local X scale (for a plane/quad instead of a sprite).")]
  public bool useSpriteFlipX = true;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logSnapWarnings = true;

  private CharacterController _cc;
  private SpriteRenderer _sr;
  private float _distanceAlongLine;
  private float _alongLineSpeed;
  private float _moveInput;
  private float _verticalVelocity;
  private bool _jumpRequested;
  private bool _facingRight = true;

  /// <summary>True while movement is being driven by this controller (set false during a LineSwitcher move or vision mode).</summary>
  public bool movementEnabled = true;

  /// <summary>Current distance along currentLine, in world units from the line's start.</summary>
  public float GetDistanceAlongLine() => _distanceAlongLine;

  /// <summary>True when the player's actual position is within snapTolerance of currentLine.</summary>
  public bool IsOnLine { get; private set; } = true;

  private void Awake() {
    _cc = GetComponent<CharacterController>();
    _sr = GetComponent<SpriteRenderer>();

    if (facingReferenceCamera == null) {
      facingReferenceCamera = Camera.main;
    }
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

    if (faceDirectionOfTravel) {
      UpdateFacing(horizontalDelta);
    }
  }

  private void UpdateAlongLineSpeed() {
    var target = _moveInput * moveSpeed;
    var rate = (_moveInput != 0f) ? acceleration : deceleration;
    _alongLineSpeed = Mathf.MoveTowards(_alongLineSpeed, target, rate * Time.deltaTime);
  }

  /// <summary>
  /// Advances distance-along-line by the current speed and returns the world-space horizontal
  /// move delta needed to get the player from its current position to the new point on the line.
  /// </summary>
  private Vector3 ComputeHorizontalDelta() {
    Vector3 beforePos = currentLine.GetPointAtDistance(currentStrand, _distanceAlongLine);
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

    Vector3 afterPos = currentLine.GetPointAtDistance(currentStrand, _distanceAlongLine);
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

  /// <summary>
  /// Flips the player left/right based on which way it's actually moving along the line,
  /// relative to the camera. Deliberately NOT a 3D rotation: with a camera parallel to the
  /// line (looking perpendicular into it, like a 2D side view), facing is just a sign —
  /// left or right — not an orientation to solve for. Corners and zig-zags just change
  /// when the sign flips, they don't need the object to actually turn in 3D.
  /// </summary>
  private void UpdateFacing(Vector3 horizontalDelta) {
    if (horizontalDelta.sqrMagnitude < 0.0000001f) {
      return; // stopped — keep whatever facing we last had
    }

    Vector3 camRight = facingReferenceCamera != null ? facingReferenceCamera.transform.right : Vector3.right;
    camRight.y = 0f;
    if (camRight.sqrMagnitude < 0.0001f) {
      camRight = Vector3.right;
    }
    camRight.Normalize();

    float side = Vector3.Dot(horizontalDelta, camRight);
    if (Mathf.Abs(side) < 0.0001f) {
      return; // moving straight toward/away from camera (e.g. depth-only step) — ambiguous, keep last facing
    }

    bool facingRight = side > 0f;
    if (facingRight == _facingRight) {
      return;
    }
    _facingRight = facingRight;

    if (useSpriteFlipX && _sr != null) {
      _sr.flipX = !facingRight;
    } else {
      Vector3 s = transform.localScale;
      s.x = Mathf.Abs(s.x) * (facingRight ? 1f : -1f);
      transform.localScale = s;
    }
  }

  private Vector3 ComputeSnapCorrection() {
    if (currentLine == null) return Vector3.zero;

    var distAlong = currentLine.FindClosestDistanceOnStrand(currentStrand, transform.position, out Vector3 closestPoint, out float distToLine);

    if (distToLine <= snapTolerance) {
      return Vector3.zero;
    }

    if (logSnapWarnings) {
      Debug.LogWarning($"[LineFollowController] Off line by {distToLine:F2}m (tolerance {snapTolerance:F2}m) — snapping back.");
    }

    // Keep the player's own tracked distance-along-line in sync with where the snap is pulling toward,
    // so ComputeHorizontalDelta doesn't fight the correction next frame.
    _distanceAlongLine = distAlong;

    Vector3 toLine = closestPoint - transform.position;
    Vector3 step = Vector3.ClampMagnitude(toLine * snapPullSpeed * Time.deltaTime, toLine.magnitude);
    return step;
  }

  private void UpdateSnapState() {
    if (currentLine == null) {
      IsOnLine = false;
      return;
    }

    currentLine.FindClosestDistanceOnStrand(currentStrand, transform.position, out _, out float distToLine);
    IsOnLine = distToLine <= snapTolerance;
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
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    if (!drawDebugGizmos || currentLine == null) return;
    Gizmos.color = IsOnLine ? Color.green : Color.red;
    Gizmos.DrawWireSphere(transform.position, snapTolerance);
  }
#endif
}
