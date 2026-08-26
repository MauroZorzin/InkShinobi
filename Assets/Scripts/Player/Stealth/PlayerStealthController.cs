using UnityEngine;

/// <summary>
/// Manages the player's stealth state and composes all stealth subsystems.
///
/// Responsibilities
/// ─────────────────
///  - Maintain the authoritative <see cref="CurrentState"/> (<see cref="StealthState"/>).
///  - React to state transitions by enabling / disabling dependent subsystems
///    (e.g. disabling <see cref="TakedownController"/> while detected).
///  - Accept events from guards (<see cref="OnGuardStartsDetecting"/>) and
///    light zones (<see cref="EnterLight"/>/<see cref="ExitLight"/>).
///  - Validate on Awake that every required stealth component is present.
///
/// </summary>
[RequireComponent(typeof(TakedownController))]
public class PlayerStealthController : MonoBehaviour, IWallSwitchPermission {
  // -------------------------------------------------------------------------
  // Stealth state
  // -------------------------------------------------------------------------

  public enum StealthState {
    /// <summary>No guard is detecting the player; takedown is available.</summary>
    Hidden,

    /// <summary>Player is in light or briefly visible but no guard has locked on yet.</summary>
    Exposed,

    /// <summary>At least one guard is actively detecting the player; takedown is locked.</summary>
    Detected
  }

  /// <summary>Current stealth state. Drives which subsystems are active.</summary>
  public StealthState CurrentState { get; private set; } = StealthState.Hidden;

  // Convenient shorthands kept for backwards-compatibility with other systems.
  public bool IsHidden => CurrentState == StealthState.Hidden;
  public bool IsInLight => _lightSourceCount > 0 || ResolveExposureProvider()?.IsExposed == true;
  public float LightExposure => _lightSourceCount > 0 ? 1f : ResolveExposureProvider()?.Exposure ?? 0f;
  public int DetectingGuardCount { get; set; }
  public int SeeingGuardCount { get; private set; }
  public bool IsCurrentlyVisible => SeeingGuardCount > 0;

  /// <summary>Unavailable from the first visible frame through the end of confirmed detection.</summary>
  public bool CanWallSwitch => !IsCurrentlyVisible && DetectingGuardCount <= 0;

  // -------------------------------------------------------------------------
  // Inspector
  // -------------------------------------------------------------------------

  [Header("Stealth Settings")]
  [Tooltip("Seconds of no detection before the player transitions back to Hidden.")]
  public float timeToHide = 1.0f;

  [Header("Subsystem References (auto-fetched if left blank)")]
  public TakedownController takedownController;

  [Tooltip("Optional component implementing ILightExposureProvider. If empty, a provider on this GameObject is used.")]
  [SerializeField] private MonoBehaviour lightExposureProvider;

  [Header("Debug")]
  [Tooltip("Draws the current stealth state above the player in the Game view.")]
  public bool debug = true;

  // -------------------------------------------------------------------------
  // Private state
  // -------------------------------------------------------------------------

  private float _hiddenTimer;
  private int _lightSourceCount;
  private ILightExposureProvider _resolvedExposureProvider;

  // -------------------------------------------------------------------------
  // Unity lifecycle
  // -------------------------------------------------------------------------

  private void Awake() {
    ValidateSubsystems();
    ResolveExposureProvider();
  }

  private void Update() {
    UpdateHiddenTimer();
    RefreshState();
  }

  // -------------------------------------------------------------------------
  // Subsystem validation
  // -------------------------------------------------------------------------

  /// <summary>
  /// Fetches required stealth components from this GameObject and logs clear
  /// errors for anything missing, so setup mistakes surface immediately.
  /// </summary>
  private void ValidateSubsystems() {

    if (!TryFetch(ref takedownController, nameof(TakedownController), mandatory: true))
      return;

  }


  private bool TryFetch<T>(ref T component, string label, bool mandatory) where T : Component {
    if (component != null) return true;     // already assigned in inspector

    component = GetComponent<T>();

    if (component != null) return true;

    string severity = mandatory ? "ERROR" : "WARNING";
    string message = $"[{nameof(PlayerStealthController)}] [{severity}] " +
                      $"Required stealth subsystem '{label}' not found on '{name}'. " +
                      (mandatory ? "Add the component to this GameObject."
                                 : "Some stealth features will be unavailable.");

    if (mandatory) { Debug.LogError(message, this); enabled = false; } else { Debug.LogWarning(message, this); }

    return false;
  }

  // -------------------------------------------------------------------------
  // State machine
  // -------------------------------------------------------------------------

  private void UpdateHiddenTimer() {
    if (DetectingGuardCount > 0) {
      _hiddenTimer = 0f;
    } else {
      _hiddenTimer += Time.deltaTime;
    }
  }

  /// <summary>
  /// Re-evaluates and applies the current state every frame.
  /// Transitions trigger <see cref="OnStateChanged"/> exactly once.
  /// </summary>
  private void RefreshState() {
    StealthState next = ComputeState();
    if (debug) Debug.Log($"[PlayerStealthController] '{name}': RefreshState() => {next} (HiddenTimer={_hiddenTimer:F2}, DetectingGuardCount={DetectingGuardCount}, IsInLight={IsInLight}), LightSourceCount={_lightSourceCount}", this);
    if (next == CurrentState) return;

    StealthState previous = CurrentState;
    CurrentState = next;
    OnStateChanged(previous, next);
  }

  private StealthState ComputeState() {
    if (DetectingGuardCount > 0) return StealthState.Detected;
    if (IsInLight) return StealthState.Exposed;
    if (_hiddenTimer >= timeToHide) return StealthState.Hidden;
    return CurrentState; // stay as-is during the hide cooldown
  }

  /// <summary>
  /// Reacts to a state transition by updating dependent subsystems.
  /// </summary>
  private void OnStateChanged(StealthState from, StealthState to) {
    // Takedown is only allowed while the player is undetected.
    bool takedownAllowed = to != StealthState.Detected;
    if (takedownController != null)
      takedownController.IsEnabled = takedownAllowed;

    // Hook for future state-driven behaviour (sound, UI, animation triggers …)
  }

  // -------------------------------------------------------------------------
  // Guard detection events (called by GuardController)
  // -------------------------------------------------------------------------

  public void OnGuardStartsDetecting() {
    DetectingGuardCount++;
    _hiddenTimer = 0f;
    RefreshState();
  }

  public void OnGuardStopsDetecting() {
    DetectingGuardCount = Mathf.Max(0, DetectingGuardCount - 1);
    RefreshState();
  }

  /// <summary>Registers immediate line of sight, before the guard's confirmation timer completes.</summary>
  public void OnGuardStartsSeeing() {
    SeeingGuardCount++;
  }

  /// <summary>Releases one guard's immediate line-of-sight contribution.</summary>
  public void OnGuardStopsSeeing() {
    SeeingGuardCount = Mathf.Max(0, SeeingGuardCount - 1);
  }

  // -------------------------------------------------------------------------
  // Light zone events (called by LightZone or LightZoneTriggerAdapter, one per active light)
  // -------------------------------------------------------------------------

  public void EnterLight() {
    Debug.Log($"[PlayerStealthController] '{name}': EnterLight");
    _lightSourceCount++;
    RefreshState();
  }

  public void ExitLight() {
    Debug.Log($"[PlayerStealthController] '{name}': ExitLight");
    _lightSourceCount = Mathf.Max(0, _lightSourceCount - 1);
    RefreshState();
  }

  private ILightExposureProvider ResolveExposureProvider() {
    if (_resolvedExposureProvider != null) return _resolvedExposureProvider;

    if (lightExposureProvider is ILightExposureProvider assignedProvider) {
      _resolvedExposureProvider = assignedProvider;
      return _resolvedExposureProvider;
    }

    MonoBehaviour[] localBehaviours = GetComponents<MonoBehaviour>();
    for (int i = 0; i < localBehaviours.Length; i++) {
      if (localBehaviours[i] is not ILightExposureProvider provider) continue;
      lightExposureProvider = localBehaviours[i];
      _resolvedExposureProvider = provider;
      break;
    }

    return _resolvedExposureProvider;
  }
}

