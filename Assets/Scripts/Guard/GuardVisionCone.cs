using UnityEngine;

/// <summary>
/// Guard vision system with TWO cones:
///   • Short cone  – always active (shadows / dark areas)
///   • Long cone   – active only when the player is in light
///
/// Angle check is done on the HORIZONTAL plane only, so eye/player height
/// differences never inflate the measured angle and break detection.
/// </summary>
public class GuardVisionCone : MonoBehaviour {
  // ── Inspector ─────────────────────────────────────────────────────────────
  [Header("Eye Level")]
  [Tooltip("World-space Y added to the guard's pivot as the ray origin (eye height).")]
  public float eyeHeight = 1.6f;

  [Tooltip("World-space Y added to the player's pivot as the aim target (chest/eye height).")]
  public float playerAimHeight = 1.4f;

  [Header("Short Cone  (always active)")]
  [Range(1f, 30f)] public float shortRange = 5f;
  [Range(1f, 180f)] public float shortAngle = 60f;

  [Header("Long Cone  (active when player is in light)")]
  [Range(5f, 60f)] public float longRange = 15f;
  [Range(1f, 180f)] public float longAngle = 40f;

  [Header("Detection")]
  [Tooltip("Layer(s) the player lives on. MUST NOT be Nothing.")]
  public LayerMask playerLayerMask;
  [Tooltip("Layer(s) that block line-of-sight. Leave Nothing to skip obstacle checks.")]
  public LayerMask obstacleMask;
  [Tooltip("Seconds to fully detect the player (0 = instant).")]
  public float detectionTime = 0.8f;

  [Header("Debug")]
  public bool showGizmos = true;
  [Tooltip("Prints per-frame scan details to the Console.")]
  public bool verboseLogging = false;
  [Tooltip("Draws runtime rays in the Scene/Game view.")]
  public bool showRuntimeRay = true;

  // ── Public state ──────────────────────────────────────────────────────────
  public bool PlayerDetected { get; private set; } = false;
  public PlayerStealthController DetectedPlayer { get; private set; }
  public float DetectionProgress => detectionTime > 0f
      ? Mathf.Clamp01(_detectionProgress / detectionTime)
      : (PlayerDetected ? 1f : 0f);

  // ── Private ───────────────────────────────────────────────────────────────
  private float _detectionProgress = 0f;
  private bool _wasDetectedLastFrame = false;
  private PlayerStealthController _trackedPlayer;

  // Eye origin: guard pivot + world-up offset only
  private Vector3 EyeOrigin => new Vector3(
      transform.position.x,
      transform.position.y + eyeHeight,
      transform.position.z);

  // ── Unity Messages ────────────────────────────────────────────────────────
  private void Start() {
    if (playerLayerMask.value == 0)
      Debug.LogWarning($"[VisionCone] '{name}': playerLayerMask is Nothing — guard will never detect anyone!", this);
  }

  private void Update() => ScanForPlayer();

  // ── Core Scan ─────────────────────────────────────────────────────────────
  private void ScanForPlayer() {
    Vector3 eye = EyeOrigin;

    // Orange ray = guard's forward direction (always visible)
    if (showRuntimeRay)
      Debug.DrawRay(eye, transform.forward * shortRange, new Color(1f, 0.55f, 0f));

    Collider[] hits = Physics.OverlapSphere(eye, longRange, playerLayerMask);

    if (verboseLogging)
      Debug.Log($"[VisionCone] '{name}' | eye={eye:F2} | OverlapSphere(r={longRange}, mask={playerLayerMask.value}) → {hits.Length} hit(s)");

    bool playerVisible = false;
    PlayerStealthController candidate = null;

    foreach (Collider col in hits) {
      PlayerStealthController psm = col.GetComponentInParent<PlayerStealthController>();
      if (psm == null) {
        if (verboseLogging)
          Debug.Log($"[VisionCone]   '{col.name}' skipped — no PlayerStealthController in parents.");
        continue;
      }

      // Aim point on the player (at chest height)
      Vector3 aimPos = new Vector3(
          psm.transform.position.x,
          psm.transform.position.y + playerAimHeight,
          psm.transform.position.z);

      // ── Horizontal angle (flat, Y-ignored) ───────────────────────────
      // Flatten both vectors to Y=0 so height difference never inflates the angle.
      Vector3 toPlayerFlat = new Vector3(aimPos.x - eye.x, 0f, aimPos.z - eye.z).normalized;
      Vector3 guardFwdFlat = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
      float horizAngle = Vector3.Angle(guardFwdFlat, toPlayerFlat);

      // True 3D distance (for range check) and direction (for LOS raycast)
      float dist3D = Vector3.Distance(eye, aimPos);
      Vector3 dir3D = (aimPos - eye).normalized;

      // LOS: offset ray start by 0.3 m so the guard's own collider can't block it.
      // The verbose log names exactly what blocks LOS so you can fix the obstacleMask.
      float losSkip = 0.3f;
      Vector3 losOrigin = eye + dir3D * losSkip;
      float losDist = Mathf.Max(0f, dist3D - losSkip);
      bool hasLOS;
      if (obstacleMask.value == 0) {
        hasLOS = true;
      } else {
        hasLOS = !Physics.Raycast(losOrigin, dir3D, losDist, obstacleMask);
        if (!hasLOS && verboseLogging) {
          Physics.Raycast(losOrigin, dir3D, out RaycastHit losHit, losDist, obstacleMask);
          Debug.Log($"[VisionCone]   LOS BLOCKED by '{losHit.collider?.name}' " +
                    $"(layer='{LayerMask.LayerToName(losHit.collider?.gameObject.layer ?? 0)}') " +
                    $"at {losHit.distance:F2}m — if this is the guard, remove its layer from obstacleMask.");
        }
      }

      bool inShort = dist3D <= shortRange && horizAngle <= shortAngle * 0.5f;
      bool inLong = dist3D <= longRange && horizAngle <= longAngle * 0.5f && psm.IsInLight;
      bool inCone = (inShort || inLong) && hasLOS;

      if (verboseLogging)
        Debug.Log($"[VisionCone]   '{psm.name}' | dist={dist3D:F2}m horizAngle={horizAngle:F1}° " +
                  $"| inShort={inShort}(range≤{shortRange}, angle≤{shortAngle * 0.5f:F0}°) " +
                  $"| inLong={inLong}(range≤{longRange}, angle≤{longAngle * 0.5f:F0}°, lit={psm.IsInLight}) " +
                  $"| LOS={hasLOS} → RESULT={inCone}");

      if (showRuntimeRay)
        Debug.DrawLine(eye, aimPos, inCone ? Color.green : Color.red);

      if (inCone) {
        playerVisible = true;
        candidate = psm;
        break;
      }
    }

    // ── Detection build-up / decay ────────────────────────────────────────
    if (playerVisible) {
      _trackedPlayer = candidate;
      _detectionProgress += Time.deltaTime;

      if (_detectionProgress >= detectionTime && !PlayerDetected) {
        PlayerDetected = true;
        DetectedPlayer = _trackedPlayer;
        _wasDetectedLastFrame = true;
        _trackedPlayer.OnGuardStartsDetecting();
        Debug.Log($"[VisionCone] '{name}' CONFIRMED detection of '{_trackedPlayer.name}'!");
      }
    } else {
      _detectionProgress = Mathf.Max(0f, _detectionProgress - Time.deltaTime * 2f);

      if (_wasDetectedLastFrame && _detectionProgress <= 0f) {
        if (DetectedPlayer != null)
          DetectedPlayer.OnGuardStopsDetecting();

        PlayerDetected = false;
        DetectedPlayer = null;
        _wasDetectedLastFrame = false;
        Debug.Log($"[VisionCone] '{name}' lost the player.");
      }
    }
  }

  // ── Gizmos ────────────────────────────────────────────────────────────────
  private void OnDrawGizmos() {
    if (!showGizmos) return;

    Vector3 eye = EyeOrigin;

    // White sphere at eye origin — verify it's at head height in Scene view
    Gizmos.color = Color.white;
    Gizmos.DrawWireSphere(eye, 0.08f);
    Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
    Gizmos.DrawLine(transform.position, eye);

    // Use a flattened forward for the cone so gizmo matches the horizontal angle check
    Vector3 flatFwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

    DrawConeGizmo(eye, flatFwd, shortRange, shortAngle, new Color(1f, 1f, 0f, 0.18f), Color.yellow);
    DrawConeGizmo(eye, flatFwd, longRange, longAngle, new Color(1f, 0.2f, 0.2f, 0.10f), new Color(1f, 0.4f, 0.4f));
  }

  private void DrawConeGizmo(Vector3 origin, Vector3 flatForward, float range, float fovDegrees, Color fill, Color outline) {
    int segments = 28;
    float halfFov = fovDegrees * 0.5f;
    float step = fovDegrees / segments;

    Gizmos.color = outline;
    Gizmos.DrawRay(origin, Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range);
    Gizmos.DrawRay(origin, Quaternion.AngleAxis(halfFov, Vector3.up) * flatForward * range);

    // Horizontal arc
    Vector3 prev = origin + Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range;
    for (int i = 1; i <= segments; i++) {
      Vector3 next = origin + Quaternion.AngleAxis(-halfFov + step * i, Vector3.up) * flatForward * range;
      Gizmos.DrawLine(prev, next);
      prev = next;
    }

    // Filled wedge
    Gizmos.color = fill;
    prev = origin + Quaternion.AngleAxis(-halfFov, Vector3.up) * flatForward * range;
    for (int i = 1; i <= segments; i++) {
      Vector3 next = origin + Quaternion.AngleAxis(-halfFov + step * i, Vector3.up) * flatForward * range;
      Gizmos.DrawLine(origin, prev);
      Gizmos.DrawLine(prev, next);
      prev = next;
    }
  }
}