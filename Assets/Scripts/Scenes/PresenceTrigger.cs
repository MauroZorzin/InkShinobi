using UnityEngine;

/// <summary>
/// Trigger zone that enables target objects/components while the player is inside,
/// and disables them again on exit. Unlike TutorialTrigger (one-shot, fires once and
/// stays that way), this follows the player's presence back and forth.
/// </summary>
public class PresenceTrigger : MonoBehaviour {
  [System.Serializable]
  public class ComponentToggle {
    [Tooltip("GameObject the component lives on. Leave empty to use the player.")]
    public GameObject target;

    [Tooltip("Component type name (e.g. 'PlayerInteractor').")]
    public string componentName;
  }

  [SerializeField]
  [Tooltip("GameObjects enabled while the player is inside, disabled again on exit.")]
  private GameObject[] objectsToToggle = new GameObject[0];

  [SerializeField]
  [Tooltip("Components enabled while the player is inside, disabled again on exit.")]
  private ComponentToggle[] componentsToToggle = new ComponentToggle[0];

  [SerializeField]
  [Tooltip("Player GameObject (auto-finds by tag if left empty).")]
  private GameObject player;

  private bool _playerInside;

  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (col != null) {
      col.isTrigger = true;
    }
  }

  private void Start() {
    if (player == null) {
      player = GameObject.FindWithTag("Player");
      if (player == null) {
        Debug.LogError($"[PresenceTrigger] '{name}': Player not found. Assign in inspector or tag as 'Player'.", this);
      }
    }
  }

  private void OnTriggerEnter(Collider other) {
    if (_playerInside || !other.CompareTag("Player")) {
      return;
    }

    _playerInside = true;
    SetToggles(true);
  }

  private void OnTriggerExit(Collider other) {
    if (!_playerInside || !other.CompareTag("Player")) {
      return;
    }

    _playerInside = false;
    SetToggles(false);
  }

  private void SetToggles(bool active) {
    foreach (GameObject obj in objectsToToggle) {
      if (obj != null) {
        obj.SetActive(active);
      }
    }

    foreach (ComponentToggle toggle in componentsToToggle) {
      SetComponentEnabled(toggle, active);
    }
  }

  private void SetComponentEnabled(ComponentToggle toggle, bool active) {
    if (string.IsNullOrEmpty(toggle.componentName)) {
      return;
    }

    GameObject target = toggle.target != null ? toggle.target : player;
    if (target == null) {
      return;
    }

    Component component = target.GetComponent(toggle.componentName);
    if (component == null) {
      Debug.LogWarning($"[PresenceTrigger] '{name}': Component '{toggle.componentName}' not found on '{target.name}'.", this);
      return;
    }

    if (component is Behaviour behaviour) {
      behaviour.enabled = active;
      return;
    }

    if (component is Collider collider) {
      collider.enabled = active;
      return;
    }

    if (component is Rigidbody rb) {
      rb.isKinematic = !active;
      rb.constraints = active ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
      return;
    }

    Debug.LogWarning($"[PresenceTrigger] '{name}': Cannot toggle component type '{component.GetType().Name}'.", this);
  }
}
