using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Guard-side contract for wall-switch preview, eligibility, immediate gameplay shutdown, and
/// the airborne ink dissolve. Milestone 6 can replace the prototype eligibility rule without
/// changing the wall-switch evaluator.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuardWallSwitchTarget : MonoBehaviour {
  private static readonly HashSet<GuardWallSwitchTarget> ActiveTargetSet = new();
  private static readonly int HighlightColorId = Shader.PropertyToID("_PreviewHighlightColor");
  private static readonly int HighlightStrengthId = Shader.PropertyToID("_PreviewHighlightStrength");
  private static readonly int DissolveId = Shader.PropertyToID("_InkDissolve");
  private static readonly int DissolveUvRectId = Shader.PropertyToID("_InkDissolveUvRect");
  private static readonly int DissolveEdgeColorId = Shader.PropertyToID("_InkDissolveEdgeColor");
  private static readonly int DissolveEdgeWidthId = Shader.PropertyToID("_InkDissolveEdgeWidth");

  [Header("Eligibility")]
  [Tooltip("A suspicious guard is aware enough to block wall switching and cannot be taken down by it.")]
  [SerializeField] private bool suspiciousBlocksSwitch = true;
  [Tooltip("An investigating guard is aware enough to block wall switching and cannot be taken down by it.")]
  [SerializeField] private bool investigatingBlocksSwitch = true;

  [Header("Presentation")]
  [Tooltip("Sprite renderers receiving whole-sprite preview and dissolve properties. Local children are used if empty.")]
  [SerializeField] private SpriteRenderer[] spriteRenderers = System.Array.Empty<SpriteRenderer>();
  [SerializeField] private Color vulnerableHighlight = new(0.9f, 0.02f, 0.015f, 0.72f);
  [SerializeField] private Color blockingHighlight = new(0.015f, 0.01f, 0.01f, 0.9f);
  [SerializeField, Range(0f, 1f)] private float highlightStrength = 0.72f;

  [Header("Death")]
  [Tooltip("Brief readable impact pose before the sprite starts dissolving.")]
  [SerializeField, Min(0f)] private float dissolveStartDelay = 0.12f;
  [SerializeField, Min(0.05f)] private float dissolveDuration = 1.1f;
  [SerializeField] private Color dissolveEdgeColor = new(0.015f, 0.01f, 0.01f, 1f);
  [SerializeField, Range(0.01f, 0.3f)] private float dissolveEdgeWidth = 0.12f;
  [SerializeField] private GameObject airborneInkPrefab;
  [SerializeField] private Vector3 airborneInkOffset = new(0f, 0.25f, 0f);
  [Tooltip("Scale multiplier applied to the airborne ink effect spawned by this death.")]
  [SerializeField, Min(0.1f)] private float airborneInkScale = 1.5f;

  private MaterialPropertyBlock propertyBlock;
  private bool dying;

  public bool IsAlive => !dying && gameObject.activeInHierarchy;
  public static IReadOnlyCollection<GuardWallSwitchTarget> ActiveTargets => ActiveTargetSet;

  private void Awake() {
    ResolveRenderers();
  }

  private void OnEnable() {
    ActiveTargetSet.Add(this);
  }

  private void OnDisable() {
    ActiveTargetSet.Remove(this);
    if (!dying) SetPreview(WallSwitchTargetDisposition.Ignored);
  }

  private void OnDestroy() {
    ActiveTargetSet.Remove(this);
  }

  public WallSwitchTargetDisposition EvaluateDisposition() {
    if (!IsAlive) return WallSwitchTargetDisposition.Ignored;

    GuardController controller = GetComponent<GuardController>();
    GuardVisionCone[] visions = GetComponentsInChildren<GuardVisionCone>(true);
    bool currentlySeeingPlayer = false;
    for (int i = 0; i < visions.Length; i++) {
      if (visions[i] != null && (visions[i].PlayerCurrentlyVisible || visions[i].PlayerDetected)) {
        currentlySeeingPlayer = true;
        break;
      }
    }

    bool aware = controller != null && controller.CurrentState switch {
      GuardController.GuardState.Alerted => true,
      GuardController.GuardState.Suspicious => suspiciousBlocksSwitch,
      GuardController.GuardState.Investigating => investigatingBlocksSwitch,
      _ => false
    };
    return aware || currentlySeeingPlayer
      ? WallSwitchTargetDisposition.Blocking
      : WallSwitchTargetDisposition.Vulnerable;
  }

  public bool TryGetBlockingVisionIntersection(
    Vector3 start,
    Vector3 end,
    float trajectoryRadius,
    out Vector3 intersection) {
    intersection = Vector3.zero;
    if (EvaluateDisposition() != WallSwitchTargetDisposition.Blocking) return false;

    GuardVisionCone[] visions = GetComponentsInChildren<GuardVisionCone>(true);
    for (int i = 0; i < visions.Length; i++) {
      GuardVisionCone vision = visions[i];
      if (vision != null && vision.isActiveAndEnabled
          && vision.TryGetWallSwitchIntersection(start, end, trajectoryRadius, out intersection))
        return true;
    }
    return false;
  }

  public void SetPreview(WallSwitchTargetDisposition disposition) {
    if (dying) return;
    ResolveRenderers();

    Color color = disposition == WallSwitchTargetDisposition.Blocking
      ? blockingHighlight
      : vulnerableHighlight;
    float strength = disposition == WallSwitchTargetDisposition.Ignored ? 0f : highlightStrength;
    ApplyProperties(color, strength, 0f);
  }

  public void BeginTakedown(Vector3 trajectoryDirection) {
    if (dying) return;
    dying = true;
    GuardController controller = GetComponent<GuardController>();
    if (controller != null) controller.PlayTakedownAudio();
    ShutDownGameplay();
    StartCoroutine(DissolveRoutine(trajectoryDirection));
  }

  public float GetTrajectoryProgress(Vector3 start, Vector3 end) {
    Vector3 delta = end - start;
    float lengthSquared = delta.sqrMagnitude;
    return lengthSquared > 0.0001f
      ? Mathf.Clamp01(Vector3.Dot(transform.position - start, delta) / lengthSquared)
      : 0f;
  }

  private void ShutDownGameplay() {
    GuardSquarePatrol squarePatrol = GetComponent<GuardSquarePatrol>();
    if (squarePatrol != null) squarePatrol.enabled = false;

    GuardController controller = GetComponent<GuardController>();
    if (controller != null) controller.enabled = false;

    GuardVisionCone[] visions = GetComponentsInChildren<GuardVisionCone>(true);
    for (int i = 0; i < visions.Length; i++) {
      visions[i].ReleaseDetection();
      visions[i].enabled = false;
    }

    GuardVisionLightRig[] rigs = GetComponentsInChildren<GuardVisionLightRig>(true);
    for (int i = 0; i < rigs.Length; i++) rigs[i].enabled = false;

    PalaceConeLightSource[] fields = GetComponentsInChildren<PalaceConeLightSource>(true);
    for (int i = 0; i < fields.Length; i++) fields[i].enabled = false;

    NavMeshAgent agent = GetComponent<NavMeshAgent>();
    if (agent != null && agent.enabled) {
      if (agent.isOnNavMesh) agent.isStopped = true;
      agent.enabled = false;
    }

    Collider[] colliders = GetComponentsInChildren<Collider>(true);
    for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
  }

  private IEnumerator DissolveRoutine(Vector3 trajectoryDirection) {
    ResolveRenderers();
    SpawnAirborneInk(trajectoryDirection);

    ApplyProperties(vulnerableHighlight, highlightStrength, 0f);
    float holdElapsed = 0f;
    while (holdElapsed < dissolveStartDelay) {
      if (!SceneTransitionManager.IsGamePaused) holdElapsed += Time.unscaledDeltaTime;
      yield return null;
    }

    float elapsed = 0f;
    while (elapsed < dissolveDuration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }

      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(elapsed / dissolveDuration);
      ApplyProperties(vulnerableHighlight, Mathf.Lerp(highlightStrength, 0f, t), t);
      yield return null;
    }

    Destroy(gameObject);
  }

  private void SpawnAirborneInk(Vector3 trajectoryDirection) {
    if (airborneInkPrefab == null) return;

    Vector3 direction = trajectoryDirection.sqrMagnitude > 0.0001f
      ? trajectoryDirection.normalized
      : transform.forward;
    Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
    GameObject instance = Instantiate(airborneInkPrefab, transform.position + airborneInkOffset, rotation);
    instance.transform.localScale *= airborneInkScale;
    PauseAwareUnscaledParticles.Configure(instance);
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    float lifetime = 1f;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      main.useUnscaledTime = true;
      lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax);
      particles[i].Play(true);
    }
    Destroy(instance, lifetime);
  }

  private void ApplyProperties(Color color, float strength, float dissolve) {
    propertyBlock ??= new MaterialPropertyBlock();
    for (int i = 0; i < spriteRenderers.Length; i++) {
      SpriteRenderer renderer = spriteRenderers[i];
      if (renderer == null) continue;
      renderer.GetPropertyBlock(propertyBlock);
      propertyBlock.SetColor(HighlightColorId, color);
      propertyBlock.SetFloat(HighlightStrengthId, strength);
      propertyBlock.SetFloat(DissolveId, dissolve);
      propertyBlock.SetColor(DissolveEdgeColorId, dissolveEdgeColor);
      propertyBlock.SetFloat(DissolveEdgeWidthId, dissolveEdgeWidth);
      propertyBlock.SetVector(DissolveUvRectId, GetSpriteUvRect(renderer));
      renderer.SetPropertyBlock(propertyBlock);
    }
  }

  private static Vector4 GetSpriteUvRect(SpriteRenderer renderer) {
    Sprite sprite = renderer != null ? renderer.sprite : null;
    Texture texture = sprite != null ? sprite.texture : null;
    if (sprite == null || texture == null || texture.width <= 0 || texture.height <= 0)
      return new Vector4(0f, 0f, 1f, 1f);

    Rect rect = sprite.rect;
    return new Vector4(
      rect.x / texture.width,
      rect.y / texture.height,
      Mathf.Max(rect.width / texture.width, 0.0001f),
      Mathf.Max(rect.height / texture.height, 0.0001f));
  }

  private void ResolveRenderers() {
    if (spriteRenderers == null || spriteRenderers.Length == 0)
      spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
  }
}
