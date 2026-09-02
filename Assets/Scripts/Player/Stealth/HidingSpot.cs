using UnityEngine;
using UnityEngine.Audio;

/// <summary>An authored hiding endpoint; the player owns all hiding state and transitions.</summary>
[DisallowMultipleComponent]
public sealed class HidingSpot : MonoBehaviour, IInteractable, IInteractionCategoryProvider {
  public enum InteractionState { Enter, Exit, Unavailable }
  private static readonly int BendStrengthId = Shader.PropertyToID("_Bend_Strength");

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

  [Header("Presentation")]
  [Tooltip("Weight applied to the player's hidden vignette while occupying this spot.")]
  [SerializeField, Range(0f, 1f)] private float hiddenVignetteWeight = 1f;
  [Tooltip("Prevents shared vegetation bending from deforming this hiding spot's renderers.")]
  [SerializeField] private bool suppressVegetationBend;

  private PlayerHidingController occupant;

  public Transform HidePoint => hidePoint;
  public Transform EffectPoint => effectPoint != null ? effectPoint : hidePoint;
  public float HiddenVignetteWeight => hiddenVignetteWeight;
  public InteractionCategory InteractionCategory => InteractionCategory.HidingSpot;

  private void Awake() {
    if (suppressVegetationBend) ApplyVegetationBendOverride();
  }

  public void Interact(PlayerInventory inventory) {
    if (occupant != null || inventory == null) return;
    PlayerHidingController player = inventory.GetComponent<PlayerHidingController>();
    if (player != null) player.TryEnter(this);
  }

  public InteractionState GetInteractionState(PlayerInventory inventory) {
    PlayerHidingController player = inventory != null
      ? inventory.GetComponent<PlayerHidingController>()
      : null;
    if (player != null && player.CurrentSpot == this && player.IsConcealed)
      return InteractionState.Exit;
    return player != null && player.CanEnter(this)
      ? InteractionState.Enter
      : InteractionState.Unavailable;
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

  public void PlayInkEffect(Vector3 frontReference) {
    if (inkCloudPrefab == null) return;
    Transform anchor = EffectPoint != null ? EffectPoint : transform;
    Vector3 effectPosition = anchor.position;
    if (hidePoint != null && IsFinite(frontReference)) {
      Vector3 hideToFront = frontReference - hidePoint.position;
      Vector3 hideToEffect = effectPosition - hidePoint.position;
      hideToFront.y = 0f;
      hideToEffect.y = 0f;
      if (hideToFront.sqrMagnitude > 0.0001f &&
          Vector3.Dot(hideToEffect, hideToFront) <= 0f) {
        float separation = Mathf.Max(hideToEffect.magnitude, 0.1f);
        Vector3 corrected = hidePoint.position + hideToFront.normalized * separation;
        corrected.y = effectPosition.y;
        effectPosition = corrected;
      }
    }
    Vector3 spawnPosition = hidePoint != null
      ? Vector3.Lerp(effectPosition, hidePoint.position, inkCloudHidePointBias)
      : effectPosition;
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

  public void PlayEnterFeedback(Vector3 frontReference) {
    PlayInkEffect(frontReference);
    PlaySound(enterSound);
  }

  public void PlayExitFeedback(Vector3 frontReference) {
    PlayInkEffect(frontReference);
    PlaySound(exitSound);
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

  private void PlaySound(AudioClip clip) {
    if (clip != null)
      OneShotAudio.PlayClipAtPoint(clip, transform.position, soundVolume, mixerGroup);
  }

  private void ApplyVegetationBendOverride() {
    Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
    var block = new MaterialPropertyBlock();
    for (int i = 0; i < renderers.Length; i++) {
      Renderer renderer = renderers[i];
      renderer.GetPropertyBlock(block);
      block.SetFloat(BendStrengthId, 0f);
      renderer.SetPropertyBlock(block);
      block.Clear();
    }
  }

}
