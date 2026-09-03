using UnityEngine;

public enum DistractionThrowFailure {
  None,
  NoSurface,
  InvalidSurface,
  TooClose,
  TooFar,
  NoBallisticSolution,
  TooFast,
  Obstructed,
  Cooldown,
  InvalidConfiguration,
  PlayerUnavailable
}

/// <summary>Immutable result shared by the distraction preview and launched Rigidbody.</summary>
public readonly struct DistractionThrowEvaluation {
  public static readonly DistractionThrowEvaluation Empty = new(
    false, false, DistractionThrowFailure.NoSurface,
    Vector3.zero, Vector3.zero, Vector3.up, Vector3.zero, 0f, null);

  public readonly bool HasTarget;
  public readonly bool IsValid;
  public readonly DistractionThrowFailure Failure;
  public readonly Vector3 Origin;
  public readonly Vector3 Target;
  public readonly Vector3 TargetNormal;
  public readonly Vector3 InitialVelocity;
  public readonly float FlightTime;
  public readonly Collider TargetCollider;

  public DistractionThrowEvaluation(
    bool hasTarget,
    bool isValid,
    DistractionThrowFailure failure,
    Vector3 origin,
    Vector3 target,
    Vector3 targetNormal,
    Vector3 initialVelocity,
    float flightTime,
    Collider targetCollider) {
    HasTarget = hasTarget;
    IsValid = isValid;
    Failure = failure;
    Origin = origin;
    Target = target;
    TargetNormal = targetNormal;
    InitialVelocity = initialVelocity;
    FlightTime = flightTime;
    TargetCollider = targetCollider;
  }

  public Vector3 PositionAt(float elapsed) =>
    Origin + InitialVelocity * elapsed + 0.5f * Physics.gravity * elapsed * elapsed;

  public DistractionThrowEvaluation Invalid(DistractionThrowFailure failure) => new(
    HasTarget, false, failure, Origin, Target, TargetNormal,
    InitialVelocity, FlightTime, TargetCollider);
}

public static class BallisticThrowSolver {
  /// <summary>Solves a gravity-driven arc through an apex above both endpoints.</summary>
  public static bool TrySolve(
    Vector3 origin,
    Vector3 target,
    float apexHeight,
    float maximumSpeed,
    out Vector3 initialVelocity,
    out float flightTime,
    out bool exceedsMaximumSpeed) {
    initialVelocity = Vector3.zero;
    flightTime = 0f;
    exceedsMaximumSpeed = false;

    float gravity = Mathf.Abs(Physics.gravity.y);
    if (gravity < 0.0001f) return false;

    float apexY = Mathf.Max(origin.y, target.y) + Mathf.Max(0.05f, apexHeight);
    float rise = apexY - origin.y;
    float fall = apexY - target.y;
    if (rise < 0f || fall < 0f) return false;

    float verticalSpeed = Mathf.Sqrt(2f * gravity * rise);
    float timeUp = verticalSpeed / gravity;
    float timeDown = Mathf.Sqrt(2f * fall / gravity);
    flightTime = timeUp + timeDown;
    if (flightTime < 0.0001f) return false;

    Vector3 horizontal = target - origin;
    horizontal.y = 0f;
    Vector3 horizontalVelocity = horizontal / flightTime;
    initialVelocity = horizontalVelocity + Vector3.up * verticalSpeed;
    exceedsMaximumSpeed = maximumSpeed > 0f && initialVelocity.magnitude > maximumSpeed;
    return true;
  }
}
