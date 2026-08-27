using UnityEngine;

/// <summary>An item lying in the world, ready to be picked up by PlayerInventory via PlayerInteractor.</summary>
public class WorldItem : MonoBehaviour, IInteractable, IInteractionRange {
  public ItemDefinition item;

  [Tooltip("When disabled, this object remains as a reusable pickup source after a successful pickup.")]
  public bool destroyOnPickup = true;

  [Tooltip("Maximum pickup distance for this item. Set to 0 to use the PlayerInteractor default range.")]
  [SerializeField, Min(0f)] private float pickupInteractionRange;

  private string runtimeItemId;
  private bool hasColorOverride;
  private Color runtimeColor = Color.white;

  public string EffectiveItemId => string.IsNullOrWhiteSpace(runtimeItemId)
      ? item != null ? item.itemId : string.Empty
      : runtimeItemId;
  public float InteractionRange => pickupInteractionRange;

  public void ConfigureRuntimeIdentity(string itemId, bool useColorOverride, Color displayColor) {
    runtimeItemId = itemId;
    hasColorOverride = useColorOverride;
    runtimeColor = useColorOverride ? displayColor : Color.white;
    ApplyRuntimePresentation();
  }

  public void Interact(PlayerInventory inventory) {
    if (item == null
        || inventory == null
        || !inventory.TryPickUp(item, EffectiveItemId, hasColorOverride, runtimeColor)) {
      return;
    }

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
