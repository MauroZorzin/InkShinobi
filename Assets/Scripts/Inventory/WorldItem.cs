using UnityEngine;

/// <summary>An item lying in the world, ready to be picked up by PlayerInventory via PlayerInteractor.</summary>
public class WorldItem : MonoBehaviour, IInteractable {
  public ItemDefinition item;

  [Tooltip("When disabled, this object remains as a reusable pickup source after a successful pickup.")]
  public bool destroyOnPickup = true;

  public void Interact(PlayerInventory inventory) {
    if (item == null || inventory == null || !inventory.TryPickUp(item)) {
      return;
    }

    if (destroyOnPickup) Destroy(gameObject);
  }
}
