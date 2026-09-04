using UnityEngine;

/// <summary>Override opzionale per tratto di closed-loop e colore del gizmo su un gruppo di LinePath.</summary>
public class LineStrandMarker : MonoBehaviour {
  [Tooltip("Overrides LinePath.closedLoop for this strand only.")]
  public bool closedLoop = false;

  [Tooltip("If true, gizmoColor below overrides LinePath.gizmoColor for this strand only.")]
  public bool overrideGizmoColor = false;
  public Color gizmoColor = Color.yellow;
}
