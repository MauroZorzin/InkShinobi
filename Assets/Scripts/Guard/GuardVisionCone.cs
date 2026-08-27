using UnityEngine;

/// <summary>
/// Detects the player with a short always-active cone and a longer cone that only applies while the player is lit.
/// </summary>
public class GuardVisionCone : MonoBehaviour {
  [Header("Eye Level")]
  [Tooltip("World-space Y offset added to the guard pivot as the ray origin.")]
  public float eyeHeight = 1.6f;

  [Tooltip("Horizontal offset of both vision-cone origins along the guard's local right axis. Positive values move them right; negative values move them left.")]
  public float eyeHorizontalOffset = 0f;

  [Tooltip("World-space Y offset added to the player pivot as the aim target.")]
  public float playerAimHeight = 1.4f;

  [Header("Short Cone (always active)")]
  [Tooltip("Maximum range for the always-active short detection cone.")]
  [Range(1f, 30f)] public float shortRange = 5f;

  [Tooltip("Field of view in degrees for the always-active short detection cone.")]
  [Range(1f, 180f)] public float shortAngle = 60f;

  [Header("Long Cone (active when player is in light)")]
  [Tooltip("Maximum range for the light-only long detection cone.")]
  [Range(5f, 60f)] public float longRange = 15f;

  [Tooltip("Field of view in degrees for the light-only long detection cone.")]
  [Range(1f, 180f)] public float longAngle = 40f;

  [Header("Detection")]
  [Tooltip("Layer mask containing player colliders.")]
  public LayerMask playerLayerMask;

  [Tooltip("Layer mask containing objects that block line of sight. Leave empty to skip obstacle checks.")]
  public LayerMask obstacleMask;

  [Tooltip("Seconds required to fully detect the player. Zero means instant detection.")]
  public float detectionTime = 0.8f;

  [Tooltip("Seconds required to release a fully confirmed detection after the player is no longer visible.")]
  [Min(0.01f)] public float detectionReleaseTime = 0.35f;

  [Tooltip("Minimum fixed-light exposure required for a wall-switch trajectory point to count as visible inside the long cone.")]
  [Range(0f, 1f)] public float wallSwitchExposureThreshold = 0.15f;

  [Header("Debug")]
  [Tooltip("Draws vision cone gizmos in the Scene view.")]
  public bool showGizmos = true;

  [Tooltip("Prints scan details to the console.")]
  public bool verboseLogging = false;

  [Tooltip("Draws runtime rays for scan direction and line-of-sight checks.")]
  public bool showRuntimeRay = true;

  /// <summary>Whether this guard has fully confirmed a player detection.</summary>
  public bool PlayerDetected { get; private set; } = false;

  /// <summary>The player currently confirmed by this cone, or null when no player is detected.</summary>
  public PlayerStealthController DetectedPlayer { get; private set; }

  /// <summary>Normalized detection timer progress from 0 to 1.</summary>
  public float DetectionProgress => detectionTime > 0f ? Mathf.Clamp01(_detectionProgress / detectionTime) : (PlayerDetected ? 1f : 0f);

  /// <summary>True during an unobstructed visible frame, before or after detection is confirmed.</summary>
  public bool PlayerCurrentlyVisible => _visiblePlayer != null;

  /// <summary>The player visible on the current frame, before confirmation, or null.</summary>
  public PlayerStealthController VisiblePlayer => _visiblePlayer;

  /// <summary>Current near/far visibility contribution used to advance the detection meter.</summary>
  public float CurrentVisibilityStrength { get; private set; }

  private const float LineOfSightOriginOffset = 0.3f;

  private float _detectionProgress = 0f;
  private bool _wasDetectedLastFrame = false;
  private PlayerStealthController _trackedPlayer;
  private PlayerStealthController _visiblePlayer;

  private Vector3 EyeOrigin => transform.position
                               + Vector3.up * eyeHeight
                               + transform.right * eyeHorizontalOffset;

  private void Start() {
    if (playerLayerMask.value == 0) {
      Debug.LogWarning($"[VisionCone] '{name}': playerLayerMask is Nothing; guard will never detect anyone.", this);
    }
  }

  private void Update() {
    ScanForPlayer();
  }

  private void ScanForPlayer() {
    Vector3 eye = EyeOrigin;

    if (showRuntimeRay) {
      Debug.DrawRay(eye, transform.forward * shortRange, new Color(1f, 0.55f, 0f));
    }

    Collider[] hits = Physics.OverlapSphere(eye, longRange, playerLayerMask);

    if (verboseLogging) {
      Debug.Log($"[VisionCone] '{name}' | eye={eye:F2} | OverlapSphere(r={longRange}, mask={playerLayerMask.value}) -> {hits.Length} hit(s)");
    }

    float strongestVisibility = 0f;
    PlayerStealthController candidate = null;

    foreach (Collider col in hits) {
      PlayerStealthController playerStealth = col.GetComponentInParent<PlayerStealthController>();
      if (playerStealth == null) {
        if (verboseLogging) {
          Debug.Log($"[VisionCone] '{col.name}' skipped; no PlayerStealthController found in parents.");
        }
        continue;
      }
      if (playerStealth.IsConcealed) continue;

      if (playerStealth.IsUndetectable) {
        if (verboseLogging) {
          Debug.Log($"[VisionCone] '{playerStealth.name}' skipped; IsUndetectable.");
        }
        continue;
      }

      Vector3 aimPosition = new(
        playerStealth.transform.position.x,
        playerStealth.transform.position.y + playerAimHeight,
        playerStealth.transform.position.z
      );

      Vector3 toPlayerFlat = new(aimPosition.x - eye.x, 0f, aimPosition.z - eye.z);
      Vector3 guardForwardFlat = new(transform.forward.x, 0f, transform.forward.z);

      if (toPlayerFlat.sqrMagnitude < 0.0001f || guardForwardFlat.sqrMagnitude < 0.0001f) {
        continue;
      }

      toPlayerFlat.Normalize();
      guardForwardFlat.Normalize();

      var horizontalAngle = Vector3.Angle(guardForwardFlat, toPlayerFlat);
      var distance = Vector3.Distance(eye, aimPosition);
      Vector3 direction = (aimPosition - eye).normalized;
      var hasLineOfSight = HasLineOfSight(eye, direction, distance);
      var inShortCone = distance <= shortRange && horizontalAngle <= shortAngle * 0.5f;
      float playerExposure = playerStealth.LightExposure;
      bool insideLongGeometry = distance <= longRange && horizontalAngle <= longAngle * 0.5f;
      float visibilityStrength = hasLineOfSight
        ? Mathf.Max(inShortCone ? 1f : 0f, insideLongGeometry && playerStealth.IsInLight ? playerExposure : 0f)
        : 0f;
      bool inLongCone = insideLongGeometry && playerStealth.IsInLight;
      bool inCone = visibilityStrength > 0f;

      if (verboseLogging) {
        Debug.Log(
          $"[VisionCone] '{playerStealth.name}' | dist={distance:F2}m angle={horizontalAngle:F1} deg " +
          $"| inShort={inShortCone} | inLong={inLongCone} | exposure={playerExposure:F2} " +
          $"| LOS={hasLineOfSight} -> strength={visibilityStrength:F2}"
        );
      }

      if (showRuntimeRay) {
        Debug.DrawLine(eye, aimPosition, inCone ? Color.green : Color.red);
      }

      if (visibilityStrength > strongestVisibility) {
        strongestVisibility = visibilityStrength;
        candidate = playerStealth;
      }
    }

    UpdateDetectionState(strongestVisibility, candidate);
  }

  /// <summary>
  /// Checks whether obstacle layers block the ray from the guard eye to the player.
  /// </summary>
  /// <param name="eye">The guard eye position.</param>
  /// <param name="direction">Normalized direction toward the player aim point.</param>
  /// <param name="distance">Distance from eye to player aim point.</param>
  /// <returns>True when the player is unobstructed.</returns>
  private bool HasLineOfSight(Vector3 eye, Vector3 direction, float distance) {
    if (obstacleMask.value == 0) {
      return true;
    }

    Vector3 origin = eye + direction * LineOfSightOriginOffset;
    var rayDistance = Mathf.Max(0f, distance - LineOfSightOriginOffset);
    var blocked = Physics.Raycast(origin, direction, out RaycastHit hit, rayDistance, obstacleMask);

    if (blocked && verboseLogging) {
      Debug.Log($"[VisionCone] LOS blocked by '{hit.collider?.name}' at {hit.distance:F2}m.");
    }

    return !blocked;
  }

  /// <summary>
  /// Advances or decays detection progress and sends stealth notifications on state transitions.
  /// </summary>
  /// <param name="visibilityStrength">Normalized detection strength. Near vision supplies one; far vision supplies light exposure.</param>
  /// <param name="candidate">The visible player, if any.</param>
  private void UpdateDetectionState(float visibilityStrength, PlayerStealthController candidate) {
    CurrentVisibilityStrength = Mathf.Clamp01(visibilityStrength);
    UpdateImmediateVisibility(visibilityStrength > 0f ? candidate : null);

    if (visibilityStrength > 0f && candidate != null) {
      _trackedPlayer = candidate;
      // Keep this as a bounded meter. Unbounded accumulation made the release delay depend on
      // how long the player had remained visible, rather than on an authored release time.
      _detectionProgress = Mathf.Min(
        Mathf.Max(0f, detectionTime),
        _detectionProgress + Time.deltaTime * Mathf.Clamp01(visibilityStrength));

      if (_detectionProgress >= detectionTime && !PlayerDetected) {
        PlayerDetected = true;
        DetectedPlayer = _trackedPlayer;
        _wasDetectedLastFrame = true;
        _trackedPlayer.OnGuardStartsDetecting();
        Debug.Log($"[VisionCone] '{name}' CONFIRMED detection of '{_trackedPlayer.name}'!");
      }

      return;
    }

    float releaseRate = detectionTime > 0f
      ? detectionTime / Mathf.Max(0.01f, detectionReleaseTime)
      : 1f / Mathf.Max(0.01f, detectionReleaseTime);
    _detectionProgress = Mathf.Max(0f, _detectionProgress - Time.deltaTime * releaseRate);

    if (_wasDetectedLastFrame && _detectionProgress <= 0f) {
      if (DetectedPlayer != null) {
        DetectedPlayer.OnGuardStopsDetecting();
      }

      PlayerDetected = false;
      DetectedPlayer = null;
      _wasDetectedLastFrame = false;
      Debug.Log($"[VisionCone] '{name}' lost the player.");
    }
  }

  private void OnDisable() {
    ReleaseDetection();
  }

  /// <summary>
  /// Immediately releases this guard's contribution to the player's detected state. Takedown and
  /// other lifecycle systems call this before disabling perception.
  /// </summary>
  public void ReleaseDetection() {
    UpdateImmediateVisibility(null);
    if (PlayerDetected && DetectedPlayer != null) DetectedPlayer.OnGuardStopsDetecting();
    _detectionProgress = 0f;
    PlayerDetected = false;
    DetectedPlayer = null;
    _trackedPlayer = null;
    _wasDetectedLastFrame = false;
    CurrentVisibilityStrength = 0f;
  }

  /// <summary>Runs the same authored eye/obstacle query used by detection for a specific player.</summary>
  public bool HasLineOfSightTo(PlayerStealthController player) {
    if (player == null) return false;
    Vector3 eye = EyeOrigin;
    Vector3 target = player.transform.position + Vector3.up * playerAimHeight;
    Vector3 delta = target - eye;
    float distance = delta.magnitude;
    return distance <= 0.0001f || HasLineOfSight(eye, delta / distance, distance);
  }

  /// <summary>
  /// Tests a thick wall-switch segment against the short cone and the light-conditional long cone.
  /// This is a pure query and therefore does not advance detection state.
  /// </summary>
  public bool TryGetWallSwitchIntersection(
    Vector3 start,
    Vector3 end,
    float trajectoryRadius,
    out Vector3 intersection) {
    Vector3 delta = end - start;
    float length = delta.magnitude;
    float spacing = Mathf.Clamp(Mathf.Max(trajectoryRadius * 0.5f, 0.05f), 0.05f, 0.2f);
    int steps = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
    Vector3 eye = EyeOrigin;

    for (int step = 0; step <= steps; step++) {
      Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
      Vector3 sightPoint = new(point.x, eye.y, point.z);
      Vector3 toPoint = sightPoint - eye;
      float sightDistance = toPoint.magnitude;
      if (sightDistance > 0.0001f && !HasLineOfSight(eye, toPoint / sightDistance, sightDistance)) continue;

      bool insideShort = IsInsideExpandedCone(point, shortRange, shortAngle, trajectoryRadius);
      bool insideLitLong = !insideShort
                           && FixedLightSource.EvaluateCombinedExposure(point) >= wallSwitchExposureThreshold
                           && IsInsideExpandedCone(point, longRange, longAngle, trajectoryRadius);
      if (!insideShort && !insideLitLong) continue;

      intersection = point;
      return true;
    }

    intersection = Vector3.zero;
    return false;
  }

  private bool IsInsideExpandedCone(Vector3 point, float range, float angle, float radius) {
    Vector3 toPoint = point - EyeOrigin;
    toPoint.y = 0f;
    float distance = toPoint.magnitude;
    if (distance > range + radius) return false;
    if (distance <= radius || distance <= 0.0001f) return true;

    Vector3 forward = transform.forward;
    forward.y = 0f;
    if (forward.sqrMagnitude <= 0.0001f) return false;
    float radiusAngle = Mathf.Asin(Mathf.Clamp01(radius / distance)) * Mathf.Rad2Deg;
    return Vector3.Angle(forward.normalized, toPoint / distance) <= angle * 0.5f + radiusAngle;
  }

  private void UpdateImmediateVisibility(PlayerStealthController visiblePlayer) {
    if (_visiblePlayer == visiblePlayer) return;
    if (_visiblePlayer != null) _visiblePlayer.OnGuardStopsSeeing();
    _visiblePlayer = visiblePlayer;
    if (_visiblePlayer != null) _visiblePlayer.OnGuardStartsSeeing();
  }

  private void OnDrawGizmos() {
    if (!showGizmos) {
      return;
    }

    Vector3 eye = EyeOrigin;

    Gizmos.color = Color.white;
    Gizmos.DrawWireSphere(eye, 0.08f);
    Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
    Gizmos.DrawLine(transform.position, eye);

    Vector3 flatForward = new(transform.forward.x, 0f, transform.forward.z);
    if (flatForward.sqrMagnitude < 0.0001f) {
      flatForward = Vector3.forward;
    } else {
      flatForward.Normalize();
    }

    DrawConeGizmo(eye, flatForward, shortRange, shortAngle, new Color(1f, 1f, 0f, 0.18f), Color.yellow);
    DrawConeGizmo(eye, flatForward, longRange, longAngle, new Color(1f, 0.2f, 0.2f, 0.10f), new Color(1f, 0.4f, 0.4f));
  }

  private void DrawConeGizmo(Vector3 origin, Vector3 flatForward, float range, float fovDegrees, Color fill, Color outline) {
    const int Segments = 28;
    var halfFov = fovDegrees * 0.5f;
    var step = fovDegrees / Segments;

    Gizmos.color = outline;
    Gizmos.DrawRay(origin, Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range);
    Gizmos.DrawRay(origin, Quaternion.AngleAxis(halfFov, Vector3.up) * flatForward * range);

    Vector3 previous = origin + Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range;
    for (var i = 1; i <= Segments; i++) {
      Vector3 next = origin + Quaternion.AngleAxis(-halfFov + step * i, Vector3.up) * flatForward * range;
      Gizmos.DrawLine(previous, next);
      previous = next;
    }

    Gizmos.color = fill;
    previous = origin + Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range;
    for (var i = 1; i <= Segments; i++) {
      Vector3 next = origin + Quaternion.AngleAxis(-halfFov + step * i, Vector3.up) * flatForward * range;
      Gizmos.DrawLine(origin, previous);
      Gizmos.DrawLine(previous, next);
      previous = next;
    }
  }
}
