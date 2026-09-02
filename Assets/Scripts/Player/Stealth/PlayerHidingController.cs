using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

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
  [FormerlySerializedAs("hiddenCameraLocalOffset")]
  [Tooltip("Player-relative hidden endpoint: X is lateral, Y is height, and Z is distance from the player on the camera's current side.")]
  [SerializeField] private Vector3 hiddenCameraRelativePosition = new(0f, 0.21f, 1.55f);
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
  private LinePath hidePathLine;
  private int hidePathStrand;
  private float hidePathDistance;
  private Vector3 hideEffectFrontReference;
  private readonly List<HidePathLeg> hidePathRoute = new();
  private float hidePathRouteLength;
  private bool hasPathHidePoint;
  private Vector3 normalCameraPosition;
  private Quaternion normalCameraRotation;
  private bool movementWasEnabled;
  private bool interactorWasEnabled;
  private bool interactionWasSuppressed;
  private bool characterWasEnabled;

  private readonly struct HidePathLeg {
    public LinePath Path { get; }
    public int Strand { get; }
    public float From { get; }
    public float To { get; }
    public float RouteStart { get; }
    public float Direction { get; }
    public float Length => Mathf.Abs(To - From);

    public HidePathLeg(
      LinePath path,
      int strand,
      float from,
      float to,
      float routeStart,
      float endpointDirection) {
      Path = path;
      Strand = strand;
      From = from;
      To = to;
      RouteStart = routeStart;
      Direction = Mathf.Abs(to - from) > 0.0001f
        ? Mathf.Sign(to - from)
        : Mathf.Sign(endpointDirection);
    }
  }

  private readonly struct PathEndpoint {
    public LinePath Path { get; }
    public int Strand { get; }
    public bool AtEnd { get; }
    public float Distance => AtEnd ? Path.GetStrandLength(Strand) : 0f;
    public Vector3 Point => Path.GetPointAtDistance(Strand, Distance);

    public PathEndpoint(LinePath path, int strand, bool atEnd) {
      Path = path;
      Strand = strand;
      AtEnd = atEnd;
    }
  }

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
    if (spot != null) spot.PlayEnterFeedback(hideEffectFrontReference);
    Vector3 hiddenCameraPosition = PlayerRelativeCamera.ResolveLocalEndpoint(
      transform,
      cameraTransform,
      hiddenCameraRelativePosition);
    StartCoroutine(BlendCamera(hiddenCameraPosition, normalCameraRotation));
    StartCoroutine(BlendVignette(spot != null ? spot.HiddenVignetteWeight : 1f, cameraBlendDuration));

    Vector3 startPosition = transform.position;
    float scriptedSpeed = transitionDuration > 0f ? hidePathRouteLength / transitionDuration : 0f;
    int activeLeg = -1;

    float elapsed = 0f;
    while (elapsed < transitionDuration) {
      elapsed += PauseAwareDelta();
      float t = Mathf.Clamp01(elapsed / transitionDuration);
      if (hasPathHidePoint)
        SetHideRoutePosition(hidePathRouteLength * Smooth(t), scriptedSpeed, ref activeLeg);
      else
        transform.position = startPosition;
      ApplyDissolve(t);
      yield return null;
    }

    if (hasPathHidePoint) {
      SetHideRoutePosition(hidePathRouteLength, scriptedSpeed, ref activeLeg);
      movement.SetLine(hidePathLine, hidePathStrand, hidePathDistance);
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
    if (spot != null) spot.PlayExitFeedback(hideEffectFrontReference);
    transform.SetPositionAndRotation(hidePosition, hideRotation);
    if (hasPathHidePoint) movement?.SetLine(hidePathLine, hidePathStrand, hidePathDistance);
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
    hidePathRoute.Clear();
    hidePathRouteLength = 0f;
    hasPathHidePoint = movement != null && movement.currentLine != null &&
                  movement.currentLine.StrandCount > 0;
    Vector3 authoredHidePoint = spot != null && spot.HidePoint != null
      ? spot.HidePoint.position
      : transform.position;

    if (!hasPathHidePoint) {
      hidePosition = authoredHidePoint;
      hideEffectFrontReference = transform.position;
      hidePathLine = null;
      hidePathStrand = 0;
      hidePathDistance = 0f;
      return;
    }

    FindClosestPathProjection(
      authoredHidePoint,
      out hidePathLine,
      out hidePathStrand,
      out hidePathDistance,
      out Vector3 pathPoint);
    hideEffectFrontReference = pathPoint;
    hasPathHidePoint = hidePathLine != null && BuildHidePathRoute(
      movement.currentLine,
      Mathf.Clamp(movement.currentStrand, 0, movement.currentLine.StrandCount - 1),
      movement.DistanceAlongLine,
      hidePathLine,
      hidePathStrand,
      hidePathDistance);
    if (!hasPathHidePoint) {
      // A disconnected authored path cannot be reached through normal line movement. Preserve
      // the previous safe behavior on the player's current strand instead of hiding in place.
      hidePathLine = movement.currentLine;
      hidePathStrand = Mathf.Clamp(movement.currentStrand, 0, hidePathLine.StrandCount - 1);
      hidePathDistance = hidePathLine.FindClosestDistanceOnStrand(
        hidePathStrand,
        authoredHidePoint,
        out pathPoint,
        out _);
      hideEffectFrontReference = pathPoint;
      hasPathHidePoint = BuildHidePathRoute(
        hidePathLine,
        hidePathStrand,
        movement.DistanceAlongLine,
        hidePathLine,
        hidePathStrand,
        hidePathDistance);
    }
    hidePosition = movement.GetRootPositionForFeetAt(pathPoint);
  }

  private static void FindClosestPathProjection(
    Vector3 worldPoint,
    out LinePath destinationPath,
    out int destinationStrand,
    out float destinationDistance,
    out Vector3 destinationPoint) {
    destinationPath = null;
    destinationStrand = -1;
    destinationDistance = 0f;
    destinationPoint = worldPoint;
    float closestDistance = float.PositiveInfinity;

    IReadOnlyList<LinePath> paths = LinePath.All;
    for (int i = 0; i < paths.Count; i++) {
      LinePath candidate = paths[i];
      if (candidate == null || !candidate.isActiveAndEnabled || candidate.StrandCount == 0) continue;
      float distanceAlong = candidate.FindClosestDistance(
        worldPoint,
        out Vector3 point,
        out float distanceToPath,
        out int strand);
      if (strand < 0 || distanceToPath >= closestDistance) continue;
      closestDistance = distanceToPath;
      destinationPath = candidate;
      destinationStrand = strand;
      destinationDistance = distanceAlong;
      destinationPoint = point;
    }
  }

  private bool BuildHidePathRoute(
    LinePath sourcePath,
    int sourceStrand,
    float sourceDistance,
    LinePath destinationPath,
    int destinationStrand,
    float destinationDistance) {
    hidePathRoute.Clear();
    hidePathRouteLength = 0f;

    float bestCost = float.PositiveInfinity;
    float directTravel = 0f;
    bool useDirectRoute = false;
    if (sourcePath == destinationPath && sourceStrand == destinationStrand) {
      directTravel = destinationDistance - sourceDistance;
      if (sourcePath.IsStrandClosedLoop(sourceStrand)) {
        float length = sourcePath.GetStrandLength(sourceStrand);
        if (length > 0f && Mathf.Abs(directTravel) > length * 0.5f)
          directTravel -= Mathf.Sign(directTravel) * length;
      }
      bestCost = Mathf.Abs(directTravel);
      useDirectRoute = true;
    }

    var endpoints = new List<PathEndpoint>();
    IReadOnlyList<LinePath> paths = LinePath.All;
    for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++) {
      LinePath path = paths[pathIndex];
      if (path == null || !path.isActiveAndEnabled) continue;
      for (int strand = 0; strand < path.StrandCount; strand++) {
        if (path.IsStrandClosedLoop(strand) || path.GetStrandLength(strand) <= 0.0001f) continue;
        endpoints.Add(new PathEndpoint(path, strand, false));
        endpoints.Add(new PathEndpoint(path, strand, true));
      }
    }

    int count = endpoints.Count;
    var distances = new float[count];
    var previous = new int[count];
    var visited = new bool[count];
    for (int i = 0; i < count; i++) {
      distances[i] = float.PositiveInfinity;
      previous[i] = -1;
      PathEndpoint endpoint = endpoints[i];
      if (endpoint.Path == sourcePath && endpoint.Strand == sourceStrand)
        distances[i] = Mathf.Abs(endpoint.Distance - sourceDistance);
    }

    float connectionTolerance = movement != null
      ? Mathf.Max(0.001f, movement.endpointConnectionTolerance)
      : 0.03f;
    float connectionToleranceSquared = connectionTolerance * connectionTolerance;
    int bestDestinationEndpoint = -1;

    for (int iteration = 0; iteration < count; iteration++) {
      int current = -1;
      float currentDistance = float.PositiveInfinity;
      for (int i = 0; i < count; i++) {
        if (!visited[i] && distances[i] < currentDistance) {
          current = i;
          currentDistance = distances[i];
        }
      }
      if (current < 0 || currentDistance >= bestCost) break;
      visited[current] = true;
      PathEndpoint endpoint = endpoints[current];

      if (endpoint.Path == destinationPath && endpoint.Strand == destinationStrand) {
        float candidateCost = currentDistance + Mathf.Abs(destinationDistance - endpoint.Distance);
        if (candidateCost < bestCost) {
          bestCost = candidateCost;
          bestDestinationEndpoint = current;
          useDirectRoute = false;
        }
      }

      int opposite = current ^ 1;
      Relax(opposite, endpoint.Path.GetStrandLength(endpoint.Strand));

      Vector3 point = endpoint.Point;
      for (int candidate = 0; candidate < count; candidate++) {
        if (candidate == current || visited[candidate]) continue;
        PathEndpoint connected = endpoints[candidate];
        if (connected.Path == endpoint.Path && connected.Strand == endpoint.Strand) continue;
        if ((connected.Point - point).sqrMagnitude <= connectionToleranceSquared)
          Relax(candidate, 0f);
      }

      void Relax(int candidate, float edgeCost) {
        float candidateDistance = currentDistance + edgeCost;
        if (candidate < 0 || candidate >= count || candidateDistance >= distances[candidate]) return;
        distances[candidate] = candidateDistance;
        previous[candidate] = current;
      }
    }

    if (useDirectRoute) {
      AddHidePathLeg(
        sourcePath,
        sourceStrand,
        sourceDistance,
        sourceDistance + directTravel);
      return true;
    }
    if (bestDestinationEndpoint < 0) return false;

    var endpointChain = new List<int>();
    for (int current = bestDestinationEndpoint; current >= 0; current = previous[current])
      endpointChain.Add(current);
    endpointChain.Reverse();

    PathEndpoint first = endpoints[endpointChain[0]];
    AddHidePathLeg(
      sourcePath,
      sourceStrand,
      sourceDistance,
      first.Distance,
      first.AtEnd ? 1f : -1f);
    for (int i = 1; i < endpointChain.Count; i++) {
      PathEndpoint from = endpoints[endpointChain[i - 1]];
      PathEndpoint to = endpoints[endpointChain[i]];
      if (from.Path == to.Path && from.Strand == to.Strand)
        AddHidePathLeg(from.Path, from.Strand, from.Distance, to.Distance);
    }
    PathEndpoint last = endpoints[endpointChain[endpointChain.Count - 1]];
    AddHidePathLeg(
      destinationPath,
      destinationStrand,
      last.Distance,
      destinationDistance,
      last.AtEnd ? -1f : 1f);
    return true;
  }

  private void AddHidePathLeg(
    LinePath path,
    int strand,
    float from,
    float to,
    float endpointDirection = 1f) {
    if (path == null) return;
    hidePathRoute.Add(new HidePathLeg(
      path,
      strand,
      from,
      to,
      hidePathRouteLength,
      endpointDirection));
    hidePathRouteLength += Mathf.Abs(to - from);
  }

  private void SetHideRoutePosition(float routeDistance, float speed, ref int activeLeg) {
    if (movement == null || hidePathRoute.Count == 0) return;
    routeDistance = Mathf.Clamp(routeDistance, 0f, hidePathRouteLength);

    int targetLeg = hidePathRoute.Count - 1;
    for (int i = 0; i < hidePathRoute.Count; i++) {
      HidePathLeg candidate = hidePathRoute[i];
      bool isLast = i == hidePathRoute.Count - 1;
      if (routeDistance < candidate.RouteStart + candidate.Length - 0.0001f || isLast) {
        targetLeg = i;
        break;
      }
    }

    while (activeLeg < targetLeg) {
      activeLeg++;
      ActivateHidePathLeg(activeLeg);
    }

    HidePathLeg leg = hidePathRoute[targetLeg];
    float localDistance = Mathf.Clamp(routeDistance - leg.RouteStart, 0f, leg.Length);
    movement.SetScriptedPathPosition(
      leg.Strand,
      leg.From + localDistance * leg.Direction,
      speed * leg.Direction);
  }

  private void ActivateHidePathLeg(int legIndex) {
    HidePathLeg leg = hidePathRoute[legIndex];
    if (movement.currentLine == leg.Path && movement.currentStrand == leg.Strand) return;

    if (legIndex <= 0) {
      movement.SetLine(leg.Path, leg.Strand, leg.From);
      return;
    }

    HidePathLeg previous = hidePathRoute[legIndex - 1];
    Vector3 incoming = previous.Path.GetDirectionAtDistance(previous.Strand, previous.To) *
                       previous.Direction;
    Vector3 outgoing = leg.Path.GetDirectionAtDistance(leg.Strand, leg.From) *
                       leg.Direction;
    movement.SetScriptedConnectedLine(
      leg.Path,
      leg.Strand,
      leg.From,
      incoming,
      outgoing);
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

#if UNITY_EDITOR
  private void OnValidate() {
    PlayerRelativeCamera.ClampDistance(ref hiddenCameraRelativePosition);
  }
#endif

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
