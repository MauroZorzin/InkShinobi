using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class HidingSpot : MonoBehaviour, IInteractable, IInteractionPrompt {
  [Tooltip("Where the player stands while hidden. Leave empty to use this object's own position.")]
  public Transform hidePoint;

  [Tooltip("Seconds the player takes to glide to/from the hide point.")]
  public float transitionDuration = 0.4f;

  [Tooltip("Particle effect played on both hide and reveal, at this hiding spot's own position.")]
  public ParticleSystem vanishEffect;

  [Tooltip("Sound played when the player hides.")]
  public AudioClip enterSound;
  [Tooltip("Sound played when the player leaves the hiding spot.")]
  public AudioClip exitSound;
  [Range(0f, 1f)] public float soundVolume = 1f;

  [Tooltip("Shown while nobody is hiding here.")]
  public string notHiddenPromptText = "Nascondi";

  [Tooltip("Shown while the player is hiding here.")]
  public string hiddenPromptText = "Esci";

  [Tooltip("Shown while nobody is hiding here but the player is currently detected by a guard.")]
  public string cannotHidePromptText = "Non puoi nasconderti ora";

  private bool _occupied;
  private bool _armedForExit;

  private Transform _player;
  private LineFollowController _lineFollowController;
  private SpriteRenderer _spriteRenderer;
  private BoxCollider _playerCollider;
  private PlayerInteractor _playerInteractor;
  private PlayerStealthController _playerStealthController;
  private InputAction _interactAction;

  private bool _wasLineFollowEnabled;
  private bool _wasSpriteEnabled;
  private bool _wasColliderEnabled;
  private Vector3 _storedPosition;
  private Quaternion _storedRotation;

  public void Interact(PlayerInventory inventory) {
    if (_occupied) {
      return;
    }

    PlayerStealthController stealth = inventory.GetComponent<PlayerStealthController>();
    if (stealth != null && stealth.DetectingGuardCount > 0) {
      return;
    }

    StartHiding(inventory.transform);
  }

  public string GetPromptText(PlayerInventory inventory) {
    if (_occupied) {
      return hiddenPromptText;
    }

    PlayerStealthController stealth = inventory.GetComponent<PlayerStealthController>();
    if (stealth != null && stealth.DetectingGuardCount > 0) {
      return cannotHidePromptText;
    }

    return notHiddenPromptText;
  }

  private void StartHiding(Transform player) {
    _player = player;
    _lineFollowController = player.GetComponent<LineFollowController>();
    _spriteRenderer = player.GetComponent<SpriteRenderer>();
    _playerCollider = player.GetComponent<BoxCollider>();
    _playerInteractor = player.GetComponent<PlayerInteractor>();
    _playerStealthController = player.GetComponent<PlayerStealthController>();

    PlayerInput playerInput = player.GetComponent<PlayerInput>();
    _interactAction = playerInput != null ? playerInput.actions["Interact"] : null;

    if (_lineFollowController != null) {
      _wasLineFollowEnabled = _lineFollowController.enabled;
      _lineFollowController.enabled = false;
    }

    if (_playerCollider != null) {
      _wasColliderEnabled = _playerCollider.enabled;
      _playerCollider.enabled = false;
    }

    if (_playerInteractor != null) {
      _playerInteractor.interactionSuppressed = true;
    }

    _storedPosition = player.position;
    _storedRotation = player.rotation;

    Vector3 target = hidePoint != null ? hidePoint.position : transform.position;
    Quaternion targetRotation = hidePoint != null ? hidePoint.rotation : player.rotation;

    StartCoroutine(Transition(target, targetRotation, OnHideTransitionComplete));
  }

  private void OnHideTransitionComplete() {
    if (_spriteRenderer != null) {
      _wasSpriteEnabled = _spriteRenderer.enabled;
      _spriteRenderer.enabled = false;
    }

    if (vanishEffect != null) {
      OneShotVfx.PlayAtPoint(vanishEffect, transform.position);
    }

    if (enterSound != null) {
      OneShotAudio.PlayClipAtPoint(enterSound, transform.position, soundVolume);
    }

    if (_playerStealthController != null) {
      _playerStealthController.IsUndetectable = true;
    }

    _occupied = true;
    _armedForExit = false;
  }

  private void Update() {
    if (!_occupied || _interactAction == null) {
      return;
    }

    if (!_armedForExit) {
      if (!_interactAction.IsPressed()) {
        _armedForExit = true;
      }
      return;
    }

    if (_interactAction.triggered) {
      StartRevealing();
    }
  }

  private void StartRevealing() {
    _occupied = false;

    if (_spriteRenderer != null) {
      _spriteRenderer.enabled = _wasSpriteEnabled;
    }

    if (_playerStealthController != null) {
      _playerStealthController.IsUndetectable = false;
    }

    if (vanishEffect != null) {
      OneShotVfx.PlayAtPoint(vanishEffect, transform.position);
    }

    if (exitSound != null) {
      OneShotAudio.PlayClipAtPoint(exitSound, transform.position, soundVolume);
    }

    StartCoroutine(Transition(_storedPosition, _storedRotation, OnRevealTransitionComplete));
  }

  private void OnRevealTransitionComplete() {
    if (_lineFollowController != null) {
      _lineFollowController.enabled = _wasLineFollowEnabled;
    }

    if (_playerCollider != null) {
      _playerCollider.enabled = _wasColliderEnabled;
    }

    if (_playerInteractor != null) {
      _playerInteractor.interactionSuppressed = false;
    }

    _interactAction = null;
    _player = null;
    _playerStealthController = null;
  }

  private IEnumerator Transition(Vector3 targetPosition, Quaternion targetRotation, System.Action onComplete) {
    Vector3 startPosition = _player.position;
    Quaternion startRotation = _player.rotation;
    float elapsed = 0f;

    while (elapsed < transitionDuration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / transitionDuration);
      _player.SetPositionAndRotation(Vector3.Lerp(startPosition, targetPosition, t), Quaternion.Slerp(startRotation, targetRotation, t));
      yield return null;
    }

    _player.SetPositionAndRotation(targetPosition, targetRotation);
    onComplete?.Invoke();
  }
}
