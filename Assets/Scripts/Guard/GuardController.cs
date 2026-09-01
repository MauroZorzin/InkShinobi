using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

/// <summary>
/// High-level guard brain. It selects behavior and destinations while GuardMotor remains the
/// sole movement authority and GuardSpriteFacing remains the sole sprite presentation authority.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent), typeof(GuardMotor))]
public class GuardController : MonoBehaviour {
  public enum GuardState { Patrol, Noticing, Chasing, Searching, Returning, TakenDown }

  private static readonly HashSet<GuardController> ActiveGuardSet = new();
  public static IReadOnlyCollection<GuardController> ActiveGuards => ActiveGuardSet;

  [Header("Patrol")]
  [Tooltip("Fallback route for guards without GuardSquarePatrol. Guards with an authored route use that component instead.")]
  public Transform[] patrolWaypoints = System.Array.Empty<Transform>();
  [Tooltip("Fallback pause at patrol points when no GuardSquarePatrol is present.")]
  public float waypointWaitTime = 2f;
  [Tooltip("Fallback patrol speed when no GuardSquarePatrol is present.")]
  public float patrolMoveSpeed = 2f;
  [SerializeField, Min(0.01f)] private float patrolStoppingDistance = 0.08f;
  [Tooltip("Delay before retrying the same patrol destination if it cannot currently produce a complete NavMesh path.")]
  [SerializeField, Min(0.1f)] private float patrolPathRetryInterval = 1f;

  [Header("Noticing")]
  [Tooltip("How quickly a stationary noticing guard turns toward the player.")]
  [SerializeField, Min(0f)] private float noticingTurnSpeed = 420f;

  [Header("Chase")]
  [Tooltip("Movement speed used while pursuing a confirmed player.")]
  public float alertMoveSpeed = 4f;
  [SerializeField, Min(0.02f)] private float chaseRepathInterval = 0.12f;
  [Tooltip("Time without direct sight before searching the last known position.")]
  [SerializeField, Min(0f)] private float lostSightGrace = 0.45f;
  [Tooltip("Distance at which an unobstructed chasing guard catches the player.")]
  [SerializeField, Min(0.05f)] private float catchDistance = 0.55f;
  [SerializeField, Min(0f)] private float chaseStoppingDistance = 0.18f;

  [Header("Search")]
  [Tooltip("Maximum time spent reaching the last known player or sound position.")]
  public float investigateDuration = 5f;
  [Tooltip("Duration of the scan after reaching the last known position.")]
  [SerializeField, Min(0f)] private float searchLookDuration = 3f;
  [Tooltip("Total left/right scan angle at the search position.")]
  [Range(10f, 180f)] public float lookAroundAngle = 90f;
  [Tooltip("Seconds per complete left-right search sweep.")]
  [Range(0.25f, 5f)] public float lookAroundDuration = 1.5f;
  [Tooltip("Legacy prefab value retained for serialization; search is now duration-based.")]
  [Range(1, 6)] public int investigateLookCount = 3;
  [Tooltip("Legacy prefab value retained for serialization; noticing follows detection progress.")]
  [Range(1, 4)] public int suspiciousLookCount = 2;
  [SerializeField, Min(0.01f)] private float searchStoppingDistance = 0.12f;

  [Header("Takedown")]
  public AudioClip takedownSound;
  public GameObject takedownReplacementPrefab;
  public float takedownDestroyDelay = 0.5f;

  [Header("Audio")]
  public AudioClip spotSound;
  public AudioClip chaseStartSound;
  public AudioClip loseSightSound;
  public AudioClip[] idleSounds = System.Array.Empty<AudioClip>();
  public float idleSoundMinInterval = 8f;
  public float idleSoundMaxInterval = 20f;
  public AudioMixerGroup mixerGroup;

  [Header("References")]
  public GuardVisionCone visionCone;
  [SerializeField] private GuardMotor motor;
  [SerializeField] private GuardSquarePatrol patrolRoute;
  [SerializeField] private GuardSpriteFacing spriteFacing;

  [Header("Debug")]
  public bool showStateLabel = true;
  [SerializeField] private bool verboseLogging;

  public GuardState CurrentState { get; private set; } = GuardState.Patrol;
  public float DetectionProgress => visionCone != null ? visionCone.DetectionProgress : 0f;
  public Vector3 LastKnownPlayerPosition => lastKnownPosition;

  private int patrolIndex;
  private int arrivedPatrolIndex = -1;
  private float stateElapsed;
  private float waypointWaitRemaining;
  private float lostSightElapsed;
  private float nextRepathTime;
  private float idleSoundTimer;
  private Vector3 lastKnownPosition;
  private Quaternion searchBaseRotation;
  private bool searchHasArrived;
  private bool catchIssued;
  private bool takedownAudioPlayed;
  private bool patrolDestinationPending;
  private float nextPatrolPathRetryTime;
  private AudioSource chaseAudioSource;
  private AudioSource alertOneShotSource;
  private Coroutine alertAudioFadeRoutine;

  /// <summary>Fades only guard alert/chase audio, leaving music and unrelated SFX untouched.</summary>
  public static void FadeOutAllAlertAudio(float duration) {
    foreach (GuardController guard in ActiveGuardSet)
      if (guard != null && guard.isActiveAndEnabled) guard.BeginAlertAudioFade(duration);
  }

  private void Awake() {
    ResolveReferences();
    CreateChaseAudioSource();
    if (takedownSound != null) takedownSound.LoadAudioData();
    idleSoundTimer = Random.Range(idleSoundMinInterval, idleSoundMaxInterval);
  }

  private void OnEnable() => ActiveGuardSet.Add(this);
  private void OnDisable() {
    spriteFacing?.SetAttacking(false);
    StopChaseSound();
    ActiveGuardSet.Remove(this);
  }

  private void OnDestroy() {
    StopChaseSound();
    ActiveGuardSet.Remove(this);
  }

  private void Start() {
    ResolveReferences();
    if (motor == null || !motor.EnsureOnNavMesh()) {
      Debug.LogError($"[Guard] '{name}' cannot start because no usable NavMesh is available.", this);
      enabled = false;
      return;
    }
    patrolIndex = patrolRoute != null && patrolRoute.Count > 0
      ? patrolRoute.InitialPointIndex
      : FindNearestFallbackWaypoint();
    EnterState(GuardState.Patrol, true);
  }

  private void Update() {
    if (CurrentState == GuardState.TakenDown || motor == null) return;
    stateElapsed += Time.deltaTime;
    UpdateKnownPlayerPosition();
    switch (CurrentState) {
      case GuardState.Patrol: UpdatePatrol(); break;
      case GuardState.Noticing: UpdateNoticing(); break;
      case GuardState.Chasing: UpdateChasing(); break;
      case GuardState.Searching: UpdateSearching(); break;
      case GuardState.Returning: UpdateReturning(); break;
    }
  }

  public void InvestigateSound(Vector3 soundPosition) {
    if (CurrentState == GuardState.TakenDown) return;
    if (visionCone != null && visionCone.PlayerDetected && visionCone.PlayerCurrentlyVisible) return;
    lastKnownPosition = soundPosition;
    // A newer sound is authoritative even while this guard is already searching. Re-entering the
    // state resets its timers, abandons the old path/scan, and issues a fresh NavMesh destination.
    EnterState(GuardState.Searching, CurrentState == GuardState.Searching);
  }

  public void ObservePlayerDoorInteraction(PlayerStealthController player, PassagewayDoor door) {
    if (CurrentState == GuardState.TakenDown || player == null || door == null || visionCone == null) return;
    if (!visionCone.TryConfirmDoorInteraction(player, door)) return;

    ApplyConfirmedDoorDetection(player, "Door interaction", true);
  }

  public void ObservePlayerHoldingDoor(PlayerStealthController player, PassagewayDoor door) {
    if (CurrentState == GuardState.TakenDown || player == null || door == null || visionCone == null) return;
    if (!visionCone.ForceConfirmPlayer(player)) return;

    ApplyConfirmedDoorDetection(player, "Player-held door", false);
  }

  private void ApplyConfirmedDoorDetection(PlayerStealthController player, string context, bool repathIfChasing) {
    lastKnownPosition = player.transform.position;
    lostSightElapsed = 0f;
    nextRepathTime = 0f;
    if (CurrentState != GuardState.Chasing) EnterState(GuardState.Chasing);
    else if (repathIfChasing)
      motor?.MoveTo(lastKnownPosition, alertMoveSpeed, chaseStoppingDistance, context);
  }

  public void PerformTakedown() {
    if (CurrentState == GuardState.TakenDown) return;
    spriteFacing?.SetAttacking(false);
    EnterState(GuardState.TakenDown);
    PlayTakedownAudio();
    if (visionCone != null) {
      visionCone.ReleaseDetection();
      visionCone.enabled = false;
    }
    if (motor != null) motor.ShutDown();
    foreach (Collider col in GetComponentsInChildren<Collider>(true)) col.enabled = false;
    GuardKeyCarrier keyCarrier = GetComponent<GuardKeyCarrier>();
    if (keyCarrier != null) {
      keyCarrier.DropKey();
    } else if (takedownReplacementPrefab != null) {
      // Compatibility fallback for older scenes not yet migrated to GuardKeyCarrier.
      Instantiate(takedownReplacementPrefab, transform.position, transform.rotation);
    }
    Destroy(gameObject, Mathf.Max(0f, takedownDestroyDelay));
  }

  public void PlayTakedownAudio() {
    if (takedownAudioPlayed || takedownSound == null) return;
    takedownAudioPlayed = true;
    OneShotAudio.PlayClipAtPoint(takedownSound, transform.position, 1f, mixerGroup);
  }

  private void UpdatePatrol() {
    UpdateIdleSound();
    if (ShouldNoticePlayer()) {
      EnterState(GuardState.Noticing);
      return;
    }
    if (waypointWaitRemaining > 0f) {
      waypointWaitRemaining -= Time.deltaTime;
      Transform arrivedPoint = GetPatrolPoint(arrivedPatrolIndex);
      if (arrivedPoint != null) motor.FaceDirection(arrivedPoint.forward, GetWaypointTurnSpeed());
      else motor.Stop();
      if (waypointWaitRemaining <= 0f) MoveToPatrolPoint(patrolIndex);
      return;
    }
    if (patrolDestinationPending) {
      RetryPatrolDestination();
      return;
    }
    if (motor.HasPathFailure) {
      SchedulePatrolDestinationRetry();
      return;
    }
    if (!motor.HasArrived) return;
    arrivedPatrolIndex = patrolIndex;
    waypointWaitRemaining = GetWaypointPause(patrolIndex);
    patrolIndex = NextPatrolIndex(patrolIndex);
    motor.Stop();
    if (waypointWaitRemaining <= 0f) MoveToPatrolPoint(patrolIndex);
  }

  private void UpdateNoticing() {
    motor.Stop();
    PlayerStealthController visible = visionCone != null ? visionCone.VisiblePlayer : null;
    if (visible != null) motor.FaceDirection(visible.transform.position - transform.position, noticingTurnSpeed);
    if (visionCone != null && visionCone.PlayerDetected) {
      EnterState(GuardState.Chasing);
      return;
    }
    // Even an unconfirmed glimpse gives the guard a last known position worth investigating.
    // DetectionProgress may continue decaying after sight is lost, but it must not send the guard
    // directly back to patrol or repeatedly bounce Searching back into Noticing.
    if (visionCone == null) EnterState(GuardState.Returning);
    else if (!visionCone.PlayerCurrentlyVisible) EnterState(GuardState.Searching);
  }

  private void UpdateChasing() {
    PlayerStealthController visible = visionCone != null ? visionCone.VisiblePlayer : null;
    if (visible != null) {
      lostSightElapsed = 0f;
      lastKnownPosition = visible.transform.position;
      if (Time.time >= nextRepathTime) {
        motor.MoveTo(lastKnownPosition, alertMoveSpeed, chaseStoppingDistance, "Chasing");
        nextRepathTime = Time.time + chaseRepathInterval;
      }
      TryCatchPlayer(visible);
      return;
    }
    // The player may deliberately hold a closed door on one of its authored LinePaths. The guard
    // can no longer see through the closed panels, but it knows why traversal is blocked and keeps
    // pursuing instead of decaying into Searching/Returning until that path becomes available.
    if (motor.IsPursuitBlockedByPlayerHeldDoor) {
      lostSightElapsed = 0f;
      return;
    }
    lostSightElapsed += Time.deltaTime;
    if (lostSightElapsed >= lostSightGrace) EnterState(GuardState.Searching);
  }

  private void UpdateSearching() {
    if (ShouldNoticePlayer()) {
      EnterState(visionCone != null && visionCone.PlayerDetected ? GuardState.Chasing : GuardState.Noticing);
      return;
    }
    if (!searchHasArrived) {
      if (!motor.HasArrived && stateElapsed < investigateDuration) return;
      searchHasArrived = true;
      stateElapsed = 0f;
      searchBaseRotation = transform.rotation;
      motor.Stop(true);
    }
    float phase = stateElapsed / Mathf.Max(0.01f, lookAroundDuration) * Mathf.PI * 2f;
    float yaw = Mathf.Sin(phase) * lookAroundAngle * 0.5f;
    Vector3 direction = (searchBaseRotation * Quaternion.Euler(0f, yaw, 0f)) * Vector3.forward;
    motor.FaceDirection(direction, noticingTurnSpeed);
    if (stateElapsed >= searchLookDuration) EnterState(GuardState.Returning);
  }

  private void UpdateReturning() {
    if (ShouldNoticePlayer()) {
      EnterState(GuardState.Noticing);
      return;
    }
    if (patrolDestinationPending) {
      RetryPatrolDestination();
      return;
    }
    if (motor.HasPathFailure) {
      SchedulePatrolDestinationRetry();
      return;
    }
    if (motor.HasArrived) EnterState(GuardState.Patrol);
  }

  private void EnterState(GuardState next, bool force = false) {
    if (!force && CurrentState == next) return;
    GuardState previous = CurrentState;
    if (previous == GuardState.Chasing && next != GuardState.Chasing) StopChaseSound();
    CurrentState = next;
    stateElapsed = 0f;
    motor?.ReleaseManualFacing();
    switch (next) {
      case GuardState.Patrol:
        waypointWaitRemaining = 0f;
        patrolDestinationPending = false;
        MoveToPatrolPoint(patrolIndex);
        break;
      case GuardState.Noticing:
        motor?.Stop();
        break;
      case GuardState.Chasing:
        lostSightElapsed = 0f;
        nextRepathTime = 0f;
        PlaySpottedSounds();
        break;
      case GuardState.Searching:
        searchHasArrived = false;
        motor?.MoveTo(lastKnownPosition, patrolMoveSpeed * 1.3f, searchStoppingDistance, "Searching");
        if (previous == GuardState.Chasing && loseSightSound != null)
          alertOneShotSource = OneShotAudio.PlayClipAtPoint(
            loseSightSound, transform.position, 1f, mixerGroup);
        break;
      case GuardState.Returning:
        patrolIndex = FindNearestPatrolPoint();
        MoveToPatrolPoint(patrolIndex);
        break;
      case GuardState.TakenDown:
        motor?.Stop(true);
        break;
    }
    if (verboseLogging) Debug.Log($"[Guard] '{name}': {previous} -> {next}.", this);
  }

  private bool ShouldNoticePlayer() => visionCone != null && visionCone.PlayerCurrentlyVisible;

  private void UpdateKnownPlayerPosition() {
    PlayerStealthController visible = visionCone != null ? visionCone.VisiblePlayer : null;
    if (visible != null) lastKnownPosition = visible.transform.position;
  }

  private void TryCatchPlayer(PlayerStealthController player) {
    if (catchIssued || player == null) return;
    Vector3 delta = player.transform.position - transform.position;
    delta.y = 0f;
    if (delta.sqrMagnitude > catchDistance * catchDistance) return;
    if (PassagewayDoor.AnyNonOpenDoorBlocksSegment(transform.position, player.transform.position)) return;
    if (visionCone != null && !visionCone.HasLineOfSightTo(player)) return;
    catchIssued = true;
    motor.Stop(true);
    spriteFacing?.SetAttacking(true);
    PlayerDeathSequence death = player.GetComponent<PlayerDeathSequence>();
    if (death != null) death.Kill(this);
    else SceneTransitionManager.ReloadCurrentScene();
  }

  private bool MoveToPatrolPoint(int index) {
    Transform point = GetPatrolPoint(index);
    if (point == null || motor == null) {
      patrolDestinationPending = true;
      nextPatrolPathRetryTime = Time.time + patrolPathRetryInterval;
      return false;
    }
    float speed = patrolRoute != null ? patrolRoute.Speed : patrolMoveSpeed;
    float stop = patrolRoute != null ? patrolRoute.ArrivalDistance : patrolStoppingDistance;
    if (patrolRoute != null) motor.SetRuntimeTurnSpeed(patrolRoute.TurnSpeed);
    bool accepted = motor.MoveTo(point.position, speed, stop, $"Patrol[{index}] {point.name}");
    patrolDestinationPending = !accepted;
    if (!accepted) nextPatrolPathRetryTime = Time.time + patrolPathRetryInterval;
    if (verboseLogging)
      Debug.Log(
        $"[Guard] '{name}' patrol request index={index}, point='{point.name}', " +
        $"position={point.position}, accepted={accepted}.", this);
    return accepted;
  }

  private void RetryPatrolDestination() {
    if (Time.time < nextPatrolPathRetryTime) return;
    MoveToPatrolPoint(patrolIndex);
  }

  private void SchedulePatrolDestinationRetry() {
    motor.Stop(true);
    patrolDestinationPending = true;
    nextPatrolPathRetryTime = Time.time + patrolPathRetryInterval;
    if (verboseLogging)
      Debug.LogWarning(
        $"[Guard] '{name}' lost its active path to patrol index {patrolIndex}; " +
        $"retrying that same point in {patrolPathRetryInterval:F2}s.", this);
  }

  private Transform GetPatrolPoint(int index) {
    if (patrolRoute != null && patrolRoute.Count > 0) return patrolRoute.GetPoint(index);
    if (patrolWaypoints == null || patrolWaypoints.Length == 0) return null;
    int wrapped = ((index % patrolWaypoints.Length) + patrolWaypoints.Length) % patrolWaypoints.Length;
    return patrolWaypoints[wrapped];
  }

  private int PatrolPointCount => patrolRoute != null && patrolRoute.Count > 0
    ? patrolRoute.Count
    : patrolWaypoints?.Length ?? 0;
  private int NextPatrolIndex(int current) => PatrolPointCount > 0 ? (current + 1) % PatrolPointCount : 0;
  private float GetWaypointPause(int reachedPointIndex) => patrolRoute != null
    ? (patrolRoute.IsCorner(reachedPointIndex) ? patrolRoute.CornerPause : 0f)
    : waypointWaitTime;
  private float GetWaypointTurnSpeed() => patrolRoute != null ? patrolRoute.TurnSpeed : noticingTurnSpeed;

  private int FindNearestPatrolPoint() {
    if (patrolRoute != null && patrolRoute.Count > 0)
      return patrolRoute.FindNearestPointIndex(transform.position);
    return FindNearestFallbackWaypoint();
  }

  private int FindNearestFallbackWaypoint() {
    if (patrolWaypoints == null || patrolWaypoints.Length == 0) return 0;
    int nearest = 0;
    float best = float.PositiveInfinity;
    for (int i = 0; i < patrolWaypoints.Length; i++) {
      if (patrolWaypoints[i] == null) continue;
      float distance = (patrolWaypoints[i].position - transform.position).sqrMagnitude;
      if (distance >= best) continue;
      best = distance;
      nearest = i;
    }
    return nearest;
  }

  private void ResolveReferences() {
    if (motor == null) motor = GetComponent<GuardMotor>();
    if (patrolRoute == null) patrolRoute = GetComponent<GuardSquarePatrol>();
    if (spriteFacing == null) spriteFacing = GetComponent<GuardSpriteFacing>();
    if (visionCone == null) visionCone = GetComponentInChildren<GuardVisionCone>(true);
  }

  private void PlaySpottedSounds() {
    if (spotSound != null)
      alertOneShotSource = OneShotAudio.PlayClipAtPoint(
        spotSound, transform.position, 1f, mixerGroup);
    if (chaseStartSound == null) return;
    CreateChaseAudioSource();
    chaseAudioSource.Stop();
    chaseAudioSource.clip = chaseStartSound;
    chaseAudioSource.outputAudioMixerGroup = mixerGroup;
    chaseAudioSource.Play();
  }

  private void CreateChaseAudioSource() {
    if (chaseAudioSource != null) return;
    chaseAudioSource = gameObject.AddComponent<AudioSource>();
    chaseAudioSource.playOnAwake = false;
    chaseAudioSource.loop = false;
    chaseAudioSource.spatialBlend = 1f;
    chaseAudioSource.dopplerLevel = 0f;
    chaseAudioSource.outputAudioMixerGroup = mixerGroup;
  }

  private void StopChaseSound() {
    if (chaseAudioSource == null) return;
    chaseAudioSource.Stop();
    chaseAudioSource.clip = null;
  }

  private void BeginAlertAudioFade(float duration) {
    if (alertAudioFadeRoutine != null) StopCoroutine(alertAudioFadeRoutine);
    alertAudioFadeRoutine = StartCoroutine(FadeAlertAudioRoutine(Mathf.Max(0f, duration)));
  }

  private IEnumerator FadeAlertAudioRoutine(float duration) {
    AudioSource chase = chaseAudioSource;
    AudioSource alert = alertOneShotSource;
    float chaseVolume = chase != null ? chase.volume : 0f;
    float alertVolume = alert != null ? alert.volume : 0f;
    float elapsed = 0f;

    while (elapsed < duration && (chase != null || alert != null)) {
      elapsed += Time.unscaledDeltaTime;
      float volumeFactor = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.001f, duration));
      if (chase != null) chase.volume = chaseVolume * volumeFactor;
      if (alert != null) alert.volume = alertVolume * volumeFactor;
      yield return null;
    }

    if (chase != null) {
      chase.Stop();
      chase.clip = null;
      chase.volume = chaseVolume;
    }
    if (alert != null) {
      alert.Stop();
      Destroy(alert.gameObject);
    }
    if (alertOneShotSource == alert) alertOneShotSource = null;
    alertAudioFadeRoutine = null;
  }

  private void UpdateIdleSound() {
    if (idleSounds == null || idleSounds.Length == 0) return;
    idleSoundTimer -= Time.deltaTime;
    if (idleSoundTimer > 0f) return;
    AudioClip clip = idleSounds[Random.Range(0, idleSounds.Length)];
    if (clip != null) OneShotAudio.PlayClipAtPoint(clip, transform.position, 1f, mixerGroup);
    idleSoundTimer = Random.Range(idleSoundMinInterval, idleSoundMaxInterval);
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    Gizmos.color = Color.cyan;
    int count = patrolRoute != null && patrolRoute.Count > 0 ? patrolRoute.Count : patrolWaypoints?.Length ?? 0;
    for (int i = 0; i < count; i++) {
      Transform a = GetPatrolPoint(i);
      Transform b = GetPatrolPoint((i + 1) % Mathf.Max(1, count));
      if (a == null) continue;
      Gizmos.DrawSphere(a.position, 0.12f);
      if (b != null && count > 1) Gizmos.DrawLine(a.position, b.position);
    }
  }
#endif
}
