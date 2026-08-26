#pragma warning disable UDR0001 // One-shot editor authoring utility; it owns no runtime state.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the Palace loop patrol with two evenly spaced intermediate points on each edge.</summary>
public static class PalacePatrolRouteSubdivisionSetup {
  private const string PendingPath = "Temp/PalacePatrolRouteSubdivision.pending";
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";

  private static readonly Vector3[] RoutePositions = {
    new(15f, 0f, 2f), new(20f, 0f, 2f), new(25f, 0f, 2f),
    new(30f, 0f, 2f), new(30f, 0f, -3f), new(30f, 0f, -8f),
    new(30f, 0f, -13f), new(25f, 0f, -13f), new(20f, 0f, -13f),
    new(15f, 0f, -13f), new(15f, 0f, -8f), new(15f, 0f, -3f)
  };

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Subdivide Guard Patrol Route")]
  public static void RunFromMenu() => Apply();

  private static void RunPendingSetup() {
    if (!File.Exists(PendingPath)) return;
    if (EditorApplication.isPlayingOrWillChangePlaymode) {
      EditorApplication.delayCall += RunPendingSetup;
      return;
    }
    Apply();
  }

  private static void Apply() {
    Scene scene = SceneManager.GetSceneByPath(ScenePath);
    bool closeWhenDone = !scene.IsValid() || !scene.isLoaded;
    if (closeWhenDone) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    Transform route = FindTransform(scene, "SquarePatrolRoute");
    GuardSquarePatrol patrol = FindComponent<GuardSquarePatrol>(scene);
    if (route == null || patrol == null)
      throw new System.InvalidOperationException("The Palace guard or SquarePatrolRoute could not be found.");

    for (int i = route.childCount - 1; i >= 0; i--)
      Undo.DestroyObjectImmediate(route.GetChild(i).gameObject);

    Transform[] points = new Transform[RoutePositions.Length];
    for (int i = 0; i < RoutePositions.Length; i++) {
      GameObject point = new($"Point{i + 1:00}");
      Undo.RegisterCreatedObjectUndo(point, "Subdivide Palace guard patrol");
      point.transform.SetParent(route, false);
      point.transform.localPosition = RoutePositions[i];
      points[i] = point.transform;
    }

    Undo.RecordObject(patrol, "Subdivide Palace guard patrol");
    patrol.Configure(points);
    EditorUtility.SetDirty(patrol);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    AssetDatabase.SaveAssets();
    if (File.Exists(PendingPath)) File.Delete(PendingPath);
    Debug.Log("Palace guard patrol subdivided into 12 evenly spaced points.");
    if (closeWhenDone) EditorSceneManager.CloseScene(scene, true);
  }

  private static T FindComponent<T>(Scene scene) where T : Component {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      T found = root.GetComponentInChildren<T>(true);
      if (found != null) return found;
    }
    return null;
  }

  private static Transform FindTransform(Scene scene, string name) {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      Transform found = FindRecursive(root.transform, name);
      if (found != null) return found;
    }
    return null;
  }

  private static Transform FindRecursive(Transform current, string name) {
    if (current.name == name) return current;
    for (int i = 0; i < current.childCount; i++) {
      Transform found = FindRecursive(current.GetChild(i), name);
      if (found != null) return found;
    }
    return null;
  }
}
