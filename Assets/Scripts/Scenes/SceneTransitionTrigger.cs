using UnityEngine;

/// <summary>
/// Starts a scene transition (via SceneTransitionManager) when something enters this trigger
/// collider — or call TriggerTransition() directly (e.g. wired to a UI Button's OnClick). The
/// destination scene is picked from the Editor, so no code is needed per use.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SceneTransitionTrigger : MonoBehaviour {
  [Tooltip("Name of the scene to load (must be added to Build Settings).")]
  public string sceneName;

  [Tooltip("Only colliders on these layers fire this trigger.")]
  public LayerMask triggerLayerMask = ~0;

  [Tooltip("If true, this trigger only fires once and then ignores further entries.")]
  public bool oneShot = true;

  [Tooltip("Load immediately without fading to or from black.")]
  public bool skipFade;

  private bool _fired;

  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (!col.isTrigger) {
      Debug.LogWarning("[SceneTransitionTrigger] Collider is not set to 'Is Trigger' — OnTriggerEnter will never fire.", this);
    }
  }

  private void OnTriggerEnter(Collider other) {
    if (oneShot && _fired) return;
    if (((1 << other.gameObject.layer) & triggerLayerMask.value) == 0) return;

    TriggerTransition();
  }

  /// <summary>Starts the transition to sceneName. Also callable directly, e.g. from a UI Button's OnClick.</summary>
  public void TriggerTransition() {
    if (string.IsNullOrEmpty(sceneName)) {
      Debug.LogWarning("[SceneTransitionTrigger] No sceneName set.", this);
      return;
    }

    _fired = true;
    SceneTransitionManager.LoadScene(sceneName, useFade: !skipFade);
  }
}
