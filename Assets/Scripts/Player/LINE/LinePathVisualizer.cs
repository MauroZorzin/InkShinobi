using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders each strand of a LinePath at runtime with a LineRenderer using the PathLine shader,
/// so the walkable path is actually visible to the player in Play mode/builds — LinePath's own
/// gizmos only ever show in the editor Scene view. Attach directly to a LinePath GameObject.
/// </summary>
[RequireComponent(typeof(LinePath))]
public class LinePathVisualizer : MonoBehaviour {
  [Tooltip("Color at the centerline of the path.")]
  public Color innerColor = Color.black;

  [Tooltip("Color at the outer edge of the path.")]
  public Color outerColor = Color.white;

  [Tooltip("How much of the half-width stays solid innerColor before the transition to outerColor starts (0-1).")]
  [Range(0f, 1f)] public float innerWidth = 0.35f;

  [Tooltip("How gradual the transition from innerColor to outerColor is (0-1). Larger = softer/wider blend.")]
  [Range(0.01f, 1f)] public float transitionSoftness = 0.5f;

  [Tooltip("Width of the rendered path line, in world units.")]
  public float lineWidth = 0.15f;

  [Tooltip("World-space distance between sampled points along a strand. Smaller = smoother curves, more vertices.")]
  public float sampleSpacing = 0.25f;

  private LinePath _linePath;
  private Material _material;
  private readonly List<LineRenderer> _renderers = new List<LineRenderer>();

  private void Awake() {
    _linePath = GetComponent<LinePath>();

    var shader = Shader.Find("Custom/PathLine");
    if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.DontSave };
  }

  private void OnEnable() {
    ApplyColors();
    Rebuild();
  }

  private void OnDisable() {
    ClearRenderers();
  }

  private void OnValidate() {
    ApplyColors();
  }

  /// <summary>Pushes the inner/outer colors and transition onto the runtime material — call after changing them in code.</summary>
  public void ApplyColors() {
    if (_material == null) return;
    _material.SetColor("_CoreColor", innerColor);
    _material.SetColor("_EdgeColor", outerColor);
    _material.SetFloat("_CoreWidth", innerWidth);
    _material.SetFloat("_Softness", transitionSoftness);
  }

  /// <summary>Destroys and recreates all strand LineRenderers from the current LinePath data. Call after moving waypoints at runtime.</summary>
  public void Rebuild() {
    ClearRenderers();
    if (_linePath == null || _material == null) return;

    for (int strand = 0; strand < _linePath.StrandCount; strand++) {
      var lr = CreateRendererForStrand(strand);
      if (lr != null) _renderers.Add(lr);
    }
  }

  private LineRenderer CreateRendererForStrand(int strandIndex) {
    float length = _linePath.GetStrandLength(strandIndex);
    if (length <= 0f) return null; // single-point or degenerate strand — nothing to draw

    bool closed = _linePath.IsStrandClosedLoop(strandIndex);
    int segments = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.05f, sampleSpacing)));
    int pointCount = closed ? segments : segments + 1;

    var go = new GameObject($"PathVisual_Strand{strandIndex}");
    go.transform.SetParent(transform, false);

    var lr = go.AddComponent<LineRenderer>();
    lr.material = _material;
    lr.useWorldSpace = true;
    lr.widthMultiplier = lineWidth;
    lr.loop = closed;
    lr.numCapVertices = 4;
    lr.alignment = LineAlignment.View;
    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    lr.receiveShadows = false;

    var positions = new Vector3[pointCount];
    for (int i = 0; i < pointCount; i++) {
      float t = (float)i / segments;
      positions[i] = _linePath.GetPointAtDistance(strandIndex, t * length);
    }
    lr.positionCount = pointCount;
    lr.SetPositions(positions);

    return lr;
  }

  private void ClearRenderers() {
    foreach (var lr in _renderers) {
      if (lr != null) Destroy(lr.gameObject);
    }
    _renderers.Clear();
  }
}
