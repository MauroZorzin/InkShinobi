using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows the carried item's icon texture directly — no 3D preview render, just ItemDefinition.icon.</summary>
public class ItemSlotUI : MonoBehaviour {
  [Tooltip("Inventory whose carried item drives this slot.")]
  [SerializeField] private PlayerInventory inventory;

  [Tooltip("RawImage that displays the carried item's icon.")]
  [SerializeField] private RawImage itemIcon;

  private void Awake() {
    UpdateSlot(inventory != null ? inventory.CurrentItem : null);
  }

  private void OnEnable() {
    if (inventory == null) {
      return;
    }
    inventory.ItemChanged += UpdateSlot;
    UpdateSlot(inventory.CurrentItem);
  }

  private void OnDisable() {
    if (inventory == null) {
      return;
    }
    inventory.ItemChanged -= UpdateSlot;
  }

  private void UpdateSlot(ItemDefinition item) {
    if (item == null || item.icon == null) {
      itemIcon.enabled = false;
      return;
    }

    itemIcon.enabled = true;
    itemIcon.texture = item.icon;
  }
}
