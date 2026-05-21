using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns everything related to performing a takedown:
///  • Serialised range / angle / layer-mask settings
///  • Runtime enable / disable flag
///  • Candidate-guard query (used by the highlighter and by TryTakedown)
///  • Input handler that fires the actual takedown
///
/// The stealth state machine (<see cref="PlayerStealthController"/>) drives
/// <see cref="IsEnabled"/> — this script never reads the stealth state directly.
/// </summary>
public class TakedownController : MonoBehaviour, ITakedownSystem {
  // -------------------------------------------------------------------------
  // Inspector
  // -------------------------------------------------------------------------

  [Header("Settings")]
  [Tooltip("Enable the takedown action at game start.")]
  public bool enabledAtStart = true;

  [Tooltip("Maximum distance to initiate a takedown.")]
  public float takedownRange = 1.5f;

  [Tooltip("Angular window (degrees) behind the guard where takedown is allowed. " +
           "Measured symmetrically around directly-behind (180 °). " +
           "E.g. 60 ° → valid from 150 °–180 °.")]
  public float takedownAngle = 60f;

  [Tooltip("LayerMask that identifies guard colliders.")]
  public LayerMask guardLayerMask;

  [Header("Debug")]
  public bool verboseLogging = false;

  // -------------------------------------------------------------------------
  // ITakedownSystem
  // -------------------------------------------------------------------------

  /// <inheritdoc/>
  public bool IsEnabled { get; set; }

  /// <inheritdoc/>
  public float TakedownRange { get; set; }

  /// <inheritdoc/>
  public float TakedownAngle { get; set; }

  /// <inheritdoc/>
  public LayerMask GuardLayerMask { get; set; }

  /// <inheritdoc/>
  public IReadOnlyList<GuardController> GetCandidates() {
    List<GuardController> result = new();

    if (guardLayerMask.value == 0) return result;

    Vector3 origin = transform.position;
    float range = Mathf.Max(0.01f, takedownRange);
    Collider[] hits = Physics.OverlapSphere(origin, range, guardLayerMask, QueryTriggerInteraction.Collide);

    foreach (Collider hit in hits) {
      if (hit == null) continue;

      GuardController guard = hit.GetComponentInParent<GuardController>();
      if (guard == null || guard.CurrentState == GuardController.GuardState.TakenDown) continue;
      if (!IsBehindGuard(origin, guard)) continue;

      result.Add(guard);
    }

    return result;
  }

  // -------------------------------------------------------------------------
  // Unity lifecycle
  // -------------------------------------------------------------------------

  private void Awake() {
    IsEnabled = enabledAtStart;
  }

  // -------------------------------------------------------------------------
  // Input (wired via PlayerInput component — action name: "Takedown")
  // -------------------------------------------------------------------------

  public void OnTakedown(InputValue value) {
    if (value.isPressed)
      TryTakedown();
  }

  // -------------------------------------------------------------------------
  // Takedown execution
  // -------------------------------------------------------------------------

  private void TryTakedown() {
    if (!IsEnabled) {
      if (verboseLogging) Debug.Log("[Takedown] Blocked — IsEnabled is false.");
      return;
    }

    if (guardLayerMask.value == 0) {
      Debug.LogWarning("[Takedown] guardLayerMask is Nothing — no guards can be found. " +
                       "Assign the guard layer in the Inspector.");
      return;
    }

    IReadOnlyList<GuardController> candidates = GetCandidates();

    if (verboseLogging)
      Debug.Log($"[Takedown] {candidates.Count} valid candidate(s) in range and behind.");

    if (candidates.Count == 0) {
      if (verboseLogging)
        Debug.Log("[Takedown] No valid target. Get behind a guard and try again.");
      return;
    }

    // Take down the closest valid candidate.
    GuardController best = null;
    float bestDist = float.MaxValue;

    foreach (GuardController guard in candidates) {
      float dist = Vector3.Distance(transform.position, guard.transform.position);
      if (dist < bestDist) { bestDist = dist; best = guard; }
    }

    best!.PerformTakedown();
    if (verboseLogging) Debug.Log($"[Takedown] SUCCESS on '{best.name}'.");
  }

  // -------------------------------------------------------------------------
  // Angle check — shared by GetCandidates and gizmo drawing
  // -------------------------------------------------------------------------

  /// <summary>
  /// Returns true if <paramref name="playerPosition"/> lies within the allowed
  /// cone directly behind <paramref name="guard"/>.
  /// </summary>
  public bool IsBehindGuard(Vector3 playerPosition, GuardController guard) {
    Vector3 toPlayerFlat = new(
        playerPosition.x - guard.transform.position.x,
        0f,
        playerPosition.z - guard.transform.position.z);

    if (toPlayerFlat.sqrMagnitude < 0.0001f) return false;

    Vector3 guardFwdFlat = new(guard.transform.forward.x, 0f, guard.transform.forward.z);
    if (guardFwdFlat.sqrMagnitude < 0.0001f) return false;

    float angleToPlayer = Vector3.Angle(guardFwdFlat.normalized, toPlayerFlat.normalized);
    float minAngleNeeded = 180f - Mathf.Max(0f, takedownAngle) * 0.5f;

    return angleToPlayer >= minAngleNeeded;
  }

  // -------------------------------------------------------------------------
  // Gizmos
  // -------------------------------------------------------------------------

  private void OnDrawGizmosSelected() {
    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}
