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
    /// <summary>The guard is briefly searching after losing a stronger stimulus.</summary>
    Suspicious,
    /// <summary>The guard is moving toward a heard sound or last known player position.</summary>
    Investigating,
    /// <summary>The guard has detected the player and is pursuing them.</summary>
    Alerted,
    /// <summary>The guard has been disabled by a player takedown.</summary>
    TakenDown
  }

  [Header("Patrol")]
  [Tooltip("World-space waypoints the guard walks between.")]
  public Transform[] patrolWaypoints = System.Array.Empty<Transform>();

  [Tooltip("Seconds the guard waits at each patrol waypoint.")]
  public float waypointWaitTime = 2f;

  [Tooltip("Movement speed used during normal patrol.")]
  public float patrolMoveSpeed = 2f;

  [Header("Alert")]
  [Tooltip("Movement speed used while chasing a detected player.")]
  public float alertMoveSpeed = 4f;

  [Tooltip("Seconds the guard investigates the last known player or sound position.")]
  public float investigateDuration = 5f;

  [Header("Takedown")]
  [Tooltip("Sound played the moment the takedown is triggered.")]
  public AudioClip takedownSound;

  [Tooltip("Prefab spawned at the guard's position on takedown. Leave empty to skip.")]
  public GameObject takedownReplacementPrefab;

  [Tooltip("Seconds to wait after takedown before destroying this guard GameObject.")]
  public float takedownDestroyDelay = 0.5f;

  [Header("References")]
  [Tooltip("Vision cone used to detect the player. Leave empty to auto-find on child GameObjects.")]
  public GuardVisionCone visionCone;

  [Header("Debug")]
  [Tooltip("Draws the current guard state above the guard in the Scene/Game view while selected.")]
  public bool showStateLabel = true;

  /// <summary>The current high-level behavior state for this guard.</summary>
  public GuardState CurrentState { get; private set; } = GuardState.Patrol;

  private NavMeshAgent _agent;
  private int _waypointIndex = 0;
  private float _waitTimer = 0f;
  private float _investigateTimer = 0f;
  private Vector3 _lastKnownPosition;
  private bool _waitingAtWaypoint = false;

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

    if (visionCone != null && visionCone.PlayerDetected) {
      _lastKnownPosition = visionCone.DetectedPlayer.transform.position;
      SetState(GuardState.Alerted);
    }

    switch (CurrentState) {
      case GuardState.Patrol:
        UpdatePatrol();
        break;
      case GuardState.Suspicious:
        UpdateSuspicious();
        break;
      case GuardState.Investigating:
        UpdateInvestigating();
        break;
      case GuardState.Alerted:
        UpdateAlerted();
        break;
    }
  }

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

  /// <summary>
  /// Changes guard state and applies speed, destination, and timer side effects.
  /// </summary>
  /// <param name="newState">The next state to enter.</param>
  private void SetState(GuardState newState) {
    if (CurrentState == newState) {
      return;
    }

    CurrentState = newState;

    switch (newState) {
      case GuardState.Patrol:
        _agent.speed = patrolMoveSpeed;
        GoToWaypoint(_waypointIndex);
        break;
      case GuardState.Suspicious:
        _agent.speed = patrolMoveSpeed * 1.5f;
        _investigateTimer = investigateDuration * 0.5f;
        break;
      case GuardState.Investigating:
        _agent.speed = patrolMoveSpeed * 1.3f;
        _agent.SetDestination(_lastKnownPosition);
        _investigateTimer = investigateDuration;
        break;
      case GuardState.Alerted:
        _agent.speed = alertMoveSpeed;
        _agent.SetDestination(_lastKnownPosition);
        _investigateTimer = investigateDuration;
        break;
      case GuardState.TakenDown:
        StartCoroutine(TakedownSequence());
        break;
    }
  }

  /// <summary>
  /// Disables guard gameplay components, spawns takedown effects, then destroys the guard.
  /// </summary>
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

    foreach (Collider collider in GetComponentsInChildren<Collider>()) {
      collider.enabled = false;
    }

    Debug.Log($"[Guard] '{name}' taken down; destroying in {takedownDestroyDelay}s.");

    yield return new WaitForSeconds(takedownDestroyDelay);
    Destroy(gameObject);
  }

  /// <summary>
  /// Advances waypoint patrol state and waits at reached waypoints.
  /// </summary>
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
  /// Sends the NavMeshAgent to a patrol waypoint.
  /// </summary>
  /// <param name="index">Waypoint index to target.</param>
  private void GoToWaypoint(int index) {
    if (patrolWaypoints == null || patrolWaypoints.Length == 0) {
      return;
    }

    _agent.isStopped = false;
    _agent.SetDestination(patrolWaypoints[index].position);
  }

  /// <summary>
  /// Counts down suspicion before returning to patrol.
  /// </summary>
  private void UpdateSuspicious() {
    _investigateTimer -= Time.deltaTime;
    if (_investigateTimer <= 0f) {
      SetState(GuardState.Patrol);
    }
  }

  /// <summary>
  /// Counts down investigation and transitions to suspicion when finished.
  /// </summary>
  private void UpdateInvestigating() {
    _investigateTimer -= Time.deltaTime;

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
      _investigateTimer -= Time.deltaTime * 2f;
    }

    if (_investigateTimer <= 0f) {
      SetState(GuardState.Suspicious);
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
