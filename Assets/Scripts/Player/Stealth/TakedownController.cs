using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Takedowns are no longer their own click-to-kill action — they happen as a side effect of a
/// wall switch: if the switch's path (player's position when confirmed -> the aimed target point)
/// passes within takedownRange of a guard, that guard is taken down the instant the switch starts
/// moving. Listens to LineAimSwitchController's events rather than owning any input itself.
///
/// Also owns the time-scale/motion-blur feel for switching. Aiming itself stays real-time — only
/// the switch sells speed: Time.timeScale ramps up to switchTimeScale and Motion Blur ramps up to
/// switchBlurIntensity the instant a switch starts moving. If that switch also lands a takedown,
/// time is slammed down to near-zero (slamTimeScale) for a hit-stop beat — during which the blur
/// is deliberately left alone rather than faded out, since the switch is still "in flight" and
/// should still read as fast, just paused on the hit — while the takedown animation/particles
/// play, then released back to switchTimeScale so the in-flight switch (whose own coroutine also
/// advances on Time.deltaTime, so it naturally freezes during the slam) can finish moving.
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
  [Tooltip("Time.timeScale while the switch itself is moving (aiming stays at normal speed).")]
  public float switchTimeScale = 1.6f;

  [Tooltip("Time.timeScale held during the takedown hit itself — near zero for a hit-stop \"slam\" beat.")]
  public float slamTimeScale = 0.05f;

  [Tooltip("Real-world seconds (NOT affected by timeScale) the slam is held before the switch is allowed to resume.")]
  public float slamHoldDuration = 0.4f;

  [Tooltip("How fast Time.timeScale ramps toward its current target, in scale-units per real second.")]
  public float timeScaleLerpSpeed = 6f;

  [Header("Motion Blur")]
  [Tooltip("Global Volume providing the URP Motion Blur override used to sell switch speed.")]
  public Volume postProcessVolume;

  [Tooltip("Motion Blur intensity while switching (and during the takedown slam). 0 = off.")]
  public float switchBlurIntensity = 1f;

  [Tooltip("How fast Motion Blur intensity ramps toward its current target, in intensity-units per real second.")]
  public float blurLerpSpeed = 6f;

  [Header("Animation")]
  [Tooltip("If enabled, play a takedown animation on the player when a switch eliminates a guard.")]
  public bool playTakedownAnimation = false;

  [Header("Particles")]
  [Tooltip("If enabled, spawn a particle effect at the guard's position the instant a takedown lands.")]
  public bool spawnTakedownParticles = false;

  [Tooltip("Particle system instantiated at the guard's position when spawnTakedownParticles is enabled. Destroyed once it finishes playing.")]
  public ParticleSystem takedownParticlesPrefab;

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
  private MotionBlur _motionBlur;
  private float _targetTimeScale = 1f;
  private float _targetBlurIntensity = 0f;

  private void Awake() {
    _aimController = GetComponent<LineAimSwitchController>();
    _animator = GetComponent<Animator>();

    IsEnabled = enabledAtStart;
    TakedownRange = takedownRange;
    GuardLayerMask = guardLayerMask;

    if (postProcessVolume != null && !postProcessVolume.profile.TryGet(out _motionBlur)) {
      Debug.LogWarning("[Takedown] postProcessVolume has no Motion Blur override; blur will not play.", this);
    }
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

    // Don't leave the whole game paused/sped-up or permanently blurred if this component (or a
    // takedown's slam coroutine) is still mid-flight when it goes away.
    StopAllCoroutines();
    Time.timeScale = 1f;
    _targetTimeScale = 1f;
    _targetBlurIntensity = 0f;
    if (_motionBlur != null) _motionBlur.intensity.value = 0f;
    IsTakingDown = false;
  }

  private void Update() {
    if (!Mathf.Approximately(Time.timeScale, _targetTimeScale)) {
      Time.timeScale = Mathf.MoveTowards(Time.timeScale, _targetTimeScale, timeScaleLerpSpeed * Time.unscaledDeltaTime);
    }

    if (_motionBlur != null && !Mathf.Approximately(_motionBlur.intensity.value, _targetBlurIntensity)) {
      _motionBlur.intensity.value = Mathf.MoveTowards(_motionBlur.intensity.value, _targetBlurIntensity, blurLerpSpeed * Time.unscaledDeltaTime);
    }
  }

  // -------------------------------------------------------------------------
  // LineAimSwitchController events
  // -------------------------------------------------------------------------

  // Aiming stays at normal time scale — only the switch itself (below) ramps speed/blur.
  private void OnAimStarted() { }

  private void OnAimEnded() { }

  private void OnSwitchStarted(Vector3 fromPosition, Vector3 toPosition) {
    _targetTimeScale = switchTimeScale;
    _targetBlurIntensity = switchBlurIntensity;

    if (!IsEnabled || guardLayerMask.value == 0) return;

    GuardController hitGuard = FindGuardAlongPath(fromPosition, toPosition);
    if (hitGuard == null) {
      if (verboseLogging) Debug.Log("[Takedown] Switch path crossed no guard.");
      return;
    }

    IsTakingDown = true;
    StartCoroutine(TakedownSlamRoutine(hitGuard));
  }

  private IEnumerator TakedownSlamRoutine(GuardController hitGuard) {
    // Slam: hold time near-frozen for a hit-stop beat. Blur is deliberately left at
    // switchBlurIntensity (not fadedto 0) — the switch is still in flight, just paused on the hit.
    _targetTimeScale = slamTimeScale;

    // The switch may have hidden the player's sprite for the flight (LineSwitcher.
    // hidePlayerDuringSwitch) — if we're about to play the takedown animation, show it again just
    // for that beat (slamHoldDuration doubles as "how long the animation plays" here) so the hit
    // actually reads on screen, then hide it again afterward until the switch itself reveals the
    // player on arrival. SetSpriteVisible is a no-op when hidePlayerDuringSwitch is off, so this
    // is safe to call unconditionally.
    LineSwitcher lineSwitcher = _aimController.lineSwitcher;
    if (playTakedownAnimation) {
      if (lineSwitcher != null) lineSwitcher.SetSpriteVisible(true);
      PlayTakedownAnimation();
    }

    if (spawnTakedownParticles) SpawnTakedownParticles(hitGuard);
    hitGuard.PerformTakedown();

    if (verboseLogging) Debug.Log($"[Takedown] SUCCESS on '{hitGuard.name}' via wall switch.");

    yield return new WaitForSecondsRealtime(slamHoldDuration);

    if (playTakedownAnimation && lineSwitcher != null) lineSwitcher.SetSpriteVisible(false);

    // Release the slam: back to switch speed/blur so the still-in-flight switch (its own
    // coroutine also advances on Time.deltaTime, so it was effectively paused during the slam
    // above) can finish moving. OnSwitchFinished handles the final cooldown back to normal.
    _targetTimeScale = switchTimeScale;
  }

  private void OnSwitchFinished() {
    _targetTimeScale = 1f;
    _targetBlurIntensity = 0f;
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

  private void SpawnTakedownParticles(GuardController hitGuard) {
    if (takedownParticlesPrefab == null) {
      Debug.LogWarning("[Takedown] spawnTakedownParticles is enabled but takedownParticlesPrefab is not assigned.", this);
      return;
    }

    ParticleSystem instance = Instantiate(takedownParticlesPrefab, hitGuard.transform.position, Quaternion.identity);
    ParticleSystem.MainModule main = instance.main;
    // constantMax only holds a real value in "Two Constants" mode; constant only in "Constant"
    // mode — whichever one isn't the active mode reads back as 0, so taking the max of both
    // covers either without needing to branch on main.startLifetime.mode.
    float lifetime = Mathf.Max(main.startLifetime.constant, main.startLifetime.constantMax);
    Destroy(instance.gameObject, main.duration + lifetime);
  }

  private void OnDrawGizmosSelected() {
    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}
