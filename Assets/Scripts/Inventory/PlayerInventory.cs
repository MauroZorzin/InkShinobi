using System;
using UnityEngine;

/// <summary>Per-instance data kept separate from the shared ItemDefinition asset.</summary>
[Serializable]
public sealed class InventoryItemInstance {
  public ItemDefinition Definition { get; }
  public string ItemId { get; }
  public bool HasColorOverride { get; }
  public Color DisplayColor { get; }

  public InventoryItemInstance(
      ItemDefinition definition,
      string itemId = null,
      bool hasColorOverride = false,
      Color displayColor = default) {
    Definition = definition;
    ItemId = string.IsNullOrWhiteSpace(itemId)
        ? definition != null ? definition.itemId : string.Empty
        : itemId.Trim();
    HasColorOverride = hasColorOverride;
    DisplayColor = hasColorOverride ? displayColor : Color.white;
  }
}

/// <summary>
/// Single-slot inventory. It owns runtime item identity and presentation data and supports pickup
/// and consumption.
/// </summary>
public class PlayerInventory : MonoBehaviour {
  public event Action<InventoryItemInstance> ItemInstanceChanged;

  public InventoryItemInstance CurrentItemInstance { get; private set; }
  public string CurrentItemId => CurrentItemInstance != null ? CurrentItemInstance.ItemId : string.Empty;
  public bool IsHoldingItem => CurrentItemInstance != null;

  /// <summary>Checks the runtime id of the carried item. Empty ids mean no requirement.</summary>
  public bool HasItem(string itemId) {
    if (string.IsNullOrWhiteSpace(itemId)) {
      return true;
    }

    return CurrentItemInstance != null
        && string.Equals(CurrentItemInstance.ItemId, itemId.Trim(), StringComparison.OrdinalIgnoreCase);
  }

  public bool TryPickUp(ItemDefinition item) {
    return TryPickUp(new InventoryItemInstance(item));
  }

  public bool TryPickUp(ItemDefinition item, string itemId, bool hasColorOverride, Color displayColor) {
    return TryPickUp(new InventoryItemInstance(item, itemId, hasColorOverride, displayColor));
  }

  public bool TryPickUp(InventoryItemInstance itemInstance) {
    if (itemInstance == null || itemInstance.Definition == null || IsHoldingItem) {
      return false;
    }

    CurrentItemInstance = itemInstance;
    NotifyItemInstanceChanged();
    return true;
  }

  public void ConsumeItem() {
    if (!IsHoldingItem) {
      return;
    }

    CurrentItemInstance = null;
    NotifyItemInstanceChanged();
  }

  private void NotifyItemInstanceChanged() {
    ItemInstanceChanged?.Invoke(CurrentItemInstance);
  }
}
