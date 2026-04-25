using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDoorInteractor : MonoBehaviour {
  [Header("Input")]
  private readonly Key interactKey = Key.X;

  [Header("Interaction")]
  [SerializeField] private float interactionRange = 2f;
  [SerializeField] private MockInventory inventory;

  [Header("Debug UI")]
  [SerializeField] private bool showDebugUI = true;

  private MockPassagewayDoor[] doors;
  private MockPassagewayDoor highlightedDoor;

  private void Start() {
    doors = FindObjectsByType<MockPassagewayDoor>(FindObjectsSortMode.None);

    if (inventory == null) {
      inventory = FindAnyObjectByType<MockInventory>();
    }
  }

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

      var distance = Vector3.Distance(transform.position, door.transform.position);

      if (distance <= nearestDistance) {
        nearestDistance = distance;
        nearestDoor = door;
      }
    }

    return nearestDoor;
  }

  private void UpdateHighlight(MockPassagewayDoor nearestDoor) {
    if (highlightedDoor != null && highlightedDoor != nearestDoor) {
      highlightedDoor.SetHighlighted(false, false);
    }

    highlightedDoor = nearestDoor;

    if (highlightedDoor != null) {
      var canUse = highlightedDoor.CanToggle(inventory);
      highlightedDoor.SetHighlighted(true, canUse);
    }
  }

  private void OnGUI() {
    if (!showDebugUI) {
      return;
    }

    string text;

    if (highlightedDoor == null) {
      text = $"Move close to a door\n{interactKey}: Interact";
    } else {
      var canUse = highlightedDoor.CanToggle(inventory);

      text = canUse ? $"{interactKey}: Toggle {highlightedDoor.name}" : $"{highlightedDoor.name} is locked";
    }

    GUI.Box(new Rect(10, 180, 300, 70), text);
  }

  private void OnDrawGizmosSelected() {
    Gizmos.color = Color.cyan;
    Gizmos.DrawWireSphere(transform.position, interactionRange);
  }
}
