using UnityEngine;

/// <summary>
/// World item that transfers its configured item definition into the player's inventory.
/// </summary>
public class PickableItem : MonoBehaviour, IInteractable {
  [Tooltip("Item definition added to the player's inventory when picked up.")]
  [SerializeField] private ItemDefinition item;

  /// <summary>
  /// Attempts to pick up this item and removes the world object on success.
  /// </summary>
  /// <param name="inventory">The inventory receiving the item.</param>
  public void Interact(PlayerInventory inventory) {
    if (inventory.TryPickUp(item)) {
      Destroy(gameObject);
    }
  }
}
