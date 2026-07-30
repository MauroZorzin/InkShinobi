using UnityEngine;

/// <summary>
/// Optional. Drop this on a strand-group child under a LinePath (see LinePath's Points header)
/// to override that one strand's closed-loop / gizmo color instead of inheriting LinePath's
/// defaults. Not required — a strand without this marker just uses LinePath's settings.
/// </summary>
public class LineStrandMarker : MonoBehaviour {
  [Tooltip("Overrides LinePath.closedLoop for this strand only.")]
  public bool closedLoop = false;

  [Tooltip("If true, gizmoColor below overrides LinePath.gizmoColor for this strand only.")]
  public bool overrideGizmoColor = false;
  public Color gizmoColor = Color.yellow;
}
