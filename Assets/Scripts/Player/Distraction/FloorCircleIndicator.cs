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

  [Header("Material")]
  [Tooltip("Leave empty to use a built-in circle material.")]
  public Material material;

  [Header("Debug")]
  public bool showGizmos = true;

  private Mesh _mesh;
  private MeshFilter _meshFilter;
  private MeshRenderer _meshRenderer;
  private Material _generatedMaterial;

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
    ApplyMaterial();
  }

  private void Update() {
    SnapToFloor();
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
    _meshRenderer.sharedMaterial = _generatedMaterial;
  }
}
