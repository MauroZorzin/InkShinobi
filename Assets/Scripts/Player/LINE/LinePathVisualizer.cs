using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders each strand of a LinePath at runtime as a procedural ribbon mesh using the InkTrail
/// shader, so the walkable path is actually visible to the player in Play mode/builds — LinePath's
/// own gizmos only ever show in the editor Scene view. Attach directly to a LinePath GameObject.
///
/// Unlike a LineRenderer (which would draw the strand's own 3D points as a straight, camera-facing
/// ribbon floating wherever the gameplay path actually is — e.g. head-height on a wire), this
/// raycasts DOWN from each sampled path point onto the scene's collision geometry and builds the
/// ribbon on the resulting surface points instead, oriented by the hit normal. The visual result
/// hugs whatever's underneath (ground, rooftops, boxes...) like a mark painted on the world, even
/// though the actual LinePath the player follows doesn't move and has no gravity applied to it.
/// Points that don't hit anything (gaps, chasms) fall back to the raw path point and are flagged
/// via vertex alpha so InkTrail.shader can fade the stroke out over them instead of drawing a
/// straight airborne segment.
/// </summary>
[RequireComponent(typeof(LinePath))]
public class LinePathVisualizer : MonoBehaviour {
  [Header("Ink Style")]
  [Tooltip("Color at the centerline of the stroke.")]
  public Color coreColor = new Color(0.05f, 0.05f, 0.08f, 1f);

  [Tooltip("Color at the outer edge of the stroke.")]
  public Color edgeColor = new Color(0.05f, 0.05f, 0.08f, 0f);

  [Tooltip("How much of the half-width stays solid coreColor before the transition to edgeColor starts (0-1).")]
  [Range(0f, 1f)] public float coreWidth = 0.3f;

  [Tooltip("How gradual the transition from coreColor to edgeColor is (0-1). Larger = softer/wider blend.")]
  [Range(0.01f, 1f)] public float transitionSoftness = 0.6f;

  [Tooltip("Noise cycles per world unit controlling the size of the gaps/blots along the stroke.")]
  public float breakupScale = 0.6f;

  [Tooltip("How much of the stroke is missing — 0 = fully solid line, 1 = fully gone.")]
  [Range(0f, 1f)] public float breakupThreshold = 0.45f;

  [Tooltip("Softness of each gap's edge (0 = hard-edged blots, larger = soft dried-ink fringes).")]
  [Range(0.001f, 0.5f)] public float breakupSoftness = 0.12f;

  [Tooltip("Slowly drifts the breakup pattern over time. 0 = static ink (recommended).")]
  public float flowSpeed = 0f;

  [Tooltip("Noise cycles per world unit controlling how wobbly/hand-drawn the stroke's edge looks.")]
  public float edgeNoiseScale = 1.5f;

  [Tooltip("How far the edge wobbles off the clean width-based falloff (0-0.5).")]
  [Range(0f, 0.5f)] public float edgeRoughness = 0.18f;

  [Header("Geometry")]
  [Tooltip("Width of the rendered stroke, in world units.")]
  public float lineWidth = 0.15f;

  [Tooltip("World-space distance between sampled points along a strand. Smaller = more accurately follows bumpy terrain, more vertices.")]
  public float sampleSpacing = 0.2f;

  [Header("Ground Projection")]
  [Tooltip("Direction each sampled path point is projected along to find the surface the ink is drawn on. World Down for a path on the ground below the (possibly airborne) LinePath.")]
  public Vector3 projectionDirection = Vector3.down;

  [Tooltip("Layers considered ground/geometry for projection.")]
  public LayerMask groundLayers = ~0;

  [Tooltip("How far back along -projectionDirection the raycast starts from the sampled path point, so geometry slightly above the point is still found.")]
  public float raycastStartBackoff = 2f;

  [Tooltip("Max raycast distance (from the backed-off start) searching for a surface.")]
  public float maxProjectionDistance = 50f;

  [Tooltip("How far above the hit surface (along its normal) the ribbon mesh sits, to avoid z-fighting.")]
  public float surfaceOffset = 0.02f;

  [Header("Debug")]
  [Tooltip("Logs shader/material setup, strand counts and how many sampled points actually hit ground on every Rebuild().")]
  public bool debugLogging = true;

  [Tooltip("Draws the raycasts, hit/fallback points and the resulting ribbon edges as scene gizmos — lets you see WHY nothing's showing (no ground hit vs. a shader/material problem) without needing the mesh to actually render.")]
  public bool drawDebugGizmos = true;

  public float gizmoPointSize = 0.05f;

  [Tooltip("Forces vertex alpha to 1 everywhere, ignoring whether a point actually hit ground. Flip this on to check whether \"0 grounded points\" is the reason nothing renders, independent of any shader/material issue.")]
  public bool debugForceFullAlpha = false;

  private LinePath _linePath;
  private Material _material;
  private readonly List<GameObject> _strandObjects = new List<GameObject>();

  private class StrandDebugInfo {
    public Vector3[] rayStarts;
    public Vector3[] rayEnds; // hit point if grounded, full ray end otherwise
    public bool[] grounded;
    public Vector3[] leftVerts;
    public Vector3[] rightVerts;
  }

  private readonly List<StrandDebugInfo> _debugStrands = new List<StrandDebugInfo>();

  private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
  private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
  private static readonly int CoreWidthId = Shader.PropertyToID("_CoreWidth");
  private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
  private static readonly int BreakupScaleId = Shader.PropertyToID("_BreakupScale");
  private static readonly int BreakupThresholdId = Shader.PropertyToID("_BreakupThreshold");
  private static readonly int BreakupSoftnessId = Shader.PropertyToID("_BreakupSoftness");
  private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
  private static readonly int EdgeNoiseScaleId = Shader.PropertyToID("_EdgeNoiseScale");
  private static readonly int EdgeRoughnessId = Shader.PropertyToID("_EdgeRoughness");

  private void Awake() {
    _linePath = GetComponent<LinePath>();

    var shader = Shader.Find("Custom/InkTrail");
    if (shader != null) {
      _material = new Material(shader) { hideFlags = HideFlags.DontSave };
    } else if (debugLogging) {
      // The single most common reason "nothing appears at all": Shader.Find returns null when the
      // shader doesn't exist, isn't imported yet, or (most likely) failed to compile — check the
      // Console for compiler errors on InkTrail.shader. Rebuild() below bails out completely with
      // _material still null, so no strand meshes get created at all in that case.
      Debug.LogError("[LinePathVisualizer] Shader 'Custom/InkTrail' not found — it either hasn't been imported yet or failed to compile. Check the Console for shader compiler errors. No ribbon meshes will be built until this is fixed.", this);
    }
  }

  private void OnEnable() {
    ApplyMaterialProperties();
    Rebuild();
  }

  private void OnDisable() {
    ClearStrandObjects();
  }

  private void OnValidate() {
    ApplyMaterialProperties();
  }

  /// <summary>Pushes the ink style parameters onto the runtime material — call after changing them in code.</summary>
  public void ApplyMaterialProperties() {
    if (_material == null) return;
    _material.SetColor(CoreColorId, coreColor);
    _material.SetColor(EdgeColorId, edgeColor);
    _material.SetFloat(CoreWidthId, coreWidth);
    _material.SetFloat(SoftnessId, transitionSoftness);
    _material.SetFloat(BreakupScaleId, breakupScale);
    _material.SetFloat(BreakupThresholdId, breakupThreshold);
    _material.SetFloat(BreakupSoftnessId, breakupSoftness);
    _material.SetFloat(FlowSpeedId, flowSpeed);
    _material.SetFloat(EdgeNoiseScaleId, edgeNoiseScale);
    _material.SetFloat(EdgeRoughnessId, edgeRoughness);
  }

  /// <summary>Destroys and recreates all strand ribbon meshes from the current LinePath data. Call after moving waypoints, or the world geometry underneath, at runtime.</summary>
  public void Rebuild() {
    ClearStrandObjects();
    _debugStrands.Clear();

    if (_linePath == null) {
      if (debugLogging) Debug.LogWarning("[LinePathVisualizer] Rebuild aborted: no LinePath found on this GameObject.", this);
      return;
    }
    if (_material == null) {
      if (debugLogging) Debug.LogWarning("[LinePathVisualizer] Rebuild aborted: material is null (shader missing/failed to compile — see the error logged in Awake). No strands will be drawn.", this);
      return;
    }
    if (_linePath.StrandCount == 0) {
      if (debugLogging) Debug.LogWarning($"[LinePathVisualizer] '{name}': LinePath reports 0 strands — nothing to draw. Check the LinePath has waypoint children (or the points array set) and is enabled.", this);
      return;
    }

    int builtCount = 0;
    for (int strand = 0; strand < _linePath.StrandCount; strand++) {
      var go = BuildStrandObject(strand);
      if (go != null) { _strandObjects.Add(go); builtCount++; }
    }

    if (debugLogging) Debug.Log($"[LinePathVisualizer] '{name}': Rebuild built {builtCount}/{_linePath.StrandCount} strand(s).", this);
  }

  private GameObject BuildStrandObject(int strandIndex) {
    float length = _linePath.GetStrandLength(strandIndex);
    if (length <= 0f) {
      if (debugLogging) Debug.LogWarning($"[LinePathVisualizer] '{name}': strand {strandIndex} has zero length (single point or degenerate) — skipped.", this);
      return null;
    }

    bool closed = _linePath.IsStrandClosedLoop(strandIndex);
    int segments = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.05f, sampleSpacing)));
    int pointCount = closed ? segments : segments + 1;
    if (pointCount < 2) return null;

    Vector3 dir = projectionDirection.sqrMagnitude > 0.0001f ? projectionDirection.normalized : Vector3.down;

    var groundPoints = new Vector3[pointCount];
    var groundNormals = new Vector3[pointCount];
    var grounded = new bool[pointCount];
    var distances = new float[pointCount];

    var dbg = new StrandDebugInfo {
      rayStarts = new Vector3[pointCount],
      rayEnds = new Vector3[pointCount],
      grounded = grounded,
    };

    int groundedCount = 0;
    for (int i = 0; i < pointCount; i++) {
      float dist = (float)i / segments * length;
      distances[i] = dist;
      Vector3 p = _linePath.GetPointAtDistance(strandIndex, dist);

      Vector3 rayStart = p - dir * raycastStartBackoff;
      float rayLength = raycastStartBackoff + maxProjectionDistance;
      dbg.rayStarts[i] = rayStart;

      if (Physics.Raycast(rayStart, dir, out RaycastHit hit, rayLength, groundLayers, QueryTriggerInteraction.Ignore)) {
        groundPoints[i] = hit.point + hit.normal * surfaceOffset;
        groundNormals[i] = hit.normal;
        grounded[i] = true;
        groundedCount++;
        dbg.rayEnds[i] = hit.point;
      } else {
        groundPoints[i] = p;
        groundNormals[i] = -dir;
        grounded[i] = false;
        dbg.rayEnds[i] = rayStart + dir * rayLength;
      }
    }

    if (debugLogging) {
      if (groundedCount == 0) {
        Debug.LogWarning($"[LinePathVisualizer] '{name}': strand {strandIndex} — 0/{pointCount} sampled points hit anything on groundLayers={groundLayers.value}. " +
                          "The ribbon exists but is fully transparent (vertex alpha 0 everywhere) — nothing will appear. " +
                          "Check: colliders actually exist under the path, they're on a layer included in Ground Layers, and Projection Direction/Max Projection Distance reach them.", this);
      } else if (groundedCount < pointCount) {
        Debug.Log($"[LinePathVisualizer] '{name}': strand {strandIndex} — {groundedCount}/{pointCount} points grounded.", this);
      }
    }

    var go = new GameObject($"InkTrail_Strand{strandIndex}");
    go.transform.SetParent(transform, false);
    // Mesh vertices below are built in WORLD space (LinePath.GetPointAtDistance and the raycast
    // hits are both world-space) — unlike LineRenderer there's no "useWorldSpace" escape hatch for
    // a MeshFilter, so this object's own transform must actually BE world identity for local space
    // to line up with the coordinates baked into the mesh.
    go.transform.position = Vector3.zero;
    go.transform.rotation = Quaternion.identity;

    var mf = go.AddComponent<MeshFilter>();
    var mr = go.AddComponent<MeshRenderer>();
    mr.sharedMaterial = _material;
    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    mr.receiveShadows = false;

    mf.sharedMesh = BuildRibbonMesh(pointCount, closed, groundPoints, groundNormals, grounded, distances, dbg);
    _debugStrands.Add(dbg);
    return go;
  }

  private Mesh BuildRibbonMesh(int pointCount, bool closed, Vector3[] points, Vector3[] normals, bool[] grounded, float[] distances, StrandDebugInfo dbg) {
    var vertices = new Vector3[pointCount * 2];
    var meshNormals = new Vector3[pointCount * 2];
    var uvs = new Vector2[pointCount * 2];
    var colors = new Color[pointCount * 2];
    float halfWidth = Mathf.Max(0.001f, lineWidth) * 0.5f;

    dbg.leftVerts = new Vector3[pointCount];
    dbg.rightVerts = new Vector3[pointCount];

    for (int i = 0; i < pointCount; i++) {
      int prev = closed ? (i - 1 + pointCount) % pointCount : Mathf.Max(0, i - 1);
      int next = closed ? (i + 1) % pointCount : Mathf.Min(pointCount - 1, i + 1);

      Vector3 tangent = points[next] - points[prev];
      if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
      tangent.Normalize();

      Vector3 normal = normals[i].sqrMagnitude > 0.0001f ? normals[i].normalized : Vector3.up;
      Vector3 right = Vector3.Cross(tangent, normal);
      if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(tangent, Vector3.up);
      right = right.normalized * halfWidth;

      int i0 = i * 2;
      int i1 = i * 2 + 1;

      vertices[i0] = points[i] - right;
      vertices[i1] = points[i] + right;
      meshNormals[i0] = normal;
      meshNormals[i1] = normal;
      dbg.leftVerts[i] = vertices[i0];
      dbg.rightVerts[i] = vertices[i1];

      // x = cumulative WORLD-SPACE distance (not 0..1) so InkTrail.shader's noise frequency stays
      // consistent regardless of strand length; y = 0/1 across width, matching PathLine's old
      // convention of "0.5 is the centerline" that the shader's core/edge falloff relies on.
      uvs[i0] = new Vector2(distances[i], 0f);
      uvs[i1] = new Vector2(distances[i], 1f);

      float alpha = (grounded[i] || debugForceFullAlpha) ? 1f : 0f;
      colors[i0] = new Color(1f, 1f, 1f, alpha);
      colors[i1] = new Color(1f, 1f, 1f, alpha);
    }

    int segCount = closed ? pointCount : pointCount - 1;
    var triangles = new int[segCount * 6];
    for (int i = 0; i < segCount; i++) {
      int a = i * 2;
      int b = i * 2 + 1;
      int c = ((i + 1) % pointCount) * 2;
      int d = ((i + 1) % pointCount) * 2 + 1;

      int t = i * 6;
      triangles[t + 0] = a;
      triangles[t + 1] = c;
      triangles[t + 2] = b;
      triangles[t + 3] = b;
      triangles[t + 4] = c;
      triangles[t + 5] = d;
    }

    var mesh = new Mesh { name = "InkTrailRibbon" };
    mesh.SetVertices(vertices);
    mesh.SetNormals(meshNormals);
    mesh.SetUVs(0, uvs);
    mesh.SetColors(colors);
    mesh.SetTriangles(triangles, 0);
    mesh.RecalculateBounds();
    return mesh;
  }

  private void ClearStrandObjects() {
    foreach (var go in _strandObjects) {
      if (go != null) Destroy(go);
    }
    _strandObjects.Clear();
  }

  private void OnDrawGizmos() {
    if (!drawDebugGizmos) return;

    foreach (var s in _debugStrands) {
      if (s.rayStarts == null) continue;

      for (int i = 0; i < s.rayStarts.Length; i++) {
        // Yellow ray = missed (drawn full length so you can see if it's simply too short/aimed
        // wrong); green ray + sphere = hit. Ray drawn regardless of hit so a "too short" miss and
        // a "hit further than expected" case both look different at a glance.
        Gizmos.color = s.grounded[i] ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0.9f, 0f, 0.5f);
        Gizmos.DrawLine(s.rayStarts[i], s.rayEnds[i]);

        Gizmos.color = s.grounded[i] ? Color.green : Color.red;
        Gizmos.DrawSphere(s.rayEnds[i], gizmoPointSize);
      }

      // Ribbon edges in cyan — if these show a sensible strip but nothing renders in Game view,
      // the problem is the material/shader (or every point above being ungrounded → alpha 0), not
      // the mesh construction itself.
      if (s.leftVerts != null && s.rightVerts != null) {
        Gizmos.color = Color.cyan;
        for (int i = 0; i < s.leftVerts.Length - 1; i++) {
          Gizmos.DrawLine(s.leftVerts[i], s.leftVerts[i + 1]);
          Gizmos.DrawLine(s.rightVerts[i], s.rightVerts[i + 1]);
        }
        for (int i = 0; i < s.leftVerts.Length; i++) {
          Gizmos.DrawLine(s.leftVerts[i], s.rightVerts[i]);
        }
      }
    }
  }
}
