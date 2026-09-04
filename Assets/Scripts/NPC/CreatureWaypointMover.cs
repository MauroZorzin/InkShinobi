using UnityEngine;
using UnityEngine.AI;

/// <summary>Muove un NavMeshAgent tra i waypoint in ordine, in loop o una tantum. Resta inattivo finché non viene chiamato StartMoving().</summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CreatureWaypointMover : MonoBehaviour {
  [Header("Route")]
  [Tooltip("World-space points the creature walks between, in order.")]
  public Transform[] waypoints = System.Array.Empty<Transform>();

  [Tooltip("Distance to a waypoint at which it counts as reached.")]
  public float arrivalThreshold = 0.3f;

  [Tooltip("Seconds to wait at each waypoint before moving to the next.")]
  public float waitTimeAtWaypoint = 0f;

  [Tooltip("If true, loops back to waypoint 0 after the last one. If false, stops once the last waypoint is reached.")]
  public bool loop = true;

  [Header("Speed")]
  public float walkSpeed = 1.5f;
  public float runSpeed = 4f;
  public bool run = false;

  [Header("Animation (optional)")]
  [Tooltip("If assigned, speedParameter/stateParameter are driven each frame from the agent's motion. Leave empty to skip driving any Animator.")]
  public Animator animator;

  [Tooltip("Animator float parameter set to the agent's speed, 0..1 as a fraction of runSpeed.")]
  public string speedParameter = "Vert";

  [Tooltip("Animator float parameter set to 1 while run is true, 0 otherwise.")]
  public string stateParameter = "State";

  [Header("Start")]
  [Tooltip("If true, starts walking the route immediately instead of waiting for StartMoving() (e.g. from a CreatureMoveTrigger).")]
  public bool startMovingOnAwake = false;

  [Header("Debug")]
  public bool drawGizmos = true;

  private NavMeshAgent _agent;
  private int _waypointIndex;
  private bool _isActive;
  private bool _isWaiting;
  private float _waitTimer;

  public bool IsActive => _isActive;
  public int CurrentWaypointIndex => _waypointIndex;

  private void Awake() {
    _agent = GetComponent<NavMeshAgent>();
    _agent.stoppingDistance = Mathf.Max(_agent.stoppingDistance, arrivalThreshold);
    ApplySpeed();

    if (startMovingOnAwake) StartMoving();
  }

  private void Update() {
    if (!_isActive) return;

    if (!_agent.isOnNavMesh) {
      Debug.LogWarning("[CreatureWaypointMover] Agent is not on a NavMesh; stopping.", this);
      Stop();
      return;
    }

    if (waypoints == null || waypoints.Length == 0) {
      Stop();
      return;
    }

    Transform wp = waypoints[_waypointIndex];
    if (wp == null) {
      AdvanceWaypoint();
      return;
    }

    ApplySpeed();

    if (_isWaiting) {
      _waitTimer -= Time.deltaTime;
      if (_waitTimer <= 0f) {
        _isWaiting = false;
        AdvanceWaypoint();
      }
    } else if (!_agent.pathPending && _agent.remainingDistance <= arrivalThreshold) {
      if (waitTimeAtWaypoint > 0f) {
        _isWaiting = true;
        _waitTimer = waitTimeAtWaypoint;
        _agent.isStopped = true;
      } else {
        AdvanceWaypoint();
      }
    }

    DriveAnimation();
  }

  public void StartMoving() {
    if (waypoints == null || waypoints.Length == 0) {
      Debug.LogWarning("[CreatureWaypointMover] No waypoints assigned.", this);
      return;
    }

    _isActive = true;
    _isWaiting = false;
    _waypointIndex = 0;
    ApplySpeed();
    SetDestinationToCurrentWaypoint();
  }

  public void Stop() {
    _isActive = false;
    _isWaiting = false;
    if (_agent.isOnNavMesh) _agent.isStopped = true;
    DriveAnimation();
  }

  private void AdvanceWaypoint() {
    _waypointIndex++;
    if (_waypointIndex >= waypoints.Length) {
      if (loop) {
        _waypointIndex = 0;
      } else {
        Stop();
        return;
      }
    }
    SetDestinationToCurrentWaypoint();
  }

  private void SetDestinationToCurrentWaypoint() {
    if (waypoints[_waypointIndex] == null) return;
    _agent.isStopped = false;
    _agent.SetDestination(waypoints[_waypointIndex].position);
  }

  private void ApplySpeed() {
    _agent.speed = run ? runSpeed : walkSpeed;
  }

  private void DriveAnimation() {
    if (animator == null) return;

    float speedFraction = runSpeed > 0.0001f ? Mathf.Clamp01(_agent.velocity.magnitude / runSpeed) : 0f;
    if (!string.IsNullOrEmpty(speedParameter)) animator.SetFloat(speedParameter, speedFraction);
    if (!string.IsNullOrEmpty(stateParameter)) animator.SetFloat(stateParameter, run ? 1f : 0f);
  }

  private void OnDrawGizmosSelected() {
    if (!drawGizmos || waypoints == null || waypoints.Length == 0) return;

    Gizmos.color = Color.cyan;
    for (var i = 0; i < waypoints.Length; i++) {
      if (waypoints[i] == null) continue;

      Gizmos.DrawSphere(waypoints[i].position, 0.15f);
      var next = (i + 1) % waypoints.Length;
      if ((loop || next != 0) && waypoints[next] != null) {
        Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
      }
    }
  }
}
