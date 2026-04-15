using UnityEngine;
using UnityEngine.InputSystem;
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class PlayerMovementController : MonoBehaviour {
  [Header("Movement")]
  public float moveSpeed = 5f;
  public float acceleration = 20f;
  public float deceleration = 25f;
  [Header("Gravity")]
  public float gravity = -20f;
  [Header("Jump")]
  public float jumpHeight = 2.5f;
  [Header("Animation")]
  public float velocityMargin = 0.1f; // minimum speed ratio before animator value clamps to 1

  // CONSTANT
  private const string IS_RUNNING_ANIMATOR_PARAMETER = "isRunning";
  private const string VELOCITY_ANIMATOR_PARAMETER = "Velocity";
  private const string IS_JUMPING_ANIMATOR_PARAMETER = "isJumping";
  private const string IS_FALLING_ANIMATOR_PARAMETER = "isFalling";

  // Private
  private CharacterController _cc;
  private Camera _cam;
  private Animator _animator;
  private SpriteRenderer _sr;
  private Vector3 _velocity;
  private float _moveInput = 0f;
  private float _verticalVelocity;
  private bool _jumpRequested = false;
  void Start() {
    _cc = GetComponent<CharacterController>();
    _cam = Camera.main;
    _animator = GetComponent<Animator>();
    _sr = GetComponent<SpriteRenderer>();
  }
#pragma warning disable IDE0051
  void OnMove(InputValue value) {
    _moveInput = value.Get<float>();
  }
  void OnJump(InputValue value) {
    if (value.isPressed && _cc.isGrounded) {
      _jumpRequested = true;
    }
  }
  void Update() {
    HandleMovement();
    ApplyGravity();
  }
  void HandleMovement() {
    Vector3 targetVelocity = _moveInput * moveSpeed * Vector3.right;
    var rate = (_moveInput != 0f) ? acceleration : deceleration;
    _velocity = Vector3.MoveTowards(_velocity, targetVelocity, rate * Time.deltaTime);
    if (_moveInput != 0f) {
      _animator.SetBool(IS_RUNNING_ANIMATOR_PARAMETER, true);
    } else {
      _animator.SetBool(IS_RUNNING_ANIMATOR_PARAMETER, false);
    }
    float speedRatio = Mathf.Abs(_velocity.x) / moveSpeed;
    float animValue = speedRatio > velocityMargin ? Mathf.Clamp(1f / speedRatio, 0f, 1f / velocityMargin) : 1f / velocityMargin;
    _animator.SetFloat(VELOCITY_ANIMATOR_PARAMETER, animValue);
    bool airborne = !_cc.isGrounded;
    _animator.SetBool(IS_JUMPING_ANIMATOR_PARAMETER, airborne && _verticalVelocity > 0f);
    _animator.SetBool(IS_FALLING_ANIMATOR_PARAMETER, airborne && _verticalVelocity <= 0f);
    if (_sr != null) {
      if (_moveInput > 0f) _sr.flipX = false;
      if (_moveInput < 0f) _sr.flipX = true;
    }
  }
  void ApplyGravity() {
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
}