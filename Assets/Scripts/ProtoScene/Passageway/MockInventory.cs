using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MockInventory : MonoBehaviour {
  [Header("Temporary Test Input")]
  private readonly Key giveDoorKeyKey = Key.K;

  [Header("Test Item")]
  [SerializeField] private string doorKeyItemId = "door_key";

  [Header("Debug UI")]
  [SerializeField] private bool showDebugUI = true;

  private readonly HashSet<string> items = new HashSet<string>();

  public bool HasItem(string itemId) {
    if (string.IsNullOrWhiteSpace(itemId)) {
      return true;
    }

    return items.Contains(itemId);
  }

  public void AddItem(string itemId) {
    if (string.IsNullOrWhiteSpace(itemId)) {
      return;
    }

    if (items.Add(itemId)) {
      Debug.Log($"Obtained item: {itemId}");
    }
  }

  private void Update() {
    if (WasKeyPressedThisFrame(giveDoorKeyKey)) {
      AddItem(doorKeyItemId);
    }
  }

  private bool WasKeyPressedThisFrame(Key key) {
    Keyboard keyboard = Keyboard.current;

    if (keyboard == null) {
      return false;
    }

    return keyboard[key].wasPressedThisFrame;
  }

  private void OnGUI() {
    if (!showDebugUI) {
      return;
    }

    var hasKeyText = HasItem(doorKeyItemId) ? "YES" : "NO";

    GUI.Box(new Rect(10, 10, 260, 70), $"Mock Inventory\n{giveDoorKeyKey}: Get Door Key\nDoor Key: {hasKeyText}");
  }
}
