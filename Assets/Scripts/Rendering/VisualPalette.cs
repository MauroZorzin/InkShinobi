using UnityEngine;

/// <summary>
/// Shared selective-color language. Gameplay definitions may provide their own identity color, while
/// common interaction and threat feedback should come from this asset for visual consistency.
/// </summary>
[CreateAssetMenu(fileName = "VisualPalette", menuName = "Ink Shinobi/Rendering/Visual Palette")]
public sealed class VisualPalette : ScriptableObject {
  [Header("Ink and Interaction")]
  [Tooltip("Neutral ink used for ordinary trails, marks, and available actions.")]
  public Color neutralInk = Color.white;

  [Tooltip("Muted feedback for blocked or unavailable actions; pair it with broken motion or shape.")]
  public Color invalid = new(0.32f, 0.34f, 0.36f, 1f);

  [Header("Threat")]
  [Tooltip("Reserved saturated accent for immediate danger, detection, and confirmed hostile actions.")]
  public Color danger = new(0.82f, 0.07f, 0.035f, 1f);

  [Tooltip("Lower-intensity searching/suspicion accent used before confirmed detection.")]
  public Color suspicion = new(0.9f, 0.52f, 0.08f, 1f);

  [Header("Environment")]
  [Tooltip("Default water accent; individual water materials may still vary around it.")]
  public Color water = new(0.02f, 0.55f, 0.78f, 1f);

  [Tooltip("Default illumination color before a LightPoint supplies an authored override.")]
  public Color light = new(1f, 0.72f, 0.28f, 1f);
}
