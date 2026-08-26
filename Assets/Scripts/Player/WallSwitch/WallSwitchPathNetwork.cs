using UnityEngine;

/// <summary>
/// Explicit scene-authored collection of paths that may participate in Palace wall switching.
/// </summary>
[DisallowMultipleComponent]
public sealed class WallSwitchPathNetwork : MonoBehaviour {
  [Tooltip("Only these authored paths can be selected as wall-switch destinations.")]
  [SerializeField] private LinePath[] switchablePaths = System.Array.Empty<LinePath>();

  public LinePath[] SwitchablePaths => switchablePaths;

  public bool TryFindDestination(
    Camera camera,
    Vector2 cursorScreenPosition,
    LinePath sourcePath,
    int sourceStrand,
    float sourceDistance,
    float parallelTolerance,
    float minimumPlaneSeparation,
    float pointMargin,
    LayerMask selectableWallLayers,
    float pathSearchRadius,
    out DestinationCandidate candidate,
    out float closestNonParallelSurfaceDistance) {
    candidate = default;
    closestNonParallelSurfaceDistance = float.PositiveInfinity;
    if (camera == null || sourcePath == null || switchablePaths == null) return false;

    // The first wall face under the cursor is authoritative. Candidate paths must belong to
    // that exact face, so neither paths behind it nor another side of the same solid block can
    // participate.
    Ray cursorRay = camera.ScreenPointToRay(cursorScreenPosition);
    if (!Physics.Raycast(
          cursorRay,
          out RaycastHit wallHit,
          camera.farClipPlane,
          selectableWallLayers,
          QueryTriggerInteraction.Ignore)) return false;

    Vector3 sourceDirection = sourcePath.GetDirectionAtDistance(sourceStrand, sourceDistance);
    sourceDirection.y = 0f;
    if (sourceDirection.sqrMagnitude < 0.0001f) return false;
    sourceDirection.Normalize();
    Vector3 sourcePoint = sourcePath.GetPointAtDistance(sourceStrand, sourceDistance);
    Vector3 sourcePlaneNormal = Vector3.Cross(Vector3.up, sourceDirection).normalized;
    int sourceSegment = FindSegmentAtDistance(sourcePath, sourceStrand, sourceDistance);

    float bestSurfaceDistance = float.PositiveInfinity;
    Vector2 surfacePoint = new(wallHit.point.x, wallHit.point.z);
    for (int pathIndex = 0; pathIndex < switchablePaths.Length; pathIndex++) {
      LinePath path = switchablePaths[pathIndex];
      if (path == null || !path.isActiveAndEnabled) continue;

      for (int strand = 0; strand < path.StrandCount; strand++) {
        int segmentCount = path.GetSegmentCount(strand);
        for (int segment = 0; segment < segmentCount; segment++) {
          // A LinePath may contain several wall segments. Exclude only the segment currently
          // supporting the player, not every other wall authored in that same component.
          if (path == sourcePath && strand == sourceStrand && segment == sourceSegment) continue;
          if (!path.TryGetSegment(strand, segment, out Vector3 start, out Vector3 end, out float startDistance, out float length)) continue;

          Vector3 candidateDirection = end - start;
          candidateDirection.y = 0f;
          if (candidateDirection.sqrMagnitude < 0.0001f) continue;
          candidateDirection.Normalize();

          Vector2 startSurface = new(start.x, start.z);
          Vector2 endSurface = new(end.x, end.z);
          Vector2 surfaceDelta = endSurface - startSurface;
          float surfaceDenominator = surfaceDelta.sqrMagnitude;
          float t = surfaceDenominator > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(surfacePoint - startSurface, surfaceDelta) / surfaceDenominator)
            : 0f;
          float distanceFromNearestPoint = Mathf.Min(length * t, length * (1f - t));
          if (distanceFromNearestPoint < pointMargin) continue;
          Vector3 point = Vector3.Lerp(start, end, t);

          // Associate the entire authored segment with the selected wall collider rather than
          // requiring the cursor ray to land beside the line itself. This lets tall walls and
          // solid inner blocks be selected across their visible face, not only at an edge.
          Vector3 closestWallPoint = wallHit.collider.ClosestPoint(point);
          float pathToWallDistance = Vector2.Distance(
            new Vector2(point.x, point.z),
            new Vector2(closestWallPoint.x, closestWallPoint.z));
          if (pathToWallDistance > pathSearchRadius) continue;

          float surfaceDistance = Vector2.Distance(surfacePoint, new Vector2(point.x, point.z));

          float angle = Vector3.Angle(sourceDirection, candidateDirection);
          float parallelAngle = Mathf.Min(angle, 180f - angle);
          if (parallelAngle > parallelTolerance) {
            closestNonParallelSurfaceDistance = Mathf.Min(closestNonParallelSurfaceDistance, surfaceDistance);
            continue;
          }

          // Parallel pieces of the same supporting plane are merely continuations of the
          // current wall (often separated by a corridor opening), not another wall to switch to.
          float planeSeparation = Mathf.Abs(Vector3.Dot(point - sourcePoint, sourcePlaneNormal));
          if (planeSeparation < minimumPlaneSeparation) continue;

          if (surfaceDistance >= bestSurfaceDistance) continue;
          bestSurfaceDistance = surfaceDistance;
          Vector3 pointScreen = camera.WorldToScreenPoint(point);
          float pixelDistance = pointScreen.z > 0f
            ? Vector2.Distance(cursorScreenPosition, new Vector2(pointScreen.x, pointScreen.y))
            : float.PositiveInfinity;
          candidate = new DestinationCandidate(
            path,
            strand,
            startDistance + length * t,
            point,
            candidateDirection,
            pixelDistance);
        }
      }
    }

    return candidate.Path != null;
  }

  private static int FindSegmentAtDistance(LinePath path, int strand, float distance) {
    int segmentCount = path != null ? path.GetSegmentCount(strand) : 0;
    for (int segment = 0; segment < segmentCount; segment++) {
      if (!path.TryGetSegment(strand, segment, out _, out _, out float startDistance, out float length)) continue;
      if (distance <= startDistance + length || segment == segmentCount - 1) return segment;
    }
    return -1;
  }

  public readonly struct DestinationCandidate {
    public LinePath Path { get; }
    public int Strand { get; }
    public float Distance { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }
    public float CursorDistancePixels { get; }

    public DestinationCandidate(
      LinePath path,
      int strand,
      float distance,
      Vector3 point,
      Vector3 direction,
      float cursorDistancePixels) {
      Path = path;
      Strand = strand;
      Distance = distance;
      Point = point;
      Direction = direction;
      CursorDistancePixels = cursorDistancePixels;
    }
  }
}
