using UnityEngine;

/// <summary>Gestisce lo stato autoritativo di furtività e visibilità del giocatore.</summary>
public class PlayerStealthController : MonoBehaviour, IWallSwitchPermission {
  public enum StealthState {
    Hidden,
    Exposed,
    Detected
  }

  public StealthState CurrentState { get; private set; } = StealthState.Hidden;

  public bool IsHidden => CurrentState == StealthState.Hidden;
  public bool IsConcealed => ResolveHidingController()?.IsConcealed == true;
  public bool IsInLight => !IsConcealed && (_lightSourceCount > 0 || ResolveExposureProvider()?.IsExposed == true);
  public float LightExposure => IsConcealed ? 0f : (_lightSourceCount > 0 ? 1f : ResolveExposureProvider()?.Exposure ?? 0f);
  public int DetectingGuardCount { get; set; }
  public bool IsUndetectable { get; set; }
  public int SeeingGuardCount { get; private set; }
  public bool IsCurrentlyVisible => !IsConcealed && SeeingGuardCount > 0;

  public bool CanWallSwitch => WallSwitchBlockReason == AimEntryBlockReason.None;

  public AimEntryBlockReason WallSwitchBlockReason {
    get {
      if (IsConcealed) return AimEntryBlockReason.Concealed;
      if (IsCurrentlyVisible || DetectingGuardCount > 0) return AimEntryBlockReason.VisibleOrDetected;
      return AimEntryBlockReason.None;
    }
  }

  [Header("Stealth Settings")]
  [Tooltip("Seconds of no detection before the player transitions back to Hidden.")]
  public float timeToHide = 1.0f;

  [Tooltip("Optional component implementing ILightExposureProvider. If empty, a provider on this GameObject is used.")]
  [SerializeField] private MonoBehaviour lightExposureProvider;

  private float _hiddenTimer;
  private int _lightSourceCount;
  private ILightExposureProvider _resolvedExposureProvider;
  private PlayerHidingController _hidingController;

  private void Awake() {
    ResolveExposureProvider();
  }

  private void Update() {
    UpdateHiddenTimer();
    RefreshState();
  }

  private void UpdateHiddenTimer() {
    if (DetectingGuardCount > 0) {
      _hiddenTimer = 0f;
    } else {
      _hiddenTimer += Time.deltaTime;
    }
  }

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

  public void OnGuardStartsSeeing() {
    SeeingGuardCount++;
  }

  public void OnGuardStopsSeeing() {
    SeeingGuardCount = Mathf.Max(0, SeeingGuardCount - 1);
  }

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
