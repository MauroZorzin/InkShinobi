using UnityEngine;

/// <summary>
/// Opt-in obstruction for objects such as closed doors. Ordinary walls and props do not block a
/// switch merely because they have colliders.
/// </summary>
[DisallowMultipleComponent]
public sealed class WallSwitchBlocker : MonoBehaviour {
  [Tooltip("Whether this object currently invalidates intersecting wall-switch trajectories.")]
  [SerializeField] private bool blocking = true;

  public bool IsBlocking => isActiveAndEnabled && blocking;

  public void SetBlocking(bool value) {
    blocking = value;
  }
}
