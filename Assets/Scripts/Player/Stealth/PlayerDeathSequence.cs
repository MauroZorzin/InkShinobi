using System.Collections;
using UnityEngine;

/// <summary>Coordinates the player's terminal ink dissolve and hands recovery to the shared modal.</summary>
[DisallowMultipleComponent]
public sealed class PlayerDeathSequence : MonoBehaviour {
  private static readonly int HighlightColorId = Shader.PropertyToID("_PreviewHighlightColor");
  private static readonly int HighlightStrengthId = Shader.PropertyToID("_PreviewHighlightStrength");
  private static readonly int DissolveId = Shader.PropertyToID("_InkDissolve");
  private static readonly int DissolveUvRectId = Shader.PropertyToID("_InkDissolveUvRect");
  private static readonly int DissolveEdgeColorId = Shader.PropertyToID("_InkDissolveEdgeColor");
  private static readonly int DissolveEdgeWidthId = Shader.PropertyToID("_InkDissolveEdgeWidth");

  [Header("Timing")]
  [Tooltip("Number of authored animation frames between a lethal hit request and the time freeze/death presentation. Zero starts immediately.")]
  [SerializeField, Min(0)] private int startDelayFrames = 4;
  [Tooltip("Frame rate used to convert Start Delay Frames into animation time. Guard attack clips currently use 24 FPS.")]
  [SerializeField, Min(1)] private int delayFrameRate = 24;

  [Header("Presentation")]
  [SerializeField] private SpriteRenderer[] spriteRenderers = System.Array.Empty<SpriteRenderer>();
  [SerializeField] private GameObject inkExplosionPrefab;
  [SerializeField] private Vector3 inkExplosionOffset = new(0f, 0.25f, 0f);
  [SerializeField, Min(0.1f)] private float inkExplosionScale = 1.8f;
  [SerializeField, Min(0f)] private float impactHold = 0.12f;
  [SerializeField, Min(0.05f)] private float dissolveDuration = 1.05f;
  [SerializeField] private Color impactColor = new(0.01f, 0.005f, 0.005f, 1f);
  [SerializeField] private Color dissolveEdgeColor = new(0.01f, 0.005f, 0.005f, 1f);
  [SerializeField, Range(0.01f, 0.3f)] private float dissolveEdgeWidth = 0.14f;

  [Header("Audio")]
  [Tooltip("Real-time seconds used to silence active guard alert and chase sounds when death begins.")]
  [SerializeField, Min(0f)] private float guardAlertFadeDuration = 0.2f;

  [Header("Camera Impact")]
  [SerializeField] private Camera gameCamera;
  [SerializeField, Min(0f)] private float cameraImpulseDuration = 0.22f;
  [SerializeField, Min(0f)] private float cameraPositionImpulse = 0.035f;
  [SerializeField, Min(0f)] private float cameraRotationImpulse = 0.9f;
  [SerializeField, Min(1f)] private float cameraImpulseFrequency = 25f;

  private MaterialPropertyBlock propertyBlock;
  private bool dead;
  private bool deathPending;
  private bool caughtLocked;
  private Coroutine delayedStartRoutine;

  public bool IsDead => dead;
  public bool IsDying => deathPending || dead;

  public void Kill(GuardController source) {
    if (dead || deathPending) return;
    if (startDelayFrames <= 0) {
      BeginDeath(source);
      return;
    }

    deathPending = true;
    delayedStartRoutine = StartCoroutine(BeginDeathAfterFrames(source));
  }

  private IEnumerator BeginDeathAfterFrames(GuardController source) {
    float totalDelay = startDelayFrames / (float)Mathf.Max(1, delayFrameRate);
    float lockTime = totalDelay * 0.5f;
    float elapsed = 0f;
    while (elapsed < totalDelay) {
      yield return null;
      if (SceneTransitionManager.IsGamePaused) continue;

      elapsed += Time.deltaTime;
      if (!caughtLocked && elapsed >= lockTime) LockCaughtPlayer();
    }

    delayedStartRoutine = null;
    deathPending = false;
    BeginDeath(source);
  }

  private void BeginDeath(GuardController source) {
    if (dead) return;
    if (!SceneTransitionManager.BeginPlayerDeath()) return;
    dead = true;
    GuardController.FadeOutAllAlertAudio(guardAlertFadeDuration);
    LockGameplay();
    StartCoroutine(DeathRoutine(source));
  }

  private void LockGameplay() {
    LockCaughtPlayer();

    Animator animator = GetComponent<Animator>();
    if (animator != null) animator.speed = 0f;
  }

  /// <summary>
  /// Stops player control midway through the attack/death wind-up while leaving animation and
  /// world time running until the full configurable delay has elapsed.
  /// </summary>
  private void LockCaughtPlayer() {
    if (caughtLocked) return;
    caughtLocked = true;

    WallSwitchController wallSwitch = GetComponent<WallSwitchController>();
    if (wallSwitch != null) wallSwitch.CancelForDeath(true);

    DistractionController distraction = GetComponent<DistractionController>();
    if (distraction != null) distraction.CancelForDeath(true);

    LineFollowController movement = GetComponent<LineFollowController>();
    if (movement != null) movement.enabled = false;

    CharacterController character = GetComponent<CharacterController>();
    if (character != null) character.enabled = false;

    PlayerInteractor interactor = GetComponent<PlayerInteractor>();
    if (interactor != null) interactor.enabled = false;
  }

  private IEnumerator DeathRoutine(GuardController source) {
    ResolveReferences();
    Vector3 impactDirection = source != null
      ? transform.position - source.transform.position
      : -transform.forward;
    impactDirection.y = 0f;
    if (impactDirection.sqrMagnitude < 0.0001f) impactDirection = transform.forward;

    SpawnInk(impactDirection.normalized);
    if (gameCamera != null && cameraImpulseDuration > 0f)
      StartCoroutine(CameraImpulseRoutine());

    ApplyDissolve(0f, 1f);
    yield return WaitForUnpausedSeconds(impactHold);

    float elapsed = 0f;
    while (elapsed < dissolveDuration) {
      if (!SceneTransitionManager.IsGamePaused) elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / dissolveDuration);
      ApplyDissolve(progress, 1f - progress);
      yield return null;
    }

    for (int i = 0; i < spriteRenderers.Length; i++)
      if (spriteRenderers[i] != null) spriteRenderers[i].enabled = false;

    SceneTransitionManager.ShowPlayerDeathModal();
  }

  private IEnumerator CameraImpulseRoutine() {
    Transform cameraTransform = gameCamera.transform;
    Vector3 basePosition = cameraTransform.localPosition;
    Quaternion baseRotation = cameraTransform.localRotation;
    float elapsed = 0f;
    while (elapsed < cameraImpulseDuration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / cameraImpulseDuration);
      float envelope = 1f - progress;
      float phase = elapsed * cameraImpulseFrequency * Mathf.PI * 2f;
      float horizontal = Mathf.Sin(phase);
      float vertical = Mathf.Sin(phase * 1.37f + 0.8f);
      cameraTransform.localPosition = basePosition
        + new Vector3(horizontal, vertical, 0f) * (cameraPositionImpulse * envelope);
      cameraTransform.localRotation = baseRotation * Quaternion.Euler(
        vertical * cameraRotationImpulse * envelope,
        horizontal * cameraRotationImpulse * 0.4f * envelope,
        horizontal * cameraRotationImpulse * envelope);
      yield return null;
    }
    cameraTransform.localPosition = basePosition;
    cameraTransform.localRotation = baseRotation;
  }

  private void SpawnInk(Vector3 direction) {
    if (inkExplosionPrefab == null) return;
    Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
    GameObject instance;
    try {
      instance = Instantiate(inkExplosionPrefab, transform.position + inkExplosionOffset, rotation);
    }
    catch (System.Exception exception) {
      Debug.LogError($"[PlayerDeath] Could not create the ink explosion. Death will continue without it.\n{exception}", this);
      return;
    }

    instance.transform.localScale *= inkExplosionScale;
    PauseAwareUnscaledParticles.Configure(instance);
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    float lifetime = 1.5f;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      main.useUnscaledTime = true;
      lifetime = Mathf.Max(lifetime, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
      particles[i].Play(true);
    }
    Destroy(instance, lifetime + 0.25f);
  }

  private void ApplyDissolve(float dissolve, float highlight) {
    propertyBlock ??= new MaterialPropertyBlock();
    for (int i = 0; i < spriteRenderers.Length; i++) {
      SpriteRenderer renderer = spriteRenderers[i];
      if (renderer == null) continue;
      renderer.GetPropertyBlock(propertyBlock);
      propertyBlock.SetColor(HighlightColorId, impactColor);
      propertyBlock.SetFloat(HighlightStrengthId, Mathf.Clamp01(highlight));
      propertyBlock.SetFloat(DissolveId, Mathf.Clamp01(dissolve));
      propertyBlock.SetVector(DissolveUvRectId, GetSpriteUvRect(renderer));
      propertyBlock.SetColor(DissolveEdgeColorId, dissolveEdgeColor);
      propertyBlock.SetFloat(DissolveEdgeWidthId, dissolveEdgeWidth);
      renderer.SetPropertyBlock(propertyBlock);
    }
  }

  private void ResolveReferences() {
    if (spriteRenderers == null || spriteRenderers.Length == 0)
      spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
    if (gameCamera == null) gameCamera = Camera.main;
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

  private static IEnumerator WaitForUnpausedSeconds(float duration) {
    float elapsed = 0f;
    while (elapsed < duration) {
      if (!SceneTransitionManager.IsGamePaused) elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
  }
}
