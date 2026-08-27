using System;
using UnityEngine;
using UnityEngine.InputSystem;

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
/// Single-slot inventory. It owns runtime item identity and presentation data, supports pickup and
/// consumption, and drops the held item when the Drop input is performed. Aiming belongs to separate abilities.
/// </summary>
public class PlayerInventory : MonoBehaviour {
  [Header("Drop")]
  [Tooltip("Point items are dropped from. Leave empty to use this transform.")]
  public Transform dropPoint;

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

#pragma warning disable IDE0051
  private void OnDrop(InputValue value) {
    if (value.isPressed) TryDrop();
  }
#pragma warning restore IDE0051

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

  public bool TryDrop() {
    if (!IsHoldingItem) {
      return false;
    }

    InventoryItemInstance itemInstance = CurrentItemInstance;
    ItemDefinition definition = itemInstance.Definition;
    if (definition.worldPrefab != null) {
      Vector3 position = dropPoint != null ? dropPoint.position : transform.position;
      GameObject spawned = Instantiate(definition.worldPrefab, position, Quaternion.identity);
      WorldItem worldItem = spawned.GetComponent<WorldItem>();
      if (worldItem != null) {
        worldItem.ConfigureRuntimeIdentity(
          itemInstance.ItemId,
          itemInstance.HasColorOverride,
          itemInstance.DisplayColor);
      }
      Debug.Log($"[PlayerInventory] Dropped '{definition.displayName}' -> spawned '{spawned.name}' at {position:F2}.");
    } else {
      Debug.LogWarning($"[PlayerInventory] '{definition.displayName}' has no World Prefab assigned - dropping it only clears the slot.");
    }

    CurrentItemInstance = null;
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
