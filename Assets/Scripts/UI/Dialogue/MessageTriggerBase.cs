using System.Collections;
using UnityEngine;

/// <summary>
/// Shared trigger-volume/one-shot/dismissal/component-toggle machinery for InformationTrigger and
/// DialogTrigger — the two differ only in which DialogueHUD slot (and therefore priority) they
/// write to, via the Show/Clear/LogTag overrides below.
///
/// Fires on player entry. By default (One Shot = true) it never refires; with One Shot = false it
/// re-arms on exit and fires again next entry. Dismissal has three modes: OnExit (clears when the
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
  [Tooltip("If true (default), this trigger fires at most once — re-entering afterward does nothing. If false, it re-arms when the player exits and fires again on the next entry, as long as its GameObject still exists (pair with Destroy After Use = false).")]
  [SerializeField] private bool oneShot = true;

  [Tooltip("Destroy this trigger's GameObject once its message has been dismissed (or, for Persistent, right after firing). Only makes sense combined with One Shot — otherwise the trigger gets destroyed before it can ever repeat.")]
  [SerializeField] private bool destroyAfterUse = true;

  [Tooltip("If true, the first time the player enters does nothing (just counts) — the trigger only actually fires from the second entry onward.")]
  [SerializeField] private bool displayOnSecondTrigger = false;

  [Tooltip("Seconds to wait after the trigger fires before actually applying component actions and showing the message. 0 = immediate.")]
  [SerializeField] private float delayBeforeDisplay = 0f;

  private bool _activated;
  private int _enterCount;

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

    if (displayOnSecondTrigger) {
      _enterCount++;
      if (_enterCount < 2) return;
    }

    _activated = true;

    if (delayBeforeDisplay > 0f) StartCoroutine(FireAfterDelay());
    else Fire();
  }

  private IEnumerator FireAfterDelay() {
    yield return new WaitForSeconds(delayBeforeDisplay);
    Fire();
  }

  private void Fire() {
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
    if (!other.CompareTag("Player")) return;

    if (_activated && dismissMode == DismissMode.OnExit) {
      if (DialogueHUD.Instance != null) Clear();
      if (destroyAfterUse) Destroy(gameObject);
    }

    if (!oneShot) _activated = false;
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
