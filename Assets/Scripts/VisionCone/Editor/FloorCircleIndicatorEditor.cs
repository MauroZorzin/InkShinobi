using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FloorCircleIndicator))]
[CanEditMultipleObjects]
public class FloorCircleIndicatorEditor : Editor {
  public override void OnInspectorGUI() {
    DrawDefaultInspector();

    GUILayout.Space(8);
    if (GUILayout.Button("Bake", GUILayout.Height(28))) {
      foreach (Object t in targets) {
        var indicator = (FloorCircleIndicator)t;
        indicator.Bake();
        EditorUtility.SetDirty(indicator);
      }
    }
  }
}
