using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Drives the Custom/WallCutout shader: on every LateUpdate, checks whether a wall sits between
/// viewer (the camera) and target (the player) and, if so, pushes a world-space CONE as GLOBAL
/// shader properties — every material using WallCutout reacts automatically, no per-wall wiring
/// needed. The cone's tip (radius 0) is at target; it widens to baseRadius at viewer. That means
/// a wall right next to the player only needs a small hole, while a wall close to the camera
/// gets a wide one — matching how much of the view each point along that line actually blocks.
/// When nothing is occluding the view, the cutout radius collapses to a large negative value so
/// every wall's clip() is a no-op and no hole is visible anywhere.
/// </summary>
public class WallCutoutController : MonoBehaviour {
  private static readonly int CutoutApexId = Shader.PropertyToID("_CutoutApex");
  private static readonly int CutoutBaseId = Shader.PropertyToID("_CutoutBase");
  private static readonly int CutoutBaseRadiusId = Shader.PropertyToID("_CutoutBaseRadius");

  [Header("References")]
  [Tooltip("The camera looking at target — becomes the cone's BASE. Defaults to Camera.main if left empty.")]
  public Transform viewer;

  [Tooltip("The subject the cutout points at — becomes the cone's APEX (radius 0). Usually the player.")]
  public Transform target;

  [Header("Occlusion Check")]
  [Tooltip("Layer(s) that can occlude the view and should be cut out.")]
  public LayerMask wallLayer;

  [Tooltip("Only cut a hole while a wall is actually between viewer and target. If false, the cutout is always active along the apex->base line.")]
  public bool onlyWhenOccluded = true;

  [Header("Cutout Shape")]
  [Tooltip("Radius (world units) of the cone at its base, i.e. at the viewer — size of the base.")]
  public float baseRadius = 1.5f;

  [Header("Debug")]
  public bool drawDebugGizmos = true;

  private void Reset() {
    wallLayer = ~0;
  }

  private void Awake() {
    if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
  }

  private void LateUpdate() {
    if (target == null || viewer == null) {
      Shader.SetGlobalFloat(CutoutBaseRadiusId, -1000f);
      return;
    }

    Vector3 apex = target.position;
    Vector3 basePoint = viewer.position;
    Vector3 toViewer = basePoint - apex;
    float fullDistance = toViewer.magnitude;
    Vector3 direction = fullDistance > 0.001f ? toViewer / fullDistance : -viewer.forward;

    bool occluded = !onlyWhenOccluded;
    if (onlyWhenOccluded && fullDistance > 0.001f
        && Physics.Raycast(viewer.position, -direction, out _, fullDistance, wallLayer, QueryTriggerInteraction.Ignore)) {
      occluded = true;
    }

    if (occluded) {
      Shader.SetGlobalVector(CutoutApexId, apex);
      Shader.SetGlobalVector(CutoutBaseId, basePoint);
      Shader.SetGlobalFloat(CutoutBaseRadiusId, baseRadius);
    } else {
      Shader.SetGlobalFloat(CutoutBaseRadiusId, -1000f);
    }
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || target == null || viewer == null) return;

    Gizmos.color = Color.cyan;
    Gizmos.DrawLine(viewer.position, target.position);

    Gizmos.color = Color.yellow;
    Gizmos.DrawWireSphere(target.position, 0.05f); // apex — tapers to a point here

    Vector3 axis = viewer.position - target.position;
    if (axis.sqrMagnitude > 0.0001f) {
      Handles.color = Color.cyan;
      Handles.DrawWireDisc(viewer.position, axis.normalized, baseRadius); // base circle at the viewer
    }
  }
#endif
}
