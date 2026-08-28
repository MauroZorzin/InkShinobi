using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The guard's only translation and rotation authority. Brains request destinations or facing;
/// they never manipulate the Transform or NavMeshAgent directly.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[DefaultExecutionOrder(-20)]
public sealed class GuardMotor : MonoBehaviour {
  [Header("Navigation")]
  [Tooltip("Maximum distance used to project authored destinations onto the baked NavMesh.")]
  [SerializeField, Min(0.05f)] private float destinationSampleRadius = 0.75f;
  [Tooltip("Maximum distance used once at startup to place the guard on the baked NavMesh.")]
  [SerializeField, Min(0.05f)] private float startupSampleRadius = 1f;

  [Header("Door Traversal")]
  [Tooltip("How far ahead along the current NavMesh route a guard detects a passageway door.")]
  [SerializeField, Min(0.05f)] private float doorApproachDetectionDistance = 0.8f;
  [Tooltip("Distance beyond the opposite side of a door at which this guard releases its passage reservation.")]
  [SerializeField, Min(0.01f)] private float doorClearanceDistance = 0.4f;

  [Header("Facing")]
  [Tooltip("Default turn speed used outside patrol-specific corner settings.")]
  [SerializeField, Min(0f)] private float turnSpeed = 360f;
  [Tooltip("Velocity below this value does not change the guard's facing.")]
  [SerializeField, Min(0f)] private float facingVelocityThreshold = 0.02f;

  [Header("Navigation Diagnostics")]
  [Tooltip("Logs accepted destinations, incomplete paths, and stalls. Intended for temporary play-test diagnosis.")]
  [SerializeField] private bool verboseNavigation;
  [SerializeField, Min(0.05f)] private float stallWarningDelay = 0.75f;
  [SerializeField, Min(0.1f)] private float repeatedWarningInterval = 1.5f;
  [SerializeField, Min(0f)] private float stallVelocityThreshold = 0.02f;

  private NavMeshAgent agent;
  private bool manualFacing;
  private Quaternion requestedFacing;
  private float requestedTurnSpeed;
  private float runtimeTurnSpeed;
  private bool hasNavigationRequest;
  private Vector3 requestedDestination;
  private Vector3 sampledDestination;
  private string requestContext;
  private float stalledDuration;
  private float nextWarningTime;
  private Vector3 lastLoggedDestination;
  private bool hasLoggedDestination;
  private NavMeshPath pathBuffer;
  private readonly Vector3[] routeCornerBuffer = new Vector3[64];
  private GuardKeyCarrier keyCarrier;
  private PassagewayDoor activeDoor;
  private float activeDoorEntrySide;
  private bool doorPassageGranted;
  private bool stoppedForDoor;
  private bool waitingForPlayerDoorPath;
  private bool movementRequested;

  public NavMeshAgent Agent => agent;
  public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;
  public bool IsMoving => IsReady && agent.velocity.sqrMagnitude > facingVelocityThreshold * facingVelocityThreshold;
  public Vector3 Velocity => IsReady ? agent.velocity : Vector3.zero;
  public bool HasArrived => IsReady
                            && hasNavigationRequest
                            && !agent.pathPending
                            && HorizontalDistance(transform.position, sampledDestination)
                               <= agent.stoppingDistance + 0.05f;
  public bool HasPathFailure => IsReady
                                && hasNavigationRequest
                                && !agent.pathPending
                                && (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)
                                && HorizontalDistance(transform.position, sampledDestination) > agent.stoppingDistance + 0.05f;
  public bool IsWaitingForPlayerDoorPath => activeDoor != null && waitingForPlayerDoorPath;
  public bool IsPursuitBlockedByPlayerHeldDoor {
    get {
      if (IsWaitingForPlayerDoorPath) return true;
      if (!IsReady || !hasNavigationRequest || pathBuffer == null) return false;

      int cornerCount = pathBuffer.GetCornersNonAlloc(routeCornerBuffer);
      if (cornerCount < 2) return false;
      foreach (PassagewayDoor door in PassagewayDoor.ActiveDoors) {
        if (door == null || !door.isActiveAndEnabled || !door.IsHeldClosedByPlayer) continue;
        for (int i = 0; i < cornerCount - 1; i++) {
          Vector3 segment = routeCornerBuffer[i + 1] - routeCornerBuffer[i];
          segment.y = 0f;
          float length = segment.magnitude;
          if (length <= 0.0001f) continue;
          if (door.TryGetApproachDistance(
                routeCornerBuffer[i], segment / length, length + agent.radius,
                agent.radius, out _)) return true;
        }
      }
      return false;
    }
  }

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();
    keyCarrier = GetComponent<GuardKeyCarrier>();
    agent.updateRotation = false;
    pathBuffer = new NavMeshPath();
    runtimeTurnSpeed = turnSpeed;
  }

  private void Start() {
    EnsureOnNavMesh();
  }

  private void LateUpdate() {
    UpdateDoorTraversal();
    UpdateNavigationDiagnostics();
    if (!IsReady) return;

    if (manualFacing) {
      transform.rotation = Quaternion.RotateTowards(
        transform.rotation,
        requestedFacing,
        requestedTurnSpeed * Time.deltaTime);
      return;
    }

    Vector3 velocity = agent.velocity;
    velocity.y = 0f;
    if (velocity.sqrMagnitude <= facingVelocityThreshold * facingVelocityThreshold) return;
    Quaternion target = Quaternion.LookRotation(velocity.normalized, Vector3.up);
    transform.rotation = Quaternion.RotateTowards(transform.rotation, target, runtimeTurnSpeed * Time.deltaTime);
  }

  private void OnDisable() => ReleaseActiveDoor(false);

  public bool EnsureOnNavMesh() {
    if (agent == null) agent = GetComponent<NavMeshAgent>();
    if (agent == null || !agent.enabled) return false;
    if (agent.isOnNavMesh) return true;
    if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, startupSampleRadius, agent.areaMask)) {
      Debug.LogError($"[GuardMotor] '{name}' could not find a NavMesh within {startupSampleRadius:F2}m.", this);
      return false;
    }
    return agent.Warp(hit.position);
  }

  public bool MoveTo(Vector3 worldPosition, float speed, float stoppingDistance, string context = null) {
    if (!EnsureOnNavMesh()) {
      LogNavigationWarning($"rejected '{context ?? "Unspecified"}' destination because the agent is not on a usable NavMesh. requested={Format(worldPosition)}");
      return false;
    }
    if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, destinationSampleRadius, agent.areaMask)) {
      LogNavigationWarning(
        $"could not sample '{context ?? "Unspecified"}' destination within {destinationSampleRadius:F2}m. " +
        $"requested={Format(worldPosition)}, agentPosition={Format(transform.position)}, areaMask={agent.areaMask}");
      return false;
    }

    if (pathBuffer == null) pathBuffer = new NavMeshPath();
    bool pathCalculated = agent.CalculatePath(hit.position, pathBuffer);
    if (!pathCalculated || pathBuffer.status != NavMeshPathStatus.PathComplete) {
      LogNavigationWarning(
        $"rejected '{context ?? "Unspecified"}' because its path is " +
        $"{(pathCalculated ? pathBuffer.status.ToString() : "not calculable")}. " +
        $"requested={Format(worldPosition)}, sampled={Format(hit.position)}, agentPosition={Format(transform.position)}");
      return false;
    }

    agent.speed = Mathf.Max(0f, speed);
    agent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
    bool accepted = agent.SetPath(pathBuffer);
    if (!accepted) {
      LogNavigationWarning(
        $"NavMeshAgent rejected '{context ?? "Unspecified"}' destination. " +
        $"requested={Format(worldPosition)}, sampled={Format(hit.position)}");
      return false;
    }

    manualFacing = false;
    movementRequested = true;
    agent.isStopped = false;

    bool materiallyDifferent = !hasNavigationRequest
                               || (hit.position - sampledDestination).sqrMagnitude > 0.25f;
    requestedDestination = worldPosition;
    sampledDestination = hit.position;
    requestContext = string.IsNullOrWhiteSpace(context) ? "Unspecified" : context;
    hasNavigationRequest = true;
    if (materiallyDifferent) stalledDuration = 0f;

    if (verboseNavigation && (!hasLoggedDestination ||
        (hit.position - lastLoggedDestination).sqrMagnitude > 0.25f)) {
      hasLoggedDestination = true;
      lastLoggedDestination = hit.position;
      Debug.Log(
        $"[GuardMotor] '{name}' accepted {requestContext} destination: " +
        $"requested={Format(requestedDestination)}, sampled={Format(sampledDestination)}, " +
        $"speed={agent.speed:F2}, stop={agent.stoppingDistance:F2}.", this);
    }

    return true;
  }

  public void Stop(bool clearPath = false) {
    movementRequested = false;
    ReleaseActiveDoor(false);
    if (!IsReady) return;
    agent.isStopped = true;
    if (clearPath) {
      if (agent.hasPath) agent.ResetPath();
      hasNavigationRequest = false;
      stalledDuration = 0f;
    }
  }

  public void Resume() {
    manualFacing = false;
    movementRequested = true;
    if (IsReady) agent.isStopped = false;
  }

  public void FaceDirection(Vector3 worldDirection, float degreesPerSecond) {
    worldDirection.y = 0f;
    if (worldDirection.sqrMagnitude <= 0.0001f) return;
    manualFacing = true;
    requestedFacing = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
    requestedTurnSpeed = Mathf.Max(0f, degreesPerSecond);
  }

  public void ReleaseManualFacing() {
    manualFacing = false;
  }

  /// <summary>Changes only the active movement state's turn rate; it does not rewrite serialized tuning.</summary>
  public void SetRuntimeTurnSpeed(float degreesPerSecond) {
    runtimeTurnSpeed = Mathf.Max(0f, degreesPerSecond);
  }

  public void ShutDown() {
    Stop(true);
    enabled = false;
    if (agent != null && agent.enabled) agent.enabled = false;
  }

  private void UpdateDoorTraversal() {
    if (!IsReady || !movementRequested || !hasNavigationRequest) {
      ReleaseActiveDoor(false);
      stoppedForDoor = false;
      return;
    }

    if (!TryGetRouteDirection(out Vector3 routeDirection)) {
      if (!agent.pathPending) ReleaseActiveDoor(true);
      return;
    }

    if (activeDoor != null) {
      if (!activeDoor.isActiveAndEnabled) {
        ReleaseActiveDoor(true);
        return;
      }

      if (doorPassageGranted && activeDoor.CurrentState == PassagewayDoor.PassageState.Open) {
        if (activeDoor.HasGuardCleared(activeDoorEntrySide, transform.position, doorClearanceDistance)) {
          activeDoor.NotifyGuardCleared(this);
          activeDoor = null;
          doorPassageGranted = false;
          stoppedForDoor = false;
          agent.isStopped = false;
          return;
        }

        bool stillApproaching = activeDoor.TryGetApproachDistance(
          transform.position,
          routeDirection,
          doorApproachDetectionDistance + doorClearanceDistance,
          agent.radius,
          out _);
        bool withinDoorway = activeDoor.DistanceFromPassagePlane(transform.position)
                             <= doorClearanceDistance + agent.radius;
        if (!stillApproaching && !withinDoorway) {
          ReleaseActiveDoor(true);
          return;
        }

        stoppedForDoor = false;
        agent.isStopped = false;
        return;
      }

      bool doorStillAhead = activeDoor.TryGetApproachDistance(
        transform.position,
        routeDirection,
        doorApproachDetectionDistance + agent.radius,
        agent.radius,
        out _);
      if (!doorStillAhead) {
        ReleaseActiveDoor(true);
        return;
      }

      ApplyDoorRequest(activeDoor.RequestGuardPassage(this, keyCarrier));
      return;
    }

    PassagewayDoor nearestDoor = FindDoorAhead(routeDirection);
    if (nearestDoor == null) {
      if (stoppedForDoor) agent.isStopped = false;
      stoppedForDoor = false;
      return;
    }

    activeDoor = nearestDoor;
    activeDoorEntrySide = activeDoor.GetPassageSide(transform.position);
    ApplyDoorRequest(activeDoor.RequestGuardPassage(this, keyCarrier));
  }

  private PassagewayDoor FindDoorAhead(Vector3 routeDirection) {
    PassagewayDoor nearest = null;
    float nearestDistance = float.PositiveInfinity;
    foreach (PassagewayDoor door in PassagewayDoor.ActiveDoors) {
      if (door == null || !door.isActiveAndEnabled) continue;
      if (!door.TryGetApproachDistance(
            transform.position,
            routeDirection,
            doorApproachDetectionDistance,
            agent.radius,
            out float distance) || distance >= nearestDistance) continue;
      nearest = door;
      nearestDistance = distance;
    }
    return nearest;
  }

  private void ApplyDoorRequest(PassagewayDoor.GuardPassageResult result) {
    waitingForPlayerDoorPath = result == PassagewayDoor.GuardPassageResult.WaitingForPlayerPath;
    doorPassageGranted = result == PassagewayDoor.GuardPassageResult.Granted;
    bool mayMove = doorPassageGranted && activeDoor != null &&
                   activeDoor.CurrentState == PassagewayDoor.PassageState.Open;
    stoppedForDoor = !mayMove;
    agent.isStopped = !mayMove;
  }

  private bool TryGetRouteDirection(out Vector3 direction) {
    direction = agent.steeringTarget - transform.position;
    direction.y = 0f;
    if (direction.sqrMagnitude <= 0.0001f) {
      direction = agent.desiredVelocity;
      direction.y = 0f;
    }
    if (direction.sqrMagnitude <= 0.0001f) return false;
    direction.Normalize();
    return true;
  }

  private void ReleaseActiveDoor(bool resumeMovement) {
    if (activeDoor != null) activeDoor.CancelGuardPassage(this);
    activeDoor = null;
    doorPassageGranted = false;
    waitingForPlayerDoorPath = false;
    stoppedForDoor = false;
    if (resumeMovement && IsReady && movementRequested) agent.isStopped = false;
  }

  private void UpdateNavigationDiagnostics() {
    if (!verboseNavigation || stoppedForDoor || !hasNavigationRequest || agent == null || !agent.enabled) return;
    if (!agent.isOnNavMesh) {
      WarnThrottled("left the NavMesh while a destination was active");
      return;
    }
    if (agent.pathPending) {
      return;
    }

    float directDistance = HorizontalDistance(transform.position, sampledDestination);
    float arrivalThreshold = agent.stoppingDistance + 0.05f;
    if (directDistance <= arrivalThreshold) {
      stalledDuration = 0f;
      return;
    }

    if (agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete)
      WarnThrottled($"has an incomplete path ({agent.pathStatus})");

    bool effectivelyStill = agent.velocity.sqrMagnitude <= stallVelocityThreshold * stallVelocityThreshold;
    bool cannotAdvance = agent.isStopped || !agent.hasPath || effectivelyStill;
    if (!cannotAdvance) {
      stalledDuration = 0f;
      return;
    }

    stalledDuration += Time.deltaTime;
    if (stalledDuration >= stallWarningDelay)
      WarnThrottled(agent.isStopped ? "is still marked stopped" : !agent.hasPath ? "has no path" : "has near-zero velocity");
  }

  private void WarnThrottled(string reason) {
    if (Time.unscaledTime < nextWarningTime) return;
    nextWarningTime = Time.unscaledTime + repeatedWarningInterval;
    Debug.LogWarning(
      $"[GuardMotor] NAVIGATION ISSUE on '{name}' ({requestContext}): {reason}. " +
      $"position={Format(transform.position)}, requested={Format(requestedDestination)}, " +
      $"sampled={Format(sampledDestination)}, isOnNavMesh={agent != null && agent.isOnNavMesh}, " +
      $"isStopped={agent != null && agent.isStopped}, hasPath={agent != null && agent.hasPath}, " +
      $"pathPending={agent != null && agent.pathPending}, pathStatus={(agent != null && agent.hasPath ? agent.pathStatus.ToString() : "NoPath")}, " +
      $"remaining={(agent != null && agent.isOnNavMesh ? agent.remainingDistance : float.PositiveInfinity):F3}, " +
      $"directDistance={HorizontalDistance(transform.position, sampledDestination):F3}, " +
      $"velocity={(agent != null ? agent.velocity.magnitude : 0f):F3}, " +
      $"desiredVelocity={(agent != null ? agent.desiredVelocity.magnitude : 0f):F3}.", this);
  }

  private void LogNavigationWarning(string message) {
    if (verboseNavigation) Debug.LogWarning($"[GuardMotor] '{name}' {message}", this);
  }

  private static float HorizontalDistance(Vector3 a, Vector3 b) {
    Vector3 delta = a - b;
    delta.y = 0f;
    return delta.magnitude;
  }

  private static string Format(Vector3 value) =>
    $"({value.x:F3}, {value.y:F3}, {value.z:F3})";

}
