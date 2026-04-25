using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDoorInteractor : MonoBehaviour {
  [Header("Input")]
  private readonly Key interactKey = Key.X;

  [Header("Interaction")]
  [SerializeField] private float interactionRange = 2f;
  [SerializeField] private MockInventory inventory;
  [SerializeField] private MockPassagewayDoor[] doors;

  [Header("Debug UI")]
  [SerializeField] private bool showDebugUI = true;
  private MockPassagewayDoor currentDoor;

  private void Update() {
    MockPassagewayDoor nearestDoor = FindNearestDoorInRange();

    UpdateHighlight(nearestDoor);

    if (nearestDoor != null && WasKeyPressedThisFrame(interactKey)) {
      nearestDoor.TryToggle(inventory);
    }
  }

  private bool WasKeyPressedThisFrame(Key key) {
    Keyboard keyboard = Keyboard.current;

    if (keyboard == null) {
      return false;
    }

    return keyboard[key].wasPressedThisFrame;
  }

  private MockPassagewayDoor FindNearestDoorInRange() {
    MockPassagewayDoor nearestDoor = null;
    var nearestDistance = interactionRange;

    foreach (MockPassagewayDoor door in doors) {
      if (door == null) {
        continue;
      }

      var distance = door.GetInteractionDistance(transform.position);

      if (distance <= nearestDistance) {
        nearestDistance = distance;
        nearestDoor = door;
      }
    }

    return nearestDoor;
  }

  private void UpdateHighlight(MockPassagewayDoor nearestDoor) {
    if (currentDoor != null && currentDoor != nearestDoor) {
      currentDoor.SetHighlighted(false, false);
    }

    currentDoor = nearestDoor;

    if (currentDoor != null) {
      var canUse = currentDoor.CanToggle(inventory);
      currentDoor.SetHighlighted(true, canUse);
    }
  }

  private void OnGUI() {
    if (!showDebugUI) {
      return;
    }

    string text;

    if (currentDoor == null) {
      text = $"Move close to a door\n{interactKey}: Interact";
    } else {
      text = currentDoor.CanToggle(inventory) ? $"{interactKey}: Toggle {currentDoor.name}" : $"{currentDoor.name} is locked";
    }

    GUI.Box(new Rect(10, 180, 300, 70), text);
  }

  private void OnDrawGizmosSelected() {
    Gizmos.color = Color.cyan;
    Gizmos.DrawWireSphere(transform.position, interactionRange);
  }
}
