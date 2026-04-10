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
  private const string IS_RUNNING_ANIMATOR_PARAMETER = "isRunning";

  private const string VELOCITY_ANIMATOR_PARAMETER = "Velocity";

  private CharacterController _cc;
  private Camera _cam;
  private Animator _animator;
  private SpriteRenderer _sr;

  private Vector3 _velocity;
  private float _moveInput = 0f;
  private float _verticalVelocity;

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
    _animator.SetFloat(VELOCITY_ANIMATOR_PARAMETER, moveSpeed / Mathf.Abs(_velocity.x) + 0.2f);
    if (_sr != null) {
      if (_moveInput > 0f) _sr.flipX = false;
      if (_moveInput < 0f) _sr.flipX = true;
    }
  }
  void ApplyGravity() {
    _verticalVelocity += gravity * Time.deltaTime;
    Vector3 finalMove = _velocity + Vector3.up * _verticalVelocity;
    _cc.Move(finalMove * Time.deltaTime);
  }
}
