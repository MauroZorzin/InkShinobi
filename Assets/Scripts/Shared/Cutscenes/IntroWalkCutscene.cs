using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Intro di scena scriptata che fa camminare il giocatore per un tratto, muovendolo direttamente via CharacterController, indipendente da LinePath.</summary>
public class IntroWalkCutscene : MonoBehaviour {
  [Header("Player")]
  [Tooltip("Auto-finds by tag if left empty.")]
  [SerializeField] private GameObject player;

  [Tooltip("Optional authored feet position used before the walk begins. When assigned, this also prevents LineFollowController.Start from snapping the player onto its gameplay line first.")]
  [SerializeField] private Transform authoredStart;

  [Tooltip("Optional authored feet position where the walk must end. When assigned with Authored Start, Direction and Walk Distance are derived from these anchors.")]
  [SerializeField] private Transform authoredEnd;

  [Tooltip("Optional gameplay line to attach after reaching Authored End. Existing scenes can leave this empty and keep using the player's current line.")]
  [SerializeField] private LinePath completionLine;

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

  [Header("Completion Tutorial")]
  [Tooltip("Information shown through the shared dialogue HUD as soon as the intro walk returns control to the player.")]
  [SerializeField, TextArea] private string completionInformation;
  [Tooltip("Consecutive rightward distance the player must travel after gaining control before the tutorial clears. Stopping or moving left resets progress. Zero keeps it visible until another system clears it.")]
  [SerializeField, Min(0f)] private float completionInformationDismissDistance = 1f;

  private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
  private static readonly int VelocityHash = Animator.StringToHash("Velocity");

  private CharacterController _cc;
  private Animator _animator;
  private LineFollowController _lineFollowController;
  private PlayerInput _playerInput;

  private Vector3 _direction;
  private float _verticalVelocity;
  private string _previousActionMap;
  private LinePath _lineAfterWalk;

  private void Start() {
    if (player == null) player = GameObject.FindWithTag("Player");
    if (player == null) {
      Debug.LogWarning($"[IntroWalkCutscene] '{name}': Player not found. Assign in inspector or tag as 'Player'.", this);
      return;
    }

    _cc = player.GetComponent<CharacterController>();
    if (_cc == null) {
      Debug.LogWarning($"[IntroWalkCutscene] '{name}': player has no CharacterController — nothing to walk.", this);
      return;
    }

    _animator = player.GetComponent<Animator>();
    _lineFollowController = player.GetComponent<LineFollowController>();
    _playerInput = player.GetComponent<PlayerInput>();

    _lineAfterWalk = completionLine != null
      ? completionLine
      : (_lineFollowController != null ? _lineFollowController.currentLine : null);

    bool hasAuthoredWalk = authoredStart != null && authoredEnd != null;
    if (hasAuthoredWalk) {
      // Azzerare currentLine qui rende irrilevante l'ordine di Start tra i componenti della scena.
      if (_lineFollowController != null) _lineFollowController.currentLine = null;
      PlacePlayerFeetAt(authoredStart.position);

      Vector3 authoredDelta = authoredEnd.position - authoredStart.position;
      authoredDelta.y = 0f;
      if (authoredDelta.sqrMagnitude > 0.0001f) {
        direction = authoredDelta.normalized;
        walkDistance = authoredDelta.magnitude;
      }
    }

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
    ShowCompletionInformation();

  }

  private void ShowCompletionInformation() {
    if (string.IsNullOrWhiteSpace(completionInformation)) return;

    if (DialogueHUD.Instance == null) {
      Debug.LogWarning($"[IntroWalkCutscene] '{name}': no DialogueHUD found for the completion tutorial.", this);
      return;
    }

    DialogueHUD.Instance.ShowInformation(completionInformation);

    if (completionInformationDismissDistance > 0f) {
      StartCoroutine(ClearCompletionInformationAfterMovement());
    }
  }

  private IEnumerator ClearCompletionInformationAfterMovement() {
    const float MovementEpsilon = 0.0001f;
    Vector3 previousPosition = player.transform.position;
    float consecutiveRightwardDistance = 0f;

    while (player != null) {
      yield return null;

      if (player == null) break;
      Vector3 currentPosition = player.transform.position;
      float rightwardStep = Vector3.Dot(currentPosition - previousPosition, _direction);
      if (rightwardStep > MovementEpsilon) consecutiveRightwardDistance += rightwardStep;
      else consecutiveRightwardDistance = 0f;
      previousPosition = currentPosition;

      if (consecutiveRightwardDistance >= completionInformationDismissDistance) break;
    }

    DialogueHUD.Instance?.ClearInformationIfMatches(completionInformation);
  }

  private void ReattachLineFollowController() {
    if (_lineFollowController == null) return;

    if (_lineAfterWalk != null) _lineFollowController.currentLine = _lineAfterWalk;

    if (_lineFollowController.currentLine != null) {
      float dist = _lineFollowController.currentLine.FindClosestDistance(player.transform.position, out _, out _, out int strand);
      _lineFollowController.SetLine(_lineFollowController.currentLine, strand, dist);
    }

    _lineFollowController.movementEnabled = true;
  }

  private void PlacePlayerFeetAt(Vector3 feetPosition) {
    Vector3 currentFeet = _lineFollowController != null ? _lineFollowController.FeetPosition : player.transform.position;
    Vector3 rootPosition = player.transform.position + feetPosition - currentFeet;
    bool controllerWasEnabled = _cc.enabled;
    if (controllerWasEnabled) _cc.enabled = false;
    player.transform.position = rootPosition;
    if (controllerWasEnabled) _cc.enabled = true;
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
      Debug.LogWarning($"[IntroWalkCutscene] '{name}': PlayerInput has no 'UI' action map — input not blocked during the walk.", this);
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
