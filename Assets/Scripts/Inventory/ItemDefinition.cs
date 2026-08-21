using UnityEngine;

/// <summary>
/// Data asset identifying an item type: what it's called, its HUD icon, and what it looks like
/// when dropped in the world.
/// </summary>
[CreateAssetMenu(menuName = "Ink Shinobi/Item")]
public class ItemDefinition : ScriptableObject {
  [Tooltip("Stable id used by item requirement checks.")]
  public string itemId;

  [Tooltip("Human-readable item name.")]
  public string displayName;

  [Tooltip("Shown in the HUD slot while this item is carried.")]
  public Texture icon;

  [Tooltip("Prefab instantiated in the world when this item is dropped. Needs a WorldItem component on it referencing this same ItemDefinition.")]
  public GameObject worldPrefab;
}
