using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sliding passageway door. A door may start locked; the matching runtime key is consumed once,
/// after which the door stays unlocked and can always be opened and closed.
/// </summary>
public class PassagewayDoor : MonoBehaviour, IInteractable, IInteractionFocus, IInteractionCategoryProvider {
  public enum PassageState { Closed, Opening, Open, Closing }
  public enum GuardPassageResult { Granted, Waiting, WaitingForPlayerPath, Denied }
  public enum InteractionState { Open, Close, Locked, Unavailable }
  private enum SlideAxis { LocalX, LocalZ }
  private enum MotionEasing { Linear, SmoothStep, EaseInOutSine, EaseOutCubic, EaseInOutCubic, CustomCurve }

  [Header("Door Panels")]
  [SerializeField] private Transform leftDoorPanel;
  [SerializeField] private Transform rightDoorPanel;
  [Tooltip("Collider that blocks movement and wall switching while the door is closed.")]
  [SerializeField] private Collider blockingCollider;
  [Tooltip("Obstacle carved into the guard NavMesh only while the door is closed.")]
  [SerializeField] private NavMeshObstacle navMeshObstacle;

  [Header("Door State")]
  [SerializeField] private bool startsOpen;
  [SerializeField] private bool autoOpenOnStart;
  [SerializeField] private bool autoCloseOnStart;
  [SerializeField] private SlideAxis slideAxis = SlideAxis.LocalX;
  [SerializeField] private float panelSlideDistance = 0.75f;
  [SerializeField, Min(0.01f)] private float animationDuration = 0.35f;
  [SerializeField] private MotionEasing motionEasing = MotionEasing.SmoothStep;
  [SerializeField] private AnimationCurve customEasingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

  [Header("Guard Passage")]
  [Tooltip("Delay after the last admitted guard clears a temporarily opened locked door before it closes.")]
  [SerializeField, Min(0f)] private float lockedGuardCloseDelay = 0.25f;
  [Tooltip("Extra depth around the closed panel used to keep a temporary locked passage open while an actor is physically crossing.")]
  [SerializeField, Min(0f)] private float passageOccupancyDepth = 0.18f;
  [Tooltip("Layers whose colliders prevent a temporary locked door from closing. Defaults to Player and Enemy.")]
  [SerializeField] private LayerMask passageOccupantLayers = (1 << 3) | (1 << 7);

  [Header("Lock")]
  [Tooltip("When enabled, this door begins locked and needs the matching key once.")]
  [SerializeField] private bool startsLocked;
  [Tooltip("Stable id that must match the runtime id of the carried key.")]
  [SerializeField] private string requiredKeyId = "door_key";
  [Tooltip("Authored lock/key colour. Door-panel colouring can use this in the later visual pass.")]
  [SerializeField] private Color requiredKeyColor = new(0.25f, 0.7f, 1f, 1f);
  [Tooltip("Name shown in the locked interaction prompt, for example Blue or Purple.")]
  [SerializeField] private string requiredKeyColorName = "Blue";
  [Header("Audio")]
  [SerializeField] private AudioSource audioSource;
  [SerializeField] private AudioClip openStartClip;
  [SerializeField] private AudioClip closeStartClip;
  [SerializeField] private AudioClip openEndClip;
  [SerializeField] private AudioClip closeEndClip;
  [SerializeField, Range(0f, 1f)] private float audioVolume = 1f;

  public bool IsOpen { get; private set; }
  public PassageState CurrentState { get; private set; } = PassageState.Closed;
  public bool IsAnimating => CurrentState == PassageState.Opening || CurrentState == PassageState.Closing;
  public event Action<PassageState> PassageStateChanged;
  public bool IsLocked { get; private set; }
  public bool StartsLocked => startsLocked;
  public string RequiredKeyId => requiredKeyId;
  public Color RequiredKeyColor => requiredKeyColor;
  public string RequiredKeyColorName => requiredKeyColorName;
  public Transform LeftDoorPanel => leftDoorPanel;
  public Transform RightDoorPanel => rightDoorPanel;
  public InteractionCategory InteractionCategory => InteractionCategory.Door;
  public bool IsHeldClosedByPlayer => CurrentState == PassageState.Closed && PlayerOccupiesDoorPath(null);
  public static IReadOnlyCollection<PassagewayDoor> ActiveDoors => ActiveDoorSet;

  private Vector3 leftClosedLocalPosition;
  private Vector3 rightClosedLocalPosition;
  private Vector3 leftOpenLocalPosition;
  private Vector3 rightOpenLocalPosition;
  private Coroutine animationCoroutine;
  private DoorLinePathState linePathState;
  private DoorKeyColorVisual keyColorVisual;
  private readonly HashSet<GuardMotor> admittedGuards = new();
  private readonly List<GuardMotor> staleGuards = new();
  private readonly Collider[] occupancyBuffer = new Collider[32];
  private bool hasGuardDirectionReservation;
  private float reservedGuardSide;
  private bool temporaryLockedGuardPassage;
  private float lockedGuardCloseElapsed;

  private static readonly HashSet<PassagewayDoor> ActiveDoorSet = new();

  public void Interact(PlayerInventory inventory) => TryToggle(inventory);

  public InteractionState GetInteractionState(PlayerInventory inventory) {
    if (animationCoroutine != null || temporaryLockedGuardPassage || HasActiveGuardTraffic()
        || PlayerOccupiesDoorPath(inventory)) return InteractionState.Unavailable;
    if (IsOpen) return InteractionState.Close;
    if (!IsLocked || PlayerHasRequiredKey(inventory)) return InteractionState.Open;
    return InteractionState.Locked;
  }

  private void Awake() {
    if (audioSource == null) audioSource = GetComponent<AudioSource>();
    if (leftDoorPanel == null && transform.childCount > 0) leftDoorPanel = transform.GetChild(0);
    if (rightDoorPanel == null && transform.childCount > 1) rightDoorPanel = transform.GetChild(1);
    linePathState = GetComponentInChildren<DoorLinePathState>(true);
    keyColorVisual = GetComponent<DoorKeyColorVisual>();

    leftClosedLocalPosition = leftDoorPanel != null ? leftDoorPanel.localPosition : Vector3.zero;
    rightClosedLocalPosition = rightDoorPanel != null ? rightDoorPanel.localPosition : Vector3.zero;
    CalculateOpenPositions();

    IsOpen = startsOpen;
    IsLocked = startsLocked && !startsOpen;
    SetPassageState(IsOpen ? PassageState.Open : PassageState.Closed);
    ApplyPanelPositions(IsOpen);
    ApplyPassageBlockingState(IsOpen);
    keyColorVisual?.Apply();
  }

  private void OnEnable() => ActiveDoorSet.Add(this);

  private void OnDisable() {
    ActiveDoorSet.Remove(this);
    admittedGuards.Clear();
    hasGuardDirectionReservation = false;
    temporaryLockedGuardPassage = false;
    lockedGuardCloseElapsed = 0f;
  }

  private void Update() {
    PruneGuardTraffic();
    if (!temporaryLockedGuardPassage || CurrentState != PassageState.Open) return;
    if (admittedGuards.Count > 0 || IsPassageOccupied()) {
      lockedGuardCloseElapsed = 0f;
      return;
    }

    lockedGuardCloseElapsed += Time.deltaTime;
    if (lockedGuardCloseElapsed < lockedGuardCloseDelay || animationCoroutine != null) return;
    animationCoroutine = StartCoroutine(AnimateDoor(false));
  }

  private void Start() {
    bool shouldAutoOpen = !startsOpen && autoOpenOnStart;
    bool shouldAutoClose = startsOpen && autoCloseOnStart;
    if (!shouldAutoOpen && !shouldAutoClose) return;

    if (shouldAutoOpen) IsLocked = false;
    animationCoroutine = StartCoroutine(AnimateDoor(shouldAutoOpen));
  }

  public bool CanToggle(PlayerInventory inventory) {
    if (animationCoroutine != null) return false;
    if (temporaryLockedGuardPassage || HasActiveGuardTraffic()) return false;
    if (PlayerOccupiesDoorPath(inventory)) return false;
    return IsOpen || !IsLocked || PlayerHasRequiredKey(inventory);
  }

  public void SetInteractionFocused(bool focused, PlayerInventory inventory) {
    if (keyColorVisual == null) keyColorVisual = GetComponent<DoorKeyColorVisual>();
    keyColorVisual?.SetHandleInteractionState(focused, focused && CanToggle(inventory));
  }

  public bool TryToggle(PlayerInventory inventory) {
    if (!CanToggle(inventory)) return false;
    return TrySetOpen(!IsOpen, inventory);
  }

  public bool TrySetOpen(bool open, PlayerInventory inventory) {
    if (animationCoroutine != null) return false;
    if (open == IsOpen) return true;
    if (!open && (temporaryLockedGuardPassage || HasActiveGuardTraffic())) return false;

    if (open && IsLocked) {
      if (!PlayerHasRequiredKey(inventory)) {
        Debug.LogWarning($"{name}: This door requires key '{requiredKeyId}' to open.", this);
        return false;
      }

      inventory.ConsumeItem();
      IsLocked = false;
    }

    animationCoroutine = StartCoroutine(AnimateDoor(open));
    NotifyGuardsOfPlayerInteraction(inventory);
    return true;
  }

  /// <summary>
  /// Requests physical passage for a guard. Unlocking is deliberately not performed here: a
  /// matching guard key grants one temporary passage through an otherwise still-locked door.
  /// </summary>
  public GuardPassageResult RequestGuardPassage(GuardMotor guard, GuardKeyCarrier keyCarrier) {
    if (guard == null || !guard.isActiveAndEnabled) return GuardPassageResult.Denied;
    PruneGuardTraffic();

    float requestSide = GetPassageSide(guard.transform.position);
    if (CurrentState == PassageState.Closing) return GuardPassageResult.Waiting;

    // Once the admitted group has cleared a temporarily opened locked door, finish closing it
    // before accepting another traversal session.
    if (temporaryLockedGuardPassage && CurrentState == PassageState.Open && admittedGuards.Count == 0)
      return GuardPassageResult.Waiting;

    if (hasGuardDirectionReservation && !IsSameSide(requestSide, reservedGuardSide))
      return GuardPassageResult.Waiting;

    if (CurrentState == PassageState.Closed) {
      // Opening swaps the door's traversable long-side paths for its short-side paths. Doing that
      // while the player follows any of those four paths would leave LineFollowController attached
      // to an invalid path, so guards obey the same safety rule as player interaction.
      if (PlayerOccupiesDoorPath(null)) {
        NotifyGuardOfPlayerHeldDoor(guard);
        return GuardPassageResult.WaitingForPlayerPath;
      }
      if (IsLocked && !GuardHasRequiredKey(keyCarrier)) return GuardPassageResult.Denied;

      ReserveGuardDirection(requestSide);
      admittedGuards.Add(guard);
      temporaryLockedGuardPassage = IsLocked;
      lockedGuardCloseElapsed = 0f;
      animationCoroutine = StartCoroutine(AnimateDoor(true));
      return GuardPassageResult.Granted;
    }

    // A player may already be opening an unlocked door. The first arriving guard establishes the
    // traversal direction; guards approaching from the other side wait until that group clears.
    if (!hasGuardDirectionReservation) ReserveGuardDirection(requestSide);
    admittedGuards.Add(guard);
    return GuardPassageResult.Granted;
  }

  public void NotifyGuardCleared(GuardMotor guard) {
    if (guard == null) return;
    admittedGuards.Remove(guard);
    if (admittedGuards.Count == 0 && !temporaryLockedGuardPassage)
      hasGuardDirectionReservation = false;
  }

  public void CancelGuardPassage(GuardMotor guard) {
    if (guard != null) admittedGuards.Remove(guard);
    if (admittedGuards.Count == 0 && !temporaryLockedGuardPassage)
      hasGuardDirectionReservation = false;
  }

  public bool TryGetApproachDistance(
    Vector3 worldOrigin,
    Vector3 worldDirection,
    float maximumDistance,
    float actorRadius,
    out float distance) {
    distance = float.PositiveInfinity;
    if (blockingCollider == null || maximumDistance <= 0f) return false;
    worldDirection.y = 0f;
    if (worldDirection.sqrMagnitude <= 0.0001f) return false;
    worldDirection.Normalize();
    Vector3 passageNormal = GetPassageNormal();
    float directionAcrossDoor = Vector3.Dot(worldDirection, passageNormal);
    if (Mathf.Abs(directionAcrossDoor) < 0.35f) return false;
    float originSide = Vector3.Dot(worldOrigin - GetPassageCenter(), passageNormal);
    if (originSide * directionAcrossDoor >= 0f) return false;

    Bounds approachBounds = blockingCollider.bounds;
    approachBounds.Expand(new Vector3(actorRadius * 2f, 0.05f, actorRadius * 2f));
    Ray approachRay = new(worldOrigin, worldDirection);
    if (!approachBounds.IntersectRay(approachRay, out distance)) return false;
    return distance <= maximumDistance;
  }

  public float GetPassageSide(Vector3 worldPosition) {
    float side = Vector3.Dot(worldPosition - GetPassageCenter(), GetPassageNormal());
    return side >= 0f ? 1f : -1f;
  }

  public bool HasGuardCleared(float entrySide, Vector3 guardPosition, float clearance) {
    float signedDistance = Vector3.Dot(guardPosition - GetPassageCenter(), GetPassageNormal());
    return signedDistance * entrySide <= -Mathf.Max(0f, clearance);
  }

  public float DistanceFromPassagePlane(Vector3 worldPosition) =>
    Mathf.Abs(Vector3.Dot(worldPosition - GetPassageCenter(), GetPassageNormal()));

  /// <summary>
  /// Returns the nearest point on the physical door face for perception. Using the door centre
  /// made a clearly visible edge fail when that centre happened to sit outside a narrow cone.
  /// </summary>
  public Vector3 GetVisionTargetPoint(Vector3 observerPosition) =>
    blockingCollider != null ? blockingCollider.ClosestPoint(observerPosition) : GetPassageCenter();

  public bool OwnsCollider(Collider candidate) =>
    candidate != null && (candidate.transform == transform || candidate.transform.IsChildOf(transform));

  /// <summary>True when any closed or transitioning door physically intersects this segment.</summary>
  public static bool AnyNonOpenDoorBlocksSegment(Vector3 start, Vector3 end) {
    foreach (PassagewayDoor door in ActiveDoorSet)
      if (door != null && door.isActiveAndEnabled && door.CurrentState != PassageState.Open
          && door.BlocksSegment(start, end)) return true;
    return false;
  }

  private bool PlayerHasRequiredKey(PlayerInventory inventory) =>
    inventory != null && !string.IsNullOrWhiteSpace(requiredKeyId) && inventory.HasItem(requiredKeyId);

  private void NotifyGuardsOfPlayerInteraction(PlayerInventory inventory) {
    if (inventory == null) return;
    PlayerStealthController player = inventory.GetComponentInParent<PlayerStealthController>();
    if (player == null) player = inventory.GetComponentInChildren<PlayerStealthController>(true);
    if (player == null) return;

    foreach (GuardController guard in GuardController.ActiveGuards)
      guard?.ObservePlayerDoorInteraction(player, this);
  }

  private void NotifyGuardOfPlayerHeldDoor(GuardMotor guard) {
    if (guard == null || LineFollowController.ActivePlayer == null) return;
    PlayerStealthController player = LineFollowController.ActivePlayer.GetComponent<PlayerStealthController>();
    if (player == null)
      player = LineFollowController.ActivePlayer.GetComponentInChildren<PlayerStealthController>(true);
    guard.GetComponent<GuardController>()?.ObservePlayerHoldingDoor(player, this);
  }

  private bool PlayerOccupiesDoorPath(PlayerInventory inventory) {
    if (linePathState == null) linePathState = GetComponentInChildren<DoorLinePathState>(true);
    if (linePathState == null) return false;

    LineFollowController movement = null;
    if (inventory != null) {
      movement = inventory.GetComponentInParent<LineFollowController>();
      if (movement == null) movement = inventory.GetComponentInChildren<LineFollowController>(true);
    }
    if (movement == null) movement = LineFollowController.ActivePlayer;
    return movement != null && linePathState.Contains(movement.currentLine);
  }

  private IEnumerator AnimateDoor(bool open) {
    Vector3 leftStart = leftDoorPanel != null ? leftDoorPanel.localPosition : Vector3.zero;
    Vector3 rightStart = rightDoorPanel != null ? rightDoorPanel.localPosition : Vector3.zero;
    Vector3 leftTarget = open ? leftOpenLocalPosition : leftClosedLocalPosition;
    Vector3 rightTarget = open ? rightOpenLocalPosition : rightClosedLocalPosition;

    SetPassageState(open ? PassageState.Opening : PassageState.Closing);

    // A closing door blocks immediately. An opening door stays blocked until the panels finish.
    if (!open) ApplyPassageBlockingState(false);
    PlayTransitionClip(open, true);

    float elapsed = 0f;
    while (elapsed < animationDuration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / animationDuration);
      float easedT = EvaluateEasing(t);
      if (leftDoorPanel != null) leftDoorPanel.localPosition = Vector3.Lerp(leftStart, leftTarget, easedT);
      if (rightDoorPanel != null) rightDoorPanel.localPosition = Vector3.Lerp(rightStart, rightTarget, easedT);
      yield return null;
    }

    ApplyPanelPositions(open);
    IsOpen = open;
    ApplyPassageBlockingState(open);
    SetPassageState(open ? PassageState.Open : PassageState.Closed);
    if (!open && temporaryLockedGuardPassage) {
      temporaryLockedGuardPassage = false;
      hasGuardDirectionReservation = false;
      admittedGuards.Clear();
      lockedGuardCloseElapsed = 0f;
    }
    PlayTransitionClip(open, false);
    animationCoroutine = null;
  }

  private void SetPassageState(PassageState state) {
    if (CurrentState == state) return;
    CurrentState = state;
    keyColorVisual?.Apply();
    PassageStateChanged?.Invoke(state);
  }

  private float EvaluateEasing(float t) => motionEasing switch {
    MotionEasing.Linear => t,
    MotionEasing.SmoothStep => Mathf.SmoothStep(0f, 1f, t),
    MotionEasing.EaseInOutSine => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t),
    MotionEasing.EaseOutCubic => 1f - Mathf.Pow(1f - t, 3f),
    MotionEasing.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f,
    MotionEasing.CustomCurve => customEasingCurve == null ? t : Mathf.Clamp01(customEasingCurve.Evaluate(t)),
    _ => t,
  };

  private void PlayTransitionClip(bool opening, bool atStart) {
    if (audioSource == null) return;
    AudioClip clip = opening
      ? atStart ? openStartClip : openEndClip
      : atStart ? closeStartClip : closeEndClip;
    if (clip != null) audioSource.PlayOneShot(clip, audioVolume);
  }

  private bool GuardHasRequiredKey(GuardKeyCarrier keyCarrier) =>
    keyCarrier != null && keyCarrier.HasKey(requiredKeyId);

  private bool HasActiveGuardTraffic() {
    PruneGuardTraffic();
    return admittedGuards.Count > 0;
  }

  private void PruneGuardTraffic() {
    if (admittedGuards.Count == 0) return;
    staleGuards.Clear();
    foreach (GuardMotor guard in admittedGuards)
      if (guard == null || !guard.isActiveAndEnabled) staleGuards.Add(guard);
    for (int i = 0; i < staleGuards.Count; i++) admittedGuards.Remove(staleGuards[i]);
    staleGuards.Clear();
    if (admittedGuards.Count == 0 && !temporaryLockedGuardPassage)
      hasGuardDirectionReservation = false;
  }

  private void ReserveGuardDirection(float side) {
    reservedGuardSide = side >= 0f ? 1f : -1f;
    hasGuardDirectionReservation = true;
  }

  private static bool IsSameSide(float left, float right) => left * right > 0f;

  private Vector3 GetPassageCenter() {
    if (blockingCollider is BoxCollider box) return box.transform.TransformPoint(box.center);
    return blockingCollider != null ? blockingCollider.bounds.center : transform.position;
  }

  private bool BlocksSegment(Vector3 start, Vector3 end) {
    if (blockingCollider == null) return false;
    if (blockingCollider is BoxCollider box) return SegmentIntersectsBox(box, start, end);

    Vector3 delta = end - start;
    float length = delta.magnitude;
    if (length <= 0.0001f) return blockingCollider.bounds.Contains(start);
    Bounds bounds = blockingCollider.bounds;
    return bounds.Contains(start) || bounds.Contains(end)
           || bounds.IntersectRay(new Ray(start, delta / length), out float distance) && distance <= length;
  }

  private static bool SegmentIntersectsBox(BoxCollider box, Vector3 worldStart, Vector3 worldEnd) {
    Vector3 start = box.transform.InverseTransformPoint(worldStart) - box.center;
    Vector3 end = box.transform.InverseTransformPoint(worldEnd) - box.center;
    Vector3 delta = end - start;
    Vector3 half = box.size * 0.5f;
    float minimum = 0f;
    float maximum = 1f;

    return ClipAxis(start.x, delta.x, half.x, ref minimum, ref maximum)
           && ClipAxis(start.y, delta.y, half.y, ref minimum, ref maximum)
           && ClipAxis(start.z, delta.z, half.z, ref minimum, ref maximum);
  }

  private static bool ClipAxis(float start, float delta, float halfExtent, ref float minimum, ref float maximum) {
    if (Mathf.Abs(delta) <= 0.000001f) return start >= -halfExtent && start <= halfExtent;
    float first = (-halfExtent - start) / delta;
    float second = (halfExtent - start) / delta;
    if (first > second) (first, second) = (second, first);
    minimum = Mathf.Max(minimum, first);
    maximum = Mathf.Min(maximum, second);
    return minimum <= maximum;
  }

  private Vector3 GetPassageNormal() {
    if (blockingCollider is BoxCollider box) {
      Vector3 scale = box.transform.lossyScale;
      float scaledX = Mathf.Abs(box.size.x * scale.x);
      float scaledZ = Mathf.Abs(box.size.z * scale.z);
      Vector3 normal = scaledX <= scaledZ ? box.transform.right : box.transform.forward;
      normal.y = 0f;
      if (normal.sqrMagnitude > 0.0001f) return normal.normalized;
    }

    Vector3 fallback = transform.forward;
    fallback.y = 0f;
    return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
  }

  private bool IsPassageOccupied() {
    if (blockingCollider == null || passageOccupantLayers.value == 0) return false;

    if (blockingCollider is BoxCollider box) {
      Vector3 scale = box.transform.lossyScale;
      Vector3 halfExtents = new(
        Mathf.Abs(box.size.x * scale.x) * 0.5f,
        Mathf.Abs(box.size.y * scale.y) * 0.5f,
        Mathf.Abs(box.size.z * scale.z) * 0.5f);
      if (halfExtents.x <= halfExtents.z) halfExtents.x += passageOccupancyDepth;
      else halfExtents.z += passageOccupancyDepth;
      int count = Physics.OverlapBoxNonAlloc(
        box.transform.TransformPoint(box.center),
        halfExtents,
        occupancyBuffer,
        box.transform.rotation,
        passageOccupantLayers,
        QueryTriggerInteraction.Ignore);
      return count > 0;
    }

    Bounds bounds = blockingCollider.bounds;
    bounds.Expand(passageOccupancyDepth * 2f);
    return Physics.OverlapBoxNonAlloc(
      bounds.center,
      bounds.extents,
      occupancyBuffer,
      Quaternion.identity,
      passageOccupantLayers,
      QueryTriggerInteraction.Ignore) > 0;
  }

  private void CalculateOpenPositions() {
    Vector3 doorLocalDirection = slideAxis == SlideAxis.LocalX ? Vector3.right : Vector3.forward;
    Vector3 worldOffset = transform.TransformDirection(doorLocalDirection).normalized * panelSlideDistance;

    // Panels may live below a scaled imported-model root. Convert the desired door-space movement
    // back into each panel parent's local space so model import scale never multiplies slide distance.
    leftOpenLocalPosition = OffsetInParentSpace(leftDoorPanel, leftClosedLocalPosition, worldOffset);
    rightOpenLocalPosition = OffsetInParentSpace(rightDoorPanel, rightClosedLocalPosition, -worldOffset);
  }

  private static Vector3 OffsetInParentSpace(Transform panel, Vector3 closedLocalPosition, Vector3 worldOffset) {
    if (panel == null || panel.parent == null) return closedLocalPosition;
    Vector3 closedWorldPosition = panel.parent.TransformPoint(closedLocalPosition);
    return panel.parent.InverseTransformPoint(closedWorldPosition + worldOffset);
  }

  private void ApplyPanelPositions(bool open) {
    CalculateOpenPositions();
    if (leftDoorPanel != null) leftDoorPanel.localPosition = open ? leftOpenLocalPosition : leftClosedLocalPosition;
    if (rightDoorPanel != null) rightDoorPanel.localPosition = open ? rightOpenLocalPosition : rightClosedLocalPosition;
  }

  private void ApplyPassageBlockingState(bool open) {
    if (blockingCollider != null) {
      blockingCollider.isTrigger = false;
      blockingCollider.enabled = !open;
    }

    if (navMeshObstacle != null) {
      // The baked navigation must remain connected so a guard can plan through a closed door and
      // discover that it needs to request passage. GuardMotor provides the runtime traversal gate.
      navMeshObstacle.carving = false;
      navMeshObstacle.enabled = false;
    }
  }

#if UNITY_EDITOR
  private void OnValidate() {
    if (startsLocked && startsOpen)
      Debug.LogWarning($"[PassagewayDoor] '{name}' starts open, so its initial lock will be ignored.", this);

    DoorKeyColorVisual colorVisual = GetComponent<DoorKeyColorVisual>();
    if (colorVisual != null) colorVisual.Apply();
  }
#endif
}
