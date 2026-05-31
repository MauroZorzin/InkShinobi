using UnityEngine;

/// <summary>
/// Emits a one-shot investigate signal to any guard that enters the attached trigger collider.
/// Spawn it at a position (thrown object, footstep, door bang) and it destroys itself after
/// <see cref="lifetime"/> seconds.
///
/// Requires a Collider on the same GameObject with <b>Is Trigger</b> checked.
/// Resize the collider in the Inspector to set the hearing range.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GuardSoundSignal : MonoBehaviour {

  [Header("Signal")]
  [Tooltip("Seconds before this GameObject destroys itself. Zero = destroy on the same frame after notifying guards.")]
  [Min(0f)] public float lifetime = 3f;

  [Tooltip("If true the signal fires immediately on Awake, contacting any guards already inside the collider.")]
  public bool triggerOnAwake = true;

  [Header("Debug")]
  [Tooltip("Logs which guards were notified.")]
  public bool verboseLogging = false;


  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (!col.isTrigger) {
      Debug.LogWarning($"[SoundSignal] '{name}': Collider is not set to Is Trigger. " +
                       "Setting it automatically.", this);
      col.isTrigger = true;
    }

    if (triggerOnAwake) {
      // OverlapSphere at birth to catch guards already inside the volume.
      // We derive the radius from a SphereCollider if present otherwise use
      // the collider bounds half-extents as a fallback.
      float radius = GetApproximateRadius(col);
      Collider[] hits = Physics.OverlapSphere(transform.position, radius);
      foreach (Collider hit in hits) {
        TryNotifyGuard(hit);
      }
    }

    if (lifetime > 0f) {
      Destroy(gameObject, lifetime);
    } else {
      Destroy(gameObject);
    }
  }

  private void OnTriggerEnter(Collider other) {
    TryNotifyGuard(other);
  }


  private void TryNotifyGuard(Collider col) {
    GuardController guard = col.GetComponentInParent<GuardController>();
    if (guard == null) {
      return;
    }

    guard.InvestigateSound(transform.position);

    if (verboseLogging) {
      Debug.Log($"[SoundSignal] '{name}' notified guard '{guard.name}'.");
    }
  }

  private static float GetApproximateRadius(Collider col) {
    if (col is SphereCollider sphere) {
      return sphere.radius * Mathf.Max(
        col.transform.lossyScale.x,
        col.transform.lossyScale.y,
        col.transform.lossyScale.z);
    }

    // Fallback: use the largest half-extent of the bounds.
    Vector3 ext = col.bounds.extents;
    return Mathf.Max(ext.x, ext.y, ext.z);
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    Collider col = GetComponent<Collider>();
    if (col == null) return;

    Gizmos.color = new Color(1f, 0.6f, 0f, 0.15f);
    Gizmos.DrawSphere(transform.position, GetApproximateRadius(col));
    Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, GetApproximateRadius(col));
  }
#endif
}