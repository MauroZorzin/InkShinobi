using UnityEngine;

/// <summary>
/// Manages the player's authoritative stealth and visibility state.
///
/// Responsibilities
/// ─────────────────
///  - Maintain the authoritative <see cref="CurrentState"/> (<see cref="StealthState"/>).
///  - Accept events from guards (<see cref="OnGuardStartsDetecting"/>) and
///    light zones (<see cref="EnterLight"/>/<see cref="ExitLight"/>).
///
/// </summary>
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
  public bool IsConcealed => ResolveHidingController()?.IsConcealed == true;
  public bool IsInLight => !IsConcealed && (_lightSourceCount > 0 || ResolveExposureProvider()?.IsExposed == true);
  public float LightExposure => IsConcealed ? 0f : (_lightSourceCount > 0 ? 1f : ResolveExposureProvider()?.Exposure ?? 0f);
  public int DetectingGuardCount { get; set; }
  public bool IsUndetectable { get; set; }
  public int SeeingGuardCount { get; private set; }
  public bool IsCurrentlyVisible => !IsConcealed && SeeingGuardCount > 0;

  /// <summary>Unavailable from the first visible frame through the end of confirmed detection.</summary>
  public bool CanWallSwitch => WallSwitchBlockReason == AimEntryBlockReason.None;

  public AimEntryBlockReason WallSwitchBlockReason {
    get {
      if (IsConcealed) return AimEntryBlockReason.Concealed;
      if (IsCurrentlyVisible || DetectingGuardCount > 0) return AimEntryBlockReason.VisibleOrDetected;
      return AimEntryBlockReason.None;
    }
  }

  // -------------------------------------------------------------------------
  // Inspector
  // -------------------------------------------------------------------------

  [Header("Stealth Settings")]
  [Tooltip("Seconds of no detection before the player transitions back to Hidden.")]
  public float timeToHide = 1.0f;

  [Tooltip("Optional component implementing ILightExposureProvider. If empty, a provider on this GameObject is used.")]
  [SerializeField] private MonoBehaviour lightExposureProvider;

  // -------------------------------------------------------------------------
  // Private state
  // -------------------------------------------------------------------------

  private float _hiddenTimer;
  private int _lightSourceCount;
  private ILightExposureProvider _resolvedExposureProvider;
  private PlayerHidingController _hidingController;

  // -------------------------------------------------------------------------
  // Unity lifecycle
  // -------------------------------------------------------------------------

  private void Awake() {
    ResolveExposureProvider();
  }

  private void Update() {
    UpdateHiddenTimer();
    RefreshState();
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
  /// State changes are applied exactly once.
  /// </summary>
  private void RefreshState() {
    StealthState next = ComputeState();
    if (next == CurrentState) return;

    CurrentState = next;
  }

  private StealthState ComputeState() {
    if (IsConcealed) return StealthState.Hidden;
    if (DetectingGuardCount > 0) return StealthState.Detected;
    if (IsInLight) return StealthState.Exposed;
    if (_hiddenTimer >= timeToHide) return StealthState.Hidden;
    return CurrentState; // stay as-is during the hide cooldown
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

  public void RefreshConcealmentState() => RefreshState();

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
    _lightSourceCount++;
    RefreshState();
  }

  public void ExitLight() {
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

  private PlayerHidingController ResolveHidingController() {
    if (_hidingController == null) _hidingController = GetComponent<PlayerHidingController>();
    return _hidingController;
  }
}
