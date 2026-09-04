using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Applies a door's authored key color only to selected material slots on its moving panels.
/// The dedicated door-accent shader exposes those submeshes to the selective-color pass while
/// the wooden parts of the same renderers remain monochrome.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(PassagewayDoor))]
public sealed class DoorKeyColorVisual : MonoBehaviour {
  // The paper texture is warm rather than neutral. Compensating its average colour keeps the
  // authored key hue after the texture is multiplied by the tint in the door-paper material.
  private static readonly Color PaperTextureNeutral = new(0.74036f, 0.65923f, 0.48930f, 1f);
  // Both paper meshes have the same imported normal, while the right panel is rotated 180 degrees.
  // The door-paper shader modulates this contribution by the texture, keeping the key cue
  // readable on both halves without flattening the paper pattern.
  private const float PanelEmissionStrength = 0.05f;
  private static readonly Color DefaultHandleColor = Color.white;
  private static readonly Color AvailableHandleColor = Color.green;
  private static readonly Color UnavailableHandleColor = Color.red;
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
  private static readonly List<DoorKeyColorVisual> ActiveVisuals = new();

  [Header("Accent Material Slots")]
  [Tooltip("Keep ordinary unlocked doors white. Explicitly locked doors use Required Key Color and retain it after being unlocked.")]
  [SerializeField] private bool colorOnlyWhenStartsLocked = true;

  [Tooltip("Material slots on the left panel that represent its paper panel.")]
  [SerializeField] private int[] leftPanelSlots = { 1 };

  [Tooltip("Material slots on the right panel that represent its paper panel.")]
  [SerializeField] private int[] rightPanelSlots = { 2 };

  [Header("Handle Interaction Feedback")]
  [SerializeField] private int[] leftHandleSlots = { 2 };
  [SerializeField] private int[] rightHandleSlots = { 0 };

  private PassagewayDoor door;
  private MaterialPropertyBlock propertyBlock;
  private bool handleFocused;
  private bool handleInteractionAllowed;

  private bool ShouldPreservePanelColor =>
    door != null && door.RequiredKey != null && (!colorOnlyWhenStartsLocked || door.StartsLocked);

  private void OnEnable() {
    if (!ActiveVisuals.Contains(this)) ActiveVisuals.Add(this);
    Apply();
  }

  private void OnDisable() => ActiveVisuals.Remove(this);

  private void OnDestroy() => ActiveVisuals.Remove(this);

#if UNITY_EDITOR
  private void OnValidate() => Apply();
#endif

  [ContextMenu("Apply Door Key Color")]
  public void Apply() {
    if (door == null) door = GetComponent<PassagewayDoor>();
    if (door == null) return;

    Renderer leftRenderer = ResolveRenderer(door.LeftDoorPanel);
    Renderer rightRenderer = ResolveRenderer(door.RightDoorPanel);

    // Shared materials are authored on SlidingDoor.prefab. This component only uses property
    // blocks, so ExecuteAlways preview cannot create per-instance material overrides in scenes.
    if (ShouldPreservePanelColor) {
      Color paperTint = CompensateForPaperTexture(door.RequiredKeyColor);
      ApplyTint(leftRenderer, leftPanelSlots, paperTint, paperTint, PanelEmissionStrength);
      ApplyTint(rightRenderer, rightPanelSlots, paperTint, paperTint, PanelEmissionStrength);
    } else {
      ClearSlots(leftRenderer, leftPanelSlots);
      ClearSlots(rightRenderer, rightPanelSlots);
    }

    Color handleColor = ResolveHandleColor();
    ApplyTint(leftRenderer, leftHandleSlots, handleColor, Color.black, 0f);
    ApplyTint(rightRenderer, rightHandleSlots, handleColor, Color.black, 0f);
  }

  private Color ResolveHandleColor() {
    // Animation is authoritative: transitional states never advertise another interaction.
    if (door.IsAnimating) return DefaultHandleColor;

    // A focused door gives explicit validity feedback. This also covers standing on one
    // of the door's own paths and trying to use a locked door without its matching key.
    if (handleFocused)
      return handleInteractionAllowed ? AvailableHandleColor : UnavailableHandleColor;

    return DefaultHandleColor;
  }

  public void SetHandleInteractionState(bool focused, bool interactionAllowed) {
    if (handleFocused == focused && handleInteractionAllowed == interactionAllowed) return;
    handleFocused = focused;
    handleInteractionAllowed = interactionAllowed;
    Apply();
  }

  private static Color CompensateForPaperTexture(Color color) {
    float red = color.r / PaperTextureNeutral.r;
    float green = color.g / PaperTextureNeutral.g;
    float blue = color.b / PaperTextureNeutral.b;
    float scale = Mathf.Max(red, green, blue);
    if (scale <= Mathf.Epsilon) return new Color(0f, 0f, 0f, color.a);
    return new Color(red / scale, green / scale, blue / scale, color.a);
  }

  private void ApplyTint(
    Renderer targetRenderer,
    int[] materialSlots,
    Color baseColor,
    Color emissionColor,
    float emissionStrength) {
    if (targetRenderer == null || materialSlots == null) return;

    propertyBlock ??= new MaterialPropertyBlock();
    Material[] materials = targetRenderer.sharedMaterials;
    int materialCount = materials.Length;
    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materialCount) continue;

      propertyBlock.Clear();
      targetRenderer.GetPropertyBlock(propertyBlock, slot);
      Material material = materials[slot];
      float alpha = material != null && material.HasProperty(ColorId)
        ? material.GetColor(ColorId).a
        : 1f;
      Color materialTint = new(baseColor.r, baseColor.g, baseColor.b, alpha);
      if (material != null && material.HasProperty(BaseColorId))
        propertyBlock.SetColor(BaseColorId, materialTint);
      if (material != null && material.HasProperty(ColorId))
        propertyBlock.SetColor(ColorId, materialTint);
      if (material != null && material.HasProperty(EmissionColorId))
        propertyBlock.SetColor(EmissionColorId, new Color(
          emissionColor.r * emissionStrength,
          emissionColor.g * emissionStrength,
          emissionColor.b * emissionStrength,
          1f));
      targetRenderer.SetPropertyBlock(propertyBlock, slot);
    }
  }

  private void ClearSlots(Renderer targetRenderer, int[] materialSlots) {
    if (targetRenderer == null || materialSlots == null) return;
    int materialCount = targetRenderer.sharedMaterials.Length;
    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materialCount) continue;
      targetRenderer.SetPropertyBlock(null, slot);
    }
  }

  private static Renderer ResolveRenderer(Transform panel) =>
    panel != null ? panel.GetComponent<Renderer>() : null;

  internal static void DrawActiveAccentMasks(RasterCommandBuffer command, Material maskMaterial) {
    if (command == null || maskMaterial == null) return;

    for (int i = ActiveVisuals.Count - 1; i >= 0; i--) {
      DoorKeyColorVisual visual = ActiveVisuals[i];
      if (visual == null) {
        ActiveVisuals.RemoveAt(i);
        continue;
      }
      if (!visual.isActiveAndEnabled || visual.door == null) continue;

      if (visual.ShouldPreservePanelColor) {
        DrawPanelMask(command, ResolveRenderer(visual.door.LeftDoorPanel), visual.leftPanelSlots, maskMaterial);
        DrawPanelMask(command, ResolveRenderer(visual.door.RightDoorPanel), visual.rightPanelSlots, maskMaterial);
      }
      DrawPanelMask(command, ResolveRenderer(visual.door.LeftDoorPanel), visual.leftHandleSlots, maskMaterial);
      DrawPanelMask(command, ResolveRenderer(visual.door.RightDoorPanel), visual.rightHandleSlots, maskMaterial);
    }
  }

  private static void DrawPanelMask(
    RasterCommandBuffer command,
    Renderer targetRenderer,
    int[] materialSlots,
    Material maskMaterial) {
    if (targetRenderer == null || !targetRenderer.enabled || materialSlots == null) return;

    int materialCount = targetRenderer.sharedMaterials.Length;
    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materialCount) continue;
      command.DrawRenderer(targetRenderer, maskMaterial, slot, 0);
    }
  }
}
