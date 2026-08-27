using System.Collections;
using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// Sliding passageway door. A door may start locked; the matching runtime key is consumed once,
/// after which the door stays unlocked and can always be opened and closed.
/// </summary>
public class PassagewayDoor : MonoBehaviour, IInteractable, IInteractionPrompt, IInteractionFocus {
  public enum PassageState { Closed, Opening, Open, Closing }
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

  [Header("Lock")]
  [FormerlySerializedAs("requiresItemToOpen")]
  [Tooltip("When enabled, this door begins locked and needs the matching key once.")]
  [SerializeField] private bool startsLocked;
  [FormerlySerializedAs("requiredItemId")]
  [Tooltip("Stable id that must match the runtime id of the carried key.")]
  [SerializeField] private string requiredKeyId = "door_key";
  [Tooltip("Authored lock/key colour. Door-panel colouring can use this in the later visual pass.")]
  [SerializeField] private Color requiredKeyColor = new(0.25f, 0.7f, 1f, 1f);
  [FormerlySerializedAs("requiresItemToClose")]
  [SerializeField, HideInInspector] private bool obsoleteRequiresItemToClose;

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
  public Transform LeftDoorPanel => leftDoorPanel;
  public Transform RightDoorPanel => rightDoorPanel;

  private Vector3 leftClosedLocalPosition;
  private Vector3 rightClosedLocalPosition;
  private Vector3 leftOpenLocalPosition;
  private Vector3 rightOpenLocalPosition;
  private Coroutine animationCoroutine;
  private DoorLinePathState linePathState;
  private DoorKeyColorVisual keyColorVisual;

  public void Interact(PlayerInventory inventory) => TryToggle(inventory);

  public string GetInteractionPrompt(PlayerInventory inventory) {
    if (animationCoroutine != null) return null;
    if (PlayerOccupiesDoorPath(inventory)) return null;
    if (IsOpen) return "[X] Close Door";
    if (!IsLocked) return "[X] Open Door";
    return PlayerHasRequiredKey(inventory) ? "[X] Unlock Door" : "[X] Locked";
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

  private void Start() {
    bool shouldAutoOpen = !startsOpen && autoOpenOnStart;
    bool shouldAutoClose = startsOpen && autoCloseOnStart;
    if (!shouldAutoOpen && !shouldAutoClose) return;

    if (shouldAutoOpen) IsLocked = false;
    animationCoroutine = StartCoroutine(AnimateDoor(shouldAutoOpen));
  }

  public bool CanToggle(PlayerInventory inventory) {
    if (animationCoroutine != null) return false;
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

    if (open && IsLocked) {
      if (!PlayerHasRequiredKey(inventory)) {
        Debug.LogWarning($"{name}: This door requires key '{requiredKeyId}' to open.", this);
        return false;
      }

      inventory.ConsumeItem();
      IsLocked = false;
    }

    animationCoroutine = StartCoroutine(AnimateDoor(open));
    return true;
  }

  private bool PlayerHasRequiredKey(PlayerInventory inventory) =>
    inventory != null && !string.IsNullOrWhiteSpace(requiredKeyId) && inventory.HasItem(requiredKeyId);

  private bool PlayerOccupiesDoorPath(PlayerInventory inventory) {
    if (inventory == null) return false;
    if (linePathState == null) linePathState = GetComponentInChildren<DoorLinePathState>(true);
    if (linePathState == null) return false;

    LineFollowController movement = inventory.GetComponentInParent<LineFollowController>();
    if (movement == null) movement = inventory.GetComponentInChildren<LineFollowController>(true);
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
      navMeshObstacle.carving = !open;
      navMeshObstacle.enabled = !open;
    }
  }

#if UNITY_EDITOR
  private void OnValidate() {
    if (startsLocked && string.IsNullOrWhiteSpace(requiredKeyId))
      Debug.LogWarning($"[PassagewayDoor] '{name}' starts locked but Required Key Id is empty.", this);
    if (startsLocked && startsOpen)
      Debug.LogWarning($"[PassagewayDoor] '{name}' starts open, so its initial lock will be ignored.", this);

    DoorKeyColorVisual colorVisual = GetComponent<DoorKeyColorVisual>();
    if (colorVisual != null) colorVisual.Apply();
  }
#endif
}
