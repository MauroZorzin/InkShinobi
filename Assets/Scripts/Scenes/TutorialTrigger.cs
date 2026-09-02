using UnityEngine;

/// <summary>
/// Self-contained tutorial trigger zone.
/// On player entry, enables/disables text GameObjects and player components.
/// All logic contained here - no dependency on other managers.
/// </summary>
public class TutorialTrigger : MonoBehaviour {

  [System.Serializable]
  public class TextAction {
    [Tooltip("Text GameObject to control")]
    public GameObject textObject;

    [Tooltip("Enable, Disable, or None")]
    public ActionType action = ActionType.None;
  }

  [System.Serializable]
  public class ComponentAction {
    [Tooltip("Player component name (e.g., 'PlayerInteractor')")]
    public string componentName;

    [Tooltip("Enable, Disable, or None")]
    public ActionType action = ActionType.None;
  }

  public enum ActionType {
    None,
    Enable,
    Disable
  }

  [SerializeField]
  [Tooltip("Text elements to show/hide")]
  private TextAction[] textActions = new TextAction[0];

  [SerializeField]
  [Tooltip("Player components to enable/disable")]
  private ComponentAction[] componentActions = new ComponentAction[0];

  [SerializeField]
  [Tooltip("Player GameObject (auto-finds if null)")]
  private GameObject player;

  [SerializeField]
  [Tooltip("Destroy this trigger after first activation")]
  private bool destroyAfterUse = true;

  private bool _activated = false;

  private void Start() {
    // Ensure collider is a trigger
    Collider col = GetComponent<Collider>();
    if (col != null) {
      col.isTrigger = true;
    }

    // Find player if not assigned
    if (player == null) {
      player = GameObject.FindWithTag("Player");
      if (player == null) {
        Debug.LogError("[TutorialTrigger] Player not found. Assign in inspector or tag as 'Player'");
      }
    }
  }

  private void OnTriggerEnter(Collider other) {
    if (_activated) return;

    if (!other.CompareTag("Player")) return;

    Execute();
  }

  /// <summary>
  /// Manually execute this trigger (for testing).
  /// </summary>
  public void Execute() {
    if (_activated) return;

    _activated = true;

    // Process text actions
    foreach (var textAction in textActions) {
      if (textAction.textObject == null) continue;

      switch (textAction.action) {
        case ActionType.Enable:
          textAction.textObject.SetActive(true);
          break;

        case ActionType.Disable:
          textAction.textObject.SetActive(false);
          break;

        case ActionType.None:
          // Do nothing
          break;
      }
    }

    // Process component actions
    foreach (var compAction in componentActions) {
      if (string.IsNullOrEmpty(compAction.componentName) || player == null) continue;

      SetComponentState(compAction.componentName, compAction.action);
    }

    if (destroyAfterUse) {
      Destroy(gameObject);
    }
  }

  private void SetComponentState(string componentName, ActionType action) {
    Component component = player.GetComponent(componentName);
    if (component == null) {
      Debug.LogError($"[TutorialTrigger] Component '{componentName}' not found on player");
      return;
    }

    bool shouldEnable = action == ActionType.Enable;

    // MonoBehaviour
    if (component is MonoBehaviour monoBehaviour) {
      monoBehaviour.enabled = shouldEnable;
      return;
    }

    // Collider
    if (component is Collider collider) {
      collider.enabled = shouldEnable;
      return;
    }

    // Rigidbody
    if (component is Rigidbody rb) {
      if (shouldEnable) {
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
      } else {
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeAll;
      }
      return;
    }

    Debug.LogWarning($"[TutorialTrigger] Cannot toggle component type: {component.GetType().Name}");
  }
}
