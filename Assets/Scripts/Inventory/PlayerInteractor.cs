using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tracks the closest nearby IInteractable every frame, dispatches interaction input to it, and drives
/// a shared prompt label whose text is picked by the target's layer (see layerPrompts).
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

  [Header("Interaction")]
  [Tooltip("World point at the center of the interaction sphere.")]
  [SerializeField] private Transform interactionPoint;

  [Tooltip("Radius used to search for interactable objects around the interaction point.")]
  [SerializeField] private float interactionRadius = 0.8f;

  [Tooltip("Broad search radius used to discover interactables with a larger per-object range, such as keys. This must be at least as large as the largest authored interaction range.")]
  [SerializeField, Min(0f)] private float extendedInteractionSearchRadius = 1f;

  [Tooltip("Text element the prompt is written to and shown/hidden on.")]
  [SerializeField] private TextMeshProUGUI promptLabel;

  [Tooltip("Which layers count as interactable, and what prompt text to show for each.")]
  [SerializeField] private LayerPrompt[] layerPrompts = System.Array.Empty<LayerPrompt>();

  private readonly Collider[] _hitBuffer = new Collider[16];
  private IInteractable _currentTarget;
  private IInteractionFocus _currentFocus;

  private void Update() {
    Collider hit = FindNearest(out IInteractable interactable);

    IInteractionFocus nextFocus = interactable as IInteractionFocus;
    if (!ReferenceEquals(_currentFocus, nextFocus)) {
      _currentFocus?.SetInteractionFocused(false, inventory);
      _currentFocus = nextFocus;
    }

    _currentTarget = interactable;
    _currentFocus?.SetInteractionFocused(true, inventory);
    UpdatePrompt(hit, interactable);
  }

  public void OnInteract(InputValue value) {
    if (value.isPressed) {
      _currentTarget?.Interact(inventory);
    }
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
    if (promptLabel == null) {
      return;
    }

    string text = interactable is IInteractionPrompt prompt
      ? prompt.GetInteractionPrompt(inventory)
      : target != null ? TextForLayer(target.gameObject.layer) : null;

    promptLabel.gameObject.SetActive(text != null);
    if (text != null) promptLabel.text = text;
  }

  private void OnDisable() {
    _currentFocus?.SetInteractionFocused(false, inventory);
    _currentFocus = null;
    _currentTarget = null;
    if (promptLabel != null) promptLabel.gameObject.SetActive(false);
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
