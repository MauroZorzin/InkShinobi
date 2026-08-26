using System.Collections.Generic;
using UnityEngine;

public enum WallSwitchFailureReason {
  None,
  PlayerUnavailable,
  NoAuthoredPath,
  CursorTooFar,
  PathsNotParallel,
  DestinationTooClose,
  DestinationTooFar,
  Blocked
}

public enum WallSwitchTargetDisposition {
  Ignored,
  Vulnerable,
  Blocking
}

/// <summary>
/// Immutable result shared by wall-switch preview and execution. Once confirmed, execution uses
/// this exact destination and target set rather than querying the scene again.
/// </summary>
public sealed class WallSwitchEvaluation {
  public static readonly WallSwitchEvaluation Empty = new(WallSwitchFailureReason.NoAuthoredPath);

  public bool IsValid => FailureReason == WallSwitchFailureReason.None;
  public WallSwitchFailureReason FailureReason { get; }
  public LinePath SourcePath { get; }
  public int SourceStrand { get; }
  public LinePath DestinationPath { get; }
  public int DestinationStrand { get; }
  public float DestinationDistance { get; }
  public Vector3 DestinationFeet { get; }
  public Vector3 DestinationRoot { get; }
  public Vector3 TrajectoryStart { get; }
  public Vector3 TrajectoryEnd { get; }
  public Vector3 CursorWorldPoint { get; }
  public float CursorDistancePixels { get; }
  public Object BlockingObject { get; }
  public Vector3 BlockingPoint { get; }
  public IReadOnlyList<GuardWallSwitchTarget> TakedownTargets { get; }
  public IReadOnlyList<GuardWallSwitchTarget> BlockingGuards { get; }

  public WallSwitchEvaluation(WallSwitchFailureReason failureReason) {
    FailureReason = failureReason;
    TakedownTargets = System.Array.Empty<GuardWallSwitchTarget>();
    BlockingGuards = System.Array.Empty<GuardWallSwitchTarget>();
  }

  public WallSwitchEvaluation(
    WallSwitchFailureReason failureReason,
    LinePath sourcePath,
    int sourceStrand,
    LinePath destinationPath,
    int destinationStrand,
    float destinationDistance,
    Vector3 destinationFeet,
    Vector3 destinationRoot,
    Vector3 trajectoryStart,
    Vector3 trajectoryEnd,
    Vector3 cursorWorldPoint,
    float cursorDistancePixels,
    Object blockingObject,
    Vector3 blockingPoint,
    List<GuardWallSwitchTarget> takedownTargets,
    List<GuardWallSwitchTarget> blockingGuards) {
    FailureReason = failureReason;
    SourcePath = sourcePath;
    SourceStrand = sourceStrand;
    DestinationPath = destinationPath;
    DestinationStrand = destinationStrand;
    DestinationDistance = destinationDistance;
    DestinationFeet = destinationFeet;
    DestinationRoot = destinationRoot;
    TrajectoryStart = trajectoryStart;
    TrajectoryEnd = trajectoryEnd;
    CursorWorldPoint = cursorWorldPoint;
    CursorDistancePixels = cursorDistancePixels;
    BlockingObject = blockingObject;
    BlockingPoint = blockingPoint;
    TakedownTargets = takedownTargets?.ToArray() ?? System.Array.Empty<GuardWallSwitchTarget>();
    BlockingGuards = blockingGuards?.ToArray() ?? System.Array.Empty<GuardWallSwitchTarget>();
  }
}
