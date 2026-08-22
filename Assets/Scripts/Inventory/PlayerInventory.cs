using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single-slot inventory. Pickup happens via PlayerInteractor finding a WorldItem (IInteractable) and
/// calling TryPickUp. Drop (right mouse) drops whatever is currently held in place, or throws it at
/// the current aim point (see ThrownItem) if AimSwitch is aiming when Drop is pressed.
/// </summary>
public class PlayerInventory : MonoBehaviour {
  [Header("Drop")]
  [Tooltip("Point items are dropped from. Leave empty to use this transform.")]
  public Transform dropPoint;

  [Header("Throw")]
  [Tooltip("If aiming when Drop is pressed, the item is thrown at the aim point instead of dropped in place. Defaults to this GameObject's AimSwitch if left empty.")]
  public AimSwitch aimSwitch;

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

  private void Awake() {
    if (aimSwitch == null) aimSwitch = GetComponent<AimSwitch>();
  }

#pragma warning disable IDE0051
  private void OnDrop(InputValue value) {
    if (!value.isPressed) return;

    bool aiming = aimSwitch != null && aimSwitch.IsAiming;
    Debug.Log($"[PlayerInventory] OnDrop pressed. aimSwitch={(aimSwitch != null ? aimSwitch.name : "NULL")}, IsAiming={aiming} -> {(aiming ? "TryThrow" : "TryDrop")}");

    if (aiming) TryThrow();
    else TryDrop();
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

    SpawnWorldPrefab();
    CurrentItem = null;
    ItemChanged?.Invoke(null);
    return true;
  }

  /// <summary>Throws the carried item toward the current aim point (a distraction) and clears the slot.</summary>
  public bool TryThrow() {
    if (!IsHoldingItem) {
      return false;
    }

    GameObject spawned = SpawnWorldPrefab();
    ThrownItem thrown = spawned != null ? spawned.GetComponent<ThrownItem>() : null;
    Debug.Log($"[PlayerInventory] TryThrow: spawned={(spawned != null ? spawned.name : "NULL")}, ThrownItem={(thrown != null ? "found" : "MISSING")}, aimSwitch={(aimSwitch != null ? "ok" : "NULL")}");

    if (thrown != null && aimSwitch != null) {
      thrown.Launch(aimSwitch.AimWorldPoint);
    }

    CurrentItem = null;
    ItemChanged?.Invoke(null);
    return true;
  }

  private GameObject SpawnWorldPrefab() {
    if (CurrentItem.worldPrefab == null) {
      Debug.LogWarning($"[PlayerInventory] '{CurrentItem.displayName}' has no World Prefab assigned — nothing spawns in the scene.");
      return null;
    }

    Vector3 position = dropPoint != null ? dropPoint.position : transform.position;
    return Instantiate(CurrentItem.worldPrefab, position, Quaternion.identity);
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
