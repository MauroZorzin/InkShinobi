using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws an invisible doorway-sized quad into one stencil bit before the moving door panels.
/// Panel materials render only where this aperture has marked the frame, preventing them from
/// showing through the zero-thickness wall surfaces while sliding.
/// </summary>
[DisallowMultipleComponent]
public sealed class DoorStencilMask : MonoBehaviour {
  [SerializeField] private Material maskMaterial;
  [SerializeField, Min(0.01f)] private float apertureWidth = 1.5f;
  [SerializeField, Min(0.01f)] private float apertureHeight = 1.5f;
  [Tooltip("Extra space around the measured doorway aperture so panel/frame contact edges are not clipped.")]
  [SerializeField, Min(0f)] private float aperturePadding;
  [SerializeField] private Vector3 apertureCenter = new(0f, 0.75f, 0f);

  private GameObject maskObject;
  private Mesh maskMesh;

  private void OnEnable() => Rebuild();

  private void OnDisable() => Release();

  private void OnDestroy() => Release();

  private void Rebuild() {
    if (!gameObject.scene.IsValid() || !isActiveAndEnabled || maskMaterial == null) {
      Release();
      return;
    }

    EnsureObjects();
    Vector3 center = apertureCenter;
    float width = apertureWidth;
    float height = apertureHeight;
    TryGetClosedPanelBounds(ref center, ref width, ref height);
    float halfWidth = width * 0.5f + aperturePadding;
    float halfHeight = height * 0.5f + aperturePadding;
    maskMesh.vertices = new[] {
      center + new Vector3(-halfWidth, -halfHeight, 0f),
      center + new Vector3(-halfWidth, halfHeight, 0f),
      center + new Vector3(halfWidth, halfHeight, 0f),
      center + new Vector3(halfWidth, -halfHeight, 0f),
    };
    maskMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    maskMesh.RecalculateBounds();
  }

  private void TryGetClosedPanelBounds(ref Vector3 center, ref float width, ref float height) {
    PassagewayDoor door = GetComponent<PassagewayDoor>();
    if (door == null) return;

    Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    bool foundBounds = false;
    IncludePanelBounds(door.LeftDoorPanel, ref minimum, ref maximum, ref foundBounds);
    IncludePanelBounds(door.RightDoorPanel, ref minimum, ref maximum, ref foundBounds);
    if (!foundBounds) return;

    center = (minimum + maximum) * 0.5f;
    width = Mathf.Max(0.01f, maximum.x - minimum.x);
    height = Mathf.Max(0.01f, maximum.y - minimum.y);
  }

  private void IncludePanelBounds(
    Transform panel,
    ref Vector3 minimum,
    ref Vector3 maximum,
    ref bool foundBounds) {
    MeshFilter filter = panel != null ? panel.GetComponent<MeshFilter>() : null;
    Mesh mesh = filter != null ? filter.sharedMesh : null;
    if (mesh == null) return;

    Bounds bounds = mesh.bounds;
    Vector3 boundsMin = bounds.min;
    Vector3 boundsMax = bounds.max;
    for (int x = 0; x <= 1; x++) {
      for (int y = 0; y <= 1; y++) {
        for (int z = 0; z <= 1; z++) {
          Vector3 meshPoint = new(
            x == 0 ? boundsMin.x : boundsMax.x,
            y == 0 ? boundsMin.y : boundsMax.y,
            z == 0 ? boundsMin.z : boundsMax.z);
          Vector3 localPoint = transform.InverseTransformPoint(panel.TransformPoint(meshPoint));
          minimum = Vector3.Min(minimum, localPoint);
          maximum = Vector3.Max(maximum, localPoint);
          foundBounds = true;
        }
      }
    }
  }

  private void EnsureObjects() {
    if (maskObject != null && maskMesh != null) return;

    maskObject = new GameObject("Door Stencil Aperture") {
      hideFlags = HideFlags.HideAndDontSave,
      layer = gameObject.layer,
    };
    Transform maskTransform = maskObject.transform;
    maskTransform.SetParent(transform, false);
    maskTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    maskTransform.localScale = Vector3.one;

    maskMesh = new Mesh {
      name = "Door Stencil Aperture",
      hideFlags = HideFlags.HideAndDontSave,
    };
    maskObject.AddComponent<MeshFilter>().sharedMesh = maskMesh;
    MeshRenderer maskRenderer = maskObject.AddComponent<MeshRenderer>();
    maskRenderer.sharedMaterial = maskMaterial;
    maskRenderer.shadowCastingMode = ShadowCastingMode.Off;
    maskRenderer.receiveShadows = false;
    maskRenderer.lightProbeUsage = LightProbeUsage.Off;
    maskRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    maskRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
  }

  private void Release() {
    GameObject oldObject = maskObject;
    Mesh oldMesh = maskMesh;
    maskObject = null;
    maskMesh = null;
    DestroyGenerated(oldObject);
    DestroyGenerated(oldMesh);
  }

  private static void DestroyGenerated(Object generatedObject) {
    if (generatedObject == null) return;
    if (Application.isPlaying) Destroy(generatedObject);
    else DestroyImmediate(generatedObject);
  }
}
