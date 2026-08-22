using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tracks the closest nearby IInteractable every frame, dispatches interaction input to it, and drives
/// a shared prompt label. Text defaults to the target's layer (see layerPrompts), but an interactable
/// implementing IInteractionPrompt can override it with its own (state-dependent) text.
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

  [Tooltip("Text element the prompt is written to and shown/hidden on.")]
  [SerializeField] private TextMeshProUGUI promptLabel;

  [Tooltip("Which layers count as interactable, and what prompt text to show for each.")]
  [SerializeField] private LayerPrompt[] layerPrompts = System.Array.Empty<LayerPrompt>();

  public bool interactionSuppressed;

  private readonly Collider[] _hitBuffer = new Collider[16];
  private IInteractable _currentTarget;

  private void Update() {
    Collider hit = FindNearest(out IInteractable interactable);
    _currentTarget = interactable;
    UpdatePrompt(hit, interactable);
  }

  public void OnInteract(InputValue value) {
    if (value.isPressed && !interactionSuppressed) {
      _currentTarget?.Interact(inventory);
    }
  }

  /// <summary>Searches the interaction volume for the closest object implementing IInteractable.</summary>
  private Collider FindNearest(out IInteractable interactable) {
    interactable = null;

    if (interactionPoint == null) {
      return null;
    }

    int hitCount = Physics.OverlapSphereNonAlloc(interactionPoint.position, interactionRadius, _hitBuffer, InteractableMask(), QueryTriggerInteraction.Collide);

    Collider closestCollider = null;
    var closestDistance = float.MaxValue;

    for (int i = 0; i < hitCount; i++) {
      Collider hit = _hitBuffer[i];
      IInteractable candidate = hit.GetComponentInParent<IInteractable>();

      if (candidate == null) {
        continue;
      }

      var distance = Vector3.Distance(interactionPoint.position, hit.transform.position);

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

    string text = null;
    if (interactable != null) {
      text = (interactable as IInteractionPrompt)?.GetPromptText(inventory);
      if (string.IsNullOrEmpty(text) && target != null) text = TextForLayer(target.gameObject.layer);
    }

    promptLabel.gameObject.SetActive(text != null);
    if (text != null) promptLabel.text = text;
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
  }
}
