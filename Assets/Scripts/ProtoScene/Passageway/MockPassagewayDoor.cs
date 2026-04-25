using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class MockPassagewayDoor : MonoBehaviour {
  [Header("Door Parts")]
  [SerializeField] private Transform hingeTransform;
  [SerializeField] private Collider blockingCollider;
  [SerializeField] private NavMeshObstacle navMeshObstacle;

  [Header("Visual Highlight")]
  [SerializeField] private Renderer[] highlightRenderers;
  [SerializeField] private Color normalColor = Color.white;
  [SerializeField] private Color usableHighlightColor = Color.green;
  [SerializeField] private Color lockedHighlightColor = Color.red;

  [Header("Door State")]
  [SerializeField] private bool startsOpen = false;
  [SerializeField] private float openAngleY = 90f;
  [SerializeField] private float animationDuration = 0.35f;

  [Header("Item Requirement")]
  [SerializeField] private bool requiresItemToOpen = false;
  [SerializeField] private bool requiresItemToClose = false;
  [SerializeField] private string requiredItemId = "door_key";

  public bool IsOpen { get; private set; }

  private Quaternion closedRotation;
  private Quaternion openRotation;
  private Coroutine animationCoroutine;

  private void Awake() {
    if (hingeTransform == null) {
      hingeTransform = transform;
    }

    closedRotation = hingeTransform.localRotation;
    openRotation = closedRotation * Quaternion.Euler(0f, openAngleY, 0f);

    if (navMeshObstacle != null) {
      navMeshObstacle.carving = true;
    }

    IsOpen = startsOpen;
    hingeTransform.localRotation = IsOpen ? openRotation : closedRotation;

    SetPassageBlocked(!IsOpen);
    SetHighlighted(false, false);
  }

  public bool CanToggle(MockInventory inventory) {
    var wantsToOpen = !IsOpen;

    if (wantsToOpen && requiresItemToOpen) {
      return PlayerHasRequiredItem(inventory);
    }

    if (!wantsToOpen && requiresItemToClose) {
      return PlayerHasRequiredItem(inventory);
    }

    return true;
  }

  public bool TryToggle(MockInventory inventory) {
    return TrySetOpen(!IsOpen, inventory);
  }

  public bool TrySetOpen(bool open, MockInventory inventory) {
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

      renderer.material.color = targetColor;
    }
  }

  private bool PlayerHasRequiredItem(MockInventory inventory) {
    if (inventory == null) {
      return false;
    }

    return inventory.HasItem(requiredItemId);
  }

  private IEnumerator AnimateDoor(bool open) {
    if (!open) {
      SetPassageBlocked(true);
    }

    Quaternion startRotation = hingeTransform.localRotation;
    Quaternion targetRotation = open ? openRotation : closedRotation;

    var elapsed = 0f;

    while (elapsed < animationDuration) {
      elapsed += Time.deltaTime;

      var t = Mathf.Clamp01(elapsed / animationDuration);
      var smoothedT = Mathf.SmoothStep(0f, 1f, t);

      hingeTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, smoothedT);

      yield return null;
    }

    hingeTransform.localRotation = targetRotation;
    IsOpen = open;

    if (open) {
      SetPassageBlocked(false);
    }

    animationCoroutine = null;
  }

  private void SetPassageBlocked(bool blocked) {
    if (blockingCollider != null) {
      blockingCollider.enabled = blocked;
    }

    if (navMeshObstacle != null) {
      navMeshObstacle.carving = true;
      navMeshObstacle.enabled = blocked;
    }
  }
}
