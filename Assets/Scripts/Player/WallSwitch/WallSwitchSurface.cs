using UnityEngine;

/// <summary>
/// Marks a collider, or a hierarchy of colliders, as geometry that participates in
/// wall-switch trajectory evaluation.
/// Its position relative to the selected destination LinePath determines its role automatically:
/// geometry behind the path receives the ink stain, while geometry in front blocks the switch.
/// </summary>
[DisallowMultipleComponent]
public sealed class WallSwitchSurface : MonoBehaviour {
  [Tooltip("Only descendant colliders on these layers participate in wall-switch trajectory evaluation.")]
  [SerializeField] private LayerMask includedLayers = ~0;

  public bool Includes(Collider candidate) {
    return candidate != null &&
           candidate.enabled &&
           !candidate.isTrigger &&
           (includedLayers.value & (1 << candidate.gameObject.layer)) != 0;
  }

  public static bool TryFind(Collider candidate, out WallSwitchSurface surface) {
    surface = candidate != null ? candidate.GetComponentInParent<WallSwitchSurface>() : null;
    return surface != null && surface.isActiveAndEnabled && surface.Includes(candidate);
  }
}
