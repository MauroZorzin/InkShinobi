using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace PxP.DOCS {

  public class DynamicOcclusionCutoutSystem : MonoBehaviour {

    // --- Materials references ---
    [Tooltip("The materials contained in Project folders that have to be updated")]
    [SerializeField] private List<Material> m_materials = new List<Material>();

    // --- Scene objects reference ---

    [Tooltip("The target behind the occluded objects\n(ex: Player behind wall)")]
    [SerializeField] public Transform m_target;

    // --- Cutout parameters ---

    [Min(0.0f)]
    [Tooltip("The radius of the occlusion mask")]
    [SerializeField] float m_maskRadius = 4f;


    // --- Debug Settings ---
    [Space(10)]
    [SerializeField] bool m_enableGizmos = false;

    // --- Private variables ---

    private Camera m_camera;
    Vector3 targetPosition;
    private float targetMaskRadius = 0.0f;

    bool isHitting = false;

    private void OnValidate() {
      m_camera = Camera.main;
    }

    private void OnDisable() {
      foreach (Material mat in m_materials) {
        mat.SetVector("_Target_Position", Vector3.zero);
        mat.SetFloat("_Radius", 0.0f);
      }
    }

    private void Awake() {
      if (m_target == null || m_camera == null)
        m_camera = Camera.main;
    }

    void Start() {
      if (m_target == null)
        UnityEngine.Debug.LogWarning($"{this} - Start - Target is not set");
      if (m_camera == null)
        UnityEngine.Debug.LogWarning($"{this} - Start - Camera is not set");
      if (m_materials == null)
        UnityEngine.Debug.LogWarning($"{this} - Start - Materials are not set");
      if (m_target == null || m_camera == null || m_materials == null) return;
    }

    void Update() {
      if (m_target == null || m_camera == null || m_materials == null) {
        UnityEngine.Debug.LogWarning($"{this} - Update Stopped - Unassigned references");
        this.enabled = false;
        return;
      }

      PhysicalCutoutDetection(m_target.position);
    }

    // --- PUBLIC METHODS ---

    /// Sets the Camera of the DOCS system
    /// </summary>
    /// <param name="target">target</param>
    public void SetCamera(Camera camera) {
      m_camera = camera;
    }

    /// <summary>
    /// Sets the radius of the cutout mask
    /// </summary>
    /// <param name="radius">raduis of the mask</param>
    public void SetMaskRadius(float radius) {
      m_maskRadius = radius;
    }

    /// <summary>
    /// Sets the Target of the DOCS system
    /// </summary>
    /// <param name="target">target</param>
    public void SetTarget(Transform target) {
      m_target = target;
    }

    /// <summary>
    /// Sets the Target of the DOCS system
    /// </summary>
    /// <param name="target">target</param>
    public void SetTarget(GameObject target) {
      m_target = target.transform;
    }


    // --- CUTOUT FUNCTIONS ---

    /// <summary>
    /// Detects occluders using a physics SphereCast. Updates the mask position and radius<br/>
    /// based on the closest hit, and supports adaptive mask radius for consistent visual size.<br/>
    /// </summary>
    /// <param name="targetPos">The target position to detect occlusion from.</param>
    private void PhysicalCutoutDetection(Vector3 targetPos) {
      float maxDistance = Vector3.Distance(targetPos, m_camera.transform.position);

      if (maxDistance <= 0f) {
        ApplyNoHit(targetPos);
        UpdateSphereAndMaterial();
        return;
      }

      if (Physics.SphereCast(targetPos, 0.1f, (m_camera.transform.position - targetPos).normalized, out RaycastHit hitInfo, maxDistance + 1f))
        ApplyHit(hitInfo.point);
      else
        ApplyNoHit(targetPos);

      UpdateSphereAndMaterial();
    }


    // --- CUTOUT BASE FUNCTIONS

    /// <summary>
    /// Applies a hit result by updating the target position and mask radius,<br/>
    /// taking into account adaptive mask radius if enabled, and resetting lerp state.
    /// </summary>
    /// <param name="hitPoint">The world position where the cast hit an occluder.</param>
    private void ApplyHit(Vector3 hitPoint) {
      if (!isHitting)
        isHitting = true;

      targetPosition = hitPoint;
      targetMaskRadius = m_maskRadius;
    }

    /// <summary>
    /// Applies a no-hit result by resetting the target position to the original target,<br/>
    /// setting the mask radius to zero, and resetting lerp state.
    /// </summary>
    /// <param name="targetPos">The original target position.</param>
    private void ApplyNoHit(Vector3 targetPos) {
      if (isHitting)
        isHitting = false;

      targetPosition = targetPos;
      targetMaskRadius = 0.0f;
    }

    /// <summary>
    /// Smoothly interpolates the current sphere position and mask radius<br/>
    /// toward their target values, then updates all associated materials.
    /// </summary>
    private void UpdateSphereAndMaterial() {
      foreach (var mat in m_materials) {
        mat.SetVector("_Target_Position", targetPosition);
        mat.SetFloat("_Radius", targetMaskRadius);
      }
    }


    // --- HELPER FUNCTIONS ---

    private void OnDrawGizmosSelected() {
#if UNITY_EDITOR
      if (!m_enableGizmos || m_target == null || m_camera == null) return;

      Vector3 camPos = m_camera.transform.position;
      Vector3 camToTarget = m_target.position - camPos;
      float camToTargetDist = camToTarget.magnitude;
      Vector3 camForward = camToTarget.normalized;

      // --- PHYSICAL DETECTION ---
      float maxDistance = camToTargetDist;
      Vector3 castOrigin = m_target.position - camForward;

      // Draw capsule along cast
      Gizmos.color = Color.yellow;
      Capsule(castOrigin, camPos, 0.1f, Gizmos.color);

      if (Physics.SphereCast(castOrigin, 0.1f, (camPos - m_target.position).normalized, out RaycastHit hit, maxDistance)) {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(hit.point, 0.5f);

        GUIStyle redStyle = new GUIStyle();
        redStyle.normal.textColor = Color.red;
        Handles.Label(hit.point, hit.collider.name, redStyle);
      }
#endif
    }


    // --- HELPER FUNCTIONS - DRAW ---

    #region DRAWING

    /// <summary>
    /// Delegate for the line drawing function.
    /// </summary>
    /// <param name="start">The start point of the line segment.</param>
    /// <param name="end">The end point of the line segment.</param>
    public delegate void DrawLineDelegate(Vector3 start, Vector3 end);
    private static DrawLineDelegate GetDrawLineDelegate() {
      // Gizmos.DrawLine uses the currently set Gizmos.color
      return (start, end) => Gizmos.DrawLine(start, end);
    }
    /// <summary>
    /// Draws the wireframe of a capsule.
    /// </summary>
    public static void Capsule(Vector3 start, Vector3 end, float radius, Color color, int segments = 64) {
      Gizmos.color = color;
      DrawCapsule(start, end, radius, segments, GetDrawLineDelegate());
    }

    /// <summary>
    /// Calculates a robust orthonormal basis (axis1, axis2) for the plane
    /// perpendicular to the given normal vector.
    /// </summary>
    private static void GetOrthonormalBasis(Vector3 normal, out Vector3 axis1, out Vector3 axis2) {
      // Choose a helper vector not parallel to the normal
      Vector3 helper =
          Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.99f ?
          Vector3.up : Vector3.right;

      // Build an orthonormal basis
      axis1 = Vector3.Cross(normal, helper).normalized;
      axis2 = Vector3.Cross(normal, axis1);
    }

    /// <summary>
    /// Draws a wireframe hemisphere.
    /// </summary>
    /// <param name="center">Center of the hemisphere.</param>
    /// <param name="up">The direction vector pointing out of the flat side.</param>
    public static void DrawHemisphere(Vector3 center, Vector3 up, float radius, int segments, DrawLineDelegate drawLine) {
      up = up.normalized;
      GetOrthonormalBasis(up, out Vector3 axis1, out Vector3 axis2);

      // Draw the base circle (flat side)
      DrawCircle(center, up, radius, segments, drawLine);

      // Draw two perpendicular semi-circles to form the dome
      // Semi-circle 1 (along axis1)
      DrawArc(center, axis1, up, radius, 0f, 180f, segments / 2, drawLine);
      // Semi-circle 2 (along axis2)
      DrawArc(center, axis2, up, radius, 0f, 180f, segments / 2, drawLine);
    }

    /// <summary>
    /// Draws a wireframe circle.
    /// </summary>
    public static void DrawCircle(Vector3 center, Vector3 normal, float radius, int segments, DrawLineDelegate drawLine) {
      normal = normal.normalized;
      GetOrthonormalBasis(normal, out Vector3 axis1, out Vector3 axis2);
      DrawArc(center, axis1, axis2, radius, 0f, 360f, segments, drawLine);
    }

    /// <summary>
    /// Draws a wireframe arc.
    /// </summary>
    public static void DrawArc(Vector3 center, Vector3 axis1, Vector3 axis2,
        float radius, float startAngleDeg, float endAngleDeg, int segments, DrawLineDelegate drawLine) {
      axis1 = axis1.normalized;
      axis2 = axis2.normalized;

      float startRad = startAngleDeg * Mathf.Deg2Rad;
      float endRad = endAngleDeg * Mathf.Deg2Rad;

      float angleRange = endRad - startRad;
      int steps = Mathf.Max(1, segments);

      Vector3 GetPoint(float angle) {
        return center + (Mathf.Cos(angle) * axis1 + Mathf.Sin(angle) * axis2) * radius;
      }

      Vector3 prevPoint = GetPoint(startRad);

      for (int i = 1; i <= steps; i++) {
        float t = (float)i / steps;
        float angle = startRad + angleRange * t;

        Vector3 nextPoint = GetPoint(angle);

        drawLine(prevPoint, nextPoint);
        prevPoint = nextPoint;
      }
    }

    /// <summary>
    /// Draws the wireframe of a capsule.
    /// </summary>
    public static void DrawCapsule(Vector3 start, Vector3 end, float radius, int segments, DrawLineDelegate drawLine) {
      Vector3 axis = end - start;
      float length = axis.magnitude;
      Vector3 normal = axis.normalized;

      GetOrthonormalBasis(normal, out Vector3 axis1, out Vector3 axis2);

      // Draw the two hemispheres
      DrawHemisphere(end, normal, radius, segments, drawLine);
      DrawHemisphere(start, -normal, radius, segments, drawLine);

      // Draw the side lines connecting the two hemispheres
      for (int i = 0; i < 4; i++) {
        float angle = i * 2f * Mathf.PI / 4;
        Vector3 offset = (Mathf.Cos(angle) * axis1 + Mathf.Sin(angle) * axis2) * radius;
        drawLine(start + offset, end + offset);
      }
    }
    #endregion
  }
}