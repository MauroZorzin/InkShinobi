using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores the player's carried items and exposes the currently selected item.
/// </summary>
public class PlayerInventory : MonoBehaviour {
  [Tooltip("Maximum number of items the inventory can hold.")]
  [SerializeField] private int maxItems = 1;

  public event Action<ItemDefinition> OnSelectedItemChanged;

  private readonly List<ItemDefinition> items = new();
  private int selectedIndex = -1;

  /// <summary>
  /// Gets the currently selected item, or null when the inventory is empty.
  /// </summary>
  public ItemDefinition SelectedItem {
    get {
      if (selectedIndex < 0 || selectedIndex >= items.Count) {
        return null;
      }
      return items[selectedIndex];
    }
  }

  public bool HasItems => items.Count > 0;
  public bool IsFull => items.Count >= maxItems;

  /// <summary>
  /// Checks whether the inventory contains an item with the requested id.
  /// </summary>
  /// <param name="itemId">The item id to search for. Empty ids are treated as no requirement.</param>
  /// <returns>True when the item is present, or when no item id is required.</returns>
  public bool HasItem(string itemId) {
    if (string.IsNullOrWhiteSpace(itemId)) {
      return true;
    }

    foreach (ItemDefinition item in items) {
      if (item == null) {
        continue;
      }

      if (string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Adds an item to the inventory and selects it when there is room.
  /// </summary>
  /// <param name="item">The item definition to add.</param>
  /// <returns>True when the item was accepted.</returns>
  public bool TryPickUp(ItemDefinition item) {
    if (item == null) {
      return false;
    }

    if (IsFull) {
      Debug.Log("Inventory is full.");
      return false;
    }

    items.Add(item);
    selectedIndex = items.Count - 1;

    OnSelectedItemChanged?.Invoke(SelectedItem);
    return true;
  }

  /// <summary>
  /// Removes the selected item and moves selection to a remaining item when possible.
  /// </summary>
  public void RemoveSelectedItem() {
    if (!HasItems) {
      return;
    }

    items.RemoveAt(selectedIndex);

    if (items.Count == 0) {
      selectedIndex = -1;
    } else {
      selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
    }

    OnSelectedItemChanged?.Invoke(SelectedItem);
  }

  /// <summary>
  /// Selects the next carried item, wrapping back to the first item.
  /// </summary>
  public void SelectNext() {
    if (items.Count <= 1) {
      return;
    }

    selectedIndex = (selectedIndex + 1) % items.Count;
    OnSelectedItemChanged?.Invoke(SelectedItem);
  }

  /// <summary>
  /// Selects the previous carried item, wrapping to the last item.
  /// </summary>
  public void SelectPrevious() {
    if (items.Count <= 1) {
      return;
    }

    selectedIndex--;

    if (selectedIndex < 0) {
      selectedIndex = items.Count - 1;
    }

    OnSelectedItemChanged?.Invoke(SelectedItem);
  }
}
