using UnityEngine;

/// <summary>
/// Takedowns are no longer their own click-to-kill action — they happen as a side effect of a
/// wall switch: if the switch's path (player's position when confirmed -> the aimed target point)
/// passes within takedownRange of a guard, that guard is taken down the instant the switch starts
/// moving. Listens to LineAimSwitchController's events rather than owning any input itself.
///
/// Also owns the time-scale feel for switching: slows down while aiming so lining up a shot feels
/// deliberate, then speeds up while the switch itself is moving for a snappy payoff, settling back
/// to normal once aiming/switching ends. Ramped (not snapped) via timeScaleLerpSpeed.
/// </summary>
[RequireComponent(typeof(LineAimSwitchController))]
public class TakedownController : MonoBehaviour, ITakedownSystem {
  private const string TAKEDOWN_ANIMATION_PARAMETER = "Takedown";

  [Header("Settings")]
  public bool enabledAtStart = true;

  [Tooltip("Max distance from the switch's path (start -> aimed target) a guard can be and still get taken down by it.")]
  public float takedownRange = 1.5f;

  public LayerMask guardLayerMask;

  [Header("Time Scale")]
  [Tooltip("Time.timeScale while aiming a switch.")]
  public float aimTimeScale = 0.35f;

  [Tooltip("Time.timeScale while the switch itself is moving.")]
  public float switchTimeScale = 1.6f;

  [Tooltip("How fast Time.timeScale ramps toward its current target, in scale-units per real second.")]
  public float timeScaleLerpSpeed = 6f;

  [Header("Animation")]
  [Tooltip("If enabled, play a takedown animation on the player when a switch eliminates a guard.")]
  public bool playTakedownAnimation = false;

  [Header("Debug")]
  public bool verboseLogging = false;

  // -------------------------------------------------------------------------
  // ITakedownSystem
  // -------------------------------------------------------------------------

  public bool IsEnabled { get; set; }
  public float TakedownRange { get; set; }
  public LayerMask GuardLayerMask { get; set; }

  /// <summary>True for the duration of a switch that is going to (or just did) take down a guard.</summary>
  public bool IsTakingDown { get; private set; }

  // -------------------------------------------------------------------------
  // Unity lifecycle
  // -------------------------------------------------------------------------

  private LineAimSwitchController _aimController;
  private Animator _animator;
  private float _targetTimeScale = 1f;

  private void Awake() {
    _aimController = GetComponent<LineAimSwitchController>();
    _animator = GetComponent<Animator>();

    IsEnabled = enabledAtStart;
    TakedownRange = takedownRange;
    GuardLayerMask = guardLayerMask;
  }

  private void OnEnable() {
    _aimController.AimStarted += OnAimStarted;
    _aimController.AimEnded += OnAimEnded;
    _aimController.SwitchStarted += OnSwitchStarted;
    _aimController.SwitchFinished += OnSwitchFinished;
  }

  private void OnDisable() {
    _aimController.AimStarted -= OnAimStarted;
    _aimController.AimEnded -= OnAimEnded;
    _aimController.SwitchStarted -= OnSwitchStarted;
    _aimController.SwitchFinished -= OnSwitchFinished;

    // Don't leave the whole game paused/sped-up if this component goes away mid-aim/switch.
    Time.timeScale = 1f;
    _targetTimeScale = 1f;
  }

  private void Update() {
    if (!Mathf.Approximately(Time.timeScale, _targetTimeScale)) {
      Time.timeScale = Mathf.MoveTowards(Time.timeScale, _targetTimeScale, timeScaleLerpSpeed * Time.unscaledDeltaTime);
    }
  }

  // -------------------------------------------------------------------------
  // LineAimSwitchController events
  // -------------------------------------------------------------------------

  private void OnAimStarted() {
    if (!IsEnabled) return;
    _targetTimeScale = aimTimeScale;
  }

  private void OnAimEnded() {
    _targetTimeScale = 1f;
  }

  private void OnSwitchStarted(Vector3 fromPosition, Vector3 toPosition) {
    _targetTimeScale = switchTimeScale;

    if (!IsEnabled || guardLayerMask.value == 0) return;

    GuardController hitGuard = FindGuardAlongPath(fromPosition, toPosition);
    if (hitGuard == null) {
      if (verboseLogging) Debug.Log("[Takedown] Switch path crossed no guard.");
      return;
    }

    IsTakingDown = true;
    if (playTakedownAnimation) PlayTakedownAnimation();
    hitGuard.PerformTakedown();

    if (verboseLogging) Debug.Log($"[Takedown] SUCCESS on '{hitGuard.name}' via wall switch.");
  }

  private void OnSwitchFinished() {
    _targetTimeScale = 1f;
    IsTakingDown = false;
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  /// <summary>Closest live guard within takedownRange of the segment from -> to, or null.</summary>
  private GuardController FindGuardAlongPath(Vector3 from, Vector3 to) {
    Collider[] hits = Physics.OverlapCapsule(from, to, Mathf.Max(0.01f, takedownRange), guardLayerMask, QueryTriggerInteraction.Collide);

    GuardController best = null;
    float bestDist = float.MaxValue;
    foreach (Collider hit in hits) {
      if (hit == null) continue;
      GuardController guard = hit.GetComponentInParent<GuardController>();
      if (guard == null || guard.CurrentState == GuardController.GuardState.TakenDown) continue;

      float dist = Vector3.Distance(from, guard.transform.position);
      if (dist < bestDist) { bestDist = dist; best = guard; }
    }
    return best;
  }

  private void PlayTakedownAnimation() {
    if (_animator == null) {
      _animator = GetComponent<Animator>();
      if (_animator == null) {
        Debug.LogWarning("[Takedown] No Animator found on player. Animation will not play.");
        return;
      }
    }

    _animator.SetTrigger(TAKEDOWN_ANIMATION_PARAMETER);
  }

  private void OnDrawGizmosSelected() {
    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}
