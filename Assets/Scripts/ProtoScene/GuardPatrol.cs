using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GuardPatrol : MonoBehaviour {
  [Header("Patrol")]
  [SerializeField] private PatrolArea patrolArea;
  [SerializeField] private float waitTimeMin = 1f;
  [SerializeField] private float waitTimeMax = 3f;

  [Header("Random Point Search")]
  [SerializeField] private float navMeshSearchRadius = 3f;
  [SerializeField] private int maxPointAttempts = 30;

  [Header("Debug")]
  [SerializeField] private bool debugLogs = true;

  private NavMeshAgent agent;
  private NavMeshPath path;

  private bool waiting;
  private float waitUntilTime;

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();
    path = new NavMeshPath();

    agent.updatePosition = true;
    agent.updateRotation = false;
  }

  private void Start() {
    if (patrolArea == null) {
      Debug.LogError($"{name}: No PatrolArea assigned.", this);
      enabled = false;
      return;
    }

    if (!agent.isOnNavMesh) {
      Debug.LogError($"{name}: This guard is not on the NavMesh.", this);
      enabled = false;
      return;
    }

    agent.isStopped = false;

    ChooseNewDestination();
  }

  private void Update() {
    if (waiting) {
      if (Time.time >= waitUntilTime) {
        waiting = false;
        ChooseNewDestination();
      }

      return;
    }

    if (agent.pathPending) {
      return;
    }

    if (!agent.hasPath) {
      ChooseNewDestination();
      return;
    }

    if (agent.remainingDistance <= agent.stoppingDistance) {
      StartWaiting();
    }
  }

  private void ChooseNewDestination() {
    for (var i = 0; i < maxPointAttempts; i++) {
      var foundPoint = patrolArea.TryGetRandomPointOnNavMesh(
          out Vector3 destination,
          navMeshSearchRadius,
          agent.areaMask,
          maxPointAttempts);

      if (!foundPoint) {
        continue;
      }

      var pathExists = NavMesh.CalculatePath(
          transform.position,
          destination,
          agent.areaMask,
          path);

      if (!pathExists || path.status != NavMeshPathStatus.PathComplete) {
        continue;
      }

      var destinationAccepted = agent.SetDestination(destination);

      if (destinationAccepted) {
        if (debugLogs) {
          Debug.Log($"{name}: Moving to {destination}.", this);
        }
        return;
      }
    }

    Debug.LogWarning(
      $"{name}: Could not find a valid patrol destination. Check PatrolArea size, Area Mask, and NavMeshModifierVolume overlap.",
      this
    );

    StartWaiting();
  }

  private void StartWaiting() {
    waiting = true;
    waitUntilTime = Time.time + Random.Range(waitTimeMin, waitTimeMax);
    agent.ResetPath();
  }
}