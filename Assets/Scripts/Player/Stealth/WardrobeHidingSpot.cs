using UnityEngine;

/// <summary>An authored wardrobe endpoint; the player owns all hiding state and transitions.</summary>
[DisallowMultipleComponent]
public sealed class WardrobeHidingSpot : MonoBehaviour, IInteractable {
  [Header("Anchors")]
  [SerializeField] private Transform hidePoint;
  [SerializeField] private Transform effectPoint;

  [Header("Ink")]
  [SerializeField] private GameObject inkCloudPrefab;
  [SerializeField, Min(0.1f)] private float inkCloudScale = 1.8f;
  [Tooltip("Moves the cloud from its effect anchor toward HidePoint, keeping it in front of the wardrobe.")]
  [SerializeField, Range(0f, 1f)] private float inkCloudHidePointBias = 0.65f;
  [Tooltip("Additional world-space height that keeps the cloud visible above the wardrobe base.")]
  [SerializeField] private float inkCloudVerticalOffset = 0.12f;

  private PlayerHidingController occupant;

  public Transform HidePoint => hidePoint;
  public Transform EffectPoint => effectPoint != null ? effectPoint : hidePoint;

  public void Interact(PlayerInventory inventory) {
    if (occupant != null || inventory == null) return;
    PlayerHidingController player = inventory.GetComponent<PlayerHidingController>();
    if (player != null) player.TryEnter(this);
  }

  public bool TryOccupy(PlayerHidingController player) {
    if (player == null || occupant != null) return false;
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

  // Intentionally no visual rejection flash: the wardrobe no longer uses a duplicate outline
  // mesh. The interaction prompt disappearing while the player is seen is sufficient for now.
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
