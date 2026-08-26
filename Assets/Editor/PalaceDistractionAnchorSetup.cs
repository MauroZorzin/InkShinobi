#pragma warning disable UDR0001 // One-shot editor authoring utility; it owns no runtime state.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds and wires the authored movable release anchor and its related Palace tuning.</summary>
public static class PalaceDistractionAnchorSetup {
  private const string PendingPath = "Temp/PalaceDistractionAnchor.pending";
  private const string PlayerPrefabPath = "Assets/Prefabs/PlayerV3.prefab";
  private const string RockPrefabPath = "Assets/Prefabs/Palace/Distraction/DistractionRock.prefab";
  private const string ItemPath = "Assets/Prefabs/Palace/Distraction/DistractionRock.asset";
  private const string EchoPrefabPath = "Assets/Prefabs/Palace/Distraction/DistractionEchoPulse.prefab";
  private const string GuardPrefabPath = "Assets/Prefabs/GuardPatrol.prefab";
  private const string PalaceScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Distraction Throw Anchor")]
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
    GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
    try {
      Transform anchor = root.transform.Find("DistractionThrowAnchor");
      if (anchor == null) {
        anchor = new GameObject("DistractionThrowAnchor").transform;
        anchor.SetParent(root.transform, false);
        anchor.localPosition = new Vector3(0f, 0.12f, 0.08f);
      }

      PalaceDistractionController controller = root.GetComponent<PalaceDistractionController>();
      DistractionTrajectoryPreview preview = root.GetComponent<DistractionTrajectoryPreview>();
      ThrownDistraction rock = AssetDatabase.LoadAssetAtPath<ThrownDistraction>(RockPrefabPath);
      ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemPath);
      if (controller == null || preview == null || rock == null || item == null)
        throw new System.InvalidOperationException("Existing Palace distraction assets are incomplete.");

      controller.Configure(rock, item, preview, anchor);
      controller.IncludeTrajectoryObstructionLayer(LayerMask.NameToLayer("Ceiling"));
      EditorUtility.SetDirty(controller);
      PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
    }
    finally {
      PrefabUtility.UnloadPrefabContents(root);
    }

    RemoveGuardIdleSound();
    NeutralizeEchoPulse();
    ConfigureCeilingObstruction();
    AssetDatabase.SaveAssets();
    if (File.Exists(PendingPath)) File.Delete(PendingPath);
    Debug.Log("Palace throw anchor, neutral distraction ripple, guard idle audio, and ceiling obstruction updated successfully.");
  }

  private static void RemoveGuardIdleSound() {
    GameObject root = PrefabUtility.LoadPrefabContents(GuardPrefabPath);
    try {
      GuardController guard = root.GetComponent<GuardController>();
      if (guard != null) {
        guard.idleSounds = System.Array.Empty<AudioClip>();
        EditorUtility.SetDirty(guard);
      }
      PrefabUtility.SaveAsPrefabAsset(root, GuardPrefabPath);
    }
    finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void NeutralizeEchoPulse() {
    GameObject root = PrefabUtility.LoadPrefabContents(EchoPrefabPath);
    try {
      FloorCircleIndicator indicator = root.GetComponent<FloorCircleIndicator>();
      if (indicator != null) {
        indicator.fillColor = new Color(1f, 1f, 1f, 0.06f);
        indicator.ringColor = new Color(1f, 1f, 1f, 0.88f);
        EditorUtility.SetDirty(indicator);
      }
      SelectiveColor selectiveColor = root.GetComponent<SelectiveColor>();
      if (selectiveColor != null) Object.DestroyImmediate(selectiveColor);
      PrefabUtility.SaveAsPrefabAsset(root, EchoPrefabPath);
    }
    finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void ConfigureCeilingObstruction() {
    Scene scene = SceneManager.GetSceneByPath(PalaceScenePath);
    bool closeWhenDone = !scene.IsValid() || !scene.isLoaded;
    if (closeWhenDone) scene = EditorSceneManager.OpenScene(PalaceScenePath, OpenSceneMode.Additive);
    GameObject grayBox = null;
    foreach (GameObject sceneRoot in scene.GetRootGameObjects())
      if (sceneRoot.name == "GrayBox") { grayBox = sceneRoot; break; }
    Transform ceiling = grayBox != null ? grayBox.transform.Find("Ceiling") : null;
    if (ceiling == null)
      throw new System.InvalidOperationException("GrayBox/Ceiling was not found in the Palace scene.");

    ceiling.gameObject.layer = LayerMask.NameToLayer("Ceiling");
    if (ceiling.GetComponent<Collider>() == null) ceiling.gameObject.AddComponent<BoxCollider>();
    EditorUtility.SetDirty(ceiling.gameObject);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    if (closeWhenDone) EditorSceneManager.CloseScene(scene, true);
  }
}
