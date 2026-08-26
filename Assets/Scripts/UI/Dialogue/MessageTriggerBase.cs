using UnityEngine;

/// <summary>
/// Shared trigger-volume/one-shot/dismissal/component-toggle machinery for InformationTrigger and
/// DialogTrigger — the two differ only in which DialogueHUD slot (and therefore priority) they
/// write to, via the Show/Clear/LogTag overrides below.
///
/// Fires once on player entry, never refires. Dismissal has three modes: OnExit (clears when the
/// player leaves the trigger volume — the GameObject is kept alive, even with destroyAfterUse set,
/// until that actually happens, since a destroyed trigger's OnTriggerExit is not guaranteed to
/// fire), Timed (auto-clears after Display Duration seconds regardless of the player's position), or
/// Persistent (never auto-clears — stays up until something else overrides or clears it). Also
/// carries TutorialTrigger's by-name component enable/disable toggles.
/// </summary>
[RequireComponent(typeof(Collider))]
public abstract class MessageTriggerBase : MonoBehaviour {
  public enum DismissMode { OnExit, Timed, Persistent }

  public enum ActionType { None, Enable, Disable }

  [System.Serializable]
  public class ComponentAction {
    [Tooltip("Player component name (e.g. 'TakedownController').")]
    public string componentName;

    public ActionType action = ActionType.None;
  }

  [TextArea]
  [SerializeField] private string message;

  [Header("Dismissal")]
  [Tooltip("OnExit = clears when the player leaves the trigger volume. Timed = auto-clears after Display Duration seconds. Persistent = never auto-clears once shown.")]
  [SerializeField] private DismissMode dismissMode = DismissMode.OnExit;

  [Tooltip("Only used when Dismiss Mode is Timed.")]
  [SerializeField] private float displayDuration = 3f;

  [Header("Component Toggles")]
  [Tooltip("Player components to enable/disable when this trigger fires.")]
  [SerializeField] private ComponentAction[] componentActions = new ComponentAction[0];

  [Tooltip("GameObject the component toggles apply to (auto-finds by tag if left empty).")]
  [SerializeField] private GameObject player;

  [Header("Lifecycle")]
  [Tooltip("Destroy this trigger's GameObject once its message has been dismissed (or, for Persistent, right after firing).")]
  [SerializeField] private bool destroyAfterUse = true;

  private bool _activated;

  /// <summary>Short name used in warnings — override per concrete trigger type.</summary>
  protected abstract string LogTag { get; }

  protected abstract void Show(string text, float timedDuration);

  protected abstract void Clear();

  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (col != null) col.isTrigger = true;

    if (player == null) player = GameObject.FindWithTag("Player");
  }

  private void OnTriggerEnter(Collider other) {
    if (_activated || !other.CompareTag("Player")) return;
    _activated = true;

    ApplyComponentActions();

    if (DialogueHUD.Instance == null) {
      Debug.LogWarning($"[{LogTag}] '{name}': no DialogueHUD in the scene — message not shown.", this);
    } else {
      Show(message, dismissMode == DismissMode.Timed ? displayDuration : 0f);
    }

    if (destroyAfterUse && dismissMode != DismissMode.OnExit) {
      if (dismissMode == DismissMode.Timed) Destroy(gameObject, displayDuration);
      else Destroy(gameObject);
    }
  }

  private void OnTriggerExit(Collider other) {
    if (!_activated || dismissMode != DismissMode.OnExit || !other.CompareTag("Player")) return;

    if (DialogueHUD.Instance != null) Clear();
    if (destroyAfterUse) Destroy(gameObject);
  }

  private void ApplyComponentActions() {
    if (player == null) return;

    foreach (ComponentAction entry in componentActions) {
      if (string.IsNullOrEmpty(entry.componentName) || entry.action == ActionType.None) continue;
      SetComponentState(entry.componentName, entry.action);
    }
  }

  private void SetComponentState(string componentName, ActionType action) {
    Component component = player.GetComponent(componentName);
    if (component == null) {
      Debug.LogWarning($"[{LogTag}] '{name}': component '{componentName}' not found on player.", this);
      return;
    }

    bool enable = action == ActionType.Enable;

    if (component is Behaviour behaviour) { behaviour.enabled = enable; return; }
    if (component is Collider collider) { collider.enabled = enable; return; }
    if (component is Rigidbody rb) {
      rb.isKinematic = !enable;
      rb.constraints = enable ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
      return;
    }

    Debug.LogWarning($"[{LogTag}] '{name}': cannot toggle component type '{component.GetType().Name}'.", this);
  }
}
