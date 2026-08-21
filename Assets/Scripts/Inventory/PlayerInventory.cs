using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single-slot inventory. Pickup happens via PlayerInteractor finding a WorldItem (IInteractable) and
/// calling TryPickUp. Drop (right mouse) drops whatever is currently held. Item-specific behavior
/// (keys, throwables, etc.) lives elsewhere — this only tracks what's held and handles the drop mechanic.
/// </summary>
public class PlayerInventory : MonoBehaviour {
  [Header("Drop")]
  [Tooltip("Point items are dropped from. Leave empty to use this transform.")]
  public Transform dropPoint;

  public event Action<ItemDefinition> ItemChanged;

  public ItemDefinition CurrentItem { get; private set; }
  public bool IsHoldingItem => CurrentItem != null;

  /// <summary>Checks whether the carried item matches the given id. Empty ids are treated as no requirement.</summary>
  public bool HasItem(string itemId) {
    if (string.IsNullOrWhiteSpace(itemId)) {
      return true;
    }

    return CurrentItem != null && string.Equals(CurrentItem.itemId, itemId, StringComparison.OrdinalIgnoreCase);
  }

#pragma warning disable IDE0051
  private void OnDrop(InputValue value) {
    if (value.isPressed) TryDrop();
  }
#pragma warning restore IDE0051

  /// <summary>Picks up an item. Called by WorldItem.Interact via PlayerInteractor. Fails if already holding one.</summary>
  public bool TryPickUp(ItemDefinition item) {
    if (item == null || IsHoldingItem) {
      return false;
    }

    CurrentItem = item;
    ItemChanged?.Invoke(CurrentItem);
    return true;
  }

  /// <summary>Drops the carried item back into the world (via its worldPrefab, if set) and clears the slot.</summary>
  public bool TryDrop() {
    if (!IsHoldingItem) {
      return false;
    }

    if (CurrentItem.worldPrefab != null) {
      Vector3 position = dropPoint != null ? dropPoint.position : transform.position;
      GameObject spawned = Instantiate(CurrentItem.worldPrefab, position, Quaternion.identity);
      Debug.Log($"[PlayerInventory] Dropped '{CurrentItem.displayName}' -> spawned '{spawned.name}' at {position:F2}.");
    } else {
      Debug.LogWarning($"[PlayerInventory] '{CurrentItem.displayName}' has no World Prefab assigned — dropping it just clears the slot, nothing spawns in the scene.");
    }

    CurrentItem = null;
    ItemChanged?.Invoke(null);
    return true;
  }

  /// <summary>Clears the carried item without spawning it back into the world — for when it's consumed/used up.</summary>
  public void ConsumeItem() {
    if (!IsHoldingItem) {
      return;
    }

    CurrentItem = null;
    ItemChanged?.Invoke(null);
  }
}
