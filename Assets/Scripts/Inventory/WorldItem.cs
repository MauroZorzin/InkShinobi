using UnityEngine;

/// <summary>An item lying in the world, ready to be picked up by PlayerInventory via PlayerInteractor.</summary>
public class WorldItem : MonoBehaviour, IInteractable {
  public ItemDefinition item;

  public void Interact(PlayerInventory inventory) {
    if (item == null || inventory == null || !inventory.TryPickUp(item)) {
      return;
    }

    Destroy(gameObject);
  }
}
