using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the selected inventory item to a RawImage fed by an ItemIconRenderer.
/// </summary>
public class ItemSlotUI : MonoBehaviour {
  [Tooltip("Inventory whose selected item drives this slot.")]
  [SerializeField] private PlayerInventory inventory;

  [Tooltip("RawImage that displays the renderer's item preview texture.")]
  [SerializeField] private RawImage itemIcon;

  [Tooltip("Renderer responsible for drawing the selected item preview.")]
  [SerializeField] private ItemIconRenderer itemIconRenderer;

  private void Awake() {
    itemIcon.texture = itemIconRenderer.RenderTexture;
    UpdateSlot(inventory != null ? inventory.SelectedItem : null);
  }

  private void OnEnable() {
    if (inventory == null) {
      return;
    }
    inventory.OnSelectedItemChanged += UpdateSlot;
    UpdateSlot(inventory.SelectedItem);
  }

  private void OnDisable() {
    if (inventory == null) {
      return;
    }
    inventory.OnSelectedItemChanged -= UpdateSlot;
  }

  /// <summary>
  /// Refreshes the slot icon to match the selected inventory item.
  /// </summary>
  /// <param name="selectedItem">The item that should be shown, or null to hide the icon.</param>
  private void UpdateSlot(ItemDefinition selectedItem) {
    if (selectedItem == null) {
      itemIcon.enabled = false;
      itemIconRenderer.ShowItem(null);
      return;
    }
    itemIcon.enabled = true;
    itemIcon.texture = itemIconRenderer.RenderTexture;
    itemIconRenderer.ShowItem(selectedItem);
  }
}
