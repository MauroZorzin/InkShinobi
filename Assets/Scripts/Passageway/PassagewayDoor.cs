using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sliding passageway door that can be toggled by the player, optionally gated by an inventory item.
/// </summary>
public class PassagewayDoor : MonoBehaviour {
  private enum SlideAxis {
    LocalX,
    LocalZ,
  }

  private enum MotionEasing {
    Linear,
    SmoothStep,
    EaseInOutSine,
    EaseOutCubic,
    EaseInOutCubic,
    CustomCurve,
  }

  [Header("Door Panels")]
  [Tooltip("Panel moved in the positive local slide direction when the door opens.")]
  [SerializeField] private Transform leftDoorPanel;

  [Tooltip("Panel moved in the negative local slide direction when the door opens.")]
  [SerializeField] private Transform rightDoorPanel;

  [Tooltip("Collider that blocks the passage while closed and becomes a trigger while open.")]
  [SerializeField] private Collider blockingCollider;

  [Tooltip("Optional NavMesh obstacle associated with the closed doorway.")]
  [SerializeField] private NavMeshObstacle navMeshObstacle;

  [Header("Visual Highlight")]
  [Tooltip("Renderers tinted when the player can or cannot use the door.")]
  [SerializeField] private Renderer[] highlightRenderers;

  [Tooltip("Color applied when the door is not highlighted.")]
  [SerializeField] private Color normalColor = Color.white;

  [Tooltip("Highlight color shown when the player can use the door.")]
  [SerializeField] private Color usableHighlightColor = Color.green;

  [Tooltip("Highlight color shown when the player is in range but cannot use the door.")]
  [SerializeField] private Color lockedHighlightColor = Color.red;

  [Header("Door State")]
  [Tooltip("Whether the door begins open when the scene starts.")]
  [SerializeField] private bool startsOpen = false;

  [Tooltip("Automatically starts an opening animation on Start when the door begins closed.")]
  [SerializeField] private bool autoOpenOnStart = false;

  [Tooltip("Automatically starts a closing animation on Start when the door begins open.")]
  [SerializeField] private bool autoCloseOnStart = false;

  [Tooltip("Local axis used by the panels when sliding open or closed.")]
  [SerializeField] private SlideAxis slideAxis = SlideAxis.LocalX;

  [Tooltip("Distance each panel moves away from its closed position.")]
  [SerializeField] private float panelSlideDistance = 0.75f;

  [Tooltip("Seconds used for each open or close animation.")]
  [SerializeField] private float animationDuration = 0.35f;

  [Tooltip("Easing function used to interpolate panel movement.")]
  [SerializeField] private MotionEasing motionEasing = MotionEasing.SmoothStep;

  [Tooltip("Curve used when Motion Easing is set to Custom Curve.")]
  [SerializeField] private AnimationCurve customEasingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

  [Header("Audio")]
  [Tooltip("Audio source used to play transition sounds.")]
  [SerializeField] private AudioSource audioSource;

  [Tooltip("Clip played when an opening animation starts.")]
  [SerializeField] private AudioClip openStartClip;

  [Tooltip("Clip played when a closing animation starts.")]
  [SerializeField] private AudioClip closeStartClip;

  [Tooltip("Clip played when an opening animation finishes.")]
  [SerializeField] private AudioClip openEndClip;

  [Tooltip("Clip played when a closing animation finishes.")]
  [SerializeField] private AudioClip closeEndClip;

  [Tooltip("Volume multiplier used for all door transition sounds.")]
  [SerializeField, Range(0f, 1f)] private float audioVolume = 1f;

  [Header("Item Requirement")]
  [Tooltip("Whether opening this door requires the configured inventory item.")]
  [SerializeField] private bool requiresItemToOpen = false;

  [Tooltip("Whether closing this door requires the configured inventory item.")]
  [SerializeField] private bool requiresItemToClose = false;

  [Tooltip("Inventory item id required when open or close requirements are enabled.")]
  [SerializeField] private string requiredItemId = "door_key";

  public bool IsOpen { get; private set; }

  private Vector3 leftClosedLocalPosition;
  private Vector3 rightClosedLocalPosition;
  private Vector3 leftOpenLocalPosition;
  private Vector3 rightOpenLocalPosition;
  private Coroutine animationCoroutine;

  private void Awake() {
    if (audioSource == null) {
      audioSource = GetComponent<AudioSource>();
    }

    if (leftDoorPanel == null && transform.childCount > 0) {
      leftDoorPanel = transform.GetChild(0);
    }

    if (rightDoorPanel == null && transform.childCount > 1) {
      rightDoorPanel = transform.GetChild(1);
    }

    if (blockingCollider != null) {
      blockingCollider.enabled = true;
      blockingCollider.isTrigger = startsOpen;
    }

    leftClosedLocalPosition = leftDoorPanel != null ? leftDoorPanel.localPosition : Vector3.zero;
    rightClosedLocalPosition = rightDoorPanel != null ? rightDoorPanel.localPosition : Vector3.zero;

    Vector3 localSlideDirection = slideAxis == SlideAxis.LocalX ? Vector3.right : Vector3.forward;
    leftOpenLocalPosition = leftClosedLocalPosition + localSlideDirection * panelSlideDistance;
    rightOpenLocalPosition = rightClosedLocalPosition - localSlideDirection * panelSlideDistance;

    if (navMeshObstacle != null) {
      navMeshObstacle.enabled = true;
      navMeshObstacle.carving = false;
    }

    IsOpen = startsOpen;
    ApplyPanelPositions(IsOpen);
    ApplyPassageBlockingState(IsOpen);

    SetHighlighted(false, false);
  }

  private void Start() {
    bool shouldAutoOpen = !startsOpen && autoOpenOnStart;
    bool shouldAutoClose = startsOpen && autoCloseOnStart;

    if (!shouldAutoOpen && !shouldAutoClose) {
      return;
    }

    // Startup transitions should not depend on inventory requirements.
    if (animationCoroutine != null) {
      StopCoroutine(animationCoroutine);
    }

    animationCoroutine = StartCoroutine(AnimateDoor(shouldAutoOpen));
  }

  /// <summary>
  /// Checks whether the supplied inventory can perform the next door toggle.
  /// </summary>
  /// <param name="inventory">The inventory used for item-gated doors.</param>
  /// <returns>True when the next open or close action is allowed.</returns>
  public bool CanToggle(PlayerInventory inventory) {
    var wantsToOpen = !IsOpen;

    if (wantsToOpen && requiresItemToOpen) {
      return PlayerHasRequiredItem(inventory);
    }

    if (!wantsToOpen && requiresItemToClose) {
      return PlayerHasRequiredItem(inventory);
    }

    return true;
  }

  /// <summary>
  /// Toggles the door to the opposite state when requirements are met.
  /// </summary>
  /// <param name="inventory">The inventory used for item-gated doors.</param>
  /// <returns>True when the toggle was accepted.</returns>
  public bool TryToggle(PlayerInventory inventory) {
    return TrySetOpen(!IsOpen, inventory);
  }

  /// <summary>
  /// Starts an open or close transition when requirements are met.
  /// </summary>
  /// <param name="open">True to open the door; false to close it.</param>
  /// <param name="inventory">The inventory used for item-gated doors.</param>
  /// <returns>True when the requested state is accepted or already reached.</returns>
  public bool TrySetOpen(bool open, PlayerInventory inventory) {
    if (open == IsOpen && animationCoroutine == null) {
      return true;
    }

    if (open && requiresItemToOpen && !PlayerHasRequiredItem(inventory)) {
      Debug.LogWarning($"{name}: This door requires item '{requiredItemId}' to open.");
      return false;
    }

    if (!open && requiresItemToClose && !PlayerHasRequiredItem(inventory)) {
      Debug.LogWarning($"{name}: This door requires item '{requiredItemId}' to close.");
      return false;
    }

    if (animationCoroutine != null) {
      StopCoroutine(animationCoroutine);
    }

    animationCoroutine = StartCoroutine(AnimateDoor(open));
    return true;
  }

  /// <summary>
  /// Updates highlight renderers to show whether the player can currently use the door.
  /// </summary>
  /// <param name="highlighted">Whether the door should be highlighted.</param>
  /// <param name="canUse">Whether the highlight should indicate a usable door.</param>
  public void SetHighlighted(bool highlighted, bool canUse) {
    Color targetColor = normalColor;

    if (highlighted) {
      targetColor = canUse ? usableHighlightColor : lockedHighlightColor;
    }

    if (highlightRenderers == null) {
      return;
    }

    foreach (Renderer renderer in highlightRenderers) {
      if (renderer == null) {
        continue;
      }

      var mat = renderer.material;
      mat.color = targetColor;
      // Use the proper emission color property for Unity's Standard shader
      mat.SetColor("_EmissionColor", targetColor);
      if (targetColor != Color.black) {
        mat.EnableKeyword("_EMISSION");
      } else {
        mat.DisableKeyword("_EMISSION");
      }
    }
  }

  /// <summary>
  /// Measures how close a world position is to the door interaction surface.
  /// </summary>
  /// <param name="worldPosition">The position to compare against the door.</param>
  /// <returns>The nearest distance to the blocking collider or door panels.</returns>
  public float GetInteractionDistance(Vector3 worldPosition) {
    if (blockingCollider != null && blockingCollider.enabled) {
      Vector3 closestPoint = blockingCollider.ClosestPoint(worldPosition);
      return Vector3.Distance(worldPosition, closestPoint);
    }

    float nearest = Vector3.Distance(worldPosition, transform.position);

    if (leftDoorPanel != null) {
      nearest = Mathf.Min(nearest, Vector3.Distance(worldPosition, leftDoorPanel.position));
    }

    if (rightDoorPanel != null) {
      nearest = Mathf.Min(nearest, Vector3.Distance(worldPosition, rightDoorPanel.position));
    }

    return nearest;
  }

  private bool PlayerHasRequiredItem(PlayerInventory inventory) {
    if (inventory == null) {
      return false;
    }

    return inventory.HasItem(requiredItemId);
  }

  /// <summary>
  /// Animates panel positions and applies the final blocking state.
  /// </summary>
  /// <param name="open">True when animating toward the open state.</param>
  private IEnumerator AnimateDoor(bool open) {
    Vector3 leftStart = leftDoorPanel != null ? leftDoorPanel.localPosition : Vector3.zero;
    Vector3 rightStart = rightDoorPanel != null ? rightDoorPanel.localPosition : Vector3.zero;
    Vector3 leftTarget = open ? leftOpenLocalPosition : leftClosedLocalPosition;
    Vector3 rightTarget = open ? rightOpenLocalPosition : rightClosedLocalPosition;

    if (!open) {
      ApplyPassageBlockingState(false);
    }

    PlayTransitionClip(open, true);

    var elapsed = 0f;

    while (elapsed < animationDuration) {
      elapsed += Time.deltaTime;

      var t = Mathf.Clamp01(elapsed / animationDuration);
      var easedT = EvaluateEasing(t);

      if (leftDoorPanel != null) {
        leftDoorPanel.localPosition = Vector3.Lerp(leftStart, leftTarget, easedT);
      }

      if (rightDoorPanel != null) {
        rightDoorPanel.localPosition = Vector3.Lerp(rightStart, rightTarget, easedT);
      }

      yield return null;
    }

    ApplyPanelPositions(open);
    IsOpen = open;
    ApplyPassageBlockingState(IsOpen);

    PlayTransitionClip(open, false);
    animationCoroutine = null;
  }

  /// <summary>
  /// Evaluates the configured easing mode for a normalized animation time.
  /// </summary>
  /// <param name="t">Normalized time in the range 0..1.</param>
  /// <returns>The eased interpolation value.</returns>
  private float EvaluateEasing(float t) {
    return motionEasing switch {
      MotionEasing.Linear => t,
      MotionEasing.SmoothStep => Mathf.SmoothStep(0f, 1f, t),
      MotionEasing.EaseInOutSine => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t),
      MotionEasing.EaseOutCubic => 1f - Mathf.Pow(1f - t, 3f),
      MotionEasing.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f,
      MotionEasing.CustomCurve => customEasingCurve == null ? t : Mathf.Clamp01(customEasingCurve.Evaluate(t)),
      _ => t,
    };
  }

  private void PlayTransitionClip(bool opening, bool atStart) {
    if (audioSource == null) {
      return;
    }

    AudioClip clip;

    if (opening) {
      clip = atStart ? openStartClip : openEndClip;
    } else {
      clip = atStart ? closeStartClip : closeEndClip;
    }

    if (clip == null) {
      return;
    }

    audioSource.PlayOneShot(clip, audioVolume);
  }

  private void ApplyPanelPositions(bool open) {
    if (leftDoorPanel != null) {
      leftDoorPanel.localPosition = open ? leftOpenLocalPosition : leftClosedLocalPosition;
    }

    if (rightDoorPanel != null) {
      rightDoorPanel.localPosition = open ? rightOpenLocalPosition : rightClosedLocalPosition;
    }
  }

  private void ApplyPassageBlockingState(bool open) {
    if (blockingCollider != null) {
      blockingCollider.enabled = true;
      blockingCollider.isTrigger = open;
    }

    if (navMeshObstacle != null) {
      navMeshObstacle.enabled = true;
      navMeshObstacle.carving = false;
    }
  }
}
