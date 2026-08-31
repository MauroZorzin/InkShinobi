using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum DistractionSupplyMode { Infinite, InventoryItem }

/// <summary>Independent toggle-to-aim distraction ability.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineFollowController), typeof(PlayerInput))]
public sealed class DistractionController : MonoBehaviour {
  private enum AimState { Idle, Aiming }

  [Header("Supply")]
  [Tooltip("Infinite throws the configured projectile and uses cooldown. Inventory Item throws and consumes the carried Throwable item and ignores cooldown.")]
  [SerializeField] private DistractionSupplyMode supplyMode = DistractionSupplyMode.Infinite;
  [SerializeField] private PlayerInventory inventory;
  [FormerlySerializedAs("projectilePrefab")]
  [SerializeField] private ThrownDistraction infiniteProjectilePrefab;

  [Header("Targeting")]
  [SerializeField] private Camera aimCamera;
  [Tooltip("Authored child transform used as the rock's actual release point.")]
  [SerializeField] private Transform throwAnchor;
  [Tooltip("Height of the lateral throw-anchor segment relative to the player root. This is the authoritative vertical position used while aiming.")]
  [SerializeField] private float throwAnchorHeight = 0.12f;
  [Tooltip("Maximum distance the release point can move left or right from its authored position.")]
  [SerializeField, Min(0f)] private float maximumAnchorOffset = 0.4f;
  [Tooltip("Number of lateral release positions tested. Odd values include the centered anchor.")]
  [SerializeField, Range(3, 21)] private int anchorSamples = 9;
  [Tooltip("World units per unscaled second used to move the release point toward the selected solution.")]
  [SerializeField, Min(0.01f)] private float anchorMovementSpeed = 3f;
  [Tooltip("Penalty for moving the release point away from the player's center.")]
  [SerializeField, Min(0f)] private float centeredAnchorPreference = 1f;
  [Tooltip("Penalty for changing sides between successive solutions. Higher values reduce anchor oscillation.")]
  [SerializeField, Min(0f)] private float anchorContinuityPreference = 0.35f;
  [SerializeField] private LayerMask landingSurfaceLayers = 1 << 11;
  [SerializeField] private LayerMask trajectoryObstructionLayers = (1 << 8) | (1 << 11) | (1 << 16);
  [SerializeField, Min(0.1f)] private float cursorRayDistance = 60f;
  [Tooltip("Minimum cursor movement in screen pixels before the world target is recalculated after the aim camera has settled.")]
  [SerializeField, Min(0f)] private float cursorMovementThreshold = 0.5f;
  [SerializeField, Min(0f)] private float minimumThrowDistance = 0.75f;
  [SerializeField, Min(0.1f)] private float maximumThrowDistance = 10f;
  [SerializeField, Min(0.05f)] private float apexHeight = 1.5f;
  [SerializeField, Min(0.1f)] private float maximumThrowSpeed = 16f;
  [SerializeField, Range(0f, 1f)] private float minimumSurfaceNormalY = 0.45f;
  [SerializeField, Range(6, 48)] private int obstructionSamples = 22;
  [Tooltip("Maximum world-space length of each collision-test segment along the curved preview. Smaller values catch corner-grazing throws more accurately.")]
  [SerializeField, Min(0.02f)] private float maximumObstructionSegmentLength = 0.08f;

  [Header("Cooldown")]
  [SerializeField, Min(0f)] private float cooldown = 2f;

  [Header("Aim presentation")]
  [SerializeField] private Transform cameraTransform;
  [SerializeField] private Vector3 aimingCameraLocalPosition = new(0f, 0.5f, -3.75f);
  [SerializeField, Range(0.01f, 1f)] private float aimingTimeScale = 0.06f;
  [SerializeField, Min(0f)] private float cameraAimDuration = 0.35f;
  [SerializeField, Min(0f)] private float cameraReturnDuration = 0.3f;
  [SerializeField] private DistractionTrajectoryPreview preview;
  [Tooltip("Optional asset-free ink sleeve connecting the player body to the moving throw anchor.")]
  [SerializeField] private ProceduralInkArm inkArm;

  [Header("Player references")]
  [SerializeField] private PlayerInput playerInput;
  [SerializeField] private LineFollowController movement;
  [SerializeField] private PlayerStealthController stealth;
  [SerializeField] private PlayerDeathSequence deathSequence;
  [SerializeField] private WallSwitchController wallSwitch;
  [SerializeField] private RejectedAimCameraFeedback rejectionFeedback;
  [Tooltip("Sprite mirrored toward the moving throw anchor while distraction aiming is active. The aim point is used only while the anchor is centered.")]
  [SerializeField] private SpriteRenderer playerRenderer;
  [Tooltip("Horizontal camera-space distance from the player's symmetry axis inside which the anchor is considered centered and the aim point decides facing instead.")]
  [SerializeField, Min(0f)] private float aimFacingDeadZone = 0.01f;
  [SerializeField] private bool verboseLogging;

  private static readonly string[] LockedActions = {
    "Move", "RotateRight", "RotateLeft", "Takedown",
    "Interact", "Vision", "Confirm", "Look", "Drop"
  };

  private readonly List<InputAction> lockedActions = new();
  private readonly RaycastHit[] obstructionHits = new RaycastHit[16];
  private AimState state;
  private DistractionThrowEvaluation evaluation = DistractionThrowEvaluation.Empty;
  private float cooldownRemaining;
  private bool movementWasEnabled;
  private bool ownsTimeScale;
  private float timeScaleBeforeAim = 1f;
  private Vector3 normalCameraLocalPosition;
  private Quaternion normalCameraLocalRotation;
  private Vector3 authoredCameraLocalPosition;
  private Quaternion authoredCameraLocalRotation;
  private bool hasAuthoredCameraPose;
  private Coroutine cameraRoutine;
  private CursorLockMode cursorLockBeforeAim;
  private bool cursorVisibleBeforeAim;
  private Vector2 lastEvaluatedCursorPosition;
  private bool hasEvaluatedCursorPosition;
  private Vector3 anchorRestLocalPosition;
  private float currentAnchorOffset;
  private float desiredAnchorOffset;
  private bool hasCursorTarget;
  private Vector3 cursorTarget;
  private Vector3 cursorTargetNormal = Vector3.up;
  private Collider cursorTargetCollider;
  private bool flipBeforeAim;
  private bool hasAimFacingSnapshot;
  private Collider[] throwerColliders = System.Array.Empty<Collider>();
  private bool loggedInvalidConfiguration;

  public bool IsAiming => state == AimState.Aiming;
  public bool IsCameraTransitioning => cameraRoutine != null;
  public float CooldownRemaining => supplyMode == DistractionSupplyMode.Infinite ? cooldownRemaining : 0f;
  public float CooldownProgress => supplyMode == DistractionSupplyMode.Infinite && cooldown > 0f
    ? Mathf.Clamp01(cooldownRemaining / cooldown)
    : 0f;
  public DistractionThrowEvaluation CurrentEvaluation => evaluation;
  public AimEntryBlockReason LastAimEntryBlockReason { get; private set; }

  private void Awake() {
    ResolveReferences();
    CaptureAuthoredCameraPose();
    CaptureAnchorRestPosition();
  }

  private void OnDisable() {
    if (state == AimState.Aiming) ExitAim(true);
    else if (cameraRoutine != null && cameraTransform != null) {
      StopCoroutine(cameraRoutine);
      cameraRoutine = null;
      GetAuthoredCameraPoseForCurrentSide(out Vector3 position, out Quaternion rotation);
      cameraTransform.localPosition = position;
      cameraTransform.localRotation = rotation;
    }
    preview?.Hide();
    inkArm?.Hide();
  }

#if UNITY_EDITOR
  private void OnValidate() {
    minimumThrowDistance = Mathf.Max(0f, minimumThrowDistance);
    maximumThrowDistance = Mathf.Max(minimumThrowDistance, maximumThrowDistance);
    apexHeight = Mathf.Max(0.05f, apexHeight);
    maximumThrowSpeed = Mathf.Max(0.1f, maximumThrowSpeed);
    obstructionSamples = Mathf.Clamp(obstructionSamples, 6, 48);
    maximumObstructionSegmentLength = Mathf.Max(0.02f, maximumObstructionSegmentLength);
    cooldown = Mathf.Max(0f, cooldown);
    cursorMovementThreshold = Mathf.Max(0f, cursorMovementThreshold);
    maximumAnchorOffset = Mathf.Max(0f, maximumAnchorOffset);
    anchorSamples = Mathf.Clamp(anchorSamples, 3, 21);
    if ((anchorSamples & 1) == 0) anchorSamples++;
    anchorMovementSpeed = Mathf.Max(0.01f, anchorMovementSpeed);
    centeredAnchorPreference = Mathf.Max(0f, centeredAnchorPreference);
    anchorContinuityPreference = Mathf.Max(0f, anchorContinuityPreference);
    aimFacingDeadZone = Mathf.Max(0f, aimFacingDeadZone);
    if (throwAnchor != null && !Application.isPlaying) {
      Vector3 anchorPosition = throwAnchor.localPosition;
      anchorPosition.y = throwAnchorHeight;
      throwAnchor.localPosition = anchorPosition;
    }
  }
#endif

#pragma warning disable IDE0051
  private void OnDistractionAim(InputValue value) {
    // PlayerInput's Send Messages notification can still reach a disabled behaviour. Respect
    // scene-specific ability loadouts instead of turning a disabled distraction into rejected
    // input feedback.
    if (!isActiveAndEnabled || !value.isPressed) return;
    if (state == AimState.Idle) BeginAim();
    else if (state == AimState.Aiming) ExitAim(false);
  }
#pragma warning restore IDE0051

  private void Update() {
    if (supplyMode == DistractionSupplyMode.Infinite && cooldownRemaining > 0f && !SceneTransitionManager.IsGamePaused)
      cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.deltaTime);
    if (state != AimState.Aiming || SceneTransitionManager.IsGamePaused) return;
    EnsureActionsLocked();
    UpdateCursorTargetWhenMoved();
    UpdateAimFacing();
    UpdateMovingAnchorAndTrajectory();
    preview?.Show(evaluation);
    if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
      TryConfirmCurrent();
  }

  public bool BeginAim() {
    ResolveReferences();
    LastAimEntryBlockReason = GetAimEntryBlockReason();
    if (LastAimEntryBlockReason != AimEntryBlockReason.None) {
      HandleRejectedEntry(LastAimEntryBlockReason);
      return false;
    }

    state = AimState.Aiming;
    CaptureAimFacing();
    movementWasEnabled = movement.enabled;
    movement.enabled = false;
    EnsureActionsLocked();
    cursorLockBeforeAim = Cursor.lockState;
    cursorVisibleBeforeAim = Cursor.visible;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;

    timeScaleBeforeAim = Time.timeScale;
    ownsTimeScale = true;
    Time.timeScale = aimingTimeScale;
    GetAuthoredCameraPoseForCurrentSide(out normalCameraLocalPosition, out normalCameraLocalRotation);
    StartCameraBlend(GetSideAwareAimPosition(), normalCameraLocalRotation, cameraAimDuration);

    CaptureAnchorRestPosition();
    currentAnchorOffset = 0f;
    desiredAnchorOffset = 0f;
    ApplyAnchorOffset(0f);
    hasEvaluatedCursorPosition = false;
    UpdateCursorTargetWhenMoved();
    UpdateAimFacing();
    UpdateMovingAnchorAndTrajectory();
    preview?.Show(evaluation);
    inkArm?.Show();
    if (verboseLogging) Debug.Log("[Distraction] Aim started.", this);
    return true;
  }

  public void CancelForDeath(bool restoreCameraImmediately = false) {
    if (state != AimState.Aiming) return;
    ExitAim(restoreCameraImmediately);
  }

  private AimEntryBlockReason GetAimEntryBlockReason() {
    if (SceneTransitionManager.IsDeathSequenceActive || deathSequence != null && deathSequence.IsDying)
      return AimEntryBlockReason.Dead;
    if (SceneTransitionManager.IsGamePaused) return AimEntryBlockReason.Paused;
    if (!enabled || state != AimState.Idle) return AimEntryBlockReason.InvalidConfiguration;
    if (cameraRoutine != null) return AimEntryBlockReason.CameraTransitioning;
    if (aimCamera == null || cameraTransform == null || throwAnchor == null || movement == null)
      return AimEntryBlockReason.InvalidConfiguration;
    if (movement.IsTurning) return AimEntryBlockReason.PlayerTurning;
    if (wallSwitch != null &&
        (wallSwitch.IsAiming || wallSwitch.IsExecuting || wallSwitch.IsCameraTransitioning))
      return AimEntryBlockReason.OtherAimModeActive;
    if (stealth != null && stealth.IsConcealed) return AimEntryBlockReason.Concealed;

    if (supplyMode == DistractionSupplyMode.Infinite) {
      if (infiniteProjectilePrefab == null) return AimEntryBlockReason.InvalidConfiguration;
      return cooldownRemaining > 0f ? AimEntryBlockReason.Cooldown : AimEntryBlockReason.None;
    }

    if (inventory == null) return AimEntryBlockReason.InvalidConfiguration;
    InventoryItemInstance itemInstance = inventory.CurrentItemInstance;
    if (itemInstance?.Definition == null || itemInstance.Definition.category != InventoryItemCategory.Throwable)
      return AimEntryBlockReason.NoThrowableItem;
    return itemInstance.Definition.distractionProjectilePrefab != null
      ? AimEntryBlockReason.None
      : AimEntryBlockReason.InvalidConfiguration;
  }

  private void HandleRejectedEntry(AimEntryBlockReason reason) {
    if (reason == AimEntryBlockReason.InvalidConfiguration && !loggedInvalidConfiguration) {
      loggedInvalidConfiguration = true;
      Debug.LogError("[Distraction] Aim entry failed because a required reference or throwable projectile is not configured.", this);
    }
    if (reason.ShouldPlayFeedback()) rejectionFeedback?.PlayRejectedAction();
    if (verboseLogging) Debug.Log($"[Distraction] Aim entry rejected: {reason}.", this);
  }

  private bool TryResolveSupply(
      out ThrownDistraction projectile,
      out InventoryItemInstance itemInstance) {
    itemInstance = null;
    if (supplyMode == DistractionSupplyMode.Infinite) {
      projectile = infiniteProjectilePrefab;
      return projectile != null;
    }

    itemInstance = inventory != null ? inventory.CurrentItemInstance : null;
    ItemDefinition definition = itemInstance?.Definition;
    projectile = definition != null ? definition.distractionProjectilePrefab : null;
    return definition != null
           && definition.category == InventoryItemCategory.Throwable
           && projectile != null;
  }

  private bool TryConfirmCurrent() {
    if (state != AimState.Aiming) return false;

    // Confirmation snapshots origin and landing point in world space. Camera movement from this
    // point onward cannot alter either endpoint or the velocity solved from them.
    if (!TryLockCurrentTrajectory(out DistractionThrowEvaluation lockedTrajectory)
        || !TryLaunch(lockedTrajectory)) return false;

    CompleteAim(true, false);
    return true;
  }

  private void ExitAim(bool restoreCameraImmediately) {
    CompleteAim(false, restoreCameraImmediately);
  }

  private void CompleteAim(bool launched, bool restoreCameraImmediately) {
    state = AimState.Idle;
    hasEvaluatedCursorPosition = false;
    hasCursorTarget = false;
    evaluation = DistractionThrowEvaluation.Empty;
    preview?.Hide();
    inkArm?.Hide();
    EndAimFacing(!launched);
    ResetThrowAnchor();
    UnlockActions();
    if (movement != null) movement.enabled = movementWasEnabled;
    Cursor.lockState = cursorLockBeforeAim;
    Cursor.visible = cursorVisibleBeforeAim;
    RestoreTimeScale();

    if (cameraTransform != null) {
      if (restoreCameraImmediately) {
        if (cameraRoutine != null) StopCoroutine(cameraRoutine);
        cameraRoutine = null;
        cameraTransform.localPosition = normalCameraLocalPosition;
        cameraTransform.localRotation = normalCameraLocalRotation;
      } else {
        StartCameraBlend(normalCameraLocalPosition, normalCameraLocalRotation, cameraReturnDuration);
      }
    }

    if (launched && supplyMode == DistractionSupplyMode.Infinite) cooldownRemaining = cooldown;
    if (verboseLogging)
      Debug.Log(launched ? "[Distraction] Projectile thrown." : "[Distraction] Aim cancelled.", this);
  }

  private bool TryLaunch(DistractionThrowEvaluation accepted) {
    if (!TryResolveSupply(out ThrownDistraction projectilePrefab, out InventoryItemInstance itemInstance))
      return false;
    if (supplyMode == DistractionSupplyMode.InventoryItem && inventory.CurrentItemInstance != itemInstance)
      return false;

    ThrownDistraction instance;
    try {
      instance = Instantiate(projectilePrefab, accepted.Origin, Quaternion.identity);
    }
    catch (System.Exception exception) {
      Debug.LogError($"[Distraction] Could not instantiate the projectile.\n{exception}", this);
      return false;
    }
    if (instance == null) return false;
    if (itemInstance?.HasColorOverride == true) instance.ApplyDisplayColor(itemInstance.DisplayColor);
    if (!instance.Launch(accepted, throwerColliders)) {
      Destroy(instance.gameObject);
      return false;
    }
    if (supplyMode == DistractionSupplyMode.InventoryItem) inventory.ConsumeItem();
    return true;
  }

  private bool TryLockCurrentTrajectory(out DistractionThrowEvaluation locked) {
    locked = evaluation;
    if (!evaluation.IsValid) return false;

    TryResolveSupply(out ThrownDistraction projectilePrefab, out _);
    float projectileRadius = projectilePrefab != null ? projectilePrefab.CollisionRadius : 0f;
    Vector3 ballisticTarget = evaluation.Target + evaluation.TargetNormal * projectileRadius;
    if (!BallisticThrowSolver.TrySolve(
          evaluation.Origin,
          ballisticTarget,
          apexHeight,
          maximumThrowSpeed,
          out Vector3 velocity,
          out float flightTime,
          out bool tooFast)
        || tooFast) return false;

    locked = new DistractionThrowEvaluation(
      true,
      true,
      DistractionThrowFailure.None,
      evaluation.Origin,
      evaluation.Target,
      evaluation.TargetNormal,
      velocity,
      flightTime,
      evaluation.TargetCollider);
    return !IsAnchorObstructed(locked.Origin) && !IsTrajectoryObstructed(locked);
  }

  private DistractionThrowEvaluation EvaluateTargetFromOrigin(Vector3 origin) {
    if (supplyMode == DistractionSupplyMode.Infinite && cooldownRemaining > 0f)
      return InvalidWithoutTarget(origin, DistractionThrowFailure.Cooldown);
    if (!TryResolveSupply(out ThrownDistraction projectilePrefab, out _))
      return InvalidWithoutTarget(origin, DistractionThrowFailure.NoInventoryItem);
    if (!hasCursorTarget)
      return InvalidWithoutTarget(origin, DistractionThrowFailure.NoSurface);

    Vector3 flatDelta = cursorTarget - origin;
    flatDelta.y = 0f;
    float distance = flatDelta.magnitude;
    DistractionThrowFailure initialFailure = cursorTargetNormal.y < minimumSurfaceNormalY
      ? DistractionThrowFailure.InvalidSurface
      : distance < minimumThrowDistance
        ? DistractionThrowFailure.TooClose
        : distance > maximumThrowDistance
          ? DistractionThrowFailure.TooFar
          : DistractionThrowFailure.None;

    float projectileRadius = projectilePrefab != null ? projectilePrefab.CollisionRadius : 0f;
    Vector3 ballisticTarget = cursorTarget + cursorTargetNormal * projectileRadius;
    bool solved = BallisticThrowSolver.TrySolve(
      origin, ballisticTarget, apexHeight, maximumThrowSpeed,
      out Vector3 velocity, out float flightTime, out bool tooFast);
    if (!solved) {
      return new DistractionThrowEvaluation(
        true, false, DistractionThrowFailure.NoBallisticSolution,
        origin, cursorTarget, cursorTargetNormal, Vector3.zero, 0f, cursorTargetCollider);
    }

    DistractionThrowEvaluation result = new(
      true, initialFailure == DistractionThrowFailure.None && !tooFast,
      tooFast ? DistractionThrowFailure.TooFast : initialFailure,
      origin, cursorTarget, cursorTargetNormal, velocity, flightTime, cursorTargetCollider);
    if (result.IsValid && (IsAnchorObstructed(origin) || IsTrajectoryObstructed(result)))
      result = result.Invalid(DistractionThrowFailure.Obstructed);
    return result;
  }

  private void UpdateCursorTargetWhenMoved() {
    if (Mouse.current == null) {
      ClearCursorTarget();
      hasEvaluatedCursorPosition = false;
      return;
    }

    // Screen coordinates are converted only from the final authored aim view. During the blend
    // there is deliberately no provisional target that could drift as the camera moves.
    if (cameraRoutine != null) {
      ClearCursorTarget();
      hasEvaluatedCursorPosition = false;
      return;
    }

    Vector2 cursorPosition = Mouse.current.position.ReadValue();
    float thresholdSquared = cursorMovementThreshold * cursorMovementThreshold;
    if (hasEvaluatedCursorPosition &&
        (cursorPosition - lastEvaluatedCursorPosition).sqrMagnitude <= thresholdSquared) return;

    lastEvaluatedCursorPosition = cursorPosition;
    hasEvaluatedCursorPosition = true;
    Ray ray = aimCamera.ScreenPointToRay(cursorPosition);
    if (Physics.Raycast(ray, out RaycastHit targetHit, cursorRayDistance,
          landingSurfaceLayers, QueryTriggerInteraction.Ignore)) {
      hasCursorTarget = true;
      cursorTarget = targetHit.point;
      cursorTargetNormal = targetHit.normal;
      cursorTargetCollider = targetHit.collider;
    } else {
      ClearCursorTarget();
    }
    SelectDesiredAnchorOffset();
  }

  private void UpdateMovingAnchorAndTrajectory() {
    if (throwAnchor == null) {
      evaluation = DistractionThrowEvaluation.Empty;
      return;
    }

    float delta = anchorMovementSpeed * Time.unscaledDeltaTime;
    currentAnchorOffset = Mathf.MoveTowards(currentAnchorOffset, desiredAnchorOffset, delta);
    ApplyAnchorOffset(currentAnchorOffset);
    evaluation = EvaluateTargetFromOrigin(throwAnchor.position);
  }

  private void SelectDesiredAnchorOffset() {
    desiredAnchorOffset = 0f;
    if (!hasCursorTarget || throwAnchor == null) return;

    Vector3 center = GetAnchorCenterWorldPosition();
    Vector3 lateral = GetAnchorLateralDirection();
    int samples = Mathf.Max(3, anchorSamples | 1);
    float bestScore = float.PositiveInfinity;
    bool found = false;

    for (int i = 0; i < samples; i++) {
      float normalized = samples > 1 ? i / (float)(samples - 1) : 0.5f;
      float offset = Mathf.Lerp(-maximumAnchorOffset, maximumAnchorOffset, normalized);
      DistractionThrowEvaluation candidate = EvaluateTargetFromOrigin(center + lateral * offset);
      if (!candidate.IsValid) continue;

      float normalization = Mathf.Max(0.001f, maximumAnchorOffset);
      float centeredCost = Mathf.Abs(offset) / normalization * centeredAnchorPreference;
      float continuityCost = Mathf.Abs(offset - currentAnchorOffset) / normalization * anchorContinuityPreference;
      float score = centeredCost + continuityCost;
      if (score >= bestScore) continue;
      bestScore = score;
      desiredAnchorOffset = offset;
      found = true;
    }

    if (!found) desiredAnchorOffset = 0f;
  }

  private bool IsAnchorObstructed(Vector3 origin) {
    TryResolveSupply(out ThrownDistraction projectilePrefab, out _);
    float radius = projectilePrefab != null ? projectilePrefab.CollisionRadius : 0.04f;
    return Physics.CheckSphere(
      origin, Mathf.Max(0.005f, radius * 0.9f), trajectoryObstructionLayers,
      QueryTriggerInteraction.Ignore);
  }

  private void ClearCursorTarget() {
    hasCursorTarget = false;
    cursorTarget = Vector3.zero;
    cursorTargetNormal = Vector3.up;
    cursorTargetCollider = null;
    desiredAnchorOffset = 0f;
  }

  private bool IsTrajectoryObstructed(DistractionThrowEvaluation result) {
    TryResolveSupply(out ThrownDistraction projectilePrefab, out _);
    float radius = projectilePrefab != null ? projectilePrefab.CollisionRadius : 0.1f;
    float estimatedArcLength = result.InitialVelocity.magnitude * result.FlightTime
                               + 0.5f * Physics.gravity.magnitude
                               * result.FlightTime * result.FlightTime;
    int sampleCount = Mathf.Clamp(
      Mathf.Max(obstructionSamples,
        Mathf.CeilToInt(estimatedArcLength / Mathf.Max(0.02f, maximumObstructionSegmentLength))),
      obstructionSamples,
      256);
    Vector3 previous = result.Origin;
    for (int i = 1; i <= sampleCount; i++) {
      float normalized = i / (float)sampleCount;
      Vector3 next = result.PositionAt(result.FlightTime * normalized);
      Vector3 segment = next - previous;
      float length = segment.magnitude;
      if (length > 0.0001f) {
        int count = Physics.SphereCastNonAlloc(
          previous, radius, segment / length, obstructionHits, length,
          trajectoryObstructionLayers, QueryTriggerInteraction.Ignore);
        for (int hitIndex = 0; hitIndex < count; hitIndex++) {
          Collider hit = obstructionHits[hitIndex].collider;
          if (hit == null || hit.transform.IsChildOf(transform)) continue;
          bool finalTargetContact = hit == result.TargetCollider && i >= sampleCount - 1;
          if (!finalTargetContact) return true;
        }
      }
      previous = next;
    }
    return false;
  }

  private static DistractionThrowEvaluation InvalidWithoutTarget(
    Vector3 origin, DistractionThrowFailure failure) => new(
      false, false, failure, origin, origin, Vector3.up, Vector3.zero, 0f, null);

  private void CaptureAnchorRestPosition() {
    if (throwAnchor != null) anchorRestLocalPosition = throwAnchor.localPosition;
  }

  private Vector3 GetAnchorCenterWorldPosition() {
    if (throwAnchor == null) return transform.position;
    Transform parent = throwAnchor.parent;
    Vector3 centerLocalPosition = anchorRestLocalPosition;
    centerLocalPosition.y = throwAnchorHeight;
    return parent != null ? parent.TransformPoint(centerLocalPosition) : centerLocalPosition;
  }

  private Vector3 GetAnchorLateralDirection() {
    Vector3 lateral = aimCamera != null
      ? Vector3.ProjectOnPlane(aimCamera.transform.right, Vector3.up)
      : Vector3.ProjectOnPlane(transform.right, Vector3.up);
    if (lateral.sqrMagnitude < 0.0001f)
      lateral = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
    return lateral.sqrMagnitude > 0.0001f ? lateral.normalized : Vector3.right;
  }

  private void ApplyAnchorOffset(float offset) {
    if (throwAnchor == null) return;
    throwAnchor.position = GetAnchorCenterWorldPosition() + GetAnchorLateralDirection() * offset;
  }

  private void ResetThrowAnchor() {
    currentAnchorOffset = 0f;
    desiredAnchorOffset = 0f;
    if (throwAnchor != null) {
      Vector3 restPosition = anchorRestLocalPosition;
      restPosition.y = throwAnchorHeight;
      throwAnchor.localPosition = restPosition;
    }
  }

  private void CaptureAimFacing() {
    if (playerRenderer == null) return;
    flipBeforeAim = playerRenderer.flipX;
    hasAimFacingSnapshot = true;
  }

  private void UpdateAimFacing() {
    if (!hasAimFacingSnapshot || playerRenderer == null || aimCamera == null || !hasCursorTarget) return;

    Vector3 cameraRight = aimCamera.transform.right;
    Vector3 playerCenter = playerRenderer.bounds.center;
    float targetSide = throwAnchor != null
      ? Vector3.Dot(throwAnchor.position - playerCenter, cameraRight)
      : 0f;

    // A centered hand does not express a side, so let the actual intended destination break the
    // tie. Once the moving anchor leaves the symmetry axis, it remains the primary visual cue.
    if (Mathf.Abs(targetSide) <= aimFacingDeadZone)
      targetSide = Vector3.Dot(cursorTarget - playerCenter, cameraRight);
    if (Mathf.Abs(targetSide) <= aimFacingDeadZone) return;

    // Account for either camera side: flipX mirrors the artwork relative to the renderer's
    // unflipped local-right direction, which may itself point screen-left after a wall switch.
    float unflippedRightSide = Vector3.Dot(playerRenderer.transform.right, cameraRight);
    if (Mathf.Abs(unflippedRightSide) < 0.001f) unflippedRightSide = 1f;
    playerRenderer.flipX = targetSide * unflippedRightSide < 0f;
  }

  private void EndAimFacing(bool restorePreviousFacing) {
    if (!hasAimFacingSnapshot) return;
    if (restorePreviousFacing && playerRenderer != null) playerRenderer.flipX = flipBeforeAim;
    hasAimFacingSnapshot = false;
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

  private Vector3 GetSideAwareAimPosition() {
    Vector3 currentHorizontal = new(normalCameraLocalPosition.x, 0f, normalCameraLocalPosition.z);
    Vector3 authoredHorizontal = new(aimingCameraLocalPosition.x, 0f, aimingCameraLocalPosition.z);
    if (currentHorizontal.sqrMagnitude < 0.0001f || authoredHorizontal.sqrMagnitude < 0.0001f)
      return aimingCameraLocalPosition;
    Vector3 horizontal = currentHorizontal.normalized * authoredHorizontal.magnitude;
    return new Vector3(horizontal.x, aimingCameraLocalPosition.y, horizontal.z);
  }

  private void EnsureActionsLocked() {
    if (playerInput == null || playerInput.actions == null) return;
    InputActionMap map = playerInput.actions.FindActionMap("Player", false);
    if (map == null) return;
    for (int i = 0; i < LockedActions.Length; i++) {
      InputAction action = map.FindAction(LockedActions[i], false);
      if (action == null || !action.enabled || lockedActions.Contains(action)) continue;
      action.Disable();
      lockedActions.Add(action);
    }
  }

  private void UnlockActions() {
    for (int i = 0; i < lockedActions.Count; i++)
      if (lockedActions[i] != null) lockedActions[i].Enable();
    lockedActions.Clear();
  }

  private void RestoreTimeScale() {
    if (!ownsTimeScale) return;
    Time.timeScale = timeScaleBeforeAim;
    ownsTimeScale = false;
  }

  private void StartCameraBlend(Vector3 position, Quaternion rotation, float duration) {
    if (cameraTransform == null) return;
    if (cameraRoutine != null) StopCoroutine(cameraRoutine);
    cameraRoutine = StartCoroutine(CameraBlend(position, rotation, duration));
  }

  private IEnumerator CameraBlend(Vector3 position, Quaternion rotation, float duration) {
    Vector3 startPosition = cameraTransform.localPosition;
    Quaternion startRotation = cameraTransform.localRotation;
    float elapsed = 0f;
    while (elapsed < duration) {
      if (!SceneTransitionManager.IsGamePaused) elapsed += Time.unscaledDeltaTime;
      float normalized = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
      float smooth = normalized * normalized * (3f - 2f * normalized);
      cameraTransform.localPosition = Vector3.Lerp(startPosition, position, smooth);
      cameraTransform.localRotation = Quaternion.Slerp(startRotation, rotation, smooth);
      yield return null;
    }
    cameraTransform.localPosition = position;
    cameraTransform.localRotation = rotation;
    cameraRoutine = null;
  }

  private void ResolveReferences() {
    if (inventory == null) inventory = GetComponent<PlayerInventory>();
    if (playerInput == null) playerInput = GetComponent<PlayerInput>();
    if (movement == null) movement = GetComponent<LineFollowController>();
    if (stealth == null) stealth = GetComponent<PlayerStealthController>();
    if (deathSequence == null) deathSequence = GetComponent<PlayerDeathSequence>();
    if (wallSwitch == null) wallSwitch = GetComponent<WallSwitchController>();
    if (rejectionFeedback == null) rejectionFeedback = GetComponentInChildren<RejectedAimCameraFeedback>(true);
    if (playerRenderer == null) playerRenderer = GetComponent<SpriteRenderer>();
    if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>(true);
    if (cameraTransform == null && aimCamera != null) cameraTransform = aimCamera.transform;
    if (preview == null) preview = GetComponent<DistractionTrajectoryPreview>();
    if (inkArm == null) inkArm = GetComponent<ProceduralInkArm>();
    if (throwAnchor == null) throwAnchor = transform.Find("DistractionThrowAnchor");
    if (throwerColliders == null || throwerColliders.Length == 0)
      throwerColliders = GetComponentsInChildren<Collider>(true);
  }

#if UNITY_EDITOR
  public void Configure(
    ThrownDistraction projectile,
    DistractionTrajectoryPreview authoredPreview,
    Transform authoredThrowAnchor) {
    infiniteProjectilePrefab = projectile;
    preview = authoredPreview;
    throwAnchor = authoredThrowAnchor;
    ResolveReferences();
    CaptureAnchorRestPosition();
  }

  public void IncludeTrajectoryObstructionLayer(int layer) {
    if (layer >= 0 && layer < 32) trajectoryObstructionLayers |= 1 << layer;
  }
#endif
}
