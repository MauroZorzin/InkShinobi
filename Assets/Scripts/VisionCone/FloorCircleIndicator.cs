using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FloorCircleIndicator : MonoBehaviour {
  [Header("Shape")]
  [Range(0.1f, 30f)] public float radius = 5f;

  [Header("Floor Snap")]
  [Tooltip("Only surfaces on these layers are matched (e.g. your Floor layer).")]
  public LayerMask floorMask;
  [Tooltip("How far above the current position the raycast starts, to catch floors level with or slightly above it.")]
  public float castUpOffset = 1f;
  [Tooltip("How far down to search for the floor below the cast start.")]
  public float maxDropDistance = 5f;
  [Tooltip("Small lift above the floor surface to avoid z-fighting.")]
  public float heightOffset = 0.02f;

  [Header("Look")]
  public Color fillColor = new Color(1f, 0.85f, 0.3f, 0.15f);
  public Color ringColor = new Color(1f, 0.85f, 0.3f, 1f);
  [Range(0f, 1f)] public float ringStart = 0.85f;
  [Range(0.001f, 0.3f)] public float softness = 0.05f;

  [Header("Wall Occlusion (baked into a texture)")]
  [Tooltip("Layers that block the light. Leave empty to ignore walls entirely.")]
  public LayerMask obstacleMask;
  [Tooltip("Height above this indicator the light originates from (e.g. the guard's torch/eyes).")]
  public float lightSourceHeight = 1.6f;
  [Tooltip("Resolution of the baked occlusion mask, per side.")]
  [Range(8, 128)] public int bakeResolution = 32;

  [Header("Material")]
  [Tooltip("Leave empty to use a built-in circle material.")]
  public Material material;

  [Header("Debug")]
  public bool showGizmos = true;
  [Range(2, 24)] public int gizmoSampleCount = 10;

  private Mesh _mesh;
  private MeshFilter _meshFilter;
  private MeshRenderer _meshRenderer;
  private Material _generatedMaterial;

  private Texture2D _occlusionTexture;
  private Color32[] _occlusionPixels;
  private int _bakedResolution = -1;

  private void OnEnable() {
    _meshFilter = GetComponent<MeshFilter>();
    _meshRenderer = GetComponent<MeshRenderer>();

    if (_mesh == null) {
      _mesh = BuildQuad();
    }
    _meshFilter.sharedMesh = _mesh;

    _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    _meshRenderer.receiveShadows = false;

    SnapToFloor();
    BakeOcclusion();
    ApplyMaterial();
  }

  private void Update() {
    SnapToFloor();
    ApplyMaterial();
  }

  [ContextMenu("Bake")]
  public void Bake() {
    SnapToFloor();
    BakeOcclusion();
    ApplyMaterial();
  }

  private void OnDrawGizmosSelected() {
    if (!showGizmos) {
      return;
    }

    Vector3 castOrigin = transform.position + Vector3.up * castUpOffset;
    bool floorHit = Physics.Raycast(castOrigin, Vector3.down, out RaycastHit floorHitInfo, castUpOffset + maxDropDistance, floorMask);

    Gizmos.color = floorHit ? Color.green : Color.red;
    Gizmos.DrawLine(castOrigin, castOrigin + Vector3.down * (castUpOffset + maxDropDistance));
    Gizmos.DrawWireSphere(castOrigin, 0.1f);
    if (floorHit) {
      Gizmos.DrawWireSphere(floorHitInfo.point, 0.15f);
    }

    Vector3 lightOrigin = transform.position + transform.up * lightSourceHeight;
    Gizmos.color = Color.yellow;
    Gizmos.DrawWireSphere(lightOrigin, 0.15f);

    int samples = Mathf.Max(2, gizmoSampleCount);
    for (int y = 0; y < samples; y++) {
      float v = (y + 0.5f) / samples;
      float offsetZ = (v - 0.5f) * radius * 2f;

      for (int x = 0; x < samples; x++) {
        float u = (x + 0.5f) / samples;
        float offsetX = (u - 0.5f) * radius * 2f;

        if (Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f > 1f) {
          continue;
        }

        Vector3 samplePoint = transform.position + transform.right * offsetX + transform.forward * offsetZ;
        bool blocked = obstacleMask.value != 0 && Physics.Linecast(lightOrigin, samplePoint, obstacleMask);

        Gizmos.color = blocked ? Color.red : Color.green;
        Gizmos.DrawSphere(samplePoint, radius * 0.02f);
      }
    }

    Gizmos.color = Color.cyan;
    const int circleSegments = 48;
    Vector3 prev = transform.position + transform.right * radius;
    for (int i = 1; i <= circleSegments; i++) {
      float angle = i / (float)circleSegments * Mathf.PI * 2f;
      Vector3 next = transform.position + transform.right * (Mathf.Cos(angle) * radius) + transform.forward * (Mathf.Sin(angle) * radius);
      Gizmos.DrawLine(prev, next);
      prev = next;
    }
  }

  private static Mesh BuildQuad() {
    var mesh = new Mesh { name = "FloorCircleIndicator" };
    mesh.vertices = new[] {
      new Vector3(-0.5f, 0f, -0.5f),
      new Vector3(0.5f, 0f, -0.5f),
      new Vector3(-0.5f, 0f, 0.5f),
      new Vector3(0.5f, 0f, 0.5f),
    };
    mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
    mesh.uv = new[] {
      new Vector2(0f, 0f),
      new Vector2(1f, 0f),
      new Vector2(0f, 1f),
      new Vector2(1f, 1f),
    };
    mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
    mesh.RecalculateBounds();
    return mesh;
  }

  private void SnapToFloor() {
    Vector3 castOrigin = transform.position + Vector3.up * castUpOffset;

    if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, castUpOffset + maxDropDistance, floorMask)) {
      transform.position = hit.point + hit.normal * heightOffset;
      transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
      _meshRenderer.enabled = true;
    } else {
      _meshRenderer.enabled = false;
    }

    Vector3 parentLossyScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
    transform.localScale = new Vector3(
      SafeDivide(radius * 2f, parentLossyScale.x),
      SafeDivide(1f, parentLossyScale.y),
      SafeDivide(radius * 2f, parentLossyScale.z)
    );
  }

  private static float SafeDivide(float value, float divisor) {
    return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
  }

  private void BakeOcclusion() {
    int res = Mathf.Max(4, bakeResolution);

    if (_occlusionTexture == null || _bakedResolution != res) {
      _occlusionTexture = new Texture2D(res, res, TextureFormat.R8, false) {
        name = "FloorCircleOcclusion",
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear
      };
      _occlusionPixels = new Color32[res * res];
      _bakedResolution = res;
    }

    Vector3 lightOrigin = transform.position + transform.up * lightSourceHeight;
    for (int y = 0; y < res; y++) {
      float v = (y + 0.5f) / res;
      float offsetZ = (v - 0.5f) * radius * 2f;

      for (int x = 0; x < res; x++) {
        float u = (x + 0.5f) / res;
        float offsetX = (u - 0.5f) * radius * 2f;

        Vector3 samplePoint = transform.position + transform.right * offsetX + transform.forward * offsetZ;
        bool blocked = obstacleMask.value != 0 && Physics.Linecast(lightOrigin, samplePoint, obstacleMask);
        byte value = blocked ? (byte)0 : (byte)255;
        _occlusionPixels[y * res + x] = new Color32(value, value, value, value);
      }
    }

    _occlusionTexture.SetPixels32(_occlusionPixels);
    _occlusionTexture.Apply();
  }

  private void ApplyMaterial() {
    if (material != null) {
      _meshRenderer.sharedMaterial = material;
      return;
    }

    if (_generatedMaterial == null) {
      Shader shader = Shader.Find("Hidden/FloorCircleIndicator");
      _generatedMaterial = new Material(shader) { name = "FloorCircleIndicator (Generated)" };
    }

    _generatedMaterial.SetColor("_FillColor", fillColor);
    _generatedMaterial.SetColor("_RingColor", ringColor);
    _generatedMaterial.SetFloat("_RingStart", ringStart);
    _generatedMaterial.SetFloat("_Softness", softness);
    if (_occlusionTexture != null) {
      _generatedMaterial.SetTexture("_OcclusionMask", _occlusionTexture);
    }
    _meshRenderer.sharedMaterial = _generatedMaterial;
  }
}
