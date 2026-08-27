using UnityEngine;

/// <summary>Displays the exact ballistic arc and landing ring evaluated by the distraction controller.</summary>
[DisallowMultipleComponent]
public sealed class DistractionTrajectoryPreview : MonoBehaviour {
  private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
  private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
  private static readonly int BreakupThresholdId = Shader.PropertyToID("_BreakupThreshold");
  private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");

  [SerializeField] private Material inkMaterial;
  [SerializeField, Range(8, 64)] private int trajectorySamples = 28;
  [SerializeField, Range(12, 96)] private int ringSamples = 48;
  [SerializeField, Min(0.005f)] private float trajectoryWidth = 0.045f;
  [SerializeField, Min(0.02f)] private float landingRadius = 0.18f;
  [SerializeField, Min(0f)] private float surfaceOffset = 0.018f;
  [SerializeField] private Color validColor = new(0.92f, 0.92f, 0.86f, 0.95f);
  [SerializeField] private Color invalidColor = new(0.9f, 0.04f, 0.025f, 0.95f);

  private LineRenderer trajectory;
  private LineRenderer landingRing;
  private MaterialPropertyBlock trajectoryProperties;
  private MaterialPropertyBlock ringProperties;

  private void Awake() {
    BuildRenderers();
    Hide();
  }

  private void OnDisable() => Hide();

#if UNITY_EDITOR
  private void OnValidate() {
    trajectorySamples = Mathf.Clamp(trajectorySamples, 8, 64);
    ringSamples = Mathf.Clamp(ringSamples, 12, 96);
    trajectoryWidth = Mathf.Max(0.005f, trajectoryWidth);
    landingRadius = Mathf.Max(0.02f, landingRadius);
  }
#endif

  public void Show(DistractionThrowEvaluation evaluation) {
    BuildRenderers();
    Color color = evaluation.IsValid ? validColor : invalidColor;
    bool showArc = evaluation.HasTarget && evaluation.FlightTime > 0f;
    trajectory.enabled = showArc;
    landingRing.enabled = evaluation.HasTarget;

    if (showArc) {
      trajectory.positionCount = trajectorySamples;
      for (int i = 0; i < trajectorySamples; i++) {
        float normalized = i / (float)(trajectorySamples - 1);
        trajectory.SetPosition(i, evaluation.PositionAt(evaluation.FlightTime * normalized));
      }
      ApplyLook(trajectory, trajectoryProperties, color);
    }

    if (evaluation.HasTarget) {
      DrawLandingRing(evaluation.Target + evaluation.TargetNormal * surfaceOffset, evaluation.TargetNormal);
      ApplyLook(landingRing, ringProperties, color);
    }
  }

  public void Hide() {
    if (trajectory != null) trajectory.enabled = false;
    if (landingRing != null) landingRing.enabled = false;
  }

  private void BuildRenderers() {
    if (trajectory == null) trajectory = CreateLine("DistractionTrajectory", false, trajectoryWidth);
    if (landingRing == null) landingRing = CreateLine("DistractionLandingRing", true, trajectoryWidth * 0.8f);
    trajectoryProperties ??= new MaterialPropertyBlock();
    ringProperties ??= new MaterialPropertyBlock();
  }

  private LineRenderer CreateLine(string objectName, bool loop, float width) {
    Transform existing = transform.Find(objectName);
    GameObject child = existing != null ? existing.gameObject : new GameObject(objectName);
    child.transform.SetParent(transform, false);
    LineRenderer line = child.GetComponent<LineRenderer>();
    if (line == null) line = child.AddComponent<LineRenderer>();
    line.useWorldSpace = true;
    line.loop = loop;
    line.alignment = LineAlignment.View;
    line.textureMode = LineTextureMode.Tile;
    line.numCapVertices = 3;
    line.numCornerVertices = 3;
    line.widthMultiplier = width;
    line.sharedMaterial = inkMaterial;
    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    line.receiveShadows = false;
    line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    line.renderingLayerMask |= SelectiveColor.RenderingLayerMask | AimPreviewRendering.RenderingLayerMask;
    return line;
  }

  private void DrawLandingRing(Vector3 center, Vector3 normal) {
    Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
    if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
    tangent.Normalize();
    Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
    landingRing.positionCount = ringSamples;
    for (int i = 0; i < ringSamples; i++) {
      float angle = i / (float)ringSamples * Mathf.PI * 2f;
      landingRing.SetPosition(i, center +
        (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * landingRadius);
    }
  }

  private static void ApplyLook(LineRenderer renderer, MaterialPropertyBlock properties, Color color) {
    renderer.GetPropertyBlock(properties);
    properties.SetColor(CoreColorId, color);
    properties.SetColor(EdgeColorId, new Color(color.r, color.g, color.b, color.a * 0.12f));
    properties.SetFloat(BreakupThresholdId, 0.1f);
    properties.SetFloat(AlphaMultiplierId, 1f);
    renderer.SetPropertyBlock(properties);
  }

#if UNITY_EDITOR
  public void Configure(Material authoredInkMaterial) => inkMaterial = authoredInkMaterial;
#endif
}
