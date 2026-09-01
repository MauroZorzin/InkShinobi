using UnityEngine;

/// <summary>
/// Explicit scene-authored collection of paths that may participate in wall switching.
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
    float wallSideTolerance,
    out DestinationCandidate candidate,
    out float closestNonParallelSurfaceDistance,
    out SelectionDiagnostics diagnostics) {
    candidate = default;
    closestNonParallelSurfaceDistance = float.PositiveInfinity;
    diagnostics = default;
    if (camera == null || sourcePath == null || switchablePaths == null) return false;

    // The first wall under the cursor is authoritative. Candidate paths must be supported by
    // that wall and lie on its player-facing side. The camera may see the opposite face, so it
    // cannot determine which of two paths sandwiching the wall is the valid destination.
    Ray cursorRay = camera.ScreenPointToRay(cursorScreenPosition);
    if (!Physics.Raycast(
          cursorRay,
          out RaycastHit wallHit,
          camera.farClipPlane,
          selectableWallLayers,
          QueryTriggerInteraction.Ignore)) {
      diagnostics = SelectionDiagnostics.NoWallHit;
      return false;
    }

    Vector3 sourceDirection = sourcePath.GetDirectionAtDistance(sourceStrand, sourceDistance);
    sourceDirection.y = 0f;
    if (sourceDirection.sqrMagnitude < 0.0001f) {
      diagnostics = SelectionDiagnostics.DegenerateSourceDirection(wallHit.collider.name);
      return false;
    }
    sourceDirection.Normalize();
    Vector3 sourcePoint = sourcePath.GetPointAtDistance(sourceStrand, sourceDistance);
    Vector3 sourcePlaneNormal = Vector3.Cross(Vector3.up, sourceDirection).normalized;
    int sourceSegment = FindSegmentAtDistance(sourcePath, sourceStrand, sourceDistance);

    float bestSurfaceDistance = float.PositiveInfinity;
    Vector2 surfacePoint = new(wallHit.point.x, wallHit.point.z);
    Vector3 playerFacingNormal = wallHit.normal;
    playerFacingNormal.y = 0f;
    bool hasHorizontalWallNormal = playerFacingNormal.sqrMagnitude > 0.0001f;
    if (hasHorizontalWallNormal) {
      playerFacingNormal.Normalize();
      if (Vector3.Dot(sourcePoint - wallHit.point, playerFacingNormal) < 0f)
        playerFacingNormal = -playerFacingNormal;
    }
    int totalSegmentsInScene = 0;
    int excludedAsSourceSegment = 0;
    int excludedAsUnreadable = 0;
    int excludedAsDegenerate = 0;
    int segmentsConsidered = 0;
    int rejectedByPointMargin = 0;
    int rejectedBySearchRadius = 0;
    int rejectedByWallSide = 0;
    int rejectedByParallel = 0;
    int rejectedByPlaneSeparation = 0;
    int accepted = 0;

    for (int pathIndex = 0; pathIndex < switchablePaths.Length; pathIndex++) {
      LinePath path = switchablePaths[pathIndex];
      if (path == null || !path.isActiveAndEnabled) continue;

      for (int strand = 0; strand < path.StrandCount; strand++) {
        int segmentCount = path.GetSegmentCount(strand);
        for (int segment = 0; segment < segmentCount; segment++) {
          totalSegmentsInScene++;
          // A LinePath may contain several wall segments. Exclude only the segment currently
          // supporting the player, not every other wall authored in that same component.
          if (path == sourcePath && strand == sourceStrand && segment == sourceSegment) { excludedAsSourceSegment++; continue; }
          if (!path.TryGetSegment(strand, segment, out Vector3 start, out Vector3 end, out float startDistance, out float length)) { excludedAsUnreadable++; continue; }

          Vector3 candidateDirection = end - start;
          candidateDirection.y = 0f;
          if (candidateDirection.sqrMagnitude < 0.0001f) { excludedAsDegenerate++; continue; }
          candidateDirection.Normalize();
          segmentsConsidered++;

          Vector2 startSurface = new(start.x, start.z);
          Vector2 endSurface = new(end.x, end.z);
          Vector2 surfaceDelta = endSurface - startSurface;
          float surfaceDenominator = surfaceDelta.sqrMagnitude;
          float t = surfaceDenominator > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(surfacePoint - startSurface, surfaceDelta) / surfaceDenominator)
            : 0f;
          float distanceFromNearestPoint = Mathf.Min(length * t, length * (1f - t));
          if (distanceFromNearestPoint < pointMargin) { rejectedByPointMargin++; continue; }
          Vector3 point = Vector3.Lerp(start, end, t);

          // Associate the entire authored segment with the selected wall collider rather than
          // requiring the cursor ray to land beside the line itself. This lets tall walls and
          // solid inner blocks be selected across their visible face, not only at an edge.
          Vector3 closestWallPoint = wallHit.collider.ClosestPoint(point);
          float pathToWallDistance = Vector2.Distance(
            new Vector2(point.x, point.z),
            new Vector2(closestWallPoint.x, closestWallPoint.z));
          if (pathToWallDistance > pathSearchRadius) { rejectedBySearchRadius++; continue; }

          // ClosestPoint alone cannot distinguish two LinePaths placed on opposite sides of one
          // collider. Orient the hit face toward the player and keep only that side's path.
          if (hasHorizontalWallNormal &&
              Vector3.Dot(point - wallHit.point, playerFacingNormal) < -wallSideTolerance) { rejectedByWallSide++; continue; }

          float surfaceDistance = Vector2.Distance(surfacePoint, new Vector2(point.x, point.z));

          float angle = Vector3.Angle(sourceDirection, candidateDirection);
          float parallelAngle = Mathf.Min(angle, 180f - angle);
          if (parallelAngle > parallelTolerance) {
            closestNonParallelSurfaceDistance = Mathf.Min(closestNonParallelSurfaceDistance, surfaceDistance);
            rejectedByParallel++;
            continue;
          }

          // Parallel pieces of the same supporting plane are merely continuations of the
          // current wall (often separated by a corridor opening), not another wall to switch to.
          float planeSeparation = Mathf.Abs(Vector3.Dot(point - sourcePoint, sourcePlaneNormal));
          if (planeSeparation < minimumPlaneSeparation) { rejectedByPlaneSeparation++; continue; }

          accepted++;
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

    diagnostics = new SelectionDiagnostics(
      true,
      wallHit.collider.name,
      true,
      totalSegmentsInScene,
      excludedAsSourceSegment,
      excludedAsUnreadable,
      excludedAsDegenerate,
      segmentsConsidered,
      rejectedByPointMargin,
      rejectedBySearchRadius,
      rejectedByWallSide,
      rejectedByParallel,
      rejectedByPlaneSeparation,
      accepted);

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

  /// <summary>Per-filter breakdown of why segment selection succeeded or failed, for debugging.</summary>
  public readonly struct SelectionDiagnostics {
    public bool HitWall { get; }
    public string HitWallName { get; }
    public bool HasValidSourceDirection { get; }
    public int TotalSegmentsInScene { get; }
    public int ExcludedAsSourceSegment { get; }
    public int ExcludedAsUnreadable { get; }
    public int ExcludedAsDegenerate { get; }
    public int SegmentsConsidered { get; }
    public int RejectedByPointMargin { get; }
    public int RejectedBySearchRadius { get; }
    public int RejectedByWallSide { get; }
    public int RejectedByParallel { get; }
    public int RejectedByPlaneSeparation { get; }
    public int Accepted { get; }

    public static SelectionDiagnostics NoWallHit => new(false, null, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static SelectionDiagnostics DegenerateSourceDirection(string wallName) =>
      new(true, wallName, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public SelectionDiagnostics(
      bool hitWall,
      string hitWallName,
      bool hasValidSourceDirection,
      int totalSegmentsInScene,
      int excludedAsSourceSegment,
      int excludedAsUnreadable,
      int excludedAsDegenerate,
      int segmentsConsidered,
      int rejectedByPointMargin,
      int rejectedBySearchRadius,
      int rejectedByWallSide,
      int rejectedByParallel,
      int rejectedByPlaneSeparation,
      int accepted) {
      HitWall = hitWall;
      HitWallName = hitWallName;
      HasValidSourceDirection = hasValidSourceDirection;
      TotalSegmentsInScene = totalSegmentsInScene;
      ExcludedAsSourceSegment = excludedAsSourceSegment;
      ExcludedAsUnreadable = excludedAsUnreadable;
      ExcludedAsDegenerate = excludedAsDegenerate;
      SegmentsConsidered = segmentsConsidered;
      RejectedByPointMargin = rejectedByPointMargin;
      RejectedBySearchRadius = rejectedBySearchRadius;
      RejectedByWallSide = rejectedByWallSide;
      RejectedByParallel = rejectedByParallel;
      RejectedByPlaneSeparation = rejectedByPlaneSeparation;
      Accepted = accepted;
    }

    public override string ToString() {
      if (!HitWall) return "no wall collider under the cursor (check selectableWallLayers / wallObstructionLayers)";
      if (!HasValidSourceDirection) return $"aimed at '{HitWallName}', but the current path direction is degenerate (zero-length)";
      return $"aimed at '{HitWallName}' — {TotalSegmentsInScene} segment(s) exist across all switchable paths " +
             $"({ExcludedAsSourceSegment} is your current segment, {ExcludedAsUnreadable} unreadable, " +
             $"{ExcludedAsDegenerate} zero-length -> {SegmentsConsidered} actually evaluated): " +
             $"{RejectedBySearchRadius} too far from the wall (wallPathSearchRadius), " +
             $"{RejectedByWallSide} on the far side of the wall (wallSideTolerance), " +
             $"{RejectedByPointMargin} too close to a corner (destinationPointMargin), " +
             $"{RejectedByParallel} not parallel enough (parallelToleranceDegrees), " +
             $"{RejectedByPlaneSeparation} on the same plane as the source (minimumDestinationPlaneSeparation), " +
             $"{Accepted} accepted.";
    }
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
