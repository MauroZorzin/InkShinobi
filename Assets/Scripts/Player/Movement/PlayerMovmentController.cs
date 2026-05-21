using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives player movement, gravity, camera-relative world rotation, and movement animation state.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(WallSwitcher))]
public class PlayerMovementController : MonoBehaviour {
  [Header("Movement")]
  public float moveSpeed = 5f;
  public float acceleration = 20f;
  public float deceleration = 25f;

  [Header("Gravity")]
  public float gravity = -20f;

  [Header("Jump")]
  public float jumpHeight = 2.5f;

  [Header("Rotation")]
  public float rotationDuration = 0.3f;

  [Header("References")]
  public Transform camPivot;

  [Header("Animation")]
  public float velocityMargin = 0.1f;

  // Constants
  private const string IS_RUNNING_ANIMATOR_PARAMETER = "isRunning";
  private const string VELOCITY_ANIMATOR_PARAMETER = "Velocity";
  private const string IS_JUMPING_ANIMATOR_PARAMETER = "isJumping";
  private const string IS_FALLING_ANIMATOR_PARAMETER = "isFalling";

  // Private
  private CharacterController _cc;
  private Camera _cam;
  private Animator _animator;
  private SpriteRenderer _sr;
  private WallSwitcher _wallSwitcher;

  private Vector3 _velocity;
  private float _verticalVelocity;
  private float _moveInput = 0f;
  private bool _jumpRequested = false;

  private int _currentRotationIndex = 0;
  private bool _isRotating = false;

  private static readonly Vector3[] Directions = new Vector3[] {
    Vector3.right,
    Vector3.forward,
    Vector3.left,
    Vector3.back
  };

  private static readonly float[] CameraYAngles = new float[] {
    0f,
    90f,
    180f,
    270f
  };

  private void Start() {
    _cc = GetComponent<CharacterController>();
    _cam = Camera.main;
    _animator = GetComponent<Animator>();
    _sr = GetComponent<SpriteRenderer>();
    _wallSwitcher = GetComponent<WallSwitcher>();

    if (camPivot == null && _cam != null) {
      camPivot = _cam.transform.parent != null ? _cam.transform.parent : _cam.transform;
    }
  }

#pragma warning disable IDE0051
  private void OnMove(InputValue value) {
    _moveInput = value.Get<float>();
  }

  private void OnSwitch(InputValue value) {
    if (value.isPressed) {
      _wallSwitcher.RequestSwitch();
    }
  }

  private void OnRotateLeft(InputValue value) {
    if (value.isPressed && !_isRotating) {
      StartCoroutine(RotateWorld(-1));
    }
  }

  private void OnRotateRight(InputValue value) {
    if (value.isPressed && !_isRotating) {
      StartCoroutine(RotateWorld(1));
    }
  }

  // Temporary method to force return to main menu for testing purposes
  // TODO: Remove this method and its input binding later
  private void OnExit(InputValue value) {
    if (value.isPressed) {
      SceneManager.LoadSceneAsync("MainMenu");
    }
  }
#pragma warning restore IDE0051

  private void Update() {
    if (!_isRotating) {
      HandleMovement();
      ApplyGravity();
    }
  }

  /// <summary>
  /// Animates a signed 90-degree camera/world rotation and updates the logical rotation index.
  /// </summary>
  /// <param name="direction">Rotation direction in quarter turns, where negative rotates left and positive rotates right.</param>
  /// <returns>Coroutine enumerator used by Unity while the rotation is in progress.</returns>
  private System.Collections.IEnumerator RotateWorld(int direction) {
    _isRotating = true;
    _velocity = Vector3.zero;

    var newIndex = (_currentRotationIndex + direction + Directions.Length) % Directions.Length;

    var elapsed = 0f;
    var startAngle = camPivot.eulerAngles.y;
    var targetAngle = startAngle + direction * 90f;

    while (elapsed < rotationDuration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, elapsed / rotationDuration);
      var angle = Mathf.Lerp(startAngle, targetAngle, t);
      camPivot.eulerAngles = new Vector3(0f, angle, 0f);
      camPivot.position = transform.position;
      yield return null;
    }

    camPivot.eulerAngles = new Vector3(0f, CameraYAngles[newIndex], 0f);
    camPivot.position = transform.position;

    _currentRotationIndex = newIndex;
    _isRotating = false;
  }

  /// <summary>
  /// Builds camera-relative horizontal velocity and updates movement animation parameters.
  /// </summary>
  private void HandleMovement() {
    Vector3 camRight = camPivot.right;
    camRight.y = 0f;
    camRight.Normalize();

    Vector3 targetVelocity = _moveInput * moveSpeed * camRight;
    var rate = (_moveInput != 0f) ? acceleration : deceleration;
    _velocity = Vector3.MoveTowards(_velocity, targetVelocity, rate * Time.deltaTime);

    _animator.SetBool(IS_RUNNING_ANIMATOR_PARAMETER, _moveInput != 0f);

    var speedRatio = Mathf.Abs(_velocity.magnitude) / moveSpeed;
    var animValue = speedRatio > velocityMargin ? Mathf.Clamp(1f / speedRatio, 0f, 1f / velocityMargin) : 1f / velocityMargin;
    _animator.SetFloat(VELOCITY_ANIMATOR_PARAMETER, animValue);

    var airborne = !_cc.isGrounded;
    _animator.SetBool(IS_JUMPING_ANIMATOR_PARAMETER, airborne && _verticalVelocity > 0f);
    _animator.SetBool(IS_FALLING_ANIMATOR_PARAMETER, airborne && _verticalVelocity <= 0f);

    if (_sr != null) {
      if (_moveInput > 0f) {
        _sr.flipX = false;
      }
      if (_moveInput < 0f) {
        _sr.flipX = true;
      }
    }
  }

  /// <summary>
  /// Applies vertical gravity and combines it with the current horizontal velocity for the controller move.
  /// </summary>
  private void ApplyGravity() {
    if (_cc.isGrounded && _verticalVelocity < 0f) {
      _verticalVelocity = -2f;
    }

    if (_jumpRequested) {
      _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
      _jumpRequested = false;
    }

    _verticalVelocity += gravity * Time.deltaTime;
    Vector3 finalMove = _velocity + Vector3.up * _verticalVelocity;
    _cc.Move(finalMove * Time.deltaTime);
  }

  /// <summary>
  /// Returns the index of the current 90-degree camera/world orientation.
  /// </summary>
  /// <returns>The current rotation index in the four-entry direction table.</returns>
  public int GetRotationIndex() => _currentRotationIndex;

  /// <summary>
  /// Returns whether a 90-degree world rotation is currently being animated.
  /// </summary>
  /// <returns>True while the rotate coroutine owns movement and camera rotation.</returns>
  public bool IsRotating() => _isRotating;

  /// <summary>
  /// Rotates any carried horizontal velocity after an external wall turn changes orientation.
  /// </summary>
  /// <param name="quarterTurns">Signed number of 90-degree turns to apply.</param>
  public void ReorientHorizontalVelocity(int quarterTurns) {
    if (quarterTurns == 0) {
      return;
    }

    var angle = 90f * quarterTurns;
    _velocity = Quaternion.AngleAxis(angle, Vector3.up) * _velocity;
    _velocity.y = 0f;
  }

  /// <summary>
  /// Clears horizontal momentum after scripted movement has repositioned the player.
  /// </summary>
  public void ResetHorizontalVelocity() {
    _velocity = Vector3.zero;
  }
}
