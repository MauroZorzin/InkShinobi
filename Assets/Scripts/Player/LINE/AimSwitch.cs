using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Aiming and line-switch targeting. The camera is static — the aim ray is cast in world space
/// from the camera through the mouse's screen position, so where you point on screen is where you aim.
/// </summary>
[RequireComponent(typeof(LineSwitcher))]
[RequireComponent(typeof(LineFollowController))]
public class AimSwitch : MonoBehaviour {
  [Header("References")]
  [Tooltip("Camera the aim ray is cast from. Defaults to Camera.main if left empty.")]
  public Camera aimCamera;

  public LineSwitcher lineSwitcher;
  public LineFollowController followController;

  [Header("Aiming")]
  [Tooltip("Max distance along the camera's forward direction searched for a candidate line.")]
  public float maxAimDistance = 25f;

  [Tooltip("How far apart (world units) sample points are taken along the look direction. Smaller = more accurate, more expensive.")]
  public float aimSampleStep = 0.25f;

  [Tooltip("Max distance from the look direction a line may be and still count as aimed-at.")]
  public float aimRadius = 0.75f;

  [Header("Path Collision Check")]
  [Tooltip("If true, an aimed-at line is invalid (and a switch confirm denied) when something on Obstruction Layers blocks the straight-line path between the player and the target point.")]
  public bool requireClearPath = true;

  [Tooltip("Layers swept for obstructions when Require Clear Path is enabled. The player's own colliders are always ignored, regardless of their layer.")]
  public LayerMask obstructionLayers = ~0;

  [Tooltip("Radius of the sphere swept along the path. Kept well under the player's body radius on purpose — a fat sphere grazes the ground/line surface itself along a near-horizontal sweep and reports false obstructions.")]
  public float pathCheckRadius = 0.2f;

  [Header("Aim Point Indicator")]
  [Tooltip("Pointer moved to the current mouse-aimed world point every frame while aiming, shown/hidden with aiming itself.")]
  public Transform aimIndicator;

  [Tooltip("Positioned at the switch target and shown only while aiming at a valid, reachable target — a preview of where the player would end up.")]
  public Transform switchPreviewOutline;

  [Header("Aim Visibility")]
  [Tooltip("If true, hides the player's sprite while aiming (after Aim Disappear Delay), and shows it again immediately as soon as aiming ends (cancelled, or the moment a confirmed switch's move finishes).")]
  public bool hidePlayerWhileAiming = false;

  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

  [Tooltip("Seconds after aiming begins before the player's sprite actually disappears. 0 = instant.")]
  public float aimDisappearDelay = 0f;

  [Tooltip("Instantiated once, aimVanishParticleDelay seconds after the sprite actually disappears (i.e. after Aim Disappear Delay has also elapsed). Left unfired (and cancelled) if aiming ends before then.")]
  public ParticleSystem aimVanishParticlesPrefab;

  [Tooltip("Seconds between the player's sprite disappearing and Aim Vanish Particles Prefab spawning.")]
  public float aimVanishParticleDelay = 0.1f;

  [Tooltip("Redirects where the vanish particle spawns — an anchor point instead of the player's own position. Leave empty to spawn at the player.")]
  public Transform aimVanishParticleSpawnPoint;

  [Header("Debug")]
  public bool drawDebugGizmos = true;
  public bool logAimHits = true;

  private bool _isAiming;
  private bool _isSwitching;

  private bool _hasAimHit;
  private LinePath _aimLinePath;
  private int _aimStrand;
  private Vector3 _aimPoint;
  private float _aimDistance;
  private bool _aimValid;
  private Collider _aimBlockingCollider;

  private Coroutine _aimVanishParticleRoutine;

  public bool IsAiming => _isAiming;
  public Vector3 AimWorldPoint { get; private set; }

  /// <summary>Fired when aiming begins.</summary>
  public event Action AimStarted;

  /// <summary>Fired whenever aiming visually stops — either cancelled with no switch, or right after a switch's move finishes.</summary>
  public event Action AimEnded;

  /// <summary>Fired the instant a confirmed switch starts moving, with (player position before the move, aimed target point) — for systems that care what the switch's path crosses (e.g. TakedownController).</summary>
  public event Action<Vector3, Vector3> SwitchStarted;

  /// <summary>Fired when a switch's move finishes, before AimEnded.</summary>
  public event Action SwitchFinished;

  private void Awake() {
    if (lineSwitcher == null) lineSwitcher = GetComponent<LineSwitcher>();
    if (followController == null) followController = GetComponent<LineFollowController>();

    if (aimCamera == null) aimCamera = Camera.main;

    if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

    if (aimIndicator != null) aimIndicator.gameObject.SetActive(false);
    if (switchPreviewOutline != null) switchPreviewOutline.gameObject.SetActive(false);
  }

#pragma warning disable IDE0051
  private void OnVision(InputValue value) {
    if (value.isPressed) BeginAim();
    else EndAim();
  }

  private void OnConfirm(InputValue value) {
    if (value.isPressed) TryConfirmSwitch();
  }
#pragma warning restore IDE0051

  public void BeginAim() {
    if (!enabled || _isAiming || _isSwitching || lineSwitcher == null || lineSwitcher.IsSwitching
        || aimCamera == null) {
      return;
    }

    _isAiming = true;

    if (aimIndicator != null) aimIndicator.gameObject.SetActive(true);

    if (hidePlayerWhileAiming && spriteRenderer != null) {
      if (_aimVanishParticleRoutine != null) StopCoroutine(_aimVanishParticleRoutine);
      _aimVanishParticleRoutine = StartCoroutine(AimVanishRoutine());
    }

    AimStarted?.Invoke();
  }

  public void EndAim() {
    if (!_isAiming || _isSwitching) {
      return;
    }

    _isAiming = false;
    _hasAimHit = false;
    _aimValid = false;

    if (aimIndicator != null) aimIndicator.gameObject.SetActive(false);
    if (switchPreviewOutline != null) switchPreviewOutline.gameObject.SetActive(false);

    if (hidePlayerWhileAiming && spriteRenderer != null) {
      spriteRenderer.enabled = true;
      if (_aimVanishParticleRoutine != null) {
        StopCoroutine(_aimVanishParticleRoutine);
        _aimVanishParticleRoutine = null;
      }
    }

    AimEnded?.Invoke();
  }

  private IEnumerator AimVanishRoutine() {
    if (aimDisappearDelay > 0f) yield return new WaitForSeconds(aimDisappearDelay);

    spriteRenderer.enabled = false;

    if (aimVanishParticleDelay > 0f) yield return new WaitForSeconds(aimVanishParticleDelay);

    if (aimVanishParticlesPrefab != null) {
      Vector3 spawnPos = aimVanishParticleSpawnPoint != null ? aimVanishParticleSpawnPoint.position : transform.position;
      OneShotVfx.PlayAtPoint(aimVanishParticlesPrefab, spawnPos);
    }

    _aimVanishParticleRoutine = null;
  }

  private Vector3 GetHuggedTarget(Vector3 targetPoint) {
    float hugHeight = followController != null ? followController.heightAboveLine : 0f;
    return targetPoint + Vector3.up * Mathf.Max(0f, hugHeight);
  }

  public bool IsPathClear(Vector3 from, Vector3 to, out Collider blockingCollider) {
    blockingCollider = null;
    if (!requireClearPath) return true;

    Vector3 delta = to - from;
    float distance = delta.magnitude;
    if (distance <= 0f) return true;

    var hits = Physics.SphereCastAll(from, Mathf.Max(0.01f, pathCheckRadius), delta / distance, distance, obstructionLayers, QueryTriggerInteraction.Ignore);
    foreach (var hit in hits) {
      if (hit.collider != null && hit.collider.transform.IsChildOf(transform)) continue;
      blockingCollider = hit.collider;
      return false;
    }
    return true;
  }

  public bool IsSwitchPathClear(Vector3 targetPoint, out Collider blockingCollider) {
    Vector3 from = followController != null ? followController.transform.position : transform.position;
    return IsPathClear(from, GetHuggedTarget(targetPoint), out blockingCollider);
  }

  public bool TryConfirmSwitch() {
    if (lineSwitcher == null || !_isAiming || !_aimValid) {
      if (logAimHits) {
        string reason = !_isAiming ? "not aiming"
          : (_hasAimHit ? $"path obstructed by '{(_aimBlockingCollider != null ? _aimBlockingCollider.name : "unknown")}'" : "no valid target");
        Debug.Log($"[AimSwitch] Confirm denied: {reason}.");
      }
      return false;
    }

    Vector3 fromPosition = transform.position;
    _isSwitching = true;

    var started = lineSwitcher.TrySwitchToLine(_aimLinePath, _aimStrand, _aimPoint, _aimDistance, OnSwitchMoveComplete);
    if (!started) {
      _isSwitching = false;
      return false;
    }

    SwitchStarted?.Invoke(fromPosition, _aimPoint);
    return true;
  }

  private void Update() {
    if (_isAiming && !_isSwitching) {
      UpdateAim();
    }
  }

  private void UpdateAim() {
    if (aimCamera == null || Mouse.current == null) return;

    Vector3 origin = followController != null ? followController.transform.position : transform.position;
    Ray mouseRay = aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

    float playerDepth = Mathf.Max(0f, Vector3.Dot(origin - mouseRay.origin, mouseRay.direction));
    Vector3 pastPlayerOrigin = mouseRay.origin + mouseRay.direction * playerDepth;
    float remainingDistance = Mathf.Max(0f, maxAimDistance - playerDepth);

    Vector3 direction;
    if (remainingDistance > 0f && Physics.Raycast(pastPlayerOrigin, mouseRay.direction, out RaycastHit mouseHit, remainingDistance, obstructionLayers, QueryTriggerInteraction.Ignore)
        && !mouseHit.collider.transform.IsChildOf(transform)) {
      Vector3 toHit = mouseHit.point - origin;
      direction = toHit.sqrMagnitude > 0.0001f ? toHit.normalized : mouseRay.direction;
    } else {
      direction = mouseRay.direction;
    }

    var wasValid = _aimValid;
    _hasAimHit = false;
    _aimValid = false;
    float bestDist = float.MaxValue;

    LinePath bestLine = null;
    int bestStrand = -1;
    Vector3 bestPoint = Vector3.zero;
    float bestDistAlong = 0f;

    float searchDistance = maxAimDistance;
    if (requireClearPath && Physics.Raycast(origin, direction, out RaycastHit obstructionHit, maxAimDistance, obstructionLayers, QueryTriggerInteraction.Ignore)
        && !obstructionHit.collider.transform.IsChildOf(transform)) {
      searchDistance = obstructionHit.distance;
    }

    int sampleCount = Mathf.Max(4, Mathf.CeilToInt(searchDistance / Mathf.Max(0.05f, aimSampleStep)));
    for (int s = 1; s <= sampleCount; s++) {
      Vector3 samplePos = origin + direction * (s * aimSampleStep);

      foreach (var line in LinePath.All) {
        if (line == null || !line.isActiveAndEnabled) continue;

        var distAlong = line.FindClosestDistance(samplePos, out Vector3 cp, out float distToLine, out int strand);
        if (strand < 0 || distToLine > aimRadius || distToLine >= bestDist) continue;
        if (!lineSwitcher.IsValidSwitchTarget(line, strand)) continue;

        bestDist = distToLine;
        bestLine = line;
        bestStrand = strand;
        bestPoint = cp;
        bestDistAlong = distAlong;
      }
    }

    _hasAimHit = bestLine != null;
    _aimBlockingCollider = null;
    if (_hasAimHit) {
      _aimLinePath = bestLine;
      _aimStrand = bestStrand;
      _aimPoint = bestPoint;
      _aimDistance = bestDistAlong;
      _aimValid = IsSwitchPathClear(bestPoint, out _aimBlockingCollider);
    }

    Vector3 aimEndPoint = _hasAimHit ? _aimPoint : origin + direction * searchDistance;
    AimWorldPoint = aimEndPoint;

    if (aimIndicator != null) {
      aimIndicator.gameObject.SetActive(!_aimValid);
      if (!_aimValid) aimIndicator.position = aimEndPoint;
    }

    if (switchPreviewOutline != null) {
      switchPreviewOutline.gameObject.SetActive(_aimValid);
      if (_aimValid) switchPreviewOutline.position = GetHuggedTarget(_aimPoint);
    }

    if (logAimHits && _aimValid != wasValid) {
      if (_aimValid) {
        Debug.Log($"[AimSwitch] Aiming at '{_aimLinePath.name}' strand={_aimStrand} point={_aimPoint:F2}");
      } else if (_hasAimHit) {
        Debug.Log($"[AimSwitch] Aiming at '{_aimLinePath.name}' strand={_aimStrand} but path is obstructed by '{(_aimBlockingCollider != null ? _aimBlockingCollider.name : "unknown")}'.");
      } else {
        Debug.Log("[AimSwitch] No valid line within aimRadius.");
      }
    }
  }

  private void OnSwitchMoveComplete() {
    _isSwitching = false;
    SwitchFinished?.Invoke();
    EndAim();
  }

#if UNITY_EDITOR
  private void OnDrawGizmos() {
    if (!drawDebugGizmos || !_hasAimHit) return;
    Gizmos.color = _aimValid ? Color.green : Color.red;
    Gizmos.DrawSphere(_aimPoint, 0.05f);
  }
#endif
}
