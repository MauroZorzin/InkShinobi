using UnityEngine;

/// <summary>
/// Authored loop-patrol route for the Palace guard. This component owns route data and tuning;
/// GuardController chooses a point and GuardMotor performs all movement.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuardSquarePatrol : MonoBehaviour {
  [SerializeField] private Transform[] points;
  [SerializeField, Min(0f)] private float speed = 1.25f;
  [SerializeField, Min(0.001f)] private float arrivalDistance = 0.03f;
  [SerializeField, Min(0f)] private float cornerPause = 0.15f;
  [SerializeField, Min(0f), Tooltip("Maximum turning speed in degrees per second. The guard turns toward the next segment during its corner pause.")]
  private float turnSpeed = 480f;
  [SerializeField] private bool startAtNearestPoint = true;

  public int Count => points?.Length ?? 0;
  public float Speed => speed;
  public float ArrivalDistance => arrivalDistance;
  public float CornerPause => cornerPause;
  public float TurnSpeed => turnSpeed;
  public int InitialPointIndex => startAtNearestPoint ? FindNearestPointIndex(transform.position) : 0;

  public Transform GetPoint(int index) {
    if (points == null || points.Length == 0) return null;
    int wrapped = ((index % points.Length) + points.Length) % points.Length;
    return points[wrapped];
  }

  /// <summary>True when this waypoint changes the direction of the closed patrol route.</summary>
  public bool IsCorner(int index) {
    if (points == null || points.Length < 3) return true;
    Transform previous = GetPoint(index - 1);
    Transform current = GetPoint(index);
    Transform next = GetPoint(index + 1);
    if (previous == null || current == null || next == null) return true;

    Vector3 incoming = current.position - previous.position;
    Vector3 outgoing = next.position - current.position;
    incoming.y = 0f;
    outgoing.y = 0f;
    if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f) return false;
    return Vector3.Angle(incoming, outgoing) > 1f;
  }

  public void Configure(Transform[] routePoints) {
    points = routePoints;
  }

  public int FindNearestPointIndex(Vector3 worldPosition) {
    if (points == null || points.Length == 0) return 0;
    int nearest = 0;
    float bestDistance = float.PositiveInfinity;
    for (int i = 0; i < points.Length; i++) {
      if (points[i] == null) continue;
      float distance = (points[i].position - worldPosition).sqrMagnitude;
      if (distance >= bestDistance) continue;
      bestDistance = distance;
      nearest = i;
    }
    return nearest;
  }
}
