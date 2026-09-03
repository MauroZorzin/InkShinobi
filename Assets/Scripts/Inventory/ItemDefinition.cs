using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Data asset identifying an inventory item: its stable identity, display name, icon, and pickup
/// audio.
/// </summary>
[CreateAssetMenu(menuName = "Ink Shinobi/Item")]
public class ItemDefinition : ScriptableObject {
  [Tooltip("Stable id used by item requirement checks.")]
  public string itemId;

  [Tooltip("Human-readable item name.")]
  public string displayName;

  [Tooltip("Shown in the HUD slot while this item is carried.")]
  public Texture icon;

  [Header("Audio")]
  [Tooltip("Played at the pickup's position when this item is picked up.")]
  public AudioClip pickupSound;
  [Range(0f, 1f)] public float pickupSoundVolume = 1f;
  [Tooltip("Mixer group pickupSound is routed through. Leave empty to go straight to Master.")]
  public AudioMixerGroup pickupSoundMixerGroup;
}
