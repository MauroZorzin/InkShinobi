using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tracks the closest nearby IInteractable every frame, dispatches interaction input to it, and drives
/// the interaction-prompt slot on the shared DialogueHUD. Text defaults to the target's layer (see
/// layerPrompts), but an interactable implementing IInteractionPrompt can override it with its own
/// (state-dependent) text. DialogueHUD itself decides whether this prompt is actually visible — an
/// active Dialogue message takes priority over it.
/// </summary>
public class PlayerInteractor : MonoBehaviour {
  [System.Serializable]
  private class LayerPrompt {
    public LayerMask layer;
    public string text = "Interagisci";
  }

  [Header("Inventory")]
  [Tooltip("Inventory used when interacting with nearby pickable objects.")]
  [SerializeField] private PlayerInventory inventory;

  [Tooltip("Shared camera feedback used when interaction is pressed without a usable target.")]
  [SerializeField] private RejectedAimCameraFeedback rejectionFeedback;

  [Header("Interaction")]
  [Tooltip("World point at the center of the interaction sphere.")]
  [SerializeField] private Transform interactionPoint;

  [Tooltip("Radius used to search for interactable objects around the interaction point.")]
  [SerializeField] private float interactionRadius = 0.8f;

  [Tooltip("Broad search radius used to discover interactables with a larger per-object range, such as keys. This must be at least as large as the largest authored interaction range.")]
  [SerializeField, Min(0f)] private float extendedInteractionSearchRadius = 1f;

  [Tooltip("Which layers count as interactable, and what prompt text to show for each.")]
  [SerializeField] private LayerPrompt[] layerPrompts = System.Array.Empty<LayerPrompt>();

  public bool interactionSuppressed;

  private readonly Collider[] _hitBuffer = new Collider[16];
  private IInteractable _currentTarget;
  private IInteractionFocus _currentFocus;

  private void Awake() {
    if (rejectionFeedback == null)
      rejectionFeedback = GetComponentInChildren<RejectedAimCameraFeedback>(true);
  }

  private void Update() {
    Collider hit = FindNearest(out IInteractable interactable);

    IInteractionFocus nextFocus = interactable as IInteractionFocus;
    if (!ReferenceEquals(_currentFocus, nextFocus)) {
      SetFocusStateIfAlive(_currentFocus, false);
      _currentFocus = nextFocus;
    }

    _currentTarget = interactable;
    SetFocusStateIfAlive(_currentFocus, true);
    UpdatePrompt(hit, interactable);
  }

  public void OnInteract(InputValue value) {
    if (!value.isPressed || interactionSuppressed) return;
    if (!IsUnityInterfaceAlive(_currentTarget)) {
      _currentTarget = null;
      if (!SceneTransitionManager.IsGamePaused && !SceneTransitionManager.IsDeathSequenceActive)
        rejectionFeedback?.PlayRejectedAction();
      return;
    }
    _currentTarget.Interact(inventory);
  }

  /// <summary>Searches the interaction volume for the closest object implementing IInteractable.</summary>
  private Collider FindNearest(out IInteractable interactable) {
    interactable = null;

    if (interactionPoint == null) {
      return null;
    }

    float searchRadius = Mathf.Max(interactionRadius, extendedInteractionSearchRadius);
    int hitCount = Physics.OverlapSphereNonAlloc(interactionPoint.position, searchRadius, _hitBuffer, InteractableMask(), QueryTriggerInteraction.Collide);

    Collider closestCollider = null;
    var closestDistance = float.MaxValue;

    for (int i = 0; i < hitCount; i++) {
      Collider hit = _hitBuffer[i];
      IInteractable candidate = hit.GetComponentInParent<IInteractable>();

      if (candidate == null) {
        continue;
      }

      float allowedRange = interactionRadius;
      if (candidate is IInteractionRange customRange && customRange.InteractionRange > 0f) {
        allowedRange = customRange.InteractionRange;
      }

      Vector3 closestPoint = hit.ClosestPoint(interactionPoint.position);
      float distance = Vector3.Distance(interactionPoint.position, closestPoint);
      if (distance > allowedRange) {
        continue;
      }

      if (distance < closestDistance) {
        closestDistance = distance;
        closestCollider = hit;
        interactable = candidate;
      }
    }

    return closestCollider;
  }

  private LayerMask InteractableMask() {
    int mask = 0;
    foreach (LayerPrompt entry in layerPrompts) {
      mask |= entry.layer.value;
    }
    return mask;
  }

  private void UpdatePrompt(Collider target, IInteractable interactable) {
    if (DialogueHUD.Instance == null) {
      return;
    }

    string text = null;
    if (interactable != null) {
      text = (interactable as IInteractionPrompt)?.GetPromptText(inventory);
      if (string.IsNullOrEmpty(text) && target != null) {
        text = TextForLayer(target.gameObject.layer);
      }
    }

    if (string.IsNullOrEmpty(text)) DialogueHUD.Instance.ClearInteractionPrompt();
    else DialogueHUD.Instance.ShowInteractionPrompt(text);
  }

  private void OnDisable() {
    SetFocusStateIfAlive(_currentFocus, false);
    _currentFocus = null;
    _currentTarget = null;
    DialogueHUD.Instance?.ClearInteractionPrompt();
  }

  private void SetFocusStateIfAlive(IInteractionFocus focus, bool focused) {
    if (IsUnityInterfaceAlive(focus)) focus.SetInteractionFocused(focused, inventory);
  }

  private static bool IsUnityInterfaceAlive(object value) {
    if (value == null) return false;
    return value is not Object unityObject || unityObject != null;
  }

  private string TextForLayer(int layer) {
    foreach (LayerPrompt entry in layerPrompts) {
      if ((entry.layer.value & (1 << layer)) != 0) {
        return entry.text;
      }
    }
    return null;
  }

  private void OnDrawGizmosSelected() {
    if (interactionPoint == null) {
      return;
    }

    Gizmos.DrawWireSphere(interactionPoint.position, interactionRadius);

    if (extendedInteractionSearchRadius > interactionRadius) {
      Color previousColor = Gizmos.color;
      Gizmos.color = Color.cyan;
      Gizmos.DrawWireSphere(interactionPoint.position, extendedInteractionSearchRadius);
      Gizmos.color = previousColor;
    }
  }
}
