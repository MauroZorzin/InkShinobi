using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Controls guard state transitions for patrolling, investigating, alerting, and takedowns.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class GuardController : MonoBehaviour {
  public enum GuardState {
    /// <summary>The guard is following its waypoint route.</summary>
    Patrol,
    /// <summary>The guard is briefly looking around after losing a weaker stimulus.</summary>
    Suspicious,
    /// <summary>The guard is moving toward a heard sound or last known player position.</summary>
    Investigating,
    /// <summary>The guard has detected the player and is pursuing them.</summary>
    Alerted,
    /// <summary>The guard has been disabled by a player takedown.</summary>
    TakenDown
  }

  // ── Patrol ────────────────────────────────────────────────────────────────

  [Header("Patrol")]
  [Tooltip("World-space waypoints the guard walks between.")]
  public Transform[] patrolWaypoints = System.Array.Empty<Transform>();

  [Tooltip("Seconds the guard waits at each patrol waypoint.")]
  public float waypointWaitTime = 2f;

  [Tooltip("Movement speed used during normal patrol.")]
  public float patrolMoveSpeed = 2f;

  // ── Alert ─────────────────────────────────────────────────────────────────

  [Header("Alert")]
  [Tooltip("Movement speed used while chasing a detected player.")]
  public float alertMoveSpeed = 4f;

  [Tooltip("Seconds the guard investigates the last known player or sound position.")]
  public float investigateDuration = 5f;

  // ── Look-Around ───────────────────────────────────────────────────────────

  [Header("Look-Around")]
  [Tooltip("Total angle swept to each side during a look-around scan (degrees).")]
  [Range(30f, 180f)] public float lookAroundAngle = 90f;

  [Tooltip("Seconds to complete one left-to-right sweep.")]
  [Range(0.5f, 5f)] public float lookAroundDuration = 1.5f;

  [Tooltip("How many left/right sweeps during investigation look-around.")]
  [Range(1, 6)] public int investigateLookCount = 3;

  [Tooltip("How many left/right sweeps during suspicious look-around (shorter).")]
  [Range(1, 4)] public int suspiciousLookCount = 2;

  // ── Takedown ──────────────────────────────────────────────────────────────

  [Header("Takedown")]
  [Tooltip("Sound played the moment the takedown is triggered.")]
  public AudioClip takedownSound;

  [Tooltip("Prefab spawned at the guard's position on takedown. Leave empty to skip.")]
  public GameObject takedownReplacementPrefab;

  [Tooltip("Seconds to wait after takedown before destroying this guard GameObject.")]
  public float takedownDestroyDelay = 0.5f;

  // ── References ────────────────────────────────────────────────────────────

  [Header("References")]
  [Tooltip("Vision cone used to detect the player. Leave empty to auto-find on child GameObjects.")]
  public GuardVisionCone visionCone;

  // ── Debug ─────────────────────────────────────────────────────────────────

  [Header("Debug")]
  [Tooltip("Draws the current guard state above the guard in the Game view.")]
  public bool showStateLabel = true;

  // ── Public state ──────────────────────────────────────────────────────────

  /// <summary>The current high-level behavior state for this guard.</summary>
  public GuardState CurrentState { get; private set; } = GuardState.Patrol;

  // ── Private fields ────────────────────────────────────────────────────────

  private NavMeshAgent _agent;
  private int _waypointIndex = 0;
  private float _waitTimer = 0f;
  private float _investigateTimer = 0f;
  private Vector3 _lastKnownPosition;
  private bool _waitingAtWaypoint = false;

  /// <summary>Set to true while a look-around coroutine is running so Update doesn't fight it.</summary>
  private bool _lookingAround = false;

  // ─────────────────────────────────────────────────────────────────────────
  // Unity lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  private void Awake() {
    _agent = GetComponent<NavMeshAgent>();

    if (visionCone == null) {
      visionCone = GetComponentInChildren<GuardVisionCone>();
    }

    if (visionCone == null) {
      Debug.LogWarning($"[Guard] {name}: No GuardVisionCone found.", this);
    }
  }

  private void Start() {
    if (patrolWaypoints != null && patrolWaypoints.Length > 0) {
      GoToWaypoint(_waypointIndex);
    }
  }

  private void Update() {
    if (CurrentState == GuardState.TakenDown) {
      return;
    }

    // Vision cone escalation — highest priority.
    if (visionCone != null && visionCone.PlayerDetected) {
      _lastKnownPosition = visionCone.DetectedPlayer.transform.position;
      SetState(GuardState.Alerted);
    }

    switch (CurrentState) {
      case GuardState.Patrol: UpdatePatrol(); break;
      case GuardState.Suspicious: UpdateSuspicious(); break;
      case GuardState.Investigating: UpdateInvestigating(); break;
      case GuardState.Alerted: UpdateAlerted(); break;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Public API
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Sends the guard to investigate a sound unless it is already alerted or taken down.
  /// </summary>
  /// <param name="soundPosition">World position of the sound source.</param>
  public void InvestigateSound(Vector3 soundPosition) {
    if (CurrentState == GuardState.TakenDown || CurrentState == GuardState.Alerted) {
      return;
    }

    _lastKnownPosition = soundPosition;
    SetState(GuardState.Investigating);
    Debug.Log($"[Guard] '{name}' heard a sound at {soundPosition:F1}; investigating.");
  }

  /// <summary>
  /// Transitions the guard into the taken-down state.
  /// </summary>
  public void PerformTakedown() {
    SetState(GuardState.TakenDown);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // State machine
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Changes guard state and applies speed, destination, and timer side effects.
  /// </summary>
  private void SetState(GuardState newState) {
    if (CurrentState == newState) {
      return;
    }

    // Cancel any running look-around when escalating.
    if (_lookingAround && (newState == GuardState.Alerted || newState == GuardState.TakenDown)) {
      StopAllCoroutines();
      _lookingAround = false;
    }

    CurrentState = newState;

    switch (newState) {
      case GuardState.Patrol:
        _agent.speed = patrolMoveSpeed;
        _agent.isStopped = false;
        GoToWaypoint(_waypointIndex);
        break;

      case GuardState.Suspicious:
        _agent.speed = patrolMoveSpeed;
        _agent.isStopped = true;
        StartCoroutine(LookAroundThenTransition(suspiciousLookCount, GuardState.Patrol));
        break;

      case GuardState.Investigating:
        _agent.speed = patrolMoveSpeed * 1.3f;
        _agent.isStopped = false;
        _agent.SetDestination(_lastKnownPosition);
        _investigateTimer = investigateDuration;
        _lookingAround = false;
        break;

      case GuardState.Alerted:
        _agent.speed = alertMoveSpeed;
        _agent.isStopped = false;
        _agent.SetDestination(_lastKnownPosition);
        _investigateTimer = investigateDuration;
        break;

      case GuardState.TakenDown:
        StartCoroutine(TakedownSequence());
        break;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Per-state update methods
  // ─────────────────────────────────────────────────────────────────────────

  private void UpdatePatrol() {
    if (patrolWaypoints == null || patrolWaypoints.Length == 0) {
      return;
    }

    if (_waitingAtWaypoint) {
      _waitTimer -= Time.deltaTime;
      if (_waitTimer <= 0f) {
        _waitingAtWaypoint = false;
        _waypointIndex = (_waypointIndex + 1) % patrolWaypoints.Length;
        GoToWaypoint(_waypointIndex);
      }

      return;
    }

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
      _waitingAtWaypoint = true;
      _waitTimer = waypointWaitTime;
    }
  }

  /// <summary>
  /// Suspicious is fully driven by the LookAround coroutine; nothing extra needed here.
  /// </summary>
  private void UpdateSuspicious() {
    // Handled by LookAroundThenTransition coroutine.
  }

  /// <summary>
  /// Moves toward the last known position; when close enough (or time runs out)
  /// starts a look-around before falling back to Suspicious.
  /// </summary>
  private void UpdateInvestigating() {
    if (_lookingAround) {
      return; // coroutine owns rotation, don't touch the timer.
    }

    bool arrivedAtDestination = !_agent.pathPending &&
                                _agent.remainingDistance <= _agent.stoppingDistance + 0.05f;

    if (arrivedAtDestination) {
      // Reached the point — stop and look around.
      _agent.isStopped = true;
      StartCoroutine(LookAroundThenTransition(investigateLookCount, GuardState.Suspicious));
      return;
    }

    _investigateTimer -= Time.deltaTime;
    if (_investigateTimer <= 0f) {
      // Timed out before arriving — look around in place then become suspicious.
      _agent.isStopped = true;
      StartCoroutine(LookAroundThenTransition(investigateLookCount, GuardState.Suspicious));
    }
  }

  /// <summary>
  /// Chases the detected player or times out into suspicion after losing sight.
  /// </summary>
  private void UpdateAlerted() {
    if (visionCone != null && visionCone.PlayerDetected) {
      _lastKnownPosition = visionCone.DetectedPlayer.transform.position;
      _agent.SetDestination(_lastKnownPosition);
      _investigateTimer = investigateDuration;
      return;
    }

    _investigateTimer -= Time.deltaTime;

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
      _investigateTimer -= Time.deltaTime * 2f;
    }

    if (_investigateTimer <= 0f) {
      SetState(GuardState.Suspicious);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Look-around coroutine
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Rotates the guard left then right <paramref name="sweepCount"/> times,
  /// then transitions to <paramref name="nextState"/>.
  /// </summary>
  private IEnumerator LookAroundThenTransition(int sweepCount, GuardState nextState) {
    _lookingAround = true;
    _agent.isStopped = true;
    _agent.updateRotation = false; // We'll drive rotation manually.

    Quaternion baseRotation = transform.rotation;
    float halfAngle = lookAroundAngle * 0.5f;

    for (var sweep = 0; sweep < sweepCount; sweep++) {
      // Sweep left.
      yield return RotateTo(baseRotation * Quaternion.Euler(0f, -halfAngle, 0f), lookAroundDuration * 0.5f);
      // Sweep right.
      yield return RotateTo(baseRotation * Quaternion.Euler(0f, halfAngle, 0f), lookAroundDuration);
      // Return to center.
      yield return RotateTo(baseRotation, lookAroundDuration * 0.5f);
    }

    _agent.updateRotation = true;
    _lookingAround = false;
    SetState(nextState);
  }

  /// <summary>
  /// Smoothly rotates to <paramref name="target"/> over <paramref name="duration"/> seconds.
  /// </summary>
  private IEnumerator RotateTo(Quaternion target, float duration) {
    Quaternion start = transform.rotation;
    float elapsed = 0f;

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      transform.rotation = Quaternion.Slerp(start, target, elapsed / duration);
      yield return null;
    }

    transform.rotation = target;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Patrol helpers
  // ─────────────────────────────────────────────────────────────────────────

  private void GoToWaypoint(int index) {
    if (patrolWaypoints == null || patrolWaypoints.Length == 0) {
      return;
    }

    _agent.isStopped = false;
    _agent.SetDestination(patrolWaypoints[index].position);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Takedown sequence
  // ─────────────────────────────────────────────────────────────────────────

  private IEnumerator TakedownSequence() {
    if (_agent != null && _agent.isOnNavMesh) {
      _agent.isStopped = true;
      _agent.enabled = false;
    }

    if (visionCone != null) {
      visionCone.enabled = false;
    }

    if (takedownSound != null) {
      AudioSource.PlayClipAtPoint(takedownSound, transform.position);
    }

    if (takedownReplacementPrefab != null) {
      Instantiate(takedownReplacementPrefab, transform.position, transform.rotation);
    }

    foreach (Collider col in GetComponentsInChildren<Collider>()) {
      col.enabled = false;
    }

    Debug.Log($"[Guard] '{name}' taken down; destroying in {takedownDestroyDelay}s.");

    yield return new WaitForSeconds(takedownDestroyDelay);
    Destroy(gameObject);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Editor helpers
  // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    if (patrolWaypoints == null) {
      return;
    }

    Gizmos.color = Color.cyan;
    for (var i = 0; i < patrolWaypoints.Length; i++) {
      if (patrolWaypoints[i] == null) {
        continue;
      }

      Gizmos.DrawSphere(patrolWaypoints[i].position, 0.15f);
      var next = (i + 1) % patrolWaypoints.Length;
      if (patrolWaypoints[next] != null) {
        Gizmos.DrawLine(patrolWaypoints[i].position, patrolWaypoints[next].position);
      }
    }
  }

  private void OnGUI() {
    if (!showStateLabel || Camera.main == null) {
      return;
    }

    Vector3 screenPosition = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f);
    if (screenPosition.z < 0) {
      return;
    }

    var label = $"[{name}] {CurrentState}";
    Color color = CurrentState switch {
      GuardState.Patrol => Color.green,
      GuardState.Suspicious => Color.yellow,
      GuardState.Investigating => new Color(1f, 0.6f, 0f),
      GuardState.Alerted => Color.red,
      GuardState.TakenDown => Color.gray,
      _ => Color.white
    };

    GUI.color = color;
    GUI.Label(new Rect(screenPosition.x - 60, Screen.height - screenPosition.y - 20, 160, 25), label);
    GUI.color = Color.white;
  }
#endif
}
