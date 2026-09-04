using UnityEngine;

/// <summary>
/// Recolors only the guard artwork's blue garment region. Neutral skin, equipment, hat,
/// shadows, and outlines are left untouched.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class GuardPaletteTint : MonoBehaviour {
  private static readonly int EnabledId = Shader.PropertyToID("_RegionRecolorEnabled");
  private static readonly int TargetColorId = Shader.PropertyToID("_RegionTargetColor");
  private static readonly int ThresholdId = Shader.PropertyToID("_RegionBlueThreshold");
  private static readonly int SoftnessId = Shader.PropertyToID("_RegionSoftness");
  private static readonly int ReferenceLuminanceId = Shader.PropertyToID("_RegionReferenceLuminance");
  private static readonly int SolidOutlineId = Shader.PropertyToID("_SolidOutline");
  private static readonly int GradientOutline1Id = Shader.PropertyToID("_GradientOutline1");
  private static readonly int GradientOutline2Id = Shader.PropertyToID("_GradientOutline2");
  private static readonly int ImageOutlineId = Shader.PropertyToID("_ImageOutline");
  private static readonly int DualOutlineColorId = Shader.PropertyToID("_DualOutlineColor");

  [SerializeField] private SpriteRenderer targetRenderer;

  [Header("Garment")]
  [Tooltip("Manual garment color. A guard carrying a defined key displays the key color instead without changing this value.")]
  [SerializeField] private Color garmentColor = new(0.28f, 0.72f, 1f, 1f);
  [SerializeField, Range(0f, 0.5f)] private float blueDetectionThreshold = 0.08f;
  [SerializeField, Range(0.001f, 0.25f)] private float detectionSoftness = 0.04f;
  [SerializeField, Range(0.01f, 1f)] private float sourceGarmentLuminance = 0.62f;

  [Header("Outline")]
  [Tooltip("When enabled, the outline automatically uses Garment Color. Disable it to use the explicit Outline Color below.")]
  [SerializeField] private bool useGarmentColorForOutline = true;
  [SerializeField] private Color outlineColor = Color.white;

  private MaterialPropertyBlock propertyBlock;

  public Color GarmentColor {
    get => ResolveGarmentColor();
    set {
      garmentColor = value;
      Apply();
    }
  }

  public Color OutlineColor {
    get => useGarmentColorForOutline ? ResolveGarmentColor() : outlineColor;
    set {
      outlineColor = value;
      useGarmentColorForOutline = false;
      Apply();
    }
  }

  public bool UseGarmentColorForOutline {
    get => useGarmentColorForOutline;
    set {
      useGarmentColorForOutline = value;
      Apply();
    }
  }

  private void OnEnable() => Apply();

  private void OnDisable() {
    ResolveRenderer();
    if (targetRenderer == null) return;
    EnsurePropertyBlock();
    targetRenderer.GetPropertyBlock(propertyBlock);
    propertyBlock.SetFloat(EnabledId, 0f);
    RestoreMaterialOutlineColors(propertyBlock);
    targetRenderer.SetPropertyBlock(propertyBlock);
  }

#if UNITY_EDITOR
  private void OnValidate() => Apply();
#endif

  [ContextMenu("Apply Guard Palette")]
  public void Apply() {
    ResolveRenderer();
    if (targetRenderer == null) return;

    // SpriteRenderer tint affects every pixel, so it must stay neutral. Garment tinting belongs
    // to the region-aware shader properties below.
    targetRenderer.color = Color.white;

    EnsurePropertyBlock();
    targetRenderer.GetPropertyBlock(propertyBlock);
    Color effectiveGarmentColor = ResolveGarmentColor();
    propertyBlock.SetFloat(EnabledId, isActiveAndEnabled ? 1f : 0f);
    propertyBlock.SetColor(TargetColorId, effectiveGarmentColor);
    propertyBlock.SetFloat(ThresholdId, blueDetectionThreshold);
    propertyBlock.SetFloat(SoftnessId, detectionSoftness);
    propertyBlock.SetFloat(ReferenceLuminanceId, sourceGarmentLuminance);
    SetOutlineColors(propertyBlock, useGarmentColorForOutline ? effectiveGarmentColor : outlineColor);
    targetRenderer.SetPropertyBlock(propertyBlock);
  }

  public void Configure(SpriteRenderer renderer, Color color) {
    targetRenderer = renderer;
    garmentColor = color;
    Apply();
  }

  private void ResolveRenderer() {
    if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>(true);
  }

  private void EnsurePropertyBlock() {
    propertyBlock ??= new MaterialPropertyBlock();
  }

  private Color ResolveGarmentColor() {
    GuardKeyCarrier carrier = GetComponent<GuardKeyCarrier>();
    DoorKeyDefinition definition = carrier != null && carrier.CarriesKey
      ? carrier.KeyDefinition
      : null;
    return definition != null ? definition.Color : garmentColor;
  }

  private static void SetOutlineColors(MaterialPropertyBlock block, Color color) {
    block.SetColor(SolidOutlineId, color);
    block.SetColor(GradientOutline1Id, color);
    block.SetColor(GradientOutline2Id, color);
    block.SetColor(ImageOutlineId, color);
    block.SetColor(DualOutlineColorId, color);
  }

  private void RestoreMaterialOutlineColors(MaterialPropertyBlock block) {
    Material material = targetRenderer != null ? targetRenderer.sharedMaterial : null;
    block.SetColor(SolidOutlineId, GetMaterialColor(material, SolidOutlineId, Color.white));
    block.SetColor(GradientOutline1Id, GetMaterialColor(material, GradientOutline1Id, Color.white));
    block.SetColor(GradientOutline2Id, GetMaterialColor(material, GradientOutline2Id, Color.white));
    block.SetColor(ImageOutlineId, GetMaterialColor(material, ImageOutlineId, Color.white));
    block.SetColor(DualOutlineColorId, GetMaterialColor(material, DualOutlineColorId, Color.black));
  }

  private static Color GetMaterialColor(Material material, int propertyId, Color fallback) =>
    material != null && material.HasProperty(propertyId) ? material.GetColor(propertyId) : fallback;
}
