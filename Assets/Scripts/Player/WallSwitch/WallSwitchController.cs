using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Action = System.Action;

/// <summary>
/// Wall-switch state machine. Space enters or cancels aiming; primary mouse confirms
/// the current immutable evaluation only when it is valid.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineFollowController))]
public sealed class WallSwitchController : MonoBehaviour {
  private enum SwitchState { Idle, Aiming, Executing }
  private enum BlockingCategory { None, AuthoredSurface, WallObstruction, GuardOrExplicitBlocker, GuardVisionField }

  [Header("Explicit references")]
  [SerializeField] private WallSwitchPathNetwork network;
  [SerializeField] private LineFollowController followController;
  [SerializeField] private CharacterController characterController;
  [SerializeField] private PlayerInput playerInput;
  [SerializeField] private Camera aimCamera;
  [SerializeField] private Transform cameraTransform;
  [SerializeField] private SpriteRenderer playerRenderer;
  [SerializeField] private WallSwitchPreview preview;
  [SerializeField] private DistractionController distractionController;
  [SerializeField] private PlayerDeathSequence deathSequence;
  [SerializeField] private RejectedAimCameraFeedback rejectionFeedback;
  [Tooltip("Optional component implementing IWallSwitchPermission for future hiding/death/detection rules.")]
  [SerializeField] private MonoBehaviour permissionSource;

  [Header("Selection")]
  [Tooltip("Maximum horizontal distance allowed between an authored LinePath and the visible wall collider under the cursor. Cursor height and the exact hit position on that wall do not affect eligibility.")]
  [SerializeField, Min(0.01f)] private float wallPathSearchRadius = 0.4f;
  [Tooltip("Small tolerance when deciding whether a LinePath lies on the player-facing side of the selected wall. Paths on its away-facing side are ignored.")]
  [SerializeField, Min(0f)] private float wallSideTolerance = 0.02f;
  [Tooltip("Camera-space depth used by the invalid cursor marker. This keeps it attached to the mouse even when the camera looks almost horizontally across the floor.")]
  [SerializeField, Min(0.01f)] private float invalidMarkerCameraDistance = 8f;
  [SerializeField, Min(0f)] private float minimumSwitchDistance = 0.75f;
  [SerializeField, Min(0.1f)] private float maximumSwitchDistance = 14f;
  [Tooltip("Allowed angular error between source and destination segments. Opposite directions count as parallel.")]
  [SerializeField, Range(0f, 45f)] private float parallelToleranceDegrees = 8f;
  [Tooltip("Minimum perpendicular separation between parallel source and destination paths. Collinear wall pieces below this distance are treated as the same wall and cannot be selected.")]
  [SerializeField, Min(0f)] private float minimumDestinationPlaneSeparation = 0.5f;
  [Tooltip("World-space margin around every authored LinePath point that cannot be selected as a destination. This prevents arrivals directly on corners.")]
  [SerializeField, Min(0f)] private float destinationPointMargin = 0.2f;
  [Tooltip("Player-local offset used for the wall-switch trajectory origin before Origin World Y is applied.")]
  [SerializeField] private Vector3 switchOriginLocalOffset = new(0f, 0.05f, 0f);

  [Header("Trajectory interactions")]
  [Tooltip("Capsule radius used both for guard intersection and explicit switch blockers.")]
  [SerializeField, Min(0.01f)] private float trajectoryRadius = 0.18f;
  [Tooltip("Layers containing GuardWallSwitchTarget or WallSwitchBlocker colliders. Ordinary props and walls are ignored.")]
  [SerializeField] private LayerMask interactionLayers;
  [Tooltip("Solid palace walls that block the center of a switch trajectory. Keep props off these layers.")]
  [SerializeField] private LayerMask wallObstructionLayers = 1 << 8;
  [Tooltip("Solid surfaces that may receive the destination stain. Triggers are ignored so broad interaction volumes do not hide the actual visual surface. This is presentation-only: " +
           "the selectable wall and final LinePath destination are still resolved independently.")]
  [SerializeField] private LayerMask markerProjectionLayers = (1 << 8) | (1 << 13);
  [SerializeField, Min(0f)] private float markerSurfaceOffset = 0.01f;
  [Tooltip("Fixed world-space Y where the wall-switch trajectory leaves the player.")]
  [SerializeField] private float originWorldY = 0.33f;
  [FormerlySerializedAs("markerWorldY")]
  [Tooltip("Fixed world-space Y used by the trajectory endpoint and destination stain, but only after the unmodified cursor has selected a valid destination surface.")]
  [SerializeField] private float destinationWorldY = 0.33f;
  [Tooltip("How far a WallSwitchSurface may extend in front of the destination LinePath before it is treated as an obstruction rather than a valid stain surface.")]
  [SerializeField, Min(0f)] private float surfacePlaneTolerance = 0.02f;
  [Tooltip("Distance ignored at both ends of the wall ray so the source and destination supporting surfaces do not block themselves.")]
  [SerializeField, Min(0f)] private float wallEndpointInset = 0.1f;

  [Header("Aim time and camera")]
  [SerializeField, Range(0.01f, 1f)] private float aimingTimeScale = 0.06f;
  [Tooltip("Camera local position while aiming. The normal position is captured on entry and never overwritten.")]
  [SerializeField] private Vector3 aimingCameraLocalPosition = new(0f, 0.5f, -3.75f);
  [SerializeField, Min(0f)] private float cameraAimDuration = 0.35f;
  [Tooltip("Duration of each 90-degree camera orbit: once before ink travel and once before reappearance.")]
  [SerializeField, Min(0f)] private float cameraSideSwitchDuration = 0.3f;
  [Tooltip("How long the arrival animation remains visible before the camera rotates to the opposite side.")]
  [SerializeField, Min(0f)] private float cameraReturnDelayAfterArrival = 0.35f;
  [SerializeField, Min(0f)] private float cameraReturnDuration = 0.3f;

  [Header("Execution")]
  [SerializeField, Min(0.01f)] private float departureHoldDuration = 0.1f;
  [SerializeField, Min(0.01f)] private float inkTravelDuration = 0.8f;
  [SerializeField, Min(0f)] private float hitStopDuration = 0.08f;
  [Tooltip("Maximum local camera displacement during a takedown impact.")]
  [SerializeField, Min(0f)] private float takedownImpulsePosition = 0.035f;
  [Tooltip("Maximum local camera rotation in degrees during a takedown impact.")]
  [SerializeField, Min(0f)] private float takedownImpulseRotation = 0.8f;
  [Tooltip("Oscillations per second used by the damped takedown camera impulse.")]
  [SerializeField, Min(0.01f)] private float takedownImpulseFrequency = 24f;
  [SerializeField, Min(0f)] private float reappearDelay = 0.04f;
  [SerializeField] private GameObject departureInkPrefab;
  [SerializeField] private GameObject arrivalInkPrefab;
  [SerializeField] private GameObject travelingInkPrefab;

  [Header("Debug")]
  [SerializeField] private bool drawDebugGizmos = true;
  [SerializeField] private bool verboseLogging;

  private readonly Collider[] interactionHits = new Collider[64];
  private readonly RaycastHit[] authoredSurfaceHits = new RaycastHit[128];
  private readonly HashSet<GuardWallSwitchTarget> uniqueTargets = new();
  private readonly HashSet<WallSwitchBlocker> uniqueBlockers = new();
  private readonly List<InputAction> lockedInputActions = new();
  private static readonly string[] ActionsLockedWhileSwitching = {
    "Move", "RotateRight", "RotateLeft", "Interact", "Look", "Drop"
  };
  private SwitchState state;
  private WallSwitchEvaluation currentEvaluation = WallSwitchEvaluation.Empty;
  private IWallSwitchPermission permission;
  private bool followWasEnabled;
  private bool ownsTimeScale;
  private float timeScaleBeforeAim = 1f;
  private Vector3 normalCameraLocalPosition;
  private Quaternion normalCameraLocalRotation;
  private Vector3 authoredCameraLocalPosition;
  private Quaternion authoredCameraLocalRotation;
  private bool hasAuthoredCameraPose;
  private Vector3 activeAimCameraLocalPosition;
  private Coroutine cameraRoutine;
  private CursorLockMode cursorLockBeforeAim;
  private bool cursorVisibleBeforeAim;
  private bool ownsCursorState;
  private bool loggedInvalidConfiguration;
  private WallSwitchPathNetwork.SelectionDiagnostics lastSelectionDiagnostics;
  private BlockingCategory lastBlockingCategory;
  private WallSwitchFailureReason lastLoggedFailureReason = (WallSwitchFailureReason)(-1);
  private LinePath lastLoggedDestinationPath;
  private int lastLoggedDestinationStrand = -1;

  public bool IsAiming => state == SwitchState.Aiming;
  public bool IsExecuting => state == SwitchState.Executing;
  public bool IsCameraTransitioning => cameraRoutine != null;
  public WallSwitchEvaluation CurrentEvaluation => currentEvaluation;
  public AimEntryBlockReason LastAimEntryBlockReason { get; private set; }

  /// <summary>Lifecycle hooks for scene tutorials and other presentation-only observers.</summary>
  public event Action AimStarted;
  public event Action AimCancelled;
  public event Action SwitchStarted;
  public event Action SwitchCompleted;

  private void Awake() {
    ResolveLocalReferences();
    CaptureAuthoredCameraPose();
    if (permissionSource is IWallSwitchPermission assignedPermission) permission = assignedPermission;
  }

  private void OnValidate() {
    wallPathSearchRadius = Mathf.Max(0.01f, wallPathSearchRadius);
    wallSideTolerance = Mathf.Max(0f, wallSideTolerance);
    invalidMarkerCameraDistance = Mathf.Max(0.01f, invalidMarkerCameraDistance);
    minimumSwitchDistance = Mathf.Max(0f, minimumSwitchDistance);
    maximumSwitchDistance = Mathf.Max(minimumSwitchDistance, maximumSwitchDistance);
    minimumDestinationPlaneSeparation = Mathf.Max(0f, minimumDestinationPlaneSeparation);
    destinationPointMargin = Mathf.Max(0f, destinationPointMargin);
    trajectoryRadius = Mathf.Max(0.01f, trajectoryRadius);
    wallEndpointInset = Mathf.Max(0f, wallEndpointInset);
    markerSurfaceOffset = Mathf.Max(0f, markerSurfaceOffset);
    surfacePlaneTolerance = Mathf.Max(0f, surfacePlaneTolerance);
  }

  private void OnDisable() {
    bool cameraWasTransitioning = cameraRoutine != null;
    StopAllCoroutines();
    if (cameraRoutine != null) cameraRoutine = null;
    preview?.Hide();
    if (playerRenderer != null) playerRenderer.enabled = true;
    if (followController != null && state != SwitchState.Idle) followController.enabled = followWasEnabled;
    UnlockPlayerActions();
    if (cameraTransform != null && (state != SwitchState.Idle || cameraWasTransitioning)) {
      GetAuthoredCameraPoseForCurrentSide(out Vector3 position, out Quaternion rotation);
      cameraTransform.localPosition = position;
      cameraTransform.localRotation = rotation;
    }
    RestoreCursorState();
    RestoreTimeScale();
    state = SwitchState.Idle;
    currentEvaluation = WallSwitchEvaluation.Empty;
  }

#pragma warning disable IDE0051
  private void OnSwitch(InputValue value) {
    // PlayerInput's Send Messages notification can still reach a disabled behaviour. Respect
    // scene-specific ability loadouts instead of turning a disabled wall switch into rejected
    // input feedback.
    if (!isActiveAndEnabled || !value.isPressed) return;

    if (state == SwitchState.Idle) BeginAim();
    else if (state == SwitchState.Aiming && !SceneTransitionManager.IsGamePaused) ExitAimWithoutSwitch();
  }
#pragma warning restore IDE0051

  private void Update() {
    if (state != SwitchState.Idle && !SceneTransitionManager.IsGamePaused) EnsurePlayerActionsLocked();
    if (state != SwitchState.Aiming || SceneTransitionManager.IsGamePaused) return;
    currentEvaluation = EvaluateAtCursor();
    LogEvaluationChange(currentEvaluation);
    preview?.Show(currentEvaluation);
    if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
      TryConfirmCurrent();
  }

  /// <summary>
  /// Logs the current evaluation only when it actually changes (failure reason or selected
  /// destination), so aiming every frame does not spam the console. Reports exactly which check
  /// is preventing a switch: the per-filter segment-selection breakdown for "no destination
  /// found", the specific min/max distance for range failures, or which of the four trajectory
  /// checks (authored surface, wall obstruction, guard/blocker, guard vision) set the block.
  /// </summary>
  private void LogEvaluationChange(WallSwitchEvaluation evaluation) {
    if (!verboseLogging) return;
    bool changed = evaluation.FailureReason != lastLoggedFailureReason
      || evaluation.DestinationPath != lastLoggedDestinationPath
      || evaluation.DestinationStrand != lastLoggedDestinationStrand;
    if (!changed) return;

    lastLoggedFailureReason = evaluation.FailureReason;
    lastLoggedDestinationPath = evaluation.DestinationPath;
    lastLoggedDestinationStrand = evaluation.DestinationStrand;

    switch (evaluation.FailureReason) {
      case WallSwitchFailureReason.None: {
        float distance = Vector3.Distance(followController.FeetPosition, evaluation.DestinationFeet);
        Debug.Log(
          $"[WallSwitch] OK -- destination '{evaluation.DestinationPath?.name}' strand {evaluation.DestinationStrand} " +
          $"at {evaluation.DestinationFeet:F2}, distance {distance:F2}.", this);
        break;
      }
      case WallSwitchFailureReason.PlayerUnavailable: {
        AimEntryBlockReason permissionReason = permission != null ? permission.WallSwitchBlockReason : AimEntryBlockReason.None;
        Debug.Log($"[WallSwitch] BLOCKED -- permission source reports {permissionReason}.", this);
        break;
      }
      case WallSwitchFailureReason.NoAuthoredPath:
      case WallSwitchFailureReason.PathsNotParallel:
        Debug.Log($"[WallSwitch] BLOCKED -- no destination selected: {lastSelectionDiagnostics}", this);
        break;
      case WallSwitchFailureReason.DestinationTooClose: {
        float distance = Vector3.Distance(followController.FeetPosition, evaluation.DestinationFeet);
        Debug.Log($"[WallSwitch] BLOCKED -- destination is {distance:F2} away, below minimumSwitchDistance ({minimumSwitchDistance:F2}).", this);
        break;
      }
      case WallSwitchFailureReason.DestinationTooFar: {
        float distance = Vector3.Distance(followController.FeetPosition, evaluation.DestinationFeet);
        Debug.Log($"[WallSwitch] BLOCKED -- destination is {distance:F2} away, above maximumSwitchDistance ({maximumSwitchDistance:F2}).", this);
        break;
      }
      case WallSwitchFailureReason.Blocked: {
        string blockerName = evaluation.BlockingObject != null ? evaluation.BlockingObject.name : "unknown object";
        Debug.Log(
          $"[WallSwitch] BLOCKED -- trajectory obstructed by '{blockerName}' " +
          $"[{lastBlockingCategory}] at {evaluation.BlockingPoint:F2}.", this);
        break;
      }
      default:
        Debug.Log($"[WallSwitch] BLOCKED -- {evaluation.FailureReason}.", this);
        break;
    }
  }

  public bool BeginAim() {
    ResolveLocalReferences();
    LastAimEntryBlockReason = GetAimEntryBlockReason();
    if (LastAimEntryBlockReason != AimEntryBlockReason.None) {
      HandleRejectedEntry(LastAimEntryBlockReason);
      return false;
    }

    state = SwitchState.Aiming;
    followWasEnabled = followController.enabled;
    followController.enabled = false;
    EnsurePlayerActionsLocked();
    CaptureAndReleaseCursor();
    timeScaleBeforeAim = Time.timeScale;
    ownsTimeScale = true;
    Time.timeScale = aimingTimeScale;

    GetAuthoredCameraPoseForCurrentSide(out normalCameraLocalPosition, out normalCameraLocalRotation);
    activeAimCameraLocalPosition = GetAimPositionForCurrentSide();
    StartCameraBlend(activeAimCameraLocalPosition, normalCameraLocalRotation, cameraAimDuration);

    lastLoggedFailureReason = (WallSwitchFailureReason)(-1);
    lastLoggedDestinationPath = null;
    lastLoggedDestinationStrand = -1;
    currentEvaluation = EvaluateAtCursor();
    LogEvaluationChange(currentEvaluation);
    preview?.Show(currentEvaluation);
    AimStarted?.Invoke();
    if (verboseLogging) Debug.Log("[WallSwitch] Aim mode entered.", this);
    return true;
  }

  public bool TryConfirmCurrent() {
    if (state != SwitchState.Aiming) return false;
    if (permission != null && permission.WallSwitchBlockReason != AimEntryBlockReason.None) {
      currentEvaluation = EvaluateAtCursor();
      preview?.Show(currentEvaluation);
      return false;
    }
    if (!currentEvaluation.IsValid) {
      if (verboseLogging) Debug.Log($"[WallSwitch] Confirmation ignored: {currentEvaluation.FailureReason}.", this);
      return false;
    }

    WallSwitchEvaluation acceptedEvaluation = currentEvaluation;
    state = SwitchState.Executing;
    SwitchStarted?.Invoke();
    RestoreCursorState();
    Time.timeScale = 0f;
    preview?.LockForExecution(acceptedEvaluation);
    StartCoroutine(ExecuteSwitch(acceptedEvaluation));
    return true;
  }

  public void CancelForDeath(bool restoreCameraImmediately = false) {
    if (state == SwitchState.Idle) return;
    StopAllCoroutines();
    preview?.Hide();
    if (playerRenderer != null) playerRenderer.enabled = true;
    if (followController != null) followController.enabled = followWasEnabled;
    UnlockPlayerActions();
    RestoreCursorState();
    RestoreTimeScale();
    if (restoreCameraImmediately && cameraTransform != null) {
      if (cameraRoutine != null) StopCoroutine(cameraRoutine);
      cameraRoutine = null;
      cameraTransform.localPosition = normalCameraLocalPosition;
      cameraTransform.localRotation = normalCameraLocalRotation;
    } else {
      StartCameraBlend(normalCameraLocalPosition, normalCameraLocalRotation, cameraReturnDuration);
    }
    state = SwitchState.Idle;
    currentEvaluation = WallSwitchEvaluation.Empty;
  }

  private AimEntryBlockReason GetAimEntryBlockReason() {
    if (SceneTransitionManager.IsDeathSequenceActive || deathSequence != null && deathSequence.IsDying)
      return AimEntryBlockReason.Dead;
    if (SceneTransitionManager.IsGamePaused) return AimEntryBlockReason.Paused;
    if (!enabled || state != SwitchState.Idle) return AimEntryBlockReason.InvalidConfiguration;
    if (cameraRoutine != null) return AimEntryBlockReason.CameraTransitioning;
    if (network == null || followController == null || aimCamera == null || cameraTransform == null)
      return AimEntryBlockReason.InvalidConfiguration;
    if (followController.currentLine == null) return AimEntryBlockReason.NoCurrentPath;
    if (followController.IsTurning) return AimEntryBlockReason.PlayerTurning;
    if (distractionController != null &&
        (distractionController.IsAiming || distractionController.IsCameraTransitioning))
      return AimEntryBlockReason.OtherAimModeActive;
    return permission != null ? permission.WallSwitchBlockReason : AimEntryBlockReason.None;
  }

  private void HandleRejectedEntry(AimEntryBlockReason reason) {
    if (reason == AimEntryBlockReason.InvalidConfiguration && !loggedInvalidConfiguration) {
      loggedInvalidConfiguration = true;
      Debug.LogError("[WallSwitch] Aim entry failed because a required reference is not configured.", this);
    }
    if (reason.ShouldPlayFeedback()) rejectionFeedback?.PlayRejectedAction();
    if (verboseLogging) Debug.Log($"[WallSwitch] Aim entry rejected: {reason}.", this);
  }

  private WallSwitchEvaluation EvaluateAtCursor() {
    Vector2 cursor = Mouse.current != null
      ? Mouse.current.position.ReadValue()
      : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    Vector3 cursorWorld = ProjectCursorMarker(cursor);

    if (permission != null && permission.WallSwitchBlockReason != AimEntryBlockReason.None)
      return BuildInvalid(WallSwitchFailureReason.PlayerUnavailable, cursorWorld);

    LinePath sourcePath = followController.currentLine;
    int sourceStrand = followController.currentStrand;
    float sourceDistance = followController.DistanceAlongLine;
    bool found = network.TryFindDestination(
      aimCamera,
      cursor,
      sourcePath,
      sourceStrand,
      sourceDistance,
      parallelToleranceDegrees,
      minimumDestinationPlaneSeparation,
      destinationPointMargin,
      wallObstructionLayers,
      wallPathSearchRadius,
      wallSideTolerance,
      out WallSwitchPathNetwork.DestinationCandidate candidate,
      out float nonParallelSurfaceDistance,
      out WallSwitchPathNetwork.SelectionDiagnostics selectionDiagnostics);
    lastSelectionDiagnostics = selectionDiagnostics;

    if (!found) {
      WallSwitchFailureReason reason = nonParallelSurfaceDistance <= wallPathSearchRadius
        ? WallSwitchFailureReason.PathsNotParallel
        : WallSwitchFailureReason.NoAuthoredPath;
      return BuildInvalid(reason, cursorWorld);
    }

    Vector3 destinationRoot = followController.GetRootPositionForFeetAt(candidate.Point);
    Vector3 originOffset = GetTrajectoryOrigin() - transform.position;
    Vector3 trajectoryStart = GetTrajectoryOrigin();
    trajectoryStart.y = originWorldY;
    Vector3 trajectoryEnd = destinationRoot + originOffset;
    trajectoryEnd.y = destinationWorldY;
    // The raw cursor projection is authoritative for selecting a valid surface. Once a valid
    // destination exists, presentation uses the authored destination height.
    cursorWorld.y = destinationWorldY;
    float distance = Vector3.Distance(followController.FeetPosition, candidate.Point);
    WallSwitchFailureReason failure = WallSwitchFailureReason.None;
    if (distance < minimumSwitchDistance) failure = WallSwitchFailureReason.DestinationTooClose;
    else if (distance > maximumSwitchDistance) failure = WallSwitchFailureReason.DestinationTooFar;

    List<GuardWallSwitchTarget> takedowns = new();
    List<GuardWallSwitchTarget> blockingGuards = new();
    Object blockingObject = null;
    Vector3 blockingPoint = Vector3.zero;
    BlockingCategory blockingCategory = BlockingCategory.None;
    EvaluateAuthoredSurfaces(
      trajectoryStart,
      trajectoryEnd,
      candidate.Point,
      candidate.Direction,
      ref cursorWorld,
      ref blockingObject,
      ref blockingPoint,
      ref blockingCategory);
    EvaluateWallObstruction(
      trajectoryStart,
      trajectoryEnd,
      ref blockingObject,
      ref blockingPoint,
      ref blockingCategory);
    EvaluateTrajectoryInteractions(
      trajectoryStart,
      trajectoryEnd,
      takedowns,
      blockingGuards,
      ref blockingObject,
      ref blockingPoint,
      ref blockingCategory);
    EvaluateBlockingVisionFields(
      trajectoryStart,
      trajectoryEnd,
      blockingGuards,
      ref blockingObject,
      ref blockingPoint,
      ref blockingCategory);
    lastBlockingCategory = blockingCategory;
    if (blockingObject != null) failure = WallSwitchFailureReason.Blocked;
    if (failure != WallSwitchFailureReason.None) takedowns.Clear();

    takedowns.Sort((left, right) => left.GetTrajectoryProgress(trajectoryStart, trajectoryEnd)
      .CompareTo(right.GetTrajectoryProgress(trajectoryStart, trajectoryEnd)));

    return new WallSwitchEvaluation(
      failure,
      sourcePath,
      sourceStrand,
      candidate.Path,
      candidate.Strand,
      candidate.Distance,
      candidate.Point,
      destinationRoot,
      trajectoryStart,
      trajectoryEnd,
      cursorWorld,
      candidate.CursorDistancePixels,
      blockingObject,
      blockingPoint,
      takedowns,
      blockingGuards);
  }

  private void EvaluateWallObstruction(
    Vector3 start,
    Vector3 end,
    ref Object blockingObject,
    ref Vector3 blockingPoint,
    ref BlockingCategory blockingCategory) {
    if (blockingObject != null) return;
    if (wallObstructionLayers.value == 0) return;

    Vector3 delta = end - start;
    float length = delta.magnitude;
    if (length <= 0.0001f) return;

    Vector3 direction = delta / length;
    float inset = Mathf.Min(wallEndpointInset, length * 0.45f);
    Vector3 rayStart = start + direction * inset;
    float rayLength = Mathf.Max(0f, length - inset * 2f);
    if (rayLength <= 0f) return;

    if (!Physics.Raycast(
          rayStart,
          direction,
          out RaycastHit hit,
          rayLength,
          wallObstructionLayers,
          QueryTriggerInteraction.Ignore)) return;

    blockingObject = hit.collider;
    blockingPoint = hit.point;
    blockingCategory = BlockingCategory.WallObstruction;
  }

  private void EvaluateTrajectoryInteractions(
    Vector3 start,
    Vector3 end,
    List<GuardWallSwitchTarget> takedowns,
    List<GuardWallSwitchTarget> blockingGuards,
    ref Object blockingObject,
    ref Vector3 blockingPoint,
    ref BlockingCategory blockingCategory) {
    if (interactionLayers.value == 0) return;

    uniqueTargets.Clear();
    uniqueBlockers.Clear();
    int hitCount = Physics.OverlapCapsuleNonAlloc(
      start,
      end,
      trajectoryRadius,
      interactionHits,
      interactionLayers,
      QueryTriggerInteraction.Collide);

    for (int i = 0; i < hitCount; i++) {
      Collider hit = interactionHits[i];
      if (hit == null || hit.transform.IsChildOf(transform)) continue;

      GuardWallSwitchTarget guard = hit.GetComponentInParent<GuardWallSwitchTarget>();
      if (guard != null && uniqueTargets.Add(guard)) {
        WallSwitchTargetDisposition disposition = guard.EvaluateDisposition();
        if (disposition == WallSwitchTargetDisposition.Vulnerable) takedowns.Add(guard);
        else if (disposition == WallSwitchTargetDisposition.Blocking) {
          blockingGuards.Add(guard);
          if (blockingObject == null) {
            blockingObject = guard;
            blockingPoint = ClosestPointOnSegment(guard.transform.position, start, end);
            blockingCategory = BlockingCategory.GuardOrExplicitBlocker;
          }
        }
      }

      WallSwitchBlocker blocker = hit.GetComponentInParent<WallSwitchBlocker>();
      if (blocker == null || !blocker.IsBlocking || !uniqueBlockers.Add(blocker)) continue;
      if (blockingObject == null) {
        blockingObject = blocker;
        blockingPoint = ClosestPointOnSegment(blocker.transform.position, start, end);
        blockingCategory = BlockingCategory.GuardOrExplicitBlocker;
      }
    }
  }

  private void EvaluateBlockingVisionFields(
    Vector3 start,
    Vector3 end,
    List<GuardWallSwitchTarget> blockingGuards,
    ref Object blockingObject,
    ref Vector3 blockingPoint,
    ref BlockingCategory blockingCategory) {
    foreach (GuardWallSwitchTarget guard in GuardWallSwitchTarget.ActiveTargets) {
      if (guard == null || !guard.IsAlive) continue;
      if (!guard.TryGetBlockingVisionIntersection(start, end, trajectoryRadius, out Vector3 intersection)) continue;

      if (!blockingGuards.Contains(guard)) blockingGuards.Add(guard);
      if (blockingObject != null) continue;
      blockingObject = guard;
      blockingPoint = intersection;
      blockingCategory = BlockingCategory.GuardVisionField;
    }
  }

  private IEnumerator ExecuteSwitch(WallSwitchEvaluation evaluation) {
    Vector3 direction = evaluation.TrajectoryEnd - evaluation.TrajectoryStart;
    Quaternion quarterTurn = Quaternion.AngleAxis(90f, Vector3.up);
    Quaternion sideTurn = Quaternion.AngleAxis(180f, Vector3.up);
    Vector3 corridorCenter = Vector3.Lerp(evaluation.TrajectoryStart, evaluation.TrajectoryEnd, 0.5f);
    Vector3 corridorCenterLocalOffset = transform.InverseTransformPoint(corridorCenter);
    corridorCenterLocalOffset.y = 0f;
    Vector3 sideAimPosition = corridorCenterLocalOffset + quarterTurn * activeAimCameraLocalPosition;
    Vector3 oppositeAimPosition = sideTurn * activeAimCameraLocalPosition;
    Vector3 oppositeNormalPosition = sideTurn * normalCameraLocalPosition;
    Quaternion sideRotation = quarterTurn * normalCameraLocalRotation;
    Quaternion oppositeRotation = sideTurn * normalCameraLocalRotation;
    if (playerRenderer != null) playerRenderer.enabled = false;
    SpawnInk(departureInkPrefab, evaluation.TrajectoryStart, direction);
    StartCameraOrbit(
      sideAimPosition,
      sideRotation,
      cameraSideSwitchDuration,
      90f,
      Vector3.zero,
      corridorCenterLocalOffset);
    yield return WaitUnpausedRealtime(Mathf.Max(departureHoldDuration, cameraSideSwitchDuration));
    GameObject traveler = SpawnInk(travelingInkPrefab, evaluation.TrajectoryStart, direction);
    int targetIndex = 0;
    float elapsed = 0f;
    while (elapsed < inkTravelDuration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }

      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / inkTravelDuration);
      Vector3 position = Vector3.Lerp(evaluation.TrajectoryStart, evaluation.TrajectoryEnd, progress);
      if (traveler != null) traveler.transform.position = position;
      preview?.SetExecutionProgress(progress);

      bool hitSomething = false;
      while (targetIndex < evaluation.TakedownTargets.Count) {
        GuardWallSwitchTarget target = evaluation.TakedownTargets[targetIndex];
        float targetProgress = target != null
          ? target.GetTrajectoryProgress(evaluation.TrajectoryStart, evaluation.TrajectoryEnd)
          : 0f;
        if (targetProgress > progress + 0.001f) break;
        if (target != null && target.IsAlive) {
          target.BeginTakedown(direction);
          hitSomething = true;
        }
        targetIndex++;
      }

      if (hitSomething && hitStopDuration > 0f)
        yield return PlayTakedownImpact(hitStopDuration);
      yield return null;
    }

    while (targetIndex < evaluation.TakedownTargets.Count) {
      GuardWallSwitchTarget target = evaluation.TakedownTargets[targetIndex++];
      if (target != null && target.IsAlive) target.BeginTakedown(direction);
    }

    StartCameraOrbit(
      oppositeAimPosition,
      oppositeRotation,
      cameraSideSwitchDuration,
      90f,
      corridorCenterLocalOffset,
      Vector3.zero);
    yield return WaitUnpausedRealtime(cameraSideSwitchDuration);
    TeleportToEvaluation(evaluation);
    SpawnInk(arrivalInkPrefab, evaluation.TrajectoryEnd, -direction);
    yield return WaitUnpausedRealtime(reappearDelay);

    if (playerRenderer != null) playerRenderer.enabled = true;
    preview?.Hide();
    yield return WaitUnpausedRealtime(cameraReturnDelayAfterArrival);
    StartCameraBlend(oppositeNormalPosition, oppositeRotation, cameraReturnDuration);
    yield return WaitUnpausedRealtime(cameraReturnDuration);
    followController.enabled = followWasEnabled;
    UnlockPlayerActions();
    RestoreCursorState();
    RestoreTimeScale();
    currentEvaluation = WallSwitchEvaluation.Empty;
    state = SwitchState.Idle;
    SwitchCompleted?.Invoke();
    if (verboseLogging) Debug.Log("[WallSwitch] Switch completed.", this);
  }

  private void TeleportToEvaluation(WallSwitchEvaluation evaluation) {
    bool controllerWasEnabled = characterController != null && characterController.enabled;
    if (controllerWasEnabled) characterController.enabled = false;
    transform.position = evaluation.DestinationRoot;
    if (controllerWasEnabled) characterController.enabled = true;
    followController.SetLine(
      evaluation.DestinationPath,
      evaluation.DestinationStrand,
      evaluation.DestinationDistance);
    LightExposure exposure = GetComponent<LightExposure>();
    if (exposure != null) exposure.RefreshExposure();
  }

  private void ExitAimWithoutSwitch() {
    preview?.Hide();
    followController.enabled = followWasEnabled;
    UnlockPlayerActions();
    RestoreCursorState();
    RestoreTimeScale();
    StartCameraBlend(normalCameraLocalPosition, normalCameraLocalRotation, cameraReturnDuration);
    currentEvaluation = WallSwitchEvaluation.Empty;
    state = SwitchState.Idle;
    AimCancelled?.Invoke();
  }

  private WallSwitchEvaluation BuildInvalid(WallSwitchFailureReason reason, Vector3 cursorWorld) {
    return new WallSwitchEvaluation(
      reason,
      followController != null ? followController.currentLine : null,
      followController != null ? followController.currentStrand : -1,
      null,
      -1,
      0f,
      Vector3.zero,
      transform.position,
      GetTrajectoryOrigin(),
      cursorWorld,
      cursorWorld,
      float.PositiveInfinity,
      null,
      Vector3.zero,
      null,
      null);
  }

  private Vector3 ProjectCursorMarker(Vector2 cursor) {
    if (aimCamera == null) return followController != null ? followController.FeetPosition : transform.position;
    Ray ray = aimCamera.ScreenPointToRay(cursor);
    if (markerProjectionLayers.value != 0 && Physics.Raycast(
          ray,
          out RaycastHit surfaceHit,
          aimCamera.farClipPlane,
          markerProjectionLayers,
          QueryTriggerInteraction.Ignore)) {
      return surfaceHit.point + surfaceHit.normal * markerSurfaceOffset;
    }
    float depth = Mathf.Max(invalidMarkerCameraDistance, aimCamera.nearClipPlane + 0.05f);
    return aimCamera.ScreenToWorldPoint(new Vector3(cursor.x, cursor.y, depth));
  }

  private void EvaluateAuthoredSurfaces(
    Vector3 trajectoryStart,
    Vector3 trajectoryEnd,
    Vector3 destinationPoint,
    Vector3 destinationDirection,
    ref Vector3 previewPoint,
    ref Object blockingObject,
    ref Vector3 blockingPoint,
    ref BlockingCategory blockingCategory) {
    Vector3 pathDirection = destinationDirection;
    pathDirection.y = 0f;
    if (pathDirection.sqrMagnitude < 0.0001f) return;
    pathDirection.Normalize();

    Vector3 frontNormal = Vector3.Cross(Vector3.up, pathDirection).normalized;
    if (Vector3.Dot(trajectoryStart - destinationPoint, frontNormal) < 0f)
      frontNormal = -frontNormal;

    Vector3 previewDelta = previewPoint - trajectoryStart;
    float previewLength = previewDelta.magnitude;
    Ray previewRay = previewLength > 0.0001f
      ? new Ray(trajectoryStart, previewDelta / previewLength)
      : default;

    Vector3 switchDelta = trajectoryEnd - trajectoryStart;
    float switchLength = switchDelta.magnitude;
    Ray switchRay = switchLength > 0.0001f
      ? new Ray(trajectoryStart, switchDelta / switchLength)
      : default;

    float nearestProjectionDistance = float.PositiveInfinity;
    RaycastHit projectionHit = default;
    if (previewLength > 0.0001f) {
      int hitCount = Physics.RaycastNonAlloc(
        previewRay,
        authoredSurfaceHits,
        previewLength,
        wallObstructionLayers,
        QueryTriggerInteraction.Ignore);
      for (int i = 0; i < hitCount; i++) {
        RaycastHit surfaceHit = authoredSurfaceHits[i];
        if (!WallSwitchSurface.TryFind(surfaceHit.collider, out _)) continue;
        float frontmostDistance = GetFrontmostPlaneDistance(
          surfaceHit.collider.bounds,
          destinationPoint,
          frontNormal);
        if (frontmostDistance > surfacePlaneTolerance ||
            surfaceHit.distance >= nearestProjectionDistance) continue;
        nearestProjectionDistance = surfaceHit.distance;
        projectionHit = surfaceHit;
      }
    }

    if (nearestProjectionDistance < float.PositiveInfinity)
      previewPoint = projectionHit.point + projectionHit.normal * markerSurfaceOffset;

    float nearestBlockingDistance = float.PositiveInfinity;
    Collider blockingSurface = null;
    if (switchLength > 0.0001f) {
      int hitCount = Physics.RaycastNonAlloc(
        switchRay,
        authoredSurfaceHits,
        switchLength,
        wallObstructionLayers,
        QueryTriggerInteraction.Ignore);
      for (int i = 0; i < hitCount; i++) {
        RaycastHit obstructionHit = authoredSurfaceHits[i];
        if (!WallSwitchSurface.TryFind(obstructionHit.collider, out _)) continue;
        float frontmostDistance = GetFrontmostPlaneDistance(
          obstructionHit.collider.bounds,
          destinationPoint,
          frontNormal);
        if (frontmostDistance <= surfacePlaneTolerance ||
            obstructionHit.distance >= nearestBlockingDistance) continue;
        nearestBlockingDistance = obstructionHit.distance;
        blockingSurface = obstructionHit.collider;
        blockingPoint = obstructionHit.point;
      }
    }

    if (blockingSurface != null) {
      blockingObject = blockingSurface;
      blockingCategory = BlockingCategory.AuthoredSurface;
    }
  }

  private static float GetFrontmostPlaneDistance(
    Bounds bounds,
    Vector3 destinationPoint,
    Vector3 frontNormal) {
    float projectedExtent = Mathf.Abs(frontNormal.x) * bounds.extents.x +
                            Mathf.Abs(frontNormal.y) * bounds.extents.y +
                            Mathf.Abs(frontNormal.z) * bounds.extents.z;
    return Vector3.Dot(bounds.center - destinationPoint, frontNormal) + projectedExtent;
  }

  private Vector3 GetTrajectoryOrigin() {
    return transform.TransformPoint(switchOriginLocalOffset);
  }

  private void ResolveLocalReferences() {
    if (followController == null) followController = GetComponent<LineFollowController>();
    if (characterController == null) characterController = GetComponent<CharacterController>();
    if (playerInput == null) playerInput = GetComponent<PlayerInput>();
    if (playerRenderer == null) playerRenderer = GetComponent<SpriteRenderer>();
    if (preview == null) preview = GetComponent<WallSwitchPreview>();
    if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>(true);
    if (cameraTransform == null && aimCamera != null) cameraTransform = aimCamera.transform;
    if (distractionController == null) distractionController = GetComponent<DistractionController>();
    if (deathSequence == null) deathSequence = GetComponent<PlayerDeathSequence>();
    if (rejectionFeedback == null) rejectionFeedback = GetComponentInChildren<RejectedAimCameraFeedback>(true);
    if (permission == null && permissionSource is IWallSwitchPermission assignedPermission)
      permission = assignedPermission;
  }

  private void CaptureAuthoredCameraPose() {
    if (hasAuthoredCameraPose || cameraTransform == null) return;
    authoredCameraLocalPosition = cameraTransform.localPosition;
    authoredCameraLocalRotation = cameraTransform.localRotation;
    hasAuthoredCameraPose = true;
  }

  private void GetAuthoredCameraPoseForCurrentSide(out Vector3 position, out Quaternion rotation) {
    CaptureAuthoredCameraPose();
    if (!hasAuthoredCameraPose) {
      position = cameraTransform != null ? cameraTransform.localPosition : Vector3.zero;
      rotation = cameraTransform != null ? cameraTransform.localRotation : Quaternion.identity;
      return;
    }

    Vector3 authoredHorizontal = new(authoredCameraLocalPosition.x, 0f, authoredCameraLocalPosition.z);
    Vector3 currentHorizontal = cameraTransform != null
      ? new Vector3(cameraTransform.localPosition.x, 0f, cameraTransform.localPosition.z)
      : authoredHorizontal;
    bool oppositeSide = authoredHorizontal.sqrMagnitude > 0.0001f
                        && currentHorizontal.sqrMagnitude > 0.0001f
                        && Vector3.Dot(authoredHorizontal, currentHorizontal) < 0f;
    Quaternion sideTurn = oppositeSide ? Quaternion.AngleAxis(180f, Vector3.up) : Quaternion.identity;
    position = sideTurn * authoredCameraLocalPosition;
    rotation = sideTurn * authoredCameraLocalRotation;
  }

  private Vector3 GetAimPositionForCurrentSide() {
    Vector3 currentHorizontal = new(normalCameraLocalPosition.x, 0f, normalCameraLocalPosition.z);
    Vector3 authoredHorizontal = new(aimingCameraLocalPosition.x, 0f, aimingCameraLocalPosition.z);
    if (currentHorizontal.sqrMagnitude < 0.0001f || authoredHorizontal.sqrMagnitude < 0.0001f)
      return aimingCameraLocalPosition;

    return Vector3.Dot(currentHorizontal, authoredHorizontal) >= 0f
      ? aimingCameraLocalPosition
      : Quaternion.AngleAxis(180f, Vector3.up) * aimingCameraLocalPosition;
  }

  private void RestoreTimeScale() {
    if (!ownsTimeScale) return;
    Time.timeScale = timeScaleBeforeAim;
    ownsTimeScale = false;
  }

  private void CaptureAndReleaseCursor() {
    if (!ownsCursorState) {
      cursorLockBeforeAim = Cursor.lockState;
      cursorVisibleBeforeAim = Cursor.visible;
      ownsCursorState = true;
    }
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
  }

  private void RestoreCursorState() {
    if (!ownsCursorState) return;
    Cursor.lockState = cursorLockBeforeAim;
    Cursor.visible = cursorVisibleBeforeAim;
    ownsCursorState = false;
  }

  private void EnsurePlayerActionsLocked() {
    if (playerInput == null || playerInput.actions == null) return;
    InputActionMap playerMap = playerInput.actions.FindActionMap("Player", false);
    if (playerMap == null || !playerMap.enabled) return;

    for (int i = 0; i < ActionsLockedWhileSwitching.Length; i++) {
      InputAction action = playerMap.FindAction(ActionsLockedWhileSwitching[i], false);
      if (action == null || !action.enabled) continue;
      action.Disable();
      if (!lockedInputActions.Contains(action)) lockedInputActions.Add(action);
    }
  }

  private void UnlockPlayerActions() {
    bool playerMapIsCurrent = playerInput != null
                              && playerInput.currentActionMap != null
                              && playerInput.currentActionMap.name == "Player";
    if (playerMapIsCurrent) {
      for (int i = 0; i < lockedInputActions.Count; i++) {
        InputAction action = lockedInputActions[i];
        if (action != null && !action.enabled) action.Enable();
      }
    }
    lockedInputActions.Clear();
  }

  private void StartCameraBlend(Vector3 targetPosition, Quaternion targetRotation, float duration) {
    if (cameraTransform == null) return;
    if (cameraRoutine != null) StopCoroutine(cameraRoutine);
    cameraRoutine = StartCoroutine(CameraBlendRoutine(targetPosition, targetRotation, duration));
  }

  private void StartCameraOrbit(
    Vector3 targetPosition,
    Quaternion targetRotation,
    float duration,
    float orbitDegrees,
    Vector3 startCenter,
    Vector3 targetCenter) {
    if (cameraTransform == null) return;
    if (cameraRoutine != null) StopCoroutine(cameraRoutine);
    cameraRoutine = StartCoroutine(CameraOrbitRoutine(
      targetPosition,
      targetRotation,
      duration,
      orbitDegrees,
      startCenter,
      targetCenter));
  }

  private IEnumerator CameraOrbitRoutine(
    Vector3 targetPosition,
    Quaternion targetRotation,
    float duration,
    float orbitDegrees,
    Vector3 startCenter,
    Vector3 targetCenter) {
    Vector3 startPosition = cameraTransform.localPosition;
    Quaternion startRotation = cameraTransform.localRotation;
    Vector3 startRelative = startPosition - startCenter;
    Vector3 targetRelative = targetPosition - targetCenter;
    Vector3 startHorizontal = new(startRelative.x, 0f, startRelative.z);
    float startRadius = startHorizontal.magnitude;
    Vector3 startDirection = startRadius > 0.0001f ? startHorizontal / startRadius : Vector3.back;
    float targetRadius = new Vector2(targetRelative.x, targetRelative.z).magnitude;
    float elapsed = 0f;

    if (duration <= 0f) {
      cameraTransform.localPosition = targetPosition;
      cameraTransform.localRotation = targetRotation;
      cameraRoutine = null;
      yield break;
    }

    while (elapsed < duration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }

      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      Vector3 direction = Quaternion.AngleAxis(orbitDegrees * t, Vector3.up) * startDirection;
      float radius = Mathf.Lerp(startRadius, targetRadius, t);
      Vector3 center = Vector3.Lerp(startCenter, targetCenter, t);
      float relativeHeight = Mathf.Lerp(startRelative.y, targetRelative.y, t);
      cameraTransform.localPosition = center + direction * radius + Vector3.up * relativeHeight;
      cameraTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
      yield return null;
    }

    cameraTransform.localPosition = targetPosition;
    cameraTransform.localRotation = targetRotation;
    cameraRoutine = null;
  }

  private IEnumerator CameraBlendRoutine(Vector3 targetPosition, Quaternion targetRotation, float duration) {
    Vector3 startPosition = cameraTransform.localPosition;
    Quaternion startRotation = cameraTransform.localRotation;
    float elapsed = 0f;
    if (duration <= 0f) {
      cameraTransform.localPosition = targetPosition;
      cameraTransform.localRotation = targetRotation;
      cameraRoutine = null;
      yield break;
    }

    while (elapsed < duration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }
      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      cameraTransform.localPosition = Vector3.Lerp(startPosition, targetPosition, t);
      cameraTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
      yield return null;
    }
    cameraTransform.localPosition = targetPosition;
    cameraTransform.localRotation = targetRotation;
    cameraRoutine = null;
  }

  private IEnumerator WaitUnpausedRealtime(float duration) {
    float elapsed = 0f;
    while (elapsed < duration) {
      if (!SceneTransitionManager.IsGamePaused) elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
  }

  private IEnumerator PlayTakedownImpact(float duration) {
    if (cameraTransform == null || duration <= 0f) yield break;

    Vector3 basePosition = cameraTransform.localPosition;
    Quaternion baseRotation = cameraTransform.localRotation;
    float elapsed = 0f;
    while (elapsed < duration) {
      if (SceneTransitionManager.IsGamePaused) {
        yield return null;
        continue;
      }

      elapsed += Time.unscaledDeltaTime;
      float normalized = Mathf.Clamp01(elapsed / duration);
      float envelope = 1f - normalized * normalized * (3f - 2f * normalized);
      float phase = elapsed * takedownImpulseFrequency * Mathf.PI * 2f;
      float horizontal = Mathf.Sin(phase);
      float vertical = Mathf.Sin(phase * 1.73f + 1.1f);

      cameraTransform.localPosition = basePosition
                                      + new Vector3(horizontal, vertical, 0f)
                                      * (takedownImpulsePosition * envelope);
      cameraTransform.localRotation = baseRotation * Quaternion.Euler(
        vertical * takedownImpulseRotation * envelope,
        horizontal * takedownImpulseRotation * 0.45f * envelope,
        horizontal * takedownImpulseRotation * envelope);
      yield return null;
    }

    cameraTransform.localPosition = basePosition;
    cameraTransform.localRotation = baseRotation;
  }

  private GameObject SpawnInk(GameObject prefab, Vector3 position, Vector3 direction) {
    if (prefab == null) return null;
    Quaternion rotation = direction.sqrMagnitude > 0.0001f
      ? Quaternion.LookRotation(direction.normalized, Vector3.up)
      : Quaternion.identity;
    GameObject instance;
    try {
      instance = Instantiate(prefab, position, rotation);
    } catch (System.Exception exception) {
      // A missing cosmetic effect must never strand execution with the player hidden and
      // Time.timeScale at zero. Continue the switch without this VFX and report the bad asset.
      Debug.LogError($"[WallSwitch] Could not instantiate an ink VFX. The switch will continue without it.\n{exception}", this);
      return null;
    }
    PauseAwareUnscaledParticles.Configure(instance);
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      main.useUnscaledTime = true;
      particles[i].Play(true);
    }
    StartCoroutine(DestroyInkAfterUnpausedRealtime(instance, GetInkLifetime(particles)));
    return instance;
  }

  private static float GetInkLifetime(ParticleSystem[] particles) {
    float lifetime = 1.5f;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      lifetime = Mathf.Max(lifetime, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
    }
    return lifetime + 0.25f;
  }

  private IEnumerator DestroyInkAfterUnpausedRealtime(GameObject instance, float lifetime) {
    yield return WaitUnpausedRealtime(lifetime);
    if (instance != null) Destroy(instance);
  }

  private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end) {
    Vector3 delta = end - start;
    float denominator = delta.sqrMagnitude;
    float t = denominator > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - start, delta) / denominator) : 0f;
    return start + delta * t;
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    if (!drawDebugGizmos || currentEvaluation == null || currentEvaluation.DestinationPath == null) return;
    Gizmos.color = currentEvaluation.IsValid ? Color.green : Color.red;
    Gizmos.DrawWireSphere(currentEvaluation.TrajectoryStart, trajectoryRadius);
    Gizmos.DrawWireSphere(currentEvaluation.TrajectoryEnd, trajectoryRadius);
    Gizmos.DrawLine(currentEvaluation.TrajectoryStart, currentEvaluation.TrajectoryEnd);
    if (currentEvaluation.BlockingObject != null) Gizmos.DrawSphere(currentEvaluation.BlockingPoint, trajectoryRadius * 1.25f);
  }
#endif
}
