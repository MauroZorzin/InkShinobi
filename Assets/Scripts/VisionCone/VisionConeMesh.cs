using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VisionConeMesh : MonoBehaviour {
  [Header("Shape")]
  [Range(1f, 180f)] public float fieldOfView = 60f;
  [Range(1f, 60f)] public float viewDistance = 10f;
  [Range(3, 120)] public int rayCount = 40;
  public float eyeHeight = 1.6f;
  [Tooltip("Vertical thickness of the cone volume, centered on Eye Height.")]
  [Range(0.05f, 10f)] public float coneThickness = 1.5f;

  [Header("Occlusion")]
  [Tooltip("Layers that block the cone. Leave empty to never get cut off.")]
  public LayerMask obstacleMask;

  [Header("Look")]
  public Color color = new Color(1f, 0.85f, 0.3f, 0.35f);
  [Tooltip("Leave empty to use a built-in fading unlit material.")]
  public Material material;

  [Header("Update")]
  public bool updateEveryFrame = true;
  [Tooltip("Seconds between mesh rebuilds when Update Every Frame is off.")]
  public float updateInterval = 0.1f;

  private Mesh _mesh;
  private MeshFilter _meshFilter;
  private MeshRenderer _meshRenderer;
  private Material _generatedMaterial;
  private float _updateTimer;

  private void OnEnable() {
    _meshFilter = GetComponent<MeshFilter>();
    _meshRenderer = GetComponent<MeshRenderer>();

    if (_mesh == null) {
      _mesh = new Mesh { name = "VisionCone" };
    }
    _meshFilter.sharedMesh = _mesh;

    ApplyMaterial();
    _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    _meshRenderer.receiveShadows = false;

    RebuildMesh();
  }

  private void Update() {
    ApplyMaterial();

    if (updateEveryFrame) {
      RebuildMesh();
      return;
    }

    _updateTimer -= Time.deltaTime;
    if (_updateTimer <= 0f) {
      _updateTimer = updateInterval;
      RebuildMesh();
    }
  }

  private void ApplyMaterial() {
    if (material != null) {
      _meshRenderer.sharedMaterial = material;
      return;
    }

    if (_generatedMaterial == null) {
      Shader shader = Shader.Find("Hidden/VisionConeUnlit");
      _generatedMaterial = new Material(shader) { name = "VisionConeMesh (Generated)" };
    }

    _generatedMaterial.SetColor("_Color", color);
    _meshRenderer.sharedMaterial = _generatedMaterial;
  }

  private void RebuildMesh() {
    int segments = Mathf.Max(1, rayCount);
    Vector3 origin = transform.position + Vector3.up * eyeHeight;
    float halfFov = fieldOfView * 0.5f;
    float angleStep = fieldOfView / segments;
    float halfThickness = coneThickness * 0.5f;

    int rimCount = segments + 1;
    var apexTop = transform.InverseTransformPoint(origin + Vector3.up * halfThickness);
    var apexBottom = transform.InverseTransformPoint(origin - Vector3.up * halfThickness);
    var rimTop = new Vector3[rimCount];
    var rimBottom = new Vector3[rimCount];

    for (int i = 0; i < rimCount; i++) {
      float angle = -halfFov + angleStep * i;
      Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;

      float distance = viewDistance;
      if (Physics.Raycast(origin, direction, out RaycastHit hit, viewDistance, obstacleMask)) {
        distance = hit.distance;
      }

      Vector3 worldPoint = origin + direction * distance;
      rimTop[i] = transform.InverseTransformPoint(worldPoint + Vector3.up * halfThickness);
      rimBottom[i] = transform.InverseTransformPoint(worldPoint - Vector3.up * halfThickness);
    }

    // Layout: 0 = apexTop, 1 = apexBottom, then rimCount top rim, then rimCount bottom rim.
    int topRimStart = 2;
    int bottomRimStart = topRimStart + rimCount;
    var vertices = new Vector3[bottomRimStart + rimCount];
    var uvs = new Vector2[vertices.Length];

    vertices[0] = apexTop;
    vertices[1] = apexBottom;
    uvs[0] = new Vector2(0.5f, 0f);
    uvs[1] = new Vector2(0.5f, 0f);

    for (int i = 0; i < rimCount; i++) {
      vertices[topRimStart + i] = rimTop[i];
      vertices[bottomRimStart + i] = rimBottom[i];
      float u = i / (float)segments;
      uvs[topRimStart + i] = new Vector2(u, 1f);
      uvs[bottomRimStart + i] = new Vector2(u, 1f);
    }

    var triangles = new int[(segments * 4 + 4) * 3];
    int t = 0;

    // Top cap fan.
    for (int i = 0; i < segments; i++) {
      triangles[t++] = 0;
      triangles[t++] = topRimStart + i;
      triangles[t++] = topRimStart + i + 1;
    }

    // Bottom cap fan (reversed winding so it faces down).
    for (int i = 0; i < segments; i++) {
      triangles[t++] = 1;
      triangles[t++] = bottomRimStart + i + 1;
      triangles[t++] = bottomRimStart + i;
    }

    // Outer curved wall between the top and bottom rims.
    for (int i = 0; i < segments; i++) {
      int topA = topRimStart + i;
      int topB = topRimStart + i + 1;
      int bottomA = bottomRimStart + i;
      int bottomB = bottomRimStart + i + 1;

      triangles[t++] = topA;
      triangles[t++] = topB;
      triangles[t++] = bottomB;

      triangles[t++] = topA;
      triangles[t++] = bottomB;
      triangles[t++] = bottomA;
    }

    // Flat end caps closing the two straight edges of the wedge.
    triangles[t++] = 0;
    triangles[t++] = bottomRimStart;
    triangles[t++] = topRimStart;
    triangles[t++] = 0;
    triangles[t++] = 1;
    triangles[t++] = bottomRimStart;

    int lastTop = topRimStart + segments;
    int lastBottom = bottomRimStart + segments;
    triangles[t++] = 0;
    triangles[t++] = lastTop;
    triangles[t++] = lastBottom;
    triangles[t++] = 0;
    triangles[t++] = lastBottom;
    triangles[t++] = 1;

    _mesh.Clear();
    _mesh.vertices = vertices;
    _mesh.uv = uvs;
    _mesh.triangles = triangles;
    _mesh.RecalculateBounds();
    _mesh.RecalculateNormals();
  }
}
