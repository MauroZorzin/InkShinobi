using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows the carried item's icon texture directly — no 3D preview render, just ItemDefinition.icon.</summary>
public class ItemSlotUI : MonoBehaviour {
  [Tooltip("Inventory whose carried item drives this slot.")]
  [SerializeField] private PlayerInventory inventory;

  [Header("Visibility")]
  [Tooltip("When enabled, the empty slot background is visible as soon as the scene starts. When disabled, the whole presentation appears only while an item is carried.")]
  [SerializeField] private bool visibleWhenEmpty = true;

  [Tooltip("Background graphic hidden together with an empty slot. The ItemSlot GameObject itself remains active so it can keep listening for pickups.")]
  [SerializeField] private Graphic slotBackground;

  [Tooltip("RawImage that displays the carried item's icon.")]
  [SerializeField] private RawImage itemIcon;

  private void Awake() {
    UpdateSlot(inventory != null ? inventory.CurrentItemInstance : null);
  }

  private void OnEnable() {
    if (inventory == null) {
      return;
    }
    inventory.ItemInstanceChanged += UpdateSlot;
    UpdateSlot(inventory.CurrentItemInstance);
  }

  private void OnDisable() {
    if (inventory == null) {
      return;
    }
    inventory.ItemInstanceChanged -= UpdateSlot;
  }

  private void UpdateSlot(InventoryItemInstance itemInstance) {
    ItemDefinition item = itemInstance != null ? itemInstance.Definition : null;
    bool hasVisibleItem = item != null && item.icon != null;

    if (slotBackground != null) {
      slotBackground.enabled = visibleWhenEmpty || hasVisibleItem;
    }

    if (itemIcon == null || !hasVisibleItem) {
      if (itemIcon != null) {
        itemIcon.enabled = false;
        itemIcon.color = Color.white;
      }
      return;
    }

    itemIcon.enabled = true;
    itemIcon.texture = item.icon;
    itemIcon.color = itemInstance.HasColorOverride ? itemInstance.DisplayColor : Color.white;
  }
}
