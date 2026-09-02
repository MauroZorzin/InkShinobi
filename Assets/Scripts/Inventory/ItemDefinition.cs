using UnityEngine;
using UnityEngine.Audio;

public enum InventoryItemCategory {
  Throwable,
  Key
}

/// <summary>
/// Data asset identifying an item type: what it's called, its HUD icon, and what it looks like
/// when dropped in the world.
/// </summary>
[CreateAssetMenu(menuName = "Ink Shinobi/Item")]
public class ItemDefinition : ScriptableObject {
  [Tooltip("How gameplay abilities may use this item. Keys cannot be thrown as distractions.")]
  public InventoryItemCategory category = InventoryItemCategory.Throwable;

  [Tooltip("Stable id used by item requirement checks.")]
  public string itemId;

  [Tooltip("Human-readable item name.")]
  public string displayName;

  [Tooltip("Shown in the HUD slot while this item is carried.")]
  public Texture icon;

  [Tooltip("Prefab instantiated in the world when this item is dropped. Needs a WorldItem component on it referencing this same ItemDefinition.")]
  public GameObject worldPrefab;

  [Tooltip("Projectile launched when this item is used by finite distraction mode. Required for Throwable items and ignored for Keys.")]
  public ThrownDistraction distractionProjectilePrefab;

  [Header("Audio")]
  [Tooltip("Played at the pickup's position when this item is picked up.")]
  public AudioClip pickupSound;
  [Range(0f, 1f)] public float pickupSoundVolume = 1f;
  [Tooltip("Mixer group pickupSound is routed through. Leave empty to go straight to Master.")]
  public AudioMixerGroup pickupSoundMixerGroup;
}
