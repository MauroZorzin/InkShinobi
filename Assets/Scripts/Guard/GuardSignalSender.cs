using UnityEngine;

/// <summary>
/// Emits an investigate signal to guards that enter the trigger collider while active.
/// Call Activate() / Deactivate(), or set IsActive in the Inspector.
/// Auto-deactivates after <see cref="lifetime"/> seconds if lifetime > 0.
///
/// Requires a Collider on this GameObject with Is Trigger checked.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GuardSoundSignal : MonoBehaviour {

  [Header("Signal")]
  [Tooltip("Whether the signal is currently active.")]
  public bool IsActive = false;

  [Tooltip("Seconds until the signal deactivates automatically. 0 = never.")]
  [Min(0f)] public float lifetime = 0f;

  [Header("Debug")]
  public bool verboseLogging = false;

  private float _activeTimer = 0f;
  private bool _wasActive = false;

  // ── Public API ────────────────────────────────────────────────────────────

  /// <summary>Turns the signal on. Optionally sets a new lifetime countdown.</summary>
  public void Activate(float newLifetime = -1f) {
    if (newLifetime >= 0f) {
      lifetime = newLifetime;
    }
    _activeTimer = 0f;
    IsActive = true;
  }

  /// <summary>Turns the signal off immediately.</summary>
  public void Deactivate() {
    IsActive = false;
    _activeTimer = 0f;
  }

  // ── Unity lifecycle ───────────────────────────────────────────────────────

  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (!col.isTrigger) {
      Debug.LogWarning($"[SoundSignal] '{name}': Collider is not Is Trigger — fixing automatically.", this);
      col.isTrigger = true;
    }
  }

  private void Update() {
    // Detect the moment IsActive flips on (either from code or Inspector toggle).
    if (IsActive && !_wasActive) {
      OnBecameActive();
    }
    _wasActive = IsActive;

    // Countdown.
    if (IsActive && lifetime > 0f) {
      _activeTimer += Time.deltaTime;
      if (_activeTimer >= lifetime) {
        Deactivate();
      }
    }
  }

  private void OnTriggerEnter(Collider other) {
    if (!IsActive) return;
    TryNotifyGuard(other);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  /// <summary>
  /// Called the frame IsActive becomes true.
  /// Does a one-shot OverlapSphere to notify guards already inside the collider,
  /// because OnTriggerEnter won't fire for them.
  /// </summary>
  private void OnBecameActive() {
    Collider col = GetComponent<Collider>();
    float radius = GetApproximateRadius(col);
    Collider[] hits = Physics.OverlapSphere(transform.position, radius);
    foreach (Collider hit in hits) {
      TryNotifyGuard(hit);
    }

    if (verboseLogging) {
      Debug.Log($"[SoundSignal] '{name}' became active (lifetime={lifetime:F1}s).");
    }
  }

  private void TryNotifyGuard(Collider col) {
    GuardController guard = col.GetComponentInParent<GuardController>();
    if (guard == null) return;

    guard.InvestigateSound(transform.position);

    if (verboseLogging) {
      Debug.Log($"[SoundSignal] '{name}' notified '{guard.name}'.");
    }
  }

  private static float GetApproximateRadius(Collider col) {
    if (col is SphereCollider sphere) {
      return sphere.radius * Mathf.Max(
        col.transform.lossyScale.x,
        col.transform.lossyScale.y,
        col.transform.lossyScale.z);
    }
    Vector3 ext = col.bounds.extents;
    return Mathf.Max(ext.x, ext.y, ext.z);
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    Collider col = GetComponent<Collider>();
    if (col == null) return;
    Color c = IsActive ? new Color(1f, 0.6f, 0f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
    Gizmos.color = new Color(c.r, c.g, c.b, 0.12f);
    Gizmos.DrawSphere(transform.position, GetApproximateRadius(col));
    Gizmos.color = c;
    Gizmos.DrawWireSphere(transform.position, GetApproximateRadius(col));
  }
#endif
}