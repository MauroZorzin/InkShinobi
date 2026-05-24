using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TakedownController : MonoBehaviour, ITakedownSystem {

  private const string TAKEDOWN_ANIMATION_PARAMETER = "Takedown";
  private Animator _animator;
  private float _takedownAnimationTimer = 0f;

  // -------------------------------------------------------------------------
  // Inspector
  // -------------------------------------------------------------------------

  [Header("Settings")]
  public bool enabledAtStart = true;
  public float takedownRange = 1.5f;
  public float takedownAngle = 60f;
  public LayerMask guardLayerMask;

  [Header("Animation")]
  [Tooltip("If enabled, play a takedown animation on the player")]
  public bool playTakedownAnimation = false;
  [Tooltip("If true, player can move during takedown animation. If false, player is locked in place.")]
  public bool canMoveWhileTakingDown = false;

  [Header("Debug")]
  public bool verboseLogging = false;

  // -------------------------------------------------------------------------
  // ITakedownSystem
  // -------------------------------------------------------------------------

  public bool IsEnabled { get; set; }
  public float TakedownRange { get; set; }
  public float TakedownAngle { get; set; }
  public LayerMask GuardLayerMask { get; set; }

  /// <summary>Indicates if a takedown is currently in progress (for movement control).</summary>
  public bool IsTakingDown { get; private set; } = false;

  public IReadOnlyList<GuardController> GetCandidates() {
    List<GuardController> result = new();
    if (guardLayerMask.value == 0) return result;

    Collider[] hits = Physics.OverlapSphere(transform.position, Mathf.Max(0.01f, takedownRange),
                                            guardLayerMask, QueryTriggerInteraction.Collide);
    foreach (Collider hit in hits) {
      if (hit == null) continue;
      GuardController guard = hit.GetComponentInParent<GuardController>();
      if (guard == null || guard.CurrentState == GuardController.GuardState.TakenDown) continue;
      if (!IsBehindGuard(transform.position, guard)) continue;
      result.Add(guard);
    }
    return result;
  }

  // -------------------------------------------------------------------------
  // Outline
  // -------------------------------------------------------------------------

  static readonly int ID_OutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
  const string SHADER_NAME = "Sprites/Outline2";

  readonly HashSet<GuardController> _outlined = new();
  MaterialPropertyBlock _block;

  void SetGuardOutline(GuardController guard, bool on) {
    SpriteRenderer sr = guard.GetComponentInChildren<SpriteRenderer>();
    if (sr == null || sr.sharedMaterial == null) return;
    if (sr.sharedMaterial.shader.name != SHADER_NAME) return;

    sr.GetPropertyBlock(_block);
    _block.SetFloat(ID_OutlineEnabled, on ? 1f : 0f);
    sr.SetPropertyBlock(_block);
  }

  void UpdateOutlines() {
    IReadOnlyList<GuardController> candidates = GetCandidates();
    HashSet<GuardController> candidateSet = new(candidates);

    foreach (GuardController guard in candidates) {
      if (_outlined.Add(guard))        // Add returns true if it was not already in the set
        SetGuardOutline(guard, true);
    }

    _outlined.RemoveWhere(guard => {
      if (candidateSet.Contains(guard)) return false;
      if (guard != null) SetGuardOutline(guard, false);
      return true;
    });
  }

  void ClearAllOutlines() {
    foreach (GuardController guard in _outlined)
      if (guard != null) SetGuardOutline(guard, false);
    _outlined.Clear();
  }

  // -------------------------------------------------------------------------
  // Unity lifecycle
  // -------------------------------------------------------------------------

  void Awake() {
    _block = new MaterialPropertyBlock();
    IsEnabled = enabledAtStart;
    _animator = GetComponent<Animator>();
  }

  void Update() {
    if (IsEnabled) UpdateOutlines();
    else ClearAllOutlines();
  }

  // -------------------------------------------------------------------------
  // Input
  // -------------------------------------------------------------------------

  public void OnTakedown(InputValue value) {
    if (value.isPressed) TryTakedown();
  }

  // -------------------------------------------------------------------------
  // Takedown execution
  // -------------------------------------------------------------------------

  public void TryTakedown() {
    if (!IsEnabled) {
      if (verboseLogging) Debug.Log("[Takedown] Blocked — IsEnabled is false.");
      return;
    }
    if (guardLayerMask.value == 0) {
      Debug.LogWarning("[Takedown] guardLayerMask is Nothing — assign the guard layer in the Inspector.");
      return;
    }

    IReadOnlyList<GuardController> candidates = GetCandidates();
    if (verboseLogging) Debug.Log($"[Takedown] {candidates.Count} valid candidate(s).");
    if (candidates.Count == 0) return;

    GuardController best = null;
    float bestDist = float.MaxValue;
    foreach (GuardController guard in candidates) {
      float dist = Vector3.Distance(transform.position, guard.transform.position);
      if (dist < bestDist) { bestDist = dist; best = guard; }
    }

    SetGuardOutline(best!, false);
    _outlined.Remove(best);

    // Play takedown animation if enabled
    if (playTakedownAnimation) {
      PlayTakedownAnimation();
    }

    // Lock movement if canMoveWhileTakingDown is false
    if (!canMoveWhileTakingDown) {
      IsTakingDown = true;
    }

    best!.PerformTakedown();
    if (verboseLogging) Debug.Log($"[Takedown] SUCCESS on '{best.name}'.");
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  private void PlayTakedownAnimation() {
    if (_animator == null) {
      _animator = GetComponent<Animator>();
      if (_animator == null) {
        Debug.LogWarning("[Takedown] No Animator found on player. Animation will not play.");
        return;
      }
    }

    // Set the parameter to true to trigger animation
    _animator.SetTrigger(TAKEDOWN_ANIMATION_PARAMETER);

    if (verboseLogging) Debug.Log($"[Takedown] Animation triggered: {TAKEDOWN_ANIMATION_PARAMETER} (duration: {_takedownAnimationTimer}s)");
  }


  public bool IsBehindGuard(Vector3 playerPosition, GuardController guard) {
    Vector3 toPlayerFlat = new(playerPosition.x - guard.transform.position.x, 0f,
                                playerPosition.z - guard.transform.position.z);
    if (toPlayerFlat.sqrMagnitude < 0.0001f) return false;

    Vector3 guardFwdFlat = new(guard.transform.forward.x, 0f, guard.transform.forward.z);
    if (guardFwdFlat.sqrMagnitude < 0.0001f) return false;

    float angle = Vector3.Angle(guardFwdFlat.normalized, toPlayerFlat.normalized);
    float minAngle = 180f - Mathf.Max(0f, takedownAngle) * 0.5f;
    return angle >= minAngle;
  }

  void OnDrawGizmosSelected() {
    Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
    Gizmos.DrawWireSphere(transform.position, takedownRange);
  }
}