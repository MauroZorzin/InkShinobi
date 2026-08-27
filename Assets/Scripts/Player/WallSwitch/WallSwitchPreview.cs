using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the transient trajectory, destination marker, and target preview state.</summary>
[DisallowMultipleComponent]
public sealed class WallSwitchPreview : MonoBehaviour {
  private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
  private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
  private static readonly int BreakupThresholdId = Shader.PropertyToID("_BreakupThreshold");
  private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");
  private static readonly int MarkerColorId = Shader.PropertyToID("_Color");
  private static readonly int MarkerTimeId = Shader.PropertyToID("_UnscaledTime");
  private static readonly int MarkerEdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
  private static readonly int MarkerMotionSpeedId = Shader.PropertyToID("_MotionSpeed");
  private static readonly int MarkerDistortionId = Shader.PropertyToID("_Distortion");

  [Header("Material")]
  [SerializeField] private Material inkMaterial;
  [SerializeField] private Material destinationMaterial;
  [SerializeField] private Camera markerCamera;

  [Header("Trajectory")]
  [Tooltip("Visible width of the ink trajectory in world units. This is independent from the collision/takedown radius.")]
  [SerializeField, Min(0.01f)] private float trajectoryWidth = 0.16f;
  [SerializeField] private Color validColor = new(0f, 0.8f, 0.02f, 0.95f);
  [SerializeField] private Color invalidColor = new(1f, 0.04f, 0.02f, 0.9f);
  [SerializeField] private Color executionColor = new(0.015f, 0.01f, 0.02f, 1f);

  [Header("Destination Ink Stain")]
  [SerializeField, Min(0.02f)] private float destinationRadius = 0.22f;
  [SerializeField] private Vector3 destinationOffset = new(0f, 0.025f, 0f);
  [Tooltip("Softness of the animated stain boundary. Lower values make its edge crisper.")]
  [SerializeField, Range(0.005f, 0.2f)] private float stainEdgeSoftness = 0.045f;
  [Tooltip("Speed at which the stain's irregular silhouette shifts. Set to zero for a static stain.")]
  [SerializeField, Range(0f, 5f)] private float stainMotionSpeed = 1.2f;
  [Tooltip("How far the animated edge deviates from a circle.")]
  [SerializeField, Range(0f, 0.35f)] private float stainDistortion = 0.16f;

  private LineRenderer trajectory;
  private MeshRenderer destination;
  private Transform destinationTransform;
  private readonly HashSet<GuardWallSwitchTarget> highlightedTargets = new();
  private readonly HashSet<GuardWallSwitchTarget> nextHighlightedTargets = new();
  private MaterialPropertyBlock trajectoryProperties;
  private MaterialPropertyBlock destinationProperties;
  private WallSwitchEvaluation currentEvaluation = WallSwitchEvaluation.Empty;
  private bool visible;
  private float markerAnimationTime;

  private void Awake() {
    BuildRenderers();
    Hide();
  }

  private void OnDisable() {
    ClearTargetHighlights();
    Hide();
  }

  private void Update() {
    if (trajectory != null) trajectory.widthMultiplier = trajectoryWidth;
    if (!visible || destinationTransform == null || markerCamera == null) return;
    if (!SceneTransitionManager.IsGamePaused) markerAnimationTime += Time.unscaledDeltaTime;
    destinationTransform.rotation = markerCamera.transform.rotation;
    destination.GetPropertyBlock(destinationProperties);
    destinationProperties.SetFloat(MarkerTimeId, markerAnimationTime);
    ApplyStainSettings(destinationProperties);
    destination.SetPropertyBlock(destinationProperties);
  }

#if UNITY_EDITOR
  private void OnValidate() {
    trajectoryWidth = Mathf.Max(0.01f, trajectoryWidth);
    destinationRadius = Mathf.Max(0.02f, destinationRadius);
    if (trajectory != null) trajectory.widthMultiplier = trajectoryWidth;
    if (destinationTransform != null)
      destinationTransform.localScale = Vector3.one * destinationRadius * 2f;
  }
#endif

  public void Show(WallSwitchEvaluation evaluation) {
    BuildRenderers();
    currentEvaluation = evaluation ?? WallSwitchEvaluation.Empty;
    visible = true;

    bool hasDestination = currentEvaluation.DestinationPath != null;
    trajectory.enabled = hasDestination;
    destination.enabled = true;

    Color look = currentEvaluation.IsValid ? validColor : invalidColor;
    if (hasDestination) {
      trajectory.positionCount = 2;
      trajectory.SetPosition(0, currentEvaluation.TrajectoryStart);
      trajectory.SetPosition(1, GetVisibleTrajectoryEnd(currentEvaluation));
      // Invalid destinations stay visually distinct through color alone. Keeping the stroke
      // continuous makes the blocked trajectory just as legible as a valid one.
      ApplyLook(trajectory, trajectoryProperties, look, currentEvaluation.IsValid ? 0.24f : 0f, 1f);
    }

    Vector3 markerPosition = currentEvaluation.BlockingObject != null
      ? currentEvaluation.BlockingPoint + destinationOffset
      : currentEvaluation.CursorWorldPoint + destinationOffset;
    DrawDestination(markerPosition);
    ApplyMarkerLook(look);
    RefreshTargetHighlights(currentEvaluation);
  }

  public void LockForExecution(WallSwitchEvaluation evaluation) {
    Show(evaluation);
    destination.enabled = true;
    ApplyLook(trajectory, trajectoryProperties, executionColor, 0.32f, 0.65f);
    ApplyMarkerLook(executionColor);
  }

  public void SetExecutionProgress(float progress) {
    if (trajectory == null || currentEvaluation == null || currentEvaluation.DestinationPath == null) return;
    trajectory.enabled = true;
    trajectory.SetPosition(0, currentEvaluation.TrajectoryStart);
    trajectory.SetPosition(1, Vector3.Lerp(
      currentEvaluation.TrajectoryStart,
      GetVisibleTrajectoryEnd(currentEvaluation),
      Mathf.Clamp01(progress)));
  }

  /// <summary>
  /// The switch still teleports to TrajectoryEnd on the selected LinePath. Only the preview is
  /// clipped to the first visible surface under the cursor, such as the front of a wardrobe.
  /// </summary>
  private static Vector3 GetVisibleTrajectoryEnd(WallSwitchEvaluation evaluation) {
    if (evaluation == null) return Vector3.zero;
    return evaluation.BlockingObject != null
      ? evaluation.BlockingPoint
      : evaluation.CursorWorldPoint;
  }

  public void Hide() {
    visible = false;
    currentEvaluation = WallSwitchEvaluation.Empty;
    if (trajectory != null) trajectory.enabled = false;
    if (destination != null) destination.enabled = false;
    ClearTargetHighlights();
  }

  private void BuildRenderers() {
    if (markerCamera == null) markerCamera = GetComponentInChildren<Camera>(true);
    if (trajectory == null) trajectory = CreateLineRenderer("WallSwitchTrajectory", false);
    if (destination == null) destination = CreateDestinationRenderer();
    trajectoryProperties ??= new MaterialPropertyBlock();
    destinationProperties ??= new MaterialPropertyBlock();
  }

  private LineRenderer CreateLineRenderer(string objectName, bool loop) {
    Transform existing = transform.Find(objectName);
    GameObject child = existing != null ? existing.gameObject : new GameObject(objectName);
    child.transform.SetParent(transform, false);

    LineRenderer line = child.GetComponent<LineRenderer>();
    if (line == null) line = child.AddComponent<LineRenderer>();
    line.useWorldSpace = true;
    line.loop = loop;
    line.alignment = LineAlignment.View;
    line.textureMode = LineTextureMode.Tile;
    line.numCapVertices = 4;
    line.numCornerVertices = 3;
    line.widthMultiplier = trajectoryWidth;
    line.sharedMaterial = inkMaterial;
    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    line.receiveShadows = false;
    line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    line.renderingLayerMask |= SelectiveColor.RenderingLayerMask | AimPreviewRendering.RenderingLayerMask;
    return line;
  }

  private void DrawDestination(Vector3 center) {
    if (destinationTransform == null) return;
    destinationTransform.position = center;
    destinationTransform.rotation = markerCamera != null ? markerCamera.transform.rotation : Quaternion.identity;
    destinationTransform.localScale = Vector3.one * destinationRadius * 2f;
  }

  private MeshRenderer CreateDestinationRenderer() {
    Transform existing = transform.Find("WallSwitchDestination");
    GameObject child = existing != null ? existing.gameObject : new GameObject("WallSwitchDestination");
    child.transform.SetParent(transform, false);
    destinationTransform = child.transform;
    LineRenderer obsoleteCircle = child.GetComponent<LineRenderer>();
    if (obsoleteCircle != null) obsoleteCircle.enabled = false;

    MeshFilter filter = child.GetComponent<MeshFilter>();
    if (filter == null) filter = child.AddComponent<MeshFilter>();
    if (filter.sharedMesh == null) {
      Mesh quad = new() { name = "WallSwitchInkMarkerQuad" };
      quad.vertices = new[] {
        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
        new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
      };
      quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
      quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
      quad.RecalculateBounds();
      filter.sharedMesh = quad;
    }

    MeshRenderer renderer = child.GetComponent<MeshRenderer>();
    if (renderer == null) renderer = child.AddComponent<MeshRenderer>();
    renderer.sharedMaterial = destinationMaterial != null ? destinationMaterial : inkMaterial;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    renderer.receiveShadows = false;
    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    renderer.renderingLayerMask |= SelectiveColor.RenderingLayerMask | AimPreviewRendering.RenderingLayerMask;
    return renderer;
  }

  private void ApplyMarkerLook(Color color) {
    if (destination == null) return;
    destination.GetPropertyBlock(destinationProperties);
    destinationProperties.SetColor(MarkerColorId, color);
    ApplyStainSettings(destinationProperties);
    destination.SetPropertyBlock(destinationProperties);
  }

  private void ApplyStainSettings(MaterialPropertyBlock properties) {
    properties.SetFloat(MarkerEdgeSoftnessId, stainEdgeSoftness);
    properties.SetFloat(MarkerMotionSpeedId, stainMotionSpeed);
    properties.SetFloat(MarkerDistortionId, stainDistortion);
  }

  private static void ApplyLook(
    LineRenderer renderer,
    MaterialPropertyBlock properties,
    Color color,
    float breakup,
    float alpha) {
    Color edge = new(color.r, color.g, color.b, color.a * 0.08f);
    renderer.GetPropertyBlock(properties);
    properties.SetColor(CoreColorId, color);
    properties.SetColor(EdgeColorId, edge);
    properties.SetFloat(BreakupThresholdId, breakup);
    properties.SetFloat(AlphaMultiplierId, alpha);
    renderer.SetPropertyBlock(properties);
  }

  private void RefreshTargetHighlights(WallSwitchEvaluation evaluation) {
    nextHighlightedTargets.Clear();
    if (evaluation != null) {
      for (int i = 0; i < evaluation.TakedownTargets.Count; i++) {
        GuardWallSwitchTarget target = evaluation.TakedownTargets[i];
        if (target == null) continue;
        target.SetPreview(WallSwitchTargetDisposition.Vulnerable);
        nextHighlightedTargets.Add(target);
      }
      for (int i = 0; i < evaluation.BlockingGuards.Count; i++) {
        GuardWallSwitchTarget target = evaluation.BlockingGuards[i];
        if (target == null) continue;
        target.SetPreview(WallSwitchTargetDisposition.Blocking);
        nextHighlightedTargets.Add(target);
      }
    }

    foreach (GuardWallSwitchTarget previous in highlightedTargets) {
      if (previous != null && !nextHighlightedTargets.Contains(previous))
        previous.SetPreview(WallSwitchTargetDisposition.Ignored);
    }

    highlightedTargets.Clear();
    foreach (GuardWallSwitchTarget target in nextHighlightedTargets) highlightedTargets.Add(target);
  }

  private void ClearTargetHighlights() {
    foreach (GuardWallSwitchTarget target in highlightedTargets) {
      if (target != null) target.SetPreview(WallSwitchTargetDisposition.Ignored);
    }
    highlightedTargets.Clear();
    nextHighlightedTargets.Clear();
  }
}
