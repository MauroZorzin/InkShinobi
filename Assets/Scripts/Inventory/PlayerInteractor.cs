using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Finds nearby interactable objects and dispatches player interaction input to the closest valid target.
/// </summary>
public class PlayerInteractor : MonoBehaviour {
  [Header("Inventory")]
  [Tooltip("Inventory used when interacting with nearby pickable objects.")]
  [SerializeField] private PlayerInventory inventory;

  [Header("Interaction")]
  [Tooltip("World point at the center of the interaction sphere.")]
  [SerializeField] private Transform interactionPoint;

  [Tooltip("Radius used to search for interactable objects around the interaction point.")]
  [SerializeField] private float interactionRadius = 0.8f;

  [Tooltip("Layer mask containing objects that can be interacted with.")]
  [SerializeField] private LayerMask interactableLayer;

  public void OnInteract(InputValue value) {
    if (value.isPressed) {
      TryInteract();
    }
  }

  /// <summary>
  /// Searches the interaction volume and interacts with the closest object implementing IInteractable.
  /// </summary>
  private void TryInteract() {
    if (inventory == null || interactionPoint == null) {
      return;
    }

    Collider[] hits = Physics.OverlapSphere(interactionPoint.position, interactionRadius, interactableLayer, QueryTriggerInteraction.Collide);

    if (hits.Length == 0) {
      return;
    }

    IInteractable closest = null;
    var closestDistance = float.MaxValue;

    foreach (Collider hit in hits) {
      IInteractable interactable = hit.GetComponentInParent<IInteractable>();

      if (interactable == null) {
        continue;
      }

      var distance = Vector3.Distance(interactionPoint.position, hit.transform.position);

      if (distance < closestDistance) {
        closestDistance = distance;
        closest = interactable;
      }
    }

    closest?.Interact(inventory);
  }

  private void OnDrawGizmosSelected() {
    if (interactionPoint == null) {
      return;
    }

    Gizmos.DrawWireSphere(interactionPoint.position, interactionRadius);
  }
}
