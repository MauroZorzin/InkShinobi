using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the player's stealth state: visibility, light exposure, and takedown capability.
/// Attach to the Player GameObject.
/// Requires a PlayerInput component with a "Takedown" action in your Input Action Asset.
/// </summary>
public class PlayerStealthController : MonoBehaviour {
  [Header("Stealth Settings")]
  [Tooltip("How long the player must avoid detection before becoming fully hidden.")]
  public float timeToHide = 1.0f;

  [Header("Takedown Settings")]
  [Tooltip("Maximum distance to initiate a takedown.")]
  public float takedownRange = 1.5f;

  [Tooltip("Allowed angle in degrees around the guard's back where takedown is possible.")]
  public float takedownAngle = 60f;

  [Tooltip("Layer mask used to find guards that can be targeted for takedown.")]
  public LayerMask guardLayerMask;

  [Header("Debug")]
  [Tooltip("Draws takedown range and angle helpers in the Scene view.")]
  public bool showDebugGizmos = true;

  [Tooltip("Logs detailed takedown check information when takedown input is pressed.")]
  public bool verboseLogging = true;

  public bool IsHidden { get; private set; } = true;
  public bool IsInLight { get; private set; } = false;
  public int DetectingGuardCount { get; private set; } = 0;

  private float _hiddenTimer = 0f;
  private LightZone _currentLightZone;

  private void Update() => UpdateHiddenState();

  public void OnTakedown(InputValue value) {
    if (value.isPressed) {
      TryTakedown();
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

  private void UpdateHiddenState() {
    if (DetectingGuardCount > 0) {
      IsHidden = false;
      _hiddenTimer = 0f;
      return;
    }

    _hiddenTimer += Time.deltaTime;
    if (_hiddenTimer >= timeToHide) {
      IsHidden = true;
    }
  }

  private void TryTakedown() {
    if (verboseLogging) {
      Debug.Log($"[Takedown] Pressed | pos={transform.position:F2} range={takedownRange} mask={guardLayerMask.value}");
    }

    if (guardLayerMask.value == 0) {
      Debug.LogWarning("[Takedown] guardLayerMask is Nothing; no guards can be found. Set it to the guard layer.");
      return;
    }

    Collider[] hits = Physics.OverlapSphere(transform.position, takedownRange, guardLayerMask);

    if (verboseLogging) {
      Debug.Log($"[Takedown] OverlapSphere found {hits.Length} collider(s) in range.");
    }

    if (hits.Length == 0) {
      Debug.Log("[Takedown] No guards within range. Try getting closer or increasing takedownRange.");
      return;
    }

    foreach (Collider hit in hits) {
      GuardController guard = hit.GetComponentInParent<GuardController>();

      if (guard == null) {
        if (verboseLogging) {
          Debug.Log($"[Takedown] '{hit.name}' hit but no GuardController found in self or parents; skipped.");
        }

        continue;
      }

      Vector3 toPlayerFlat = new(
        transform.position.x - guard.transform.position.x,
        0f,
        transform.position.z - guard.transform.position.z
      );

      Vector3 guardForwardFlat = new(
        guard.transform.forward.x,
        0f,
        guard.transform.forward.z
      );

      if (toPlayerFlat.sqrMagnitude < 0.0001f || guardForwardFlat.sqrMagnitude < 0.0001f) {
        continue;
      }

      toPlayerFlat.Normalize();
      guardForwardFlat.Normalize();

      var angleToPlayer = Vector3.Angle(guardForwardFlat, toPlayerFlat);
      var minAngleNeeded = 180f - takedownAngle * 0.5f;
      var behindGuard = angleToPlayer >= minAngleNeeded;

      if (verboseLogging) {
        Debug.Log($"[Takedown] Guard '{guard.name}' | " +
                  $"dist={Vector3.Distance(transform.position, guard.transform.position):F2}m | " +
                  $"horizAngle={angleToPlayer:F1} deg (need >= {minAngleNeeded:F1} deg) | " +
                  $"behindGuard={behindGuard} | guardState={guard.CurrentState}");
      }

      if (behindGuard) {
        guard.PerformTakedown();
        Debug.Log($"[Takedown] SUCCESS on '{guard.name}'!");
        return;
      }
    }

    Debug.Log("[Takedown] Guard(s) found but none were approached from behind. Get behind the guard and try again.");
  }

  private void OnDrawGizmosSelected() {
    if (!showDebugGizmos) {
      return;
    }

    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}
