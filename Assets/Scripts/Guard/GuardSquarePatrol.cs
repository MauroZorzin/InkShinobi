using UnityEngine;

/// <summary>
/// Minimal loop-patrol motor for the Palace prototype. It follows authored centerline points
/// directly; perception, pursuit, combat, and NavMesh decisions remain separate future systems.
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

  private int targetIndex;
  private float waitUntil;

  private void OnEnable() {
    if (points == null || points.Length == 0) return;
    targetIndex = startAtNearestPoint ? FindNearestPointIndex() : 0;
  }

  private void Update() {
    if (points == null || points.Length < 2) return;
    Transform target = points[targetIndex];
    if (target == null) return;

    Vector3 destination = target.position;
    destination.y = transform.position.y;

    Vector3 travelDirection = destination - transform.position;
    travelDirection.y = 0f;
    if (travelDirection.sqrMagnitude > arrivalDistance * arrivalDistance) {
      Quaternion targetRotation = Quaternion.LookRotation(travelDirection.normalized, Vector3.up);
      transform.rotation = Quaternion.RotateTowards(
        transform.rotation,
        targetRotation,
        turnSpeed * Time.deltaTime);
    }

    if (Time.time < waitUntil) return;

    transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);

    if ((transform.position - destination).sqrMagnitude > arrivalDistance * arrivalDistance) return;
    transform.position = destination;
    targetIndex = (targetIndex + 1) % points.Length;
    waitUntil = Time.time + cornerPause;
  }

  public void Configure(Transform[] routePoints) {
    points = routePoints;
    targetIndex = FindNearestPointIndex();
  }

  private int FindNearestPointIndex() {
    if (points == null || points.Length == 0) return 0;
    int nearest = 0;
    float bestDistance = float.PositiveInfinity;
    for (int i = 0; i < points.Length; i++) {
      if (points[i] == null) continue;
      float distance = (points[i].position - transform.position).sqrMagnitude;
      if (distance >= bestDistance) continue;
      bestDistance = distance;
      nearest = i;
    }
    return nearest;
  }
}
