using UnityEngine;

/// <summary>
/// Boolean light zone: any player inside this trigger collider is considered "in light",
/// which activates the guard's long vision cone.
///
/// Setup:
///   1. Create a GameObject, add this script.
///   2. Add a Collider (Box, Sphere, etc.) and set it as a Trigger.
///   3. Position/scale the collider to match your light pool in the scene.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LightZone : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("Optional light source linked to this zone (purely visual reference)")]
    public Light linkedLight;

    [Tooltip("Gizmo colour in the Scene view")]
    public Color gizmoColor = new Color(1f, 0.92f, 0.016f, 0.25f);

    // ── Unity Messages ────────────────────────────────────────────────────────
    private void Awake()
    {
        // Ensure the collider is a trigger
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[LightZone] {name}: Collider was not a trigger — fixed automatically.", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerStealthController player = other.GetComponentInParent<PlayerStealthController>();
        if (player != null)
        {
            player.EnterLight(this);
            Debug.Log($"[LightZone] Player entered light zone: {name}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerStealthController player = other.GetComponentInParent<PlayerStealthController>();
        if (player != null)
        {
            player.ExitLight(this);
            Debug.Log($"[LightZone] Player exited light zone: {name}");
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = gizmoColor;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
        {
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
        else if (col is CapsuleCollider)
        {
            // Fallback: draw sphere at center
            Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}
