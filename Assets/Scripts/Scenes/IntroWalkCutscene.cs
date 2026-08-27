using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scripted "walk forward for a bit" scene intro, independent of LinePath — the player is moved
/// directly via CharacterController.Move() along an explicit world-space direction (flattened and
/// normalized once at Start()), for walkDistance world units. No waypoints, no LinePath needed.
///
/// Follows the same "temporarily take over the CharacterController" pattern LineSwitcher already
/// uses for line-switch moves: disables LineFollowController.movementEnabled for the duration (if a
/// LineFollowController is present — this script doesn't require one), applies its own minimal
/// gravity (mirroring LineFollowController.ApplyGravityAndMove so the walk doesn't float on uneven
/// ground), and drives the same Animator parameters (isRunning/Velocity) so the existing walk
/// animation plays. LineFollowController tracks the player's position along its LinePath as its own
/// private "distance along line" — since this script moves the CharacterController directly, that
/// value goes stale during the walk. Handing control back re-syncs it the same way
/// LineFollowController.Start() does (LinePath.FindClosestDistance + SetLine), so movement resumes
/// from wherever the player actually ended up instead of clamping at the line's start/end like it
/// hit a wall. All player gameplay input is blocked for the duration by switching the
/// PlayerInput component to its "UI" action map (same idiom SceneTransitionManager uses for the
/// pause dialog) — not just movement, so Interact/Switch/Takedown/RotateLeft/RotateRight are blocked
/// too, and restored to whatever map was active before once the walk finishes. The walk always plays
/// out in full; there is no skip input.
///
/// Place once in a scene, e.g. on an empty "IntroWalk" GameObject — starts automatically on
/// Start(), no trigger volume involved.
/// </summary>
public class IntroWalkCutscene : MonoBehaviour {
  [Header("Player")]
  [Tooltip("Auto-finds by tag if left empty.")]
  [SerializeField] private GameObject player;

  [Header("Walk")]
  [Tooltip("World-space direction the player walks in. Flattened onto the horizontal plane and normalized at Start() — the Y value doesn't matter.")]
  [SerializeField] private Vector3 direction = Vector3.forward;

  [Tooltip("How far the player walks before stopping, in world units.")]
  [SerializeField] private float walkDistance = 5f;

  [Tooltip("Units per second — also drives the Animator's Velocity/isRunning so the walk animation matches.")]
  [SerializeField] private float walkSpeed = 4f;

  [Header("Gravity")]
  [Tooltip("Matches LineFollowController's default so the player falls/settles the same way during the walk.")]
  [SerializeField] private float gravity = -20f;

  [Header("Debug")]
  [SerializeField] private bool debugLogging = true;

  private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
  private static readonly int VelocityHash = Animator.StringToHash("Velocity");

  private CharacterController _cc;
  private Animator _animator;
  private LineFollowController _lineFollowController;
  private PlayerInput _playerInput;

  private Vector3 _direction;
  private float _verticalVelocity;
  private string _previousActionMap;

  private void Start() {
    if (player == null) player = GameObject.FindWithTag("Player");
    if (player == null) {
      if (debugLogging) Debug.LogWarning($"[IntroWalkCutscene] '{name}': Player not found. Assign in inspector or tag as 'Player'.", this);
      return;
    }

    _cc = player.GetComponent<CharacterController>();
    if (_cc == null) {
      if (debugLogging) Debug.LogWarning($"[IntroWalkCutscene] '{name}': player has no CharacterController — nothing to walk.", this);
      return;
    }

    _animator = player.GetComponent<Animator>();
    _lineFollowController = player.GetComponent<LineFollowController>();
    _playerInput = player.GetComponent<PlayerInput>();

    _direction = direction;
    _direction.y = 0f;
    _direction = _direction.sqrMagnitude > 0.0001f ? _direction.normalized : Vector3.forward;

    SwitchToUiInput();
    if (_lineFollowController != null) _lineFollowController.movementEnabled = false;

    StartCoroutine(WalkRoutine());
  }

  private IEnumerator WalkRoutine() {
    SetAnimator(true);

    float traveled = 0f;
    while (traveled < walkDistance) {
      float step = Mathf.Min(walkSpeed * Time.deltaTime, walkDistance - traveled);
      ApplyGravityAndMove(_direction * step);
      traveled += step;
      yield return null;
    }

    SetAnimator(false);
    ReattachLineFollowController();
    RestoreGameplayInput();

    if (debugLogging) Debug.Log($"[IntroWalkCutscene] '{name}': walk complete, control returned.", this);
  }

  private void ReattachLineFollowController() {
    if (_lineFollowController == null) return;

    if (_lineFollowController.currentLine != null) {
      float dist = _lineFollowController.currentLine.FindClosestDistance(player.transform.position, out _, out _, out int strand);
      _lineFollowController.SetLine(_lineFollowController.currentLine, strand, dist);
    }

    _lineFollowController.movementEnabled = true;
  }

  private void ApplyGravityAndMove(Vector3 horizontalDelta) {
    if (_cc.isGrounded && _verticalVelocity < 0f) {
      _verticalVelocity = -2f;
    }
    _verticalVelocity += gravity * Time.deltaTime;

    _cc.Move(horizontalDelta + Vector3.up * (_verticalVelocity * Time.deltaTime));
  }

  private void SetAnimator(bool running) {
    if (_animator == null) return;
    _animator.SetBool(IsRunningHash, running);
    _animator.SetFloat(VelocityHash, running ? walkSpeed : 0f);
  }

  private void SwitchToUiInput() {
    if (_playerInput == null) return;

    InputActionMap uiMap = _playerInput.actions != null ? _playerInput.actions.FindActionMap("UI", false) : null;
    if (uiMap == null) {
      if (debugLogging) Debug.LogWarning($"[IntroWalkCutscene] '{name}': PlayerInput has no 'UI' action map — input not blocked during the walk.", this);
      return;
    }

    _previousActionMap = _playerInput.currentActionMap != null ? _playerInput.currentActionMap.name : "Player";
    _playerInput.SwitchCurrentActionMap(uiMap.name);
  }

  private void RestoreGameplayInput() {
    if (_playerInput == null || _previousActionMap == null) return;

    if (_playerInput.actions != null && _playerInput.actions.FindActionMap(_previousActionMap, false) != null) {
      _playerInput.SwitchCurrentActionMap(_previousActionMap);
    }

    _previousActionMap = null;
  }
}
