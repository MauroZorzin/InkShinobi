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

  [Header("Facing")]
  [Tooltip("Default turn speed used outside patrol-specific corner settings.")]
  [SerializeField, Min(0f)] private float turnSpeed = 360f;
  [Tooltip("Velocity below this value does not change the guard's facing.")]
  [SerializeField, Min(0f)] private float facingVelocityThreshold = 0.02f;

  private NavMeshAgent agent;
  private bool manualFacing;
  private Quaternion requestedFacing;
  private float requestedTurnSpeed;
  private float runtimeTurnSpeed;

  public NavMeshAgent Agent => agent;
  public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;
  public bool IsMoving => IsReady && agent.velocity.sqrMagnitude > facingVelocityThreshold * facingVelocityThreshold;
  public Vector3 Velocity => IsReady ? agent.velocity : Vector3.zero;
  public bool HasArrived => IsReady
                            && !agent.pathPending
                            && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.02f);

  private void Awake() {
    agent = GetComponent<NavMeshAgent>();
    agent.updateRotation = false;
    runtimeTurnSpeed = turnSpeed;
  }

  private void Start() {
    EnsureOnNavMesh();
  }

  private void LateUpdate() {
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

  public bool MoveTo(Vector3 worldPosition, float speed, float stoppingDistance) {
    if (!EnsureOnNavMesh()) return false;
    if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, destinationSampleRadius, agent.areaMask))
      return false;

    manualFacing = false;
    agent.speed = Mathf.Max(0f, speed);
    agent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
    agent.isStopped = false;
    return agent.SetDestination(hit.position);
  }

  public void Stop(bool clearPath = false) {
    if (!IsReady) return;
    agent.isStopped = true;
    if (clearPath && agent.hasPath) agent.ResetPath();
  }

  public void Resume() {
    manualFacing = false;
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
}
