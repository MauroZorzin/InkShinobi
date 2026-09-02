using UnityEngine;
using UnityEngine.Audio;

/// <summary>An authored hiding endpoint; the player owns all hiding state and transitions.</summary>
[DisallowMultipleComponent]
public sealed class HidingSpot : MonoBehaviour, IInteractable, IInteractionPrompt {
  [Header("Anchors")]
  [SerializeField] private Transform hidePoint;
  [SerializeField] private Transform effectPoint;

  [Header("Ink")]
  [SerializeField] private GameObject inkCloudPrefab;
  [SerializeField, Min(0.1f)] private float inkCloudScale = 1.8f;
  [Tooltip("Moves the cloud from its effect anchor toward HidePoint, keeping it in front of the hiding geometry.")]
  [SerializeField, Range(0f, 1f)] private float inkCloudHidePointBias = 0.65f;
  [Tooltip("Additional world-space height that keeps the cloud visible above the hiding spot's base.")]
  [SerializeField] private float inkCloudVerticalOffset = 0.12f;

  [Header("Audio")]
  [SerializeField] private AudioClip enterSound;
  [SerializeField] private AudioClip exitSound;
  [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;
  [SerializeField] private AudioMixerGroup mixerGroup;

  [Header("Prompts")]
  [SerializeField] private string canHidePromptText = "[X] to hide";
  [SerializeField] private string cannotHidePromptText = "Can't hide now";
  [SerializeField] private string hiddenPromptText = "[X] to exit";

  [Header("Presentation")]
  [Tooltip("Weight applied to the player's hidden vignette while occupying this spot.")]
  [SerializeField, Range(0f, 1f)] private float hiddenVignetteWeight = 1f;

  private PlayerHidingController occupant;

  public Transform HidePoint => hidePoint;
  public Transform EffectPoint => effectPoint != null ? effectPoint : hidePoint;
  public float HiddenVignetteWeight => hiddenVignetteWeight;

  public void Interact(PlayerInventory inventory) {
    if (occupant != null || inventory == null) return;
    PlayerHidingController player = inventory.GetComponent<PlayerHidingController>();
    if (player != null) player.TryEnter(this);
  }

  public string GetPromptText(PlayerInventory inventory) {
    PlayerHidingController player = inventory != null
      ? inventory.GetComponent<PlayerHidingController>()
      : null;
    if (player != null && player.CurrentSpot == this && player.IsConcealed)
      return hiddenPromptText;
    return player != null && player.CanEnter(this)
      ? canHidePromptText
      : cannotHidePromptText;
  }

  public bool CanOccupy(PlayerHidingController player) => player != null && occupant == null;

  public bool TryOccupy(PlayerHidingController player) {
    if (!CanOccupy(player)) return false;
    occupant = player;
    return true;
  }

  public void Release(PlayerHidingController player) {
    if (occupant != player) return;
    occupant = null;
  }

  public void PlayInkEffect() {
    if (inkCloudPrefab == null) return;
    Transform anchor = EffectPoint != null ? EffectPoint : transform;
    Vector3 spawnPosition = hidePoint != null
      ? Vector3.Lerp(anchor.position, hidePoint.position, inkCloudHidePointBias)
      : anchor.position;
    spawnPosition += Vector3.up * inkCloudVerticalOffset;
    GameObject instance = Instantiate(inkCloudPrefab, spawnPosition, anchor.rotation);
    instance.transform.localScale *= inkCloudScale;
    PauseAwareUnscaledParticles.Configure(instance);
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    float lifetime = 1.5f;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      main.useUnscaledTime = true;
      lifetime = Mathf.Max(lifetime, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
      particles[i].Play(true);
    }
    Destroy(instance, lifetime + 0.25f);
  }

  public void PlayEnterFeedback() {
    PlayInkEffect();
    PlaySound(enterSound);
  }

  public void PlayExitFeedback() {
    PlayInkEffect();
    PlaySound(exitSound);
  }

  private void PlaySound(AudioClip clip) {
    if (clip != null)
      OneShotAudio.PlayClipAtPoint(clip, transform.position, soundVolume, mixerGroup);
  }

  // The state-aware prompt communicates rejection without requiring a duplicate outline mesh.
  public void ShowRejectedFeedback() { }

#if UNITY_EDITOR
  public void Configure(
    Transform authoredHidePoint,
    Transform authoredEffectPoint,
    GameObject authoredInkCloud) {
    hidePoint = authoredHidePoint;
    effectPoint = authoredEffectPoint;
    inkCloudPrefab = authoredInkCloud;
  }
#endif
}