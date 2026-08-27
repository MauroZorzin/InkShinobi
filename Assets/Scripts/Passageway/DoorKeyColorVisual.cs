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
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
  private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
  private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
  private static readonly List<DoorKeyColorVisual> ActiveVisuals = new();

  [Header("Accent Material Slots")]
  [Tooltip("Keep ordinary unlocked doors white. Explicitly locked doors use Required Key Color and retain it after being unlocked.")]
  [SerializeField] private bool colorOnlyWhenStartsLocked = true;

  [Tooltip("Brightness of the colored regions. One displays the authored key color without scene-light attenuation.")]
  [SerializeField, Range(0f, 2f)] private float accentBrightness = 1f;

  [Tooltip("Lighting-independent color added by the existing opaque wall material.")]
  [SerializeField, Range(0f, 2f)] private float emissionStrength = 0.65f;

  [Tooltip("Textured material used by panels that carry a key color.")]
  [SerializeField] private Material coloredPanelMaterial;

  [Tooltip("Neutral textured material used by panels that do not carry a key color.")]
  [SerializeField] private Material uncoloredPanelMaterial;

  [Tooltip("Brightness applied to uncolored panels and the plaster part of the door frame.")]
  [SerializeField, Range(0f, 1f)] private float neutralArchitectureBrightness = 0.85f;

  [Tooltip("Material slots on SM_doorWall that use the architectural plaster texture.")]
  [SerializeField] private int[] doorWallSlots = { 1 };

  [Tooltip("Material slots on the left panel that represent its white panel and handle.")]
  [SerializeField] private int[] leftPanelSlots = { 1 };

  [Tooltip("Material slots on the right panel that represent its white panel and handle.")]
  [SerializeField] private int[] rightPanelSlots = { 2 };

  [Header("Handle Interaction Feedback")]
  [SerializeField] private int[] leftHandleSlots = { 2 };
  [SerializeField] private int[] rightHandleSlots = { 0 };
  [SerializeField] private Color defaultHandleColor = Color.white;
  [SerializeField] private Color availableHandleColor = Color.green;
  [SerializeField] private Color unavailableHandleColor = Color.red;

  private PassagewayDoor door;
  private MaterialPropertyBlock propertyBlock;
  private Renderer doorWallRenderer;
  private bool handleFocused;
  private bool handleInteractionAllowed;

  private bool ShouldPreservePanelColor =>
    door != null && (!colorOnlyWhenStartsLocked || door.StartsLocked);

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
    Material panelMaterial = ShouldPreservePanelColor ? coloredPanelMaterial : uncoloredPanelMaterial;
    SetMaterialSlots(leftRenderer, leftPanelSlots, panelMaterial);
    SetMaterialSlots(rightRenderer, rightPanelSlots, panelMaterial);
    if (ShouldPreservePanelColor) {
      ApplyToPanel(leftRenderer, leftPanelSlots, door.RequiredKeyColor, true);
      ApplyToPanel(rightRenderer, rightPanelSlots, door.RequiredKeyColor, true);
    } else {
      ApplyNeutralArchitecture(leftRenderer, leftPanelSlots);
      ApplyNeutralArchitecture(rightRenderer, rightPanelSlots);
    }

    ApplyNeutralArchitecture(ResolveDoorWallRenderer(), doorWallSlots);

    Color handleColor = ResolveHandleColor();
    ApplyToPanel(leftRenderer, leftHandleSlots, handleColor, false);
    ApplyToPanel(rightRenderer, rightHandleSlots, handleColor, false);
  }

  private Color ResolveHandleColor() {
    // Animation is authoritative: transitional states never advertise another interaction.
    if (door.IsAnimating) return defaultHandleColor;

    // A focused door gives explicit validity feedback. This also covers standing on one
    // of the door's own paths and trying to use a locked door without its matching key.
    if (handleFocused)
      return handleInteractionAllowed ? availableHandleColor : unavailableHandleColor;

    // Away from the player, only a door that is still locked remains red.
    return door.IsLocked ? unavailableHandleColor : availableHandleColor;
  }

  public void SetHandleInteractionState(bool focused, bool interactionAllowed) {
    if (handleFocused == focused && handleInteractionAllowed == interactionAllowed) return;
    handleFocused = focused;
    handleInteractionAllowed = interactionAllowed;
    Apply();
  }

  private void ApplyToPanel(Renderer targetRenderer, int[] materialSlots, Color color, bool textureEmission) {
    if (targetRenderer == null || materialSlots == null) return;

    propertyBlock ??= new MaterialPropertyBlock();
    Material[] materials = targetRenderer.sharedMaterials;
    int materialCount = materials.Length;
    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materialCount) continue;

      propertyBlock.Clear();
      targetRenderer.GetPropertyBlock(propertyBlock, slot);
      Color opaqueColor = new(
        color.r * accentBrightness,
        color.g * accentBrightness,
        color.b * accentBrightness,
        1f);
      propertyBlock.SetColor(BaseColorId, opaqueColor);
      propertyBlock.SetColor(ColorId, opaqueColor);
      propertyBlock.SetColor(EmissionColorId, new Color(
        color.r * emissionStrength,
        color.g * emissionStrength,
        color.b * emissionStrength,
        1f));
      if (textureEmission && materials[slot] != null) {
        Texture baseTexture = materials[slot].GetTexture(BaseMapId);
        if (baseTexture != null) propertyBlock.SetTexture(EmissionMapId, baseTexture);
      }
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

  private void ApplyNeutralArchitecture(Renderer targetRenderer, int[] materialSlots) {
    if (targetRenderer == null || materialSlots == null) return;
    propertyBlock ??= new MaterialPropertyBlock();
    int materialCount = targetRenderer.sharedMaterials.Length;
    Color neutral = new(
      neutralArchitectureBrightness,
      neutralArchitectureBrightness,
      neutralArchitectureBrightness,
      1f);

    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materialCount) continue;
      propertyBlock.Clear();
      targetRenderer.GetPropertyBlock(propertyBlock, slot);
      propertyBlock.SetColor(BaseColorId, neutral);
      propertyBlock.SetColor(ColorId, neutral);
      propertyBlock.SetColor(EmissionColorId, Color.black);
      targetRenderer.SetPropertyBlock(propertyBlock, slot);
    }
  }

  private Renderer ResolveDoorWallRenderer() {
    if (doorWallRenderer != null) return doorWallRenderer;
    Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
    for (int i = 0; i < renderers.Length; i++) {
      if (renderers[i].name != "SM_doorWall") continue;
      doorWallRenderer = renderers[i];
      break;
    }
    return doorWallRenderer;
  }

  private static void SetMaterialSlots(Renderer targetRenderer, int[] materialSlots, Material material) {
    if (targetRenderer == null || materialSlots == null || material == null) return;
    Material[] materials = targetRenderer.sharedMaterials;
    bool changed = false;
    for (int i = 0; i < materialSlots.Length; i++) {
      int slot = materialSlots[i];
      if (slot < 0 || slot >= materials.Length || materials[slot] == material) continue;
      materials[slot] = material;
      changed = true;
    }
    if (changed) targetRenderer.sharedMaterials = materials;
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
