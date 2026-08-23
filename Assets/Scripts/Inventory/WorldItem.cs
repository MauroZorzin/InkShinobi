using UnityEngine;

/// <summary>An item lying in the world, ready to be picked up by PlayerInventory via PlayerInteractor.</summary>
public class WorldItem : MonoBehaviour, IInteractable {
  public ItemDefinition item;

  [Tooltip("If false, this stays in the world after pickup — an infinite source of this item.")]
  public bool destroyOnPickup = true;

  public void Interact(PlayerInventory inventory) {
    if (item == null || inventory == null || !inventory.TryPickUp(item)) {
      return;
    }

    if (destroyOnPickup) {
      Destroy(gameObject);
    }
  }
}
