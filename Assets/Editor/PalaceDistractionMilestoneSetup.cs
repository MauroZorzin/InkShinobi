#pragma warning disable UDR0001 // One-shot editor authoring utility; it owns no runtime state.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the reusable distraction assets and enables them only in the Palace scene.</summary>
public static class PalaceDistractionMilestoneSetup {
  private const string PendingPath = "Temp/PalaceDistractionMilestone.pending";
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";
  private const string PlayerPrefabPath = "Assets/Prefabs/PlayerV3.prefab";
  private const string SourceRockPath = "Assets/Rock_Small_01.prefab";
  private const string RockTexturePath = "Assets/Rock_Small_01_Albedo.png";
  private const string Folder = "Assets/Prefabs/Palace/Distraction";
  private const string EchoPrefabPath = Folder + "/DistractionEchoPulse.prefab";
  private const string RockPrefabPath = Folder + "/DistractionRock.prefab";
  private const string PickupPrefabPath = Folder + "/DistractionRockPickup.prefab";
  private const string ItemPath = Folder + "/DistractionRock.asset";
  private const string PreviewMaterialPath = "Assets/Art/Materials/Palace/PalaceWallSwitchInk.mat";
  private const string CircleMaterialPath = "Assets/Scripts/VisionCone/Hidden_FloorCircleIndicator.mat";

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Distraction Milestone")]
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
    EnsureFolders();
    DistractionEchoPulse echo = CreateEchoPrefab();
    ThrownDistraction rock = CreateRockPrefab(echo);
    ItemDefinition item = CreateItem(rock.gameObject);
    CreatePickupPrefab(item);
    ConfigurePlayerPrefab(rock, item);
    AssetDatabase.ImportAsset("Assets/Settings/PlayerInputSystem.inputactions", ImportAssetOptions.ForceUpdate);
    EnablePalacePlayer();
    AssetDatabase.SaveAssets();
    if (File.Exists(PendingPath)) File.Delete(PendingPath);
    Debug.Log("Palace distraction ability, reusable rock, pickup source, and EchoPulse authored successfully.");
  }

  private static DistractionEchoPulse CreateEchoPrefab() {
    GameObject root = new("DistractionEchoPulse");
    root.AddComponent<MeshFilter>();
    root.AddComponent<MeshRenderer>();
    FloorCircleIndicator indicator = root.AddComponent<FloorCircleIndicator>();
    indicator.radius = 0.1f;
    indicator.floorMask = 1 << LayerMask.NameToLayer("Floor");
    indicator.castUpOffset = 0.5f;
    indicator.maxDropDistance = 1.5f;
    indicator.heightOffset = 0.025f;
    indicator.fillColor = new Color(1f, 1f, 1f, 0.06f);
    indicator.ringColor = new Color(1f, 1f, 1f, 0.88f);
    indicator.ringStart = 0.92f;
    indicator.softness = 0.035f;
    indicator.material = AssetDatabase.LoadAssetAtPath<Material>(CircleMaterialPath);
    DistractionEchoPulse pulse = root.AddComponent<DistractionEchoPulse>();
    pulse.Configure(0.85f);
    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, EchoPrefabPath);
    Object.DestroyImmediate(root);
    return saved.GetComponent<DistractionEchoPulse>();
  }

  private static ThrownDistraction CreateRockPrefab(DistractionEchoPulse echo) {
    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRockPath);
    if (source == null) throw new FileNotFoundException($"Missing rock source at {SourceRockPath}.");
    GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
    root.name = "DistractionRock";
    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    root.transform.localScale = Vector3.one * 0.04f;
    root.layer = LayerMask.NameToLayer("Item");

    Rigidbody body = root.GetComponent<Rigidbody>();
    if (body == null) body = root.AddComponent<Rigidbody>();
    body.mass = 0.18f;
    body.useGravity = true;
    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    body.interpolation = RigidbodyInterpolation.Interpolate;

    SphereCollider physical = root.GetComponent<SphereCollider>();
    if (physical == null) physical = root.AddComponent<SphereCollider>();
    physical.isTrigger = false;
    physical.radius = 1f;

    if (root.GetComponent<SelectiveColor>() == null) root.AddComponent<SelectiveColor>();
    GameObject signalObject = new("SoundSignal");
    signalObject.transform.SetParent(root.transform, false);
    SphereCollider signalCollider = signalObject.AddComponent<SphereCollider>();
    signalCollider.isTrigger = true;
    signalCollider.radius = 0.1f;
    GuardSoundSignal signal = signalObject.AddComponent<GuardSoundSignal>();
    signal.Configure(8f, 1 << LayerMask.NameToLayer("Enemy"));

    ThrownDistraction thrown = root.GetComponent<ThrownDistraction>();
    if (thrown == null) thrown = root.AddComponent<ThrownDistraction>();
    thrown.Configure(1 << LayerMask.NameToLayer("Floor"), 0.04f, signal, echo, true);
    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, RockPrefabPath);
    Object.DestroyImmediate(root);
    return saved.GetComponent<ThrownDistraction>();
  }

  private static ItemDefinition CreateItem(GameObject rockPrefab) {
    ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemPath);
    if (item == null) {
      item = ScriptableObject.CreateInstance<ItemDefinition>();
      AssetDatabase.CreateAsset(item, ItemPath);
    }
    item.itemId = "distraction_rock";
    item.displayName = "Distraction Rock";
    item.icon = AssetDatabase.LoadAssetAtPath<Texture>(RockTexturePath);
    item.worldPrefab = rockPrefab;
    EditorUtility.SetDirty(item);
    return item;
  }

  private static void CreatePickupPrefab(ItemDefinition item) {
    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRockPath);
    GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
    root.name = "DistractionRockPickup";
    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    root.transform.localScale = Vector3.one * 0.055f;
    root.layer = LayerMask.NameToLayer("PickUp");
    SphereCollider trigger = root.GetComponent<SphereCollider>();
    if (trigger == null) trigger = root.AddComponent<SphereCollider>();
    trigger.isTrigger = true;
    trigger.radius = 1.35f;
    WorldItem worldItem = root.GetComponent<WorldItem>();
    if (worldItem == null) worldItem = root.AddComponent<WorldItem>();
    worldItem.item = item;
    worldItem.destroyOnPickup = true;
    if (root.GetComponent<SelectiveColor>() == null) root.AddComponent<SelectiveColor>();
    PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
    Object.DestroyImmediate(root);
  }

  private static void ConfigurePlayerPrefab(ThrownDistraction rock, ItemDefinition item) {
    GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
    try {
      DistractionTrajectoryPreview preview = root.GetComponent<DistractionTrajectoryPreview>();
      if (preview == null) preview = root.AddComponent<DistractionTrajectoryPreview>();
      preview.Configure(AssetDatabase.LoadAssetAtPath<Material>(PreviewMaterialPath));
      preview.enabled = false;

      Transform throwAnchor = root.transform.Find("DistractionThrowAnchor");
      if (throwAnchor == null) {
        GameObject anchorObject = new("DistractionThrowAnchor");
        throwAnchor = anchorObject.transform;
        throwAnchor.SetParent(root.transform, false);
        throwAnchor.localPosition = new Vector3(0f, 0.12f, 0.08f);
      }

      PalaceDistractionController controller = root.GetComponent<PalaceDistractionController>();
      if (controller == null) controller = root.AddComponent<PalaceDistractionController>();
      controller.Configure(rock, item, preview, throwAnchor);
      controller.enabled = false;
      EditorUtility.SetDirty(preview);
      EditorUtility.SetDirty(controller);
      PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
    }
    finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void EnablePalacePlayer() {
    Scene scene = SceneManager.GetSceneByPath(ScenePath);
    bool closeWhenDone = !scene.IsValid() || !scene.isLoaded;
    if (closeWhenDone) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
    PalaceDistractionController controller = FindComponent<PalaceDistractionController>(scene);
    if (controller == null)
      throw new System.InvalidOperationException("The Palace PlayerV3 instance has no distraction controller after prefab authoring.");
    DistractionTrajectoryPreview preview = controller.GetComponent<DistractionTrajectoryPreview>();
    controller.enabled = true;
    if (preview != null) preview.enabled = true;
    EditorUtility.SetDirty(controller);
    if (preview != null) EditorUtility.SetDirty(preview);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    if (closeWhenDone) EditorSceneManager.CloseScene(scene, true);
  }

  private static T FindComponent<T>(Scene scene) where T : Component {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      T found = root.GetComponentInChildren<T>(true);
      if (found != null) return found;
    }
    return null;
  }

  private static void EnsureFolders() {
    if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Palace"))
      AssetDatabase.CreateFolder("Assets/Prefabs", "Palace");
    if (!AssetDatabase.IsValidFolder(Folder))
      AssetDatabase.CreateFolder("Assets/Prefabs/Palace", "Distraction");
  }
}
