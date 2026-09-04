using UnityEngine;

/// <summary>Avvia il percorso di un CreatureWaypointMover quando qualcosa entra in questo trigger collider.</summary>
[RequireComponent(typeof(Collider))]
public class CreatureMoveTrigger : MonoBehaviour {
  [Tooltip("The creature to start moving when this trigger fires.")]
  public CreatureWaypointMover target;

  [Tooltip("Only colliders on these layers fire this trigger.")]
  public LayerMask triggerLayerMask = ~0;

  [Tooltip("If true, this trigger only fires once and then ignores further entries.")]
  public bool oneShot = true;

  private bool _fired;

  private void Awake() {
    Collider col = GetComponent<Collider>();
    if (!col.isTrigger) {
      Debug.LogWarning("[CreatureMoveTrigger] Collider is not set to 'Is Trigger' — OnTriggerEnter will never fire.", this);
    }
  }

  private void OnTriggerEnter(Collider other) {
    if (oneShot && _fired) return;
    if (((1 << other.gameObject.layer) & triggerLayerMask.value) == 0) return;

    if (target == null) {
      Debug.LogWarning("[CreatureMoveTrigger] No target CreatureWaypointMover assigned.", this);
      return;
    }

    target.StartMoving();
    _fired = true;
  }
}
