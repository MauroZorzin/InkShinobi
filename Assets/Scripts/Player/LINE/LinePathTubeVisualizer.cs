using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Alternative to LinePathVisualizer for rendering a LinePath's strands at runtime, using the
/// InkTube shader instead of InkTrail/InkGlow. LinePathVisualizer projects each sampled path point
/// DOWN onto the terrain below so the stroke hugs the ground; this component does the opposite —
/// no raycasting at all — and builds an actual round 3D tube directly from the LinePath's own 3D
/// points, so the ink floats exactly where the traceable line actually is (e.g. head-height on a
/// wire), following it 1:1. Being real geometry (not a camera-facing billboard), it reads correctly
/// as a 3D shape from any angle, including near-top-down.
///
/// The tube's circular cross-section is baked once per Rebuild() using a rotation-minimizing
/// (parallel transport) frame, so it doesn't twist along the path. InkTube's vertex shader then
/// rigidly translates each whole ring, every frame, by two traveling sine waves along axes derived
/// from that ring's tangent — one wave per axis of the plane perpendicular to travel direction — so
/// the entire tube continuously slithers through 3D space like a snake curving in every direction,
/// not just side-to-side, without ever needing the mesh itself to be rebuilt.
///
/// It also drives InkTube's completion front: every frame it reads the assigned
/// LineFollowController's progress along whichever strand the player is currently on and pushes it
/// to that strand's material as a world-space distance, fading the line in as the player advances —
/// communicating how much of the path has actually been walked.
///
/// And it drives InkTube's player anchor: on whichever strand the player is currently on, it pushes
/// anchorTarget's world position (any assigned GameObject — defaults to progressSource's own
/// transform) plus the path's own (wave-free) point at that same distance. The shader uses the gap
/// between those two to pull the tube's local neighborhood into passing exactly through
/// anchorTarget, fading back to the ordinary serpentine wave over playerPullRadius — like a rope
/// threaded through the player that slides along as they walk the path.
///
/// Attach directly to a LinePath GameObject, alongside or instead of LinePathVisualizer.
/// </summary>
[RequireComponent(typeof(LinePath))]
public class LinePathTubeVisualizer : MonoBehaviour {
  [Header("Ink Style")]
  public Color color = new Color(0.35f, 0.85f, 1f, 1f);

  [Header("Serpentine Motion")]
  [Tooltip("Cycles per world unit for the primary traveling wave, displacing along one axis of the plane perpendicular to the tube's local travel direction.")]
  public float waveFrequency1 = 1.5f;

  [Tooltip("How far the primary wave displaces the tube, in world units.")]
  public float waveAmplitude1 = 0.15f;

  [Tooltip("How fast the primary wave's phase travels along the tube.")]
  public float waveSpeed1 = 1.5f;

  [Tooltip("Cycles per world unit for the secondary traveling wave, displacing along the OTHER axis of that same perpendicular plane — combined with the primary wave this is what makes the tube curve through every direction instead of a single flat ripple.")]
  public float waveFrequency2 = 0.6f;

  public float waveAmplitude2 = 0.08f;
  public float waveSpeed2 = 0.9f;

  [Tooltip("A third traveling wave, summed onto the SAME axis as the primary wave (independent frequency/amplitude/speed) — layers a second harmonic on top of the main curve. Higher frequency + lower amplitude than the primary wave reads as fine detail wiggling on top of the big S-curves; similar frequency at a different speed reads as a more irregular, less perfectly periodic slither.")]
  public float waveFrequency3 = 3f;

  public float waveAmplitude3 = 0.04f;
  public float waveSpeed3 = 2.2f;

  [Header("Completion / Progress")]
  [Tooltip("If false, the line is always fully visible and LineFollowController progress is ignored.")]
  public bool showProgress = true;

  [Tooltip("Defaults to the GameObject tagged \"Player\"'s LineFollowController if left empty. Its progress along whichever strand it's currently on drives the completion front below.")]
  public LineFollowController progressSource;

  [Tooltip("World-space width of the fade transition at the completion front.")]
  public float progressSoftness = 0.6f;

  [Tooltip("How visible the line is ahead of the completion front (0 = invisible, 1 = same as completed).")]
  [Range(0f, 1f)] public float minAlpha = 0.25f;

  [Header("Player Anchor")]
  [Tooltip("The exact point the tube is pulled through — assign any GameObject (a waist/spine bone, a child offset object, etc). Defaults to progressSource's own transform if left empty.")]
  public Transform anchorTarget;

  [Tooltip("0 = the tube ignores the player and just follows its own serpentine path; 1 = wherever the player currently is on the path, the tube is pulled to pass exactly through Anchor Target, like a rope threaded through them.")]
  [Range(0f, 1f)] public float playerPullStrength = 1f;

  [Tooltip("How far along the path (world units) the pull toward the anchor fades out. Smaller = a tight kink right at the anchor; larger = a long, gentle bend.")]
  public float playerPullRadius = 1.2f;

  [Tooltip("Extra vertical offset added on top of Anchor Target's own position — leave at 0 if Anchor Target is already placed exactly where the tube should pass (e.g. a waist bone); use this only as a quick approximation when Anchor Target is a root positioned at the feet.")]
  public float playerAnchorHeight = 0f;

  [Header("Geometry")]
  [Tooltip("Radius of the tube, in world units. Kept very thin.")]
  public float tubeRadius = 0.02f;

  [Tooltip("Number of sides around the tube's circular cross-section.")]
  [Range(3, 24)] public int radialSegments = 8;

  [Tooltip("World-space distance between sampled rings along a strand. This also determines how smoothly the serpentine wave itself is drawn — too coarse relative to the HIGHEST of Wave Frequency 1/2/3 and the wave looks like straight rigid segments joined at angles instead of a smooth curve. Rule of thumb: keep this well under (1 / highest wave frequency) / 10.")]
  public float sampleSpacing = 0.05f;

  [Range(0f, 1f)] public float alphaMultiplier = 1f;

  [Header("Debug")]
  [Tooltip("Logs shader/material setup and strand counts on every Rebuild().")]
  public bool debugLogging = true;

  [Tooltip("Draws each strand's centerline as a scene gizmo.")]
  public bool drawDebugGizmos = true;

  private LinePath _linePath;
  private Shader _shader;
  private readonly List<GameObject> _strandObjects = new List<GameObject>();
  private readonly List<Material> _strandMaterials = new List<Material>();
  private readonly List<float> _strandLengths = new List<float>();
  private readonly List<bool> _strandClosed = new List<bool>();
  private readonly List<Vector3[]> _debugCenterlines = new List<Vector3[]>();

  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int WaveFrequency1Id = Shader.PropertyToID("_WaveFrequency1");
  private static readonly int WaveAmplitude1Id = Shader.PropertyToID("_WaveAmplitude1");
  private static readonly int WaveSpeed1Id = Shader.PropertyToID("_WaveSpeed1");
  private static readonly int WaveFrequency2Id = Shader.PropertyToID("_WaveFrequency2");
  private static readonly int WaveAmplitude2Id = Shader.PropertyToID("_WaveAmplitude2");
  private static readonly int WaveSpeed2Id = Shader.PropertyToID("_WaveSpeed2");
  private static readonly int WaveFrequency3Id = Shader.PropertyToID("_WaveFrequency3");
  private static readonly int WaveAmplitude3Id = Shader.PropertyToID("_WaveAmplitude3");
  private static readonly int WaveSpeed3Id = Shader.PropertyToID("_WaveSpeed3");
  private static readonly int ProgressDistanceId = Shader.PropertyToID("_ProgressDistance");
  private static readonly int ProgressSoftnessId = Shader.PropertyToID("_ProgressSoftness");
  private static readonly int MinAlphaId = Shader.PropertyToID("_MinAlpha");
  private static readonly int PlayerAnchorWorldPosId = Shader.PropertyToID("_PlayerAnchorWorldPos");
  private static readonly int PlayerPathPointWorldPosId = Shader.PropertyToID("_PlayerPathPointWorldPos");
  private static readonly int PlayerDistanceId = Shader.PropertyToID("_PlayerDistance");
  private static readonly int PlayerPullRadiusId = Shader.PropertyToID("_PlayerPullRadius");
  private static readonly int PlayerPullStrengthId = Shader.PropertyToID("_PlayerPullStrength");
  private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");

  private void Awake() {
    _linePath = GetComponent<LinePath>();

    if (progressSource == null) {
      var player = GameObject.FindGameObjectWithTag("Player");
      if (player != null) progressSource = player.GetComponent<LineFollowController>();
    }

    if (anchorTarget == null && progressSource != null) anchorTarget = progressSource.transform;

    _shader = Shader.Find("Custom/InkTube");
    if (_shader == null && debugLogging) {
      Debug.LogError("[LinePathTubeVisualizer] Shader 'Custom/InkTube' not found — it either hasn't been imported yet or failed to compile. Check the Console for shader compiler errors. No strands will be built until this is fixed.", this);
    }
  }

  private void OnEnable() {
    Rebuild();
    ApplyMaterialProperties();
  }

  private void OnDisable() {
    ClearStrandObjects();
  }

  private void OnValidate() {
    ApplyMaterialProperties();
  }

  private void Update() {
    for (int strand = 0; strand < _strandMaterials.Count; strand++) {
      var mat = _strandMaterials[strand];
      if (mat == null) continue;

      float length = _strandLengths[strand];
      bool active = progressSource != null && progressSource.currentLine == _linePath && progressSource.currentStrand == strand;
      float dist = active ? progressSource.GetDistanceAlongLine() : 0f;
      float progress = _strandClosed[strand] ? Mathf.Repeat(dist, Mathf.Max(length, 0.0001f)) : Mathf.Clamp(dist, 0f, length);

      mat.SetFloat(ProgressDistanceId, showProgress && active ? progress : length);

      // Kept as a distance FAR from anything the tube could ever sample, rather than a sentinel
      // like -1, so the pull falloff below never has to special-case "no player on this strand" —
      // the smoothstep just naturally evaluates to 0 everywhere.
      mat.SetFloat(PlayerDistanceId, active ? progress : -1000000f);
      if (active && anchorTarget != null) {
        Vector3 anchor = anchorTarget.position + Vector3.up * playerAnchorHeight;
        Vector3 pathPoint = _linePath.GetPointAtDistance(strand, progress);
        mat.SetVector(PlayerAnchorWorldPosId, anchor);
        mat.SetVector(PlayerPathPointWorldPosId, pathPoint);
      }
    }
  }

  /// <summary>Pushes the ink style parameters onto every strand's runtime material (not the per-strand progress, which Update() drives) — call after changing them in code.</summary>
  public void ApplyMaterialProperties() {
    foreach (var mat in _strandMaterials) {
      if (mat == null) continue;
      mat.SetColor(ColorId, color);
      mat.SetFloat(WaveFrequency1Id, waveFrequency1);
      mat.SetFloat(WaveAmplitude1Id, waveAmplitude1);
      mat.SetFloat(WaveSpeed1Id, waveSpeed1);
      mat.SetFloat(WaveFrequency2Id, waveFrequency2);
      mat.SetFloat(WaveAmplitude2Id, waveAmplitude2);
      mat.SetFloat(WaveSpeed2Id, waveSpeed2);
      mat.SetFloat(WaveFrequency3Id, waveFrequency3);
      mat.SetFloat(WaveAmplitude3Id, waveAmplitude3);
      mat.SetFloat(WaveSpeed3Id, waveSpeed3);
      mat.SetFloat(ProgressSoftnessId, progressSoftness);
      mat.SetFloat(MinAlphaId, minAlpha);
      mat.SetFloat(PlayerPullRadiusId, playerPullRadius);
      mat.SetFloat(PlayerPullStrengthId, playerPullStrength);
      mat.SetFloat(AlphaMultiplierId, alphaMultiplier);
    }
  }

  /// <summary>Destroys and recreates all strand ribbon meshes/materials from the current LinePath data. Call after moving waypoints at runtime.</summary>
  public void Rebuild() {
    ClearStrandObjects();
    _debugCenterlines.Clear();

    if (_linePath == null) {
      if (debugLogging) Debug.LogWarning("[LinePathTubeVisualizer] Rebuild aborted: no LinePath found on this GameObject.", this);
      return;
    }
    if (_shader == null) {
      if (debugLogging) Debug.LogWarning("[LinePathTubeVisualizer] Rebuild aborted: shader missing/failed to compile (see the error logged in Awake). No strands will be drawn.", this);
      return;
    }
    if (_linePath.StrandCount == 0) {
      if (debugLogging) Debug.LogWarning($"[LinePathTubeVisualizer] '{name}': LinePath reports 0 strands — nothing to draw.", this);
      return;
    }

    int builtCount = 0;
    for (int strand = 0; strand < _linePath.StrandCount; strand++) {
      var go = BuildStrandObject(strand);
      if (go != null) builtCount++;
    }

    if (debugLogging) Debug.Log($"[LinePathTubeVisualizer] '{name}': Rebuild built {builtCount}/{_linePath.StrandCount} strand(s).", this);
  }

  private GameObject BuildStrandObject(int strandIndex) {
    float length = _linePath.GetStrandLength(strandIndex);
    if (length <= 0f) {
      if (debugLogging) Debug.LogWarning($"[LinePathTubeVisualizer] '{name}': strand {strandIndex} has zero length (single point or degenerate) — skipped.", this);
      _strandObjects.Add(null);
      _strandMaterials.Add(null);
      _strandLengths.Add(0f);
      _strandClosed.Add(false);
      return null;
    }

    bool closed = _linePath.IsStrandClosedLoop(strandIndex);
    int segments = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.05f, sampleSpacing)));
    int pointCount = closed ? segments : segments + 1;

    var points = new Vector3[pointCount];
    var tangents = new Vector3[pointCount];
    var distances = new float[pointCount];

    for (int i = 0; i < pointCount; i++) {
      float dist = (float)i / segments * length;
      distances[i] = dist;
      points[i] = _linePath.GetPointAtDistance(strandIndex, dist);
      tangents[i] = _linePath.GetDirectionAtDistance(strandIndex, dist);
    }

    var go = new GameObject($"InkTube_Strand{strandIndex}");
    go.transform.SetParent(transform, false);
    // Mesh vertices below are built in WORLD space (LinePath.GetPointAtDistance is world-space) —
    // this object's own transform must stay world identity for local space to line up.
    go.transform.position = Vector3.zero;
    go.transform.rotation = Quaternion.identity;

    var mat = new Material(_shader) { hideFlags = HideFlags.DontSave };

    var mf = go.AddComponent<MeshFilter>();
    var mr = go.AddComponent<MeshRenderer>();
    mr.sharedMaterial = mat;
    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    mr.receiveShadows = false;

    var normals = ComputeParallelTransportFrames(tangents);
    mf.sharedMesh = BuildTubeMesh(pointCount, closed, points, tangents, normals, distances);

    _strandObjects.Add(go);
    _strandMaterials.Add(mat);
    _strandLengths.Add(length);
    _strandClosed.Add(closed);
    _debugCenterlines.Add(points);

    return go;
  }

  /// <summary>
  /// Rotation-minimizing frame propagation: starts from an arbitrary normal orthogonal to the first
  /// tangent, then at each following sample rotates the previous normal by exactly the rotation
  /// between consecutive tangents. This keeps the tube's cross-section from twisting along straight
  /// or gently curving stretches the way naively re-deriving "up" at each point (a Frenet frame)
  /// would — that flips/spins whenever the path's curvature direction reverses or goes near-straight.
  /// </summary>
  private Vector3[] ComputeParallelTransportFrames(Vector3[] tangents) {
    var normals = new Vector3[tangents.Length];
    if (tangents.Length == 0) return normals;

    Vector3 t0 = tangents[0].sqrMagnitude > 0.0001f ? tangents[0].normalized : Vector3.forward;
    Vector3 reference = Mathf.Abs(Vector3.Dot(t0, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
    Vector3 n0 = Vector3.Cross(t0, reference);
    if (n0.sqrMagnitude < 0.0001f) n0 = Vector3.Cross(t0, Vector3.forward);
    normals[0] = n0.normalized;

    for (int i = 1; i < tangents.Length; i++) {
      Vector3 prevT = tangents[i - 1].sqrMagnitude > 0.0001f ? tangents[i - 1].normalized : t0;
      Vector3 currT = tangents[i].sqrMagnitude > 0.0001f ? tangents[i].normalized : prevT;

      Quaternion rot = Quaternion.FromToRotation(prevT, currT);
      Vector3 n = rot * normals[i - 1];
      n = Vector3.ProjectOnPlane(n, currT);
      normals[i] = n.sqrMagnitude > 0.0001f ? n.normalized : normals[i - 1];
    }

    return normals;
  }

  /// <summary>
  /// Bakes a round tube: each ring's circular cross-section is placed using the (twist-free)
  /// parallel-transport frame above, so the STATIC shape never seams or twists visibly. That same
  /// ring-shared (normal, binormal) pair is ALSO carried per vertex — normal via the NORMAL channel
  /// (repurposed; the tube is unlit, so no true shading normal is needed) and binormal via TANGENT
  /// (repurposed the same way) — so InkTube's vertex shader can rigidly translate the whole ring
  /// along those exact two axes for the serpentine wave. Using this precomputed, twist-free frame
  /// instead of re-deriving one from the tangent alone in the shader avoids the wave's axes jumping
  /// unpredictably at every path corner, which read as rigid, faceted segments instead of a smooth
  /// slither.
  /// </summary>
  private Mesh BuildTubeMesh(int pointCount, bool closed, Vector3[] points, Vector3[] tangents, Vector3[] normals, float[] distances) {
    var vertices = new Vector3[pointCount * radialSegments];
    var meshNormals = new Vector3[pointCount * radialSegments];
    var meshTangents = new Vector4[pointCount * radialSegments];
    var uvs = new Vector2[pointCount * radialSegments];

    for (int i = 0; i < pointCount; i++) {
      Vector3 tangent = tangents[i].sqrMagnitude > 0.0001f ? tangents[i].normalized : Vector3.forward;
      Vector3 normal = normals[i];
      Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
      Vector4 binormal4 = new Vector4(binormal.x, binormal.y, binormal.z, 1f);

      for (int seg = 0; seg < radialSegments; seg++) {
        float angle = (float)seg / radialSegments * Mathf.PI * 2f;
        Vector3 radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);

        int idx = i * radialSegments + seg;
        vertices[idx] = points[i] + radial * tubeRadius;
        meshNormals[idx] = normal;
        meshTangents[idx] = binormal4;
        // x = cumulative WORLD-SPACE distance (matches InkTube's _ProgressDistance units directly);
        // y = fraction around the tube's circumference (currently unused by the shader, kept for
        // possible future use — e.g. per-fragment shading).
        uvs[idx] = new Vector2(distances[i], (float)seg / radialSegments);
      }
    }

    int ringSegCount = closed ? pointCount : pointCount - 1;
    var triangles = new int[ringSegCount * radialSegments * 6];
    int t = 0;
    for (int i = 0; i < ringSegCount; i++) {
      int ringA = i;
      int ringB = (i + 1) % pointCount;

      for (int seg = 0; seg < radialSegments; seg++) {
        int segNext = (seg + 1) % radialSegments;
        int a = ringA * radialSegments + seg;
        int b = ringA * radialSegments + segNext;
        int c = ringB * radialSegments + seg;
        int d = ringB * radialSegments + segNext;

        triangles[t + 0] = a;
        triangles[t + 1] = c;
        triangles[t + 2] = b;
        triangles[t + 3] = b;
        triangles[t + 4] = c;
        triangles[t + 5] = d;
        t += 6;
      }
    }

    var mesh = new Mesh { name = "InkTube" };
    mesh.SetVertices(vertices);
    mesh.SetNormals(meshNormals);
    mesh.SetTangents(meshTangents);
    mesh.SetUVs(0, uvs);
    mesh.SetTriangles(triangles, 0);
    mesh.RecalculateBounds();

    // The vertex shader translates each ring by up to all three serpentine wave amplitudes, which
    // RecalculateBounds() above knows nothing about — without padding, off-screen frustum culling
    // could clip a visible tube early.
    var bounds = mesh.bounds;
    float pad = (waveAmplitude1 + waveAmplitude2 + waveAmplitude3) * 2f;
    bounds.Expand(Mathf.Max(pad, 0.01f));
    mesh.bounds = bounds;

    return mesh;
  }

  private void ClearStrandObjects() {
    foreach (var go in _strandObjects) {
      if (go != null) Destroy(go);
    }
    _strandObjects.Clear();
    _strandMaterials.Clear();
    _strandLengths.Clear();
    _strandClosed.Clear();
  }

  private void OnDrawGizmos() {
    if (!drawDebugGizmos) return;

    Gizmos.color = Color.magenta;
    foreach (var centerline in _debugCenterlines) {
      if (centerline == null) continue;
      for (int i = 0; i < centerline.Length - 1; i++) {
        Gizmos.DrawLine(centerline[i], centerline[i + 1]);
      }
    }
  }
}
