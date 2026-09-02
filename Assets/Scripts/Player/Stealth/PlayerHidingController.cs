using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Authoritative player-side lifecycle for entering, occupying, and exiting hiding spots.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInput), typeof(PlayerStealthController))]
public sealed class PlayerHidingController : MonoBehaviour {
  private static readonly int DissolveId = Shader.PropertyToID("_InkDissolve");
  private static readonly int DissolveUvRectId = Shader.PropertyToID("_InkDissolveUvRect");
  private static readonly int DissolveEdgeColorId = Shader.PropertyToID("_InkDissolveEdgeColor");
  private static readonly int DissolveEdgeWidthId = Shader.PropertyToID("_InkDissolveEdgeWidth");

  [Header("Player Presentation")]
  [SerializeField] private SpriteRenderer[] spriteRenderers = System.Array.Empty<SpriteRenderer>();
  [SerializeField] private Color dissolveEdgeColor = new(0.01f, 0.005f, 0.005f, 1f);
  [SerializeField, Range(0.01f, 0.3f)] private float dissolveEdgeWidth = 0.14f;
  [SerializeField, Min(0.05f)] private float transitionDuration = 0.45f;

  [Header("Camera")]
  [SerializeField] private Transform cameraTransform;
  [Tooltip("Offset added to the current camera-local pose while hidden. Positive Z moves the camera closer to the player.")]
  [SerializeField] private Vector3 hiddenCameraLocalOffset = new(0f, -0.04f, 0.45f);
  [SerializeField, Min(0.05f)] private float cameraBlendDuration = 0.4f;

  [Header("Hidden Vignette")]
  [SerializeField] private Volume vignetteVolume;
  [SerializeField] private VolumeProfile vignetteProfile;
  [SerializeField] private Color vignetteColor = new(0.005f, 0.003f, 0.008f, 1f);
  [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.48f;
  [SerializeField, Range(0f, 1f)] private float vignetteSmoothness = 0.42f;

  [Header("References")]
  [SerializeField] private PlayerInput playerInput;
  [SerializeField] private PlayerStealthController stealth;
  [SerializeField] private LineFollowController movement;
  [SerializeField] private PlayerInteractor interactor;
  [SerializeField] private CharacterController characterController;

  public bool IsConcealed { get; private set; }
  public bool IsTransitioning { get; private set; }
  public HidingSpot CurrentSpot { get; private set; }

  private MaterialPropertyBlock propertyBlock;
  private VolumeProfile runtimeVignetteProfile;
  private Vignette vignette;
  private string actionMapBeforeHiding = "Player";
  private Vector3 hidePosition;
  private Quaternion hideRotation;
  private int hidePathStrand;
  private float hidePathDistance;
  private bool hasPathHidePoint;
  private Vector3 normalCameraPosition;
  private Quaternion normalCameraRotation;
  private bool movementWasEnabled;
  private bool interactorWasEnabled;
  private bool interactionWasSuppressed;
  private bool characterWasEnabled;

  private void Awake() {
    ResolveReferences();
    ConfigureVignette();
    ApplyDissolve(0f);
  }

  public bool TryEnter(HidingSpot spot) {
    ResolveReferences();
    if (!CanEnter(spot)) {
      if (spot != null && stealth != null && stealth.IsCurrentlyVisible)
        spot.ShowRejectedFeedback();
      return false;
    }
    if (!spot.TryOccupy(this)) return false;

    CurrentSpot = spot;
    IsConcealed = true;
    IsTransitioning = true;
    ConfigureHidePoint(spot);
    // Hiding anchors define positions only. Rotating the player here would also rotate its child
    // camera in world space, while the hiding presentation is meant to be a pure camera zoom.
    hideRotation = transform.rotation;
    CaptureAndLockGameplay();
    stealth?.RefreshConcealmentState();
    StartCoroutine(EnterRoutine());
    return true;
  }

  public bool CanEnter(HidingSpot spot) {
    ResolveReferences();
    if (spot == null || CurrentSpot != null || IsTransitioning || IsConcealed) return false;
    if (SceneTransitionManager.IsGamePaused || SceneTransitionManager.IsDeathSequenceActive) return false;
    if (stealth != null && stealth.IsCurrentlyVisible) return false;
    return spot.CanOccupy(this);
  }

#pragma warning disable IDE0051
  private void OnExitHide(InputValue value) {
    if (value.isPressed && IsConcealed && !IsTransitioning && CurrentSpot != null)
      StartCoroutine(ExitRoutine());
  }
#pragma warning restore IDE0051

  private IEnumerator EnterRoutine() {
    HidingSpot spot = CurrentSpot;
    spot?.PlayEnterFeedback();
    StartCoroutine(BlendCamera(normalCameraPosition + hiddenCameraLocalOffset, normalCameraRotation));
    StartCoroutine(BlendVignette(spot != null ? spot.HiddenVignetteWeight : 1f, cameraBlendDuration));

    Vector3 startPosition = transform.position;
    float startPathDistance = movement != null ? movement.DistanceAlongLine : 0f;
    float pathTravel = hasPathHidePoint ? GetShortestPathTravel(startPathDistance, hidePathDistance) : 0f;
    float scriptedSpeed = transitionDuration > 0f ? pathTravel / transitionDuration : 0f;

    float elapsed = 0f;
    while (elapsed < transitionDuration) {
      elapsed += PauseAwareDelta();
      float t = Mathf.Clamp01(elapsed / transitionDuration);
      if (hasPathHidePoint)
        movement.SetScriptedPathPosition(
          hidePathStrand,
          startPathDistance + pathTravel * Smooth(t),
          scriptedSpeed);
      else
        transform.position = startPosition;
      ApplyDissolve(t);
      yield return null;
    }

    if (hasPathHidePoint) {
      movement.SetScriptedPathPosition(hidePathStrand, hidePathDistance, scriptedSpeed);
      movement.FinishScriptedPathMovement();
      hidePosition = transform.position;
      hideRotation = transform.rotation;
    }
    SetRenderersEnabled(false);
    IsTransitioning = false;
  }

  private IEnumerator ExitRoutine() {
    IsTransitioning = true;
    HidingSpot spot = CurrentSpot;
    spot?.PlayExitFeedback();
    transform.SetPositionAndRotation(hidePosition, hideRotation);
    if (hasPathHidePoint) movement?.SetLine(movement.currentLine, hidePathStrand, hidePathDistance);
    SetRenderersEnabled(true);
    ApplyDissolve(1f);
    StartCoroutine(BlendCamera(normalCameraPosition, normalCameraRotation));
    StartCoroutine(BlendVignette(0f, cameraBlendDuration));

    float elapsed = 0f;
    while (elapsed < transitionDuration) {
      elapsed += PauseAwareDelta();
      ApplyDissolve(1f - Mathf.Clamp01(elapsed / transitionDuration));
      yield return null;
    }

    ApplyDissolve(0f);
    IsConcealed = false;
    IsTransitioning = false;
    CurrentSpot = null;
    RestoreGameplay();
    stealth?.RefreshConcealmentState();
    spot?.Release(this);
  }

  private void ConfigureHidePoint(HidingSpot spot) {
    hasPathHidePoint = movement != null && movement.currentLine != null &&
                  movement.currentLine.StrandCount > 0;
    Vector3 authoredHidePoint = spot != null && spot.HidePoint != null
      ? spot.HidePoint.position
      : transform.position;

    if (!hasPathHidePoint) {
      hidePosition = authoredHidePoint;
      hidePathStrand = 0;
      hidePathDistance = 0f;
      return;
    }

    hidePathStrand = Mathf.Clamp(movement.currentStrand, 0, movement.currentLine.StrandCount - 1);
    hidePathDistance = movement.currentLine.FindClosestDistanceOnStrand(
      hidePathStrand,
      authoredHidePoint,
      out Vector3 pathPoint,
      out _);
    hidePosition = movement.GetRootPositionForFeetAt(pathPoint);
  }

  private float GetShortestPathTravel(float fromDistance, float toDistance) {
    float travel = toDistance - fromDistance;
    if (movement == null || movement.currentLine == null ||
        !movement.currentLine.IsStrandClosedLoop(hidePathStrand)) return travel;

    float length = movement.currentLine.GetStrandLength(hidePathStrand);
    if (length > 0f && Mathf.Abs(travel) > length * 0.5f)
      travel -= Mathf.Sign(travel) * length;
    return travel;
  }

  private void CaptureAndLockGameplay() {
    actionMapBeforeHiding = playerInput != null && playerInput.currentActionMap != null
      ? playerInput.currentActionMap.name
      : "Player";
    if (playerInput?.actions?.FindActionMap("Hiding", false) != null)
      playerInput.SwitchCurrentActionMap("Hiding");
    else
      Debug.LogError("[PlayerHiding] The shared Input Actions asset has no Hiding map.", this);

    movementWasEnabled = movement != null && movement.enabled;
    interactorWasEnabled = interactor != null && interactor.enabled;
    interactionWasSuppressed = interactor != null && interactor.interactionSuppressed;
    characterWasEnabled = characterController != null && characterController.enabled;
    if (movement != null) movement.enabled = false;
    if (interactor != null) interactor.interactionSuppressed = true;
    if (characterController != null) characterController.enabled = false;

    if (cameraTransform != null) {
      normalCameraPosition = cameraTransform.localPosition;
      normalCameraRotation = cameraTransform.localRotation;
    }
  }

  private void RestoreGameplay() {
    if (characterController != null) characterController.enabled = characterWasEnabled;
    if (movement != null) movement.enabled = movementWasEnabled;
    if (interactor != null) {
      interactor.interactionSuppressed = interactionWasSuppressed;
      interactor.enabled = interactorWasEnabled;
    }
    string map = string.IsNullOrEmpty(actionMapBeforeHiding) ? "Player" : actionMapBeforeHiding;
    if (playerInput?.actions?.FindActionMap(map, false) != null)
      playerInput.SwitchCurrentActionMap(map);
  }

  private IEnumerator BlendCamera(Vector3 targetPosition, Quaternion targetRotation) {
    if (cameraTransform == null) yield break;
    Vector3 startPosition = cameraTransform.localPosition;
    Quaternion startRotation = cameraTransform.localRotation;
    float elapsed = 0f;
    while (elapsed < cameraBlendDuration) {
      elapsed += PauseAwareDelta();
      float t = Smooth(Mathf.Clamp01(elapsed / cameraBlendDuration));
      cameraTransform.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, t);
      cameraTransform.localRotation = Quaternion.SlerpUnclamped(startRotation, targetRotation, t);
      yield return null;
    }
    cameraTransform.localPosition = targetPosition;
    cameraTransform.localRotation = targetRotation;
  }

  private IEnumerator BlendVignette(float targetWeight, float duration) {
    if (vignetteVolume == null) yield break;
    float startWeight = vignetteVolume.weight;
    float elapsed = 0f;
    while (elapsed < duration) {
      elapsed += PauseAwareDelta();
      vignetteVolume.weight = Mathf.Lerp(startWeight, targetWeight, Smooth(Mathf.Clamp01(elapsed / duration)));
      yield return null;
    }
    vignetteVolume.weight = targetWeight;
  }

  private void ConfigureVignette() {
    if (vignetteVolume == null || vignetteProfile == null) return;
    runtimeVignetteProfile = Instantiate(vignetteProfile);
    vignetteVolume.isGlobal = true;
    vignetteVolume.priority = 30f;
    vignetteVolume.weight = 0f;
    vignetteVolume.profile = runtimeVignetteProfile;
    if (!runtimeVignetteProfile.TryGet(out vignette)) vignette = runtimeVignetteProfile.Add<Vignette>(true);
    vignette.color.overrideState = true;
    vignette.color.value = vignetteColor;
    vignette.intensity.overrideState = true;
    vignette.intensity.value = vignetteIntensity;
    vignette.smoothness.overrideState = true;
    vignette.smoothness.value = vignetteSmoothness;
    vignette.rounded.overrideState = true;
    vignette.rounded.value = true;
  }

  private void ApplyDissolve(float value) {
    propertyBlock ??= new MaterialPropertyBlock();
    for (int i = 0; i < spriteRenderers.Length; i++) {
      SpriteRenderer renderer = spriteRenderers[i];
      if (renderer == null) continue;
      renderer.GetPropertyBlock(propertyBlock);
      propertyBlock.SetFloat(DissolveId, Mathf.Clamp01(value));
      propertyBlock.SetVector(DissolveUvRectId, GetSpriteUvRect(renderer));
      propertyBlock.SetColor(DissolveEdgeColorId, dissolveEdgeColor);
      propertyBlock.SetFloat(DissolveEdgeWidthId, dissolveEdgeWidth);
      renderer.SetPropertyBlock(propertyBlock);
    }
  }

  private void SetRenderersEnabled(bool enabled) {
    for (int i = 0; i < spriteRenderers.Length; i++)
      if (spriteRenderers[i] != null) spriteRenderers[i].enabled = enabled;
  }

  private void ResolveReferences() {
    if (playerInput == null) playerInput = GetComponent<PlayerInput>();
    if (stealth == null) stealth = GetComponent<PlayerStealthController>();
    if (movement == null) movement = GetComponent<LineFollowController>();
    if (interactor == null) interactor = GetComponent<PlayerInteractor>();
    if (characterController == null) characterController = GetComponent<CharacterController>();
    if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    if (spriteRenderers == null || spriteRenderers.Length == 0)
      spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
  }

  private static float PauseAwareDelta() => SceneTransitionManager.IsGamePaused ? 0f : Time.unscaledDeltaTime;
  private static float Smooth(float t) => t * t * (3f - 2f * t);

  private static Vector4 GetSpriteUvRect(SpriteRenderer renderer) {
    Sprite sprite = renderer != null ? renderer.sprite : null;
    Texture texture = sprite != null ? sprite.texture : null;
    if (sprite == null || texture == null || texture.width <= 0 || texture.height <= 0)
      return new Vector4(0f, 0f, 1f, 1f);
    Rect rect = sprite.rect;
    return new Vector4(rect.x / texture.width, rect.y / texture.height,
      Mathf.Max(rect.width / texture.width, 0.0001f), Mathf.Max(rect.height / texture.height, 0.0001f));
  }

  private void OnDestroy() {
    if (runtimeVignetteProfile != null) Destroy(runtimeVignetteProfile);
  }
}