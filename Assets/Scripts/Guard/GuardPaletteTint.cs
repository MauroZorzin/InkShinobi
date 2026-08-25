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

  [SerializeField] private SpriteRenderer targetRenderer;
  [SerializeField] private Color garmentColor = new(0.28f, 0.72f, 1f, 1f);
  [SerializeField, Range(0f, 0.5f)] private float blueDetectionThreshold = 0.08f;
  [SerializeField, Range(0.001f, 0.25f)] private float detectionSoftness = 0.04f;
  [SerializeField, Range(0.01f, 1f)] private float sourceGarmentLuminance = 0.62f;

  private MaterialPropertyBlock propertyBlock;

  public Color GarmentColor {
    get => garmentColor;
    set {
      garmentColor = value;
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
    propertyBlock.SetFloat(EnabledId, isActiveAndEnabled ? 1f : 0f);
    propertyBlock.SetColor(TargetColorId, garmentColor);
    propertyBlock.SetFloat(ThresholdId, blueDetectionThreshold);
    propertyBlock.SetFloat(SoftnessId, detectionSoftness);
    propertyBlock.SetFloat(ReferenceLuminanceId, sourceGarmentLuminance);
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
}
