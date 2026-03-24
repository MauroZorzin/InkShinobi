using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovementController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float acceleration = 20f;
    public float deceleration = 25f;

    private CharacterController _cc;
    private Camera _cam;
    private Animator _animator;
    private SpriteRenderer _sr;

    private Vector3 _velocity;
    private float _moveInput = 0f;

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        _cam = Camera.main;
        _animator = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();
    }

#pragma warning disable IDE0051
    void OnMove(InputValue value)
    {
        _moveInput = value.Get<float>();
    }

    void Update()
    {
        HandleMovement();
    }

    void HandleMovement()
    {
        // Derive direction from input sign instead of a hardcoded vector
        Vector3 targetVelocity = Vector3.right * _moveInput * moveSpeed;
        float rate = (_moveInput != 0f) ? acceleration : deceleration;
        _velocity = Vector3.MoveTowards(_velocity, targetVelocity, rate * Time.deltaTime);

        // Actually apply the velocity to the CharacterController
        _cc.Move(_velocity * Time.deltaTime);
    }
}