using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the player's stealth state: visibility, light exposure, and takedown capability.
/// Attach to the Player GameObject.
/// Requires a PlayerInput component with a "Takedown" action in your Input Action Asset.
/// </summary>
public class PlayerStealthController : MonoBehaviour {
  [Header("Stealth Settings")]
  [Tooltip("How long the player must stay still/hidden before becoming fully hidden")]
  public float timeToHide = 1.0f;

  [Header("Takedown Settings")]
  [Tooltip("Maximum distance to initiate a takedown")]
  public float takedownRange = 1.5f;
  [Tooltip("The angle (degrees) behind the guard within which takedown is allowed — measured from directly behind (180°)")]
  public float takedownAngle = 60f;
  [Tooltip("LayerMask for detecting guards")]
  public LayerMask guardLayerMask;

  [Header("Debug")]
  public bool showDebugGizmos = true;
  [Tooltip("Logs detailed takedown check info every time you press the takedown button")]
  public bool verboseLogging = true;

  // ── Public read-only state ──────────────────────────────────────────────
  public bool IsHidden { get; private set; } = true;
  public bool IsInLight { get; private set; } = false;
  public int DetectingGuardCount { get; private set; } = 0;

  // ── Private ─────────────────────────────────────────────────────────────
  private float _hiddenTimer = 0f;
  private LightZone _currentLightZone;

  // ── Unity Messages ───────────────────────────────────────────────────────
  private void Update() => UpdateHiddenState();

  public void OnTakedown(InputValue value) {
    if (value.isPressed)
      TryTakedown();
  }

  // ── Stealth Logic ────────────────────────────────────────────────────────
  private void UpdateHiddenState() {
    if (DetectingGuardCount > 0) {
      IsHidden = false;
      _hiddenTimer = 0f;
    } else {
      _hiddenTimer += Time.deltaTime;
      if (_hiddenTimer >= timeToHide)
        IsHidden = true;
    }
  }

  public void OnGuardStartsDetecting() {
    DetectingGuardCount++;
    IsHidden = false;
    _hiddenTimer = 0f;
  }

  public void OnGuardStopsDetecting() {
    DetectingGuardCount = Mathf.Max(0, DetectingGuardCount - 1);
  }

  // ── Light Zone ───────────────────────────────────────────────────────────
  public void EnterLight(LightZone zone) {
    _currentLightZone = zone;
    IsInLight = true;
  }

  public void ExitLight(LightZone zone) {
    if (_currentLightZone == zone) {
      _currentLightZone = null;
      IsInLight = false;
    }
  }

  // ── Takedown ─────────────────────────────────────────────────────────────
  private void TryTakedown() {
    if (verboseLogging)
      Debug.Log($"[Takedown] Pressed | pos={transform.position:F2} range={takedownRange} mask={guardLayerMask.value}");

    // ── 1. Layer mask sanity check ────────────────────────────────────────
    if (guardLayerMask.value == 0) {
      Debug.LogWarning("[Takedown] guardLayerMask is Nothing — no guards can ever be found! Set it to the guard's layer.");
      return;
    }

    // ── 2. Overlap sphere ─────────────────────────────────────────────────
    Collider[] hits = Physics.OverlapSphere(transform.position, takedownRange, guardLayerMask);

    if (verboseLogging)
      Debug.Log($"[Takedown] OverlapSphere found {hits.Length} collider(s) in range.");

    if (hits.Length == 0) {
      Debug.Log("[Takedown] No guards within range. Try getting closer or increasing takedownRange.");
      return;
    }

    // ── 3. Check each candidate ───────────────────────────────────────────
    foreach (Collider hit in hits) {
      GuardController guard = hit.GetComponentInParent<GuardController>();

      if (guard == null) {
        if (verboseLogging)
          Debug.Log($"[Takedown]   '{hit.name}' hit but no GuardController found in self or parents — skipped. " +
                    "Make sure GuardController is on the same GameObject as (or a parent of) the collider.");
        continue;
      }

      // Direction from guard to player, flattened to horizontal
      Vector3 toPlayerFlat = new Vector3(
          transform.position.x - guard.transform.position.x, 0f,
          transform.position.z - guard.transform.position.z).normalized;
      Vector3 guardFwdFlat = new Vector3(
          guard.transform.forward.x, 0f,
          guard.transform.forward.z).normalized;

      // Angle between guard's forward and the direction TO the player.
      // If player is directly behind: angle = 180°.
      // takedownAngle defines the half-window around 180°, e.g. 60° → valid from 150°–180°.
      float angleToPlayer = Vector3.Angle(guardFwdFlat, toPlayerFlat);
      float minAngleNeeded = 180f - takedownAngle * 0.5f;
      bool behindGuard = angleToPlayer >= minAngleNeeded;

      if (verboseLogging)
        Debug.Log($"[Takedown]   Guard '{guard.name}' | " +
                  $"dist={Vector3.Distance(transform.position, guard.transform.position):F2}m | " +
                  $"horizAngle={angleToPlayer:F1}° (need >={minAngleNeeded:F1}° to be behind) | " +
                  $"behindGuard={behindGuard} | guardState={guard.CurrentState}");

      if (behindGuard) {
        guard.PerformTakedown();
        Debug.Log($"[Takedown] SUCCESS on '{guard.name}'!");
        return;
      }
    }

    Debug.Log("[Takedown] Guard(s) found but none were approached from behind. " +
              "Get behind the guard and try again.");
  }

  // ── Gizmos ───────────────────────────────────────────────────────────────
  private void OnDrawGizmosSelected() {
    if (!showDebugGizmos) return;

    // Takedown range sphere
    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}