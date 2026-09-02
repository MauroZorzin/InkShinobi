using UnityEngine;

/// <summary>An item lying in the world, ready to be picked up by PlayerInventory via PlayerInteractor.</summary>
public class WorldItem : MonoBehaviour, IInteractable, IInteractionPriority, IInteractionPrompt {
  // Pickups win over larger interaction volumes (e.g. doors) that happen to be geometrically closer.
  private const int PickupPriority = 1;

  public ItemDefinition item;

  [SerializeField] private string pickupPromptText = "[X] to pickup";

  [Tooltip("When disabled, this object remains as a reusable pickup source after a successful pickup.")]
  public bool destroyOnPickup = true;

  [Header("Runtime identity")]
  [Tooltip("Optional authored runtime id. Guard drops set this automatically; use it for keys placed directly in a scene.")]
  [SerializeField] private string runtimeItemId;
  [Tooltip("Tint this shared world prefab with Runtime Color.")]
  [SerializeField] private bool hasColorOverride;
  [SerializeField] private Color runtimeColor = Color.white;

  public string EffectiveItemId => string.IsNullOrWhiteSpace(runtimeItemId)
      ? item != null ? item.itemId : string.Empty
      : runtimeItemId;
  public int Priority => PickupPriority;

  private void Awake() => ApplyRuntimePresentation();

#if UNITY_EDITOR
  private void OnValidate() => ApplyRuntimePresentation();
#endif

  public string GetPromptText(PlayerInventory inventory) => pickupPromptText;

  public void ConfigureRuntimeIdentity(string itemId, bool useColorOverride, Color displayColor) {
    runtimeItemId = itemId;
    hasColorOverride = useColorOverride;
    runtimeColor = useColorOverride ? displayColor : Color.white;
    ApplyRuntimePresentation();
  }

  public void Interact(PlayerInventory inventory) {
    if (item == null || inventory == null || !inventory.TryPickUp(item, EffectiveItemId, hasColorOverride, runtimeColor)) {
      return;
    }

    if (item.pickupSound != null)
      OneShotAudio.PlayClipAtPoint(item.pickupSound, transform.position, item.pickupSoundVolume, item.pickupSoundMixerGroup);

    if (destroyOnPickup) Destroy(gameObject);
  }

  private void ApplyRuntimePresentation() {
    if (!hasColorOverride) {
      return;
    }

    SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
    for (int i = 0; i < renderers.Length; i++) {
      renderers[i].color = runtimeColor;
      renderers[i].renderingLayerMask |= SelectiveColor.RenderingLayerMask;
    }
  }
}
