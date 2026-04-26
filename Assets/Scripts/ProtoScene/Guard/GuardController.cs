using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GuardController : MonoBehaviour
{
  // ── States ────────────────────────────────────────────────────────────────
  public enum GuardState { Patrol, Suspicious, Alerted, TakenDown }

  [Header("Patrol")]
  [Tooltip("World-space waypoints the guard walks between")]
  public Transform[] patrolWaypoints;
  public float waypointWaitTime = 2f;
  public float patrolMoveSpeed = 2f;

  [Header("Alert")]
  public float alertMoveSpeed = 4f;
  [Tooltip("How long the guard investigates the last known position")]
  public float investigateDuration = 5f;

  [Header("References")]
  [Tooltip("Leave empty to auto-find on this or child GameObjects")]
  public GuardVisionCone visionCone;

  [Header("Debug")]
  public bool showStateLabel = true;

  // ── Public ────────────────────────────────────────────────────────────────
  public GuardState CurrentState { get; private set; } = GuardState.Patrol;

  // ── Private ───────────────────────────────────────────────────────────────
  private NavMeshAgent _agent;
  private int _waypointIndex = 0;
  private float _waitTimer = 0f;
  private float _investigateTimer = 0f;
  private Vector3 _lastKnownPosition;
  private bool _waitingAtWaypoint = false;

  // ── Unity Messages ────────────────────────────────────────────────────────
  private void Awake()
  {
    _agent = GetComponent<NavMeshAgent>();

    if (visionCone == null)
      visionCone = GetComponentInChildren<GuardVisionCone>();

    if (visionCone == null)
      Debug.LogWarning($"[Guard] {name}: No GuardVisionCone found!", this);
  }

  private void Start()
  {
    if (patrolWaypoints.Length > 0)
      GoToWaypoint(_waypointIndex);
  }

  private void Update()
  {
    if (CurrentState == GuardState.TakenDown) return;

    // Always check vision
    if (visionCone != null && visionCone.PlayerDetected)
    {
      _lastKnownPosition = visionCone.DetectedPlayer.transform.position;
      SetState(GuardState.Alerted);
    }

    switch (CurrentState)
    {
      case GuardState.Patrol: UpdatePatrol(); break;
      case GuardState.Suspicious: UpdateSuspicious(); break;
      case GuardState.Alerted: UpdateAlerted(); break;
    }
  }

  // ── State Machine ─────────────────────────────────────────────────────────
  private void SetState(GuardState newState)
  {
    if (CurrentState == newState) return;
    CurrentState = newState;

    switch (newState)
    {
      case GuardState.Patrol:
        _agent.speed = patrolMoveSpeed;
        GoToWaypoint(_waypointIndex);
        break;

      case GuardState.Suspicious:
        _agent.speed = patrolMoveSpeed * 1.5f;
        _investigateTimer = investigateDuration * 0.5f;
        break;

      case GuardState.Alerted:
        _agent.speed = alertMoveSpeed;
        _agent.SetDestination(_lastKnownPosition);
        _investigateTimer = investigateDuration;
        break;

      case GuardState.TakenDown:
        if (_agent != null && _agent.isOnNavMesh)
        {
          _agent.isStopped = true;
          _agent.enabled = false;
        }
        foreach (Collider c in GetComponentsInChildren<Collider>())
          c.enabled = false;
        if (visionCone != null) visionCone.enabled = false;
        Debug.Log($"[Guard] {name} has been taken down.");
        break;
    }
  }

  // ── Patrol ────────────────────────────────────────────────────────────────
  private void UpdatePatrol()
  {
    if (patrolWaypoints.Length == 0) return;

    if (_waitingAtWaypoint)
    {
      _waitTimer -= Time.deltaTime;
      if (_waitTimer <= 0f)
      {
        _waitingAtWaypoint = false;
        _waypointIndex = (_waypointIndex + 1) % patrolWaypoints.Length;
        GoToWaypoint(_waypointIndex);
      }
      return;
    }

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
    {
      _waitingAtWaypoint = true;
      _waitTimer = waypointWaitTime;
    }
  }

  private void GoToWaypoint(int index)
  {
    if (patrolWaypoints.Length == 0) return;
    _agent.isStopped = false;
    _agent.SetDestination(patrolWaypoints[index].position);
  }

  // ── Suspicious ────────────────────────────────────────────────────────────
  private void UpdateSuspicious()
  {
    _investigateTimer -= Time.deltaTime;
    if (_investigateTimer <= 0f)
      SetState(GuardState.Patrol);
  }

  // ── Alerted ───────────────────────────────────────────────────────────────
  private void UpdateAlerted()
  {
    if (visionCone != null && visionCone.PlayerDetected)
    {
      // Keep chasing live position
      _lastKnownPosition = visionCone.DetectedPlayer.transform.position;
      _agent.SetDestination(_lastKnownPosition);
      _investigateTimer = investigateDuration; // reset timer while visible
      return;
    }

    // Player lost – investigate last known position
    _investigateTimer -= Time.deltaTime;

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
    {
      // Arrived at last known pos; look around then go suspicious
      _investigateTimer -= Time.deltaTime * 2f;
    }

    if (_investigateTimer <= 0f)
      SetState(GuardState.Suspicious);
  }

  // ── Takedown ──────────────────────────────────────────────────────────────
  /// <summary>Called by PlayerStealthController when a valid takedown is executed.</summary>
  public void PerformTakedown()
  {
    SetState(GuardState.TakenDown);
  }

  // ── Gizmos ────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (patrolWaypoints == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < patrolWaypoints.Length; i++)
        {
            if (patrolWaypoints[i] == null) continue;
            Gizmos.DrawSphere(patrolWaypoints[i].position, 0.15f);
            int next = (i + 1) % patrolWaypoints.Length;
            if (patrolWaypoints[next] != null)
                Gizmos.DrawLine(patrolWaypoints[i].position, patrolWaypoints[next].position);
        }
    }

    private void OnGUI()
    {
        if (!showStateLabel || Camera.main == null) return;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f);
        if (screenPos.z < 0) return;

        string label = $"[{name}] {CurrentState}";
        Color  col   = CurrentState switch
        {
            GuardState.Patrol    => Color.green,
            GuardState.Suspicious => Color.yellow,
            GuardState.Alerted   => Color.red,
            GuardState.TakenDown => Color.gray,
            _                    => Color.white
        };

        GUI.color = col;
        GUI.Label(new Rect(screenPos.x - 60, Screen.height - screenPos.y - 20, 160, 25), label);
        GUI.color = Color.white;
    }
#endif
}