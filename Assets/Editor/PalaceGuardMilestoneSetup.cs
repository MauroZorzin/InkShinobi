#pragma warning disable UDR0001 // One-shot editor authoring utility; it owns no runtime state.
using System.IO;
using TMPro;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>One-shot authoring for Palace Milestones 5 and 6. No runtime synchronization.</summary>
public static class PalaceGuardMilestoneSetup {
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";
  private const string GuardPrefabPath = "Assets/Prefabs/GuardPatrol.prefab";
  private const string PendingPath = "Temp/PalaceGuardMilestone56.pending";
  private const string FontPath = "Assets/Art/UI/Fonts/Kipish_Regular_SDF.asset";
  private const string ThreatProfilePath = "Assets/Scripts/Player/Stealth/PlayerDetectedVolumeProfile.asset";

  private static NavMeshSurface pendingSurface;
  private static Scene pendingScene;
  private static bool closeWhenFinished;

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Guard Milestones 5 and 6")]
  public static void RunFromMenu() => ApplySetup();

  private static void RunPendingSetup() {
    if (!File.Exists(PendingPath)) return;
    if (EditorApplication.isPlayingOrWillChangePlaymode) {
      EditorApplication.delayCall += RunPendingSetup;
      return;
    }
    ApplySetup();
  }

  private static void ApplySetup() {
    EnsureGuardPrefabMotor();

    Scene palace = SceneManager.GetSceneByPath(ScenePath);
    closeWhenFinished = !palace.IsValid() || !palace.isLoaded;
    if (closeWhenFinished) palace = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    GameObject navigation = FindRoot(palace, "PalaceNavigation") ?? new GameObject("PalaceNavigation");
    SceneManager.MoveGameObjectToScene(navigation, palace);
    NavMeshSurface surface = navigation.GetComponent<NavMeshSurface>();
    if (surface == null) surface = navigation.AddComponent<NavMeshSurface>();
    surface.collectObjects = CollectObjects.All;
    surface.layerMask = (1 << 8) | (1 << 11); // Wall collision and Floor.
    surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    surface.ignoreNavMeshAgent = true;
    surface.ignoreNavMeshObstacle = true;
    surface.overrideVoxelSize = true;
    surface.voxelSize = 0.08f;
    surface.minRegionArea = 0.25f;

    GuardController guard = FindInScene<GuardController>(palace);
    if (guard == null) throw new System.InvalidOperationException("Palace guard was not found.");
    NavMeshAgent agent = guard.GetComponent<NavMeshAgent>();
    if (agent == null) agent = guard.gameObject.AddComponent<NavMeshAgent>();
    agent.enabled = true;
    agent.updateRotation = false;
    GuardMotor motor = guard.GetComponent<GuardMotor>();
    if (motor == null) motor = guard.gameObject.AddComponent<GuardMotor>();
    motor.enabled = true;
    guard.enabled = true;

    PlayerStealthController player = FindInScene<PlayerStealthController>(palace);
    if (player == null) throw new System.InvalidOperationException("Palace player was not found.");
    PlayerDeathSequence death = player.GetComponent<PlayerDeathSequence>();
    if (death == null) death = player.gameObject.AddComponent<PlayerDeathSequence>();
    death.enabled = true;

    ConfigureStateIndicator(guard, palace);
    ConfigureThreatFeedback(player, palace);

    EditorSceneManager.MarkSceneDirty(palace);
    EditorSceneManager.SaveScene(palace);

    pendingSurface = surface;
    pendingScene = palace;
    NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });
    EditorApplication.update -= FinishWhenBaked;
    EditorApplication.update += FinishWhenBaked;
  }

  private static void FinishWhenBaked() {
    if (pendingSurface == null || NavMeshAssetManager.instance.IsSurfaceBaking(pendingSurface)) return;
    EditorApplication.update -= FinishWhenBaked;
    EditorSceneManager.MarkSceneDirty(pendingScene);
    EditorSceneManager.SaveScene(pendingScene);
    AssetDatabase.SaveAssets();
    if (File.Exists(PendingPath)) File.Delete(PendingPath);
    Debug.Log("Palace Guard Milestones 5 and 6 configured and NavMesh baked successfully.");
    if (closeWhenFinished && pendingScene.IsValid() && pendingScene.isLoaded)
      EditorSceneManager.CloseScene(pendingScene, true);
    pendingSurface = null;
  }

  private static void ConfigureStateIndicator(GuardController guard, Scene palace) {
    Transform existing = guard.transform.Find("StateIndicator");
    GameObject indicator = existing != null ? existing.gameObject : new GameObject("StateIndicator");
    if (existing == null) indicator.transform.SetParent(guard.transform, false);
    indicator.transform.localPosition = new Vector3(0f, 1.05f, 0f);

    TextMeshPro text = indicator.GetComponent<TextMeshPro>();
    if (text == null) text = indicator.AddComponent<TextMeshPro>();
    TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
    if (font != null) text.font = font;
    text.text = "";
    text.alignment = TextAlignmentOptions.Center;
    text.fontSize = 3.5f;
    text.fontStyle = FontStyles.SmallCaps;
    text.textWrappingMode = TextWrappingModes.NoWrap;
    text.sortingOrder = 50;
    RectTransform rect = text.rectTransform;
    rect.sizeDelta = new Vector2(1.2f, 1.2f);

    GuardStateIndicator presenter = indicator.GetComponent<GuardStateIndicator>();
    if (presenter == null) presenter = indicator.AddComponent<GuardStateIndicator>();
    presenter.Configure(guard, text, FindGameplayCamera(palace));
    if (indicator.GetComponent<SelectiveColor>() == null) indicator.AddComponent<SelectiveColor>();
  }

  private static void EnsureGuardPrefabMotor() {
    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(GuardPrefabPath);
    try {
      GuardController controller = prefabRoot.GetComponent<GuardController>();
      if (controller == null)
        throw new System.InvalidOperationException("GuardPatrol prefab has no GuardController.");
      if (prefabRoot.GetComponent<GuardMotor>() == null) prefabRoot.AddComponent<GuardMotor>();
      PrefabUtility.SaveAsPrefabAsset(prefabRoot, GuardPrefabPath);
    }
    finally {
      PrefabUtility.UnloadPrefabContents(prefabRoot);
    }
  }

  private static void ConfigureThreatFeedback(PlayerStealthController player, Scene palace) {
    GameObject root = FindRoot(palace, "GuardThreatFeedback") ?? new GameObject("GuardThreatFeedback");
    SceneManager.MoveGameObjectToScene(root, palace);
    Volume volume = root.GetComponent<Volume>();
    if (volume == null) volume = root.AddComponent<Volume>();
    volume.isGlobal = true;
    GuardThreatFeedback feedback = root.GetComponent<GuardThreatFeedback>();
    if (feedback == null) feedback = root.AddComponent<GuardThreatFeedback>();
    VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ThreatProfilePath);
    feedback.Configure(profile);
  }

  private static T FindInScene<T>(Scene scene) where T : Component {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      T found = root.GetComponentInChildren<T>(true);
      if (found != null) return found;
    }
    return null;
  }

  private static Camera FindGameplayCamera(Scene scene) {
    Camera fallback = null;
    foreach (GameObject root in scene.GetRootGameObjects()) {
      foreach (Camera camera in root.GetComponentsInChildren<Camera>(true)) {
        fallback ??= camera;
        if (camera.isActiveAndEnabled && camera.CompareTag("MainCamera")) return camera;
      }
    }
    return fallback;
  }

  private static GameObject FindRoot(Scene scene, string name) {
    foreach (GameObject root in scene.GetRootGameObjects())
      if (root.name == name) return root;
    return null;
  }
}
