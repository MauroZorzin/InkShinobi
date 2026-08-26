#pragma warning disable UDR0001 // One-shot editor authoring utility; it owns no runtime state.
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the reusable hiding wardrobe and cleanly authors the Palace loop hiding spots and paths.</summary>
public static class PalaceHidingMilestoneSetup {
  private const string PendingPath = "Temp/PalaceHidingMilestone.pending";
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";
  private const string SourceWardrobePath = "Assets/Geometry/JapanPalace/Prefabs/SM_wardrobe.prefab";
  private const string PrefabFolder = "Assets/Prefabs/Palace";
  private const string PrefabPath = PrefabFolder + "/HidingWardrobe.prefab";
  private const string WoodMaterialPath = "Assets/Geometry/JapanPalace/Materials/wood.mat";
  private const string WallMaterialPath = "Assets/Geometry/JapanPalace/Materials/wall.mat";
  private const string InkPrefabGuid = "c59e67dd51d4e524b8f0c53f527172fb";

  private struct HidingSpotSpec {
    public readonly string Name;
    public readonly Vector3 Position;
    public readonly float Yaw;

    public HidingSpotSpec(string name, Vector3 position, float yaw) {
      Name = name;
      Position = position;
      Yaw = yaw;
    }
  }

  private static readonly string[] GeneratedHidingSpotNames = {
    "NorthHidingWardrobe",
    "EastHidingWardrobe",
    "SouthHidingWardrobe",
    "WestHidingWardrobe"
  };

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Hiding Milestone")]
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
    EnsureFolder();
    GameObject hidingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    if (hidingPrefab == null) hidingPrefab = CreateHidingPrefab();

    Scene palace = SceneManager.GetSceneByPath(ScenePath);
    bool closeWhenDone = !palace.IsValid() || !palace.isLoaded;
    if (closeWhenDone) palace = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    EnsureLoopHidingSpots(palace, hidingPrefab);
    DoubleLoopLinePathWallOffset(palace);

    EditorSceneManager.MarkSceneDirty(palace);
    EditorSceneManager.SaveScene(palace);
    AssetDatabase.SaveAssets();
    if (File.Exists(PendingPath)) File.Delete(PendingPath);
    Debug.Log("Palace wardrobes corrected and every LinePath moved away from its supporting wall to a 0.2 offset.");
    if (closeWhenDone) EditorSceneManager.CloseScene(palace, true);
  }

  private static void EnsureLoopHidingSpots(Scene palace, GameObject hidingPrefab) {
    Transform template = FindTransform(palace, "LoopHidingWardrobe");
    if (template == null || template.GetComponent<WardrobeHidingSpot>() == null)
      throw new System.InvalidOperationException(
        "The correctly tuned LoopHidingWardrobe must remain in the Loop and is used as the cloning template.");
    Transform templateParent = template.parent;
    Transform loop = templateParent != null && templateParent.name == "HidingSpots"
      ? templateParent.parent
      : templateParent;
    if (loop == null)
      throw new System.InvalidOperationException("The Palace Loop hierarchy could not be found.");
    if (template.parent != loop) Undo.SetTransformParent(template, loop, "Restore original Palace hiding spot hierarchy");

    RemoveIncorrectGeneratedSpots(loop);

    Transform northLight = FindTransform(palace, "NorthLight");
    Transform eastLight = FindTransform(palace, "EastLight");
    Transform southLight = FindTransform(palace, "SouthLight");
    Transform westLight = FindTransform(palace, "WestLight");
    if (northLight == null || eastLight == null || southLight == null || westLight == null)
      throw new System.InvalidOperationException("One or more Palace loop fixed lights could not be found.");

    float wallOffset = Mathf.Abs(template.position.z - southLight.position.z);
    float templateYaw = template.eulerAngles.y;
    HidingSpotSpec[] copies = {
      new("NorthHidingWardrobe",
        new Vector3(northLight.position.x, template.position.y, northLight.position.z + wallOffset),
        templateYaw + 180f),
      new("EastHidingWardrobe",
        new Vector3(eastLight.position.x + wallOffset, template.position.y, eastLight.position.z),
        templateYaw - 90f),
      new("WestHidingWardrobe",
        new Vector3(westLight.position.x - wallOffset, template.position.y, westLight.position.z),
        templateYaw + 90f)
    };

    for (int i = 0; i < copies.Length; i++) {
      HidingSpotSpec spec = copies[i];
      GameObject instance = Object.Instantiate(template.gameObject, loop);
      instance.name = spec.Name;
      instance.transform.SetPositionAndRotation(spec.Position, Quaternion.Euler(0f, spec.Yaw, 0f));
      Undo.RegisterCreatedObjectUndo(instance, "Create Palace loop hiding spot");
      EditorUtility.SetDirty(instance);
      EditorUtility.SetDirty(instance.transform);
    }
  }

  private static void RemoveIncorrectGeneratedSpots(Transform loop) {
    for (int i = 0; i < GeneratedHidingSpotNames.Length; i++) {
      Transform generated = FindTransformRecursive(loop, GeneratedHidingSpotNames[i]);
      if (generated != null) Undo.DestroyObjectImmediate(generated.gameObject);
    }

    Transform obsoleteContainer = loop.Find("HidingSpots");
    if (obsoleteContainer != null && obsoleteContainer.childCount == 0)
      Undo.DestroyObjectImmediate(obsoleteContainer.gameObject);
  }

  private static void DoubleLoopLinePathWallOffset(Scene palace) {
    List<Collider> wallColliders = CollectWallColliders(palace);
    if (wallColliders.Count == 0)
      throw new System.InvalidOperationException("No Wall-layer colliders were found in the Palace scene.");

    SetPathPointsAwayFromWalls(palace, "OuterRoutePath", new[] {
      new Vector3(0f, 0f, 0.85f),
      new Vector3(7.15f, 0f, 0.85f),
      new Vector3(7.15f, 0f, 12.85f),
      new Vector3(18.85f, 0f, 12.85f),
      new Vector3(18.85f, 0f, 2.85f),
      new Vector3(30.85f, 0f, 2.85f),
      new Vector3(30.85f, 0f, -13.85f),
      new Vector3(14.15f, 0f, -13.85f),
      new Vector3(14.15f, 0f, 2.85f),
      new Vector3(17.15f, 0f, 2.85f),
      new Vector3(17.15f, 0f, 11.15f),
      new Vector3(8.85f, 0f, 11.15f),
      new Vector3(8.85f, 0f, -0.85f),
      new Vector3(0f, 0f, -0.85f)
    }, false, wallColliders);

    SetPathPointsAwayFromWalls(palace, "NearUpperBlockPath",
      RectanglePoints(15.9f, 21.6f, 1.1f, -4.6f), true, wallColliders);
    SetPathPointsAwayFromWalls(palace, "FarUpperBlockPath",
      RectanglePoints(23.4f, 29.1f, 1.1f, -4.6f), true, wallColliders);
    SetPathPointsAwayFromWalls(palace, "NearLowerBlockPath",
      RectanglePoints(15.9f, 21.6f, -6.4f, -12.1f), true, wallColliders);
    SetPathPointsAwayFromWalls(palace, "FarLowerBlockPath",
      RectanglePoints(23.4f, 29.1f, -6.4f, -12.1f), true, wallColliders);
  }

  private static List<Collider> CollectWallColliders(Scene palace) {
    int wallLayer = LayerMask.NameToLayer("Wall");
    List<Collider> colliders = new();
    foreach (GameObject root in palace.GetRootGameObjects()) {
      foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
        if (collider.enabled && collider.gameObject.layer == wallLayer) colliders.Add(collider);
      }
    }
    return colliders;
  }

  private static void SetPathPointsAwayFromWalls(
    Scene palace,
    string pathName,
    Vector3[] originalPositions,
    bool closed,
    List<Collider> wallColliders) {
    Vector3[] corrected = new Vector3[originalPositions.Length];
    for (int i = 0; i < originalPositions.Length; i++) {
      bool moveX = SegmentUsesZAxis(originalPositions, i, i - 1, closed) ||
                   SegmentUsesZAxis(originalPositions, i, i + 1, closed);
      bool moveZ = SegmentUsesXAxis(originalPositions, i, i - 1, closed) ||
                   SegmentUsesXAxis(originalPositions, i, i + 1, closed);
      corrected[i] = OffsetPointAwayFromWall(
        pathName, i, originalPositions[i], moveX, moveZ, wallColliders);
    }
    SetPathPoints(palace, pathName, corrected);
  }

  private static bool SegmentUsesXAxis(Vector3[] points, int from, int to, bool closed) {
    if (!TryResolvePoint(points, to, closed, out Vector3 other)) return false;
    Vector3 delta = other - points[from];
    return Mathf.Abs(delta.x) > Mathf.Abs(delta.z) && Mathf.Abs(delta.x) > 0.001f;
  }

  private static bool SegmentUsesZAxis(Vector3[] points, int from, int to, bool closed) {
    if (!TryResolvePoint(points, to, closed, out Vector3 other)) return false;
    Vector3 delta = other - points[from];
    return Mathf.Abs(delta.z) > Mathf.Abs(delta.x) && Mathf.Abs(delta.z) > 0.001f;
  }

  private static bool TryResolvePoint(Vector3[] points, int index, bool closed, out Vector3 point) {
    if (closed) index = (index + points.Length) % points.Length;
    if (index < 0 || index >= points.Length) {
      point = default;
      return false;
    }
    point = points[index];
    return true;
  }

  private static Vector3 OffsetPointAwayFromWall(
    string pathName,
    int pointIndex,
    Vector3 original,
    bool moveX,
    bool moveZ,
    List<Collider> wallColliders) {
    const float AddedClearance = 0.1f;
    const float MaximumSupportingWallDistance = 0.3f;
    float bestXDistance = float.PositiveInfinity;
    float bestZDistance = float.PositiveInfinity;
    float xDirection = 0f;
    float zDirection = 0f;

    for (int i = 0; i < wallColliders.Count; i++) {
      Collider wall = wallColliders[i];
      Vector3 sample = new(original.x, wall.bounds.center.y, original.z);
      Vector3 closest = wall.ClosestPoint(sample);
      Vector2 difference = new(sample.x - closest.x, sample.z - closest.z);
      float distance = difference.magnitude;
      if (distance < 0.001f || distance > MaximumSupportingWallDistance) continue;

      if (moveX && Mathf.Abs(difference.x) > 0.02f && Mathf.Abs(difference.x) < bestXDistance) {
        bestXDistance = Mathf.Abs(difference.x);
        xDirection = Mathf.Sign(difference.x);
      }
      if (moveZ && Mathf.Abs(difference.y) > 0.02f && Mathf.Abs(difference.y) < bestZDistance) {
        bestZDistance = Mathf.Abs(difference.y);
        zDirection = Mathf.Sign(difference.y);
      }
    }

    if ((moveX && xDirection == 0f) || (moveZ && zDirection == 0f))
      throw new System.InvalidOperationException(
        $"Could not determine every supporting-wall direction for {pathName}/Point{pointIndex + 1:00}.");

    return new Vector3(
      original.x + xDirection * AddedClearance,
      original.y,
      original.z + zDirection * AddedClearance);
  }

  private static Vector3[] RectanglePoints(float minX, float maxX, float maxZ, float minZ) => new[] {
    new Vector3(minX, 0f, maxZ),
    new Vector3(maxX, 0f, maxZ),
    new Vector3(maxX, 0f, minZ),
    new Vector3(minX, 0f, minZ)
  };

  private static void SetPathPoints(Scene palace, string pathName, Vector3[] positions) {
    Transform pathTransform = FindTransform(palace, pathName);
    if (pathTransform == null)
      throw new System.InvalidOperationException($"Palace LinePath '{pathName}' could not be found.");
    if (pathTransform.childCount != positions.Length)
      throw new System.InvalidOperationException(
        $"Palace LinePath '{pathName}' has {pathTransform.childCount} points; expected {positions.Length}.");

    Undo.RecordObjects(GetChildTransforms(pathTransform), "Double Palace LinePath wall offset");
    for (int i = 0; i < positions.Length; i++) {
      pathTransform.GetChild(i).localPosition = positions[i];
      EditorUtility.SetDirty(pathTransform.GetChild(i));
    }

    LinePath path = pathTransform.GetComponent<LinePath>();
    if (path != null) {
      path.Rebuild();
      EditorUtility.SetDirty(path);
    }
  }

  private static Object[] GetChildTransforms(Transform parent) {
    Object[] children = new Object[parent.childCount];
    for (int i = 0; i < parent.childCount; i++) children[i] = parent.GetChild(i);
    return children;
  }

  private static GameObject CreateHidingPrefab() {
    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceWardrobePath);
    if (source == null)
      throw new System.InvalidOperationException("Hiding wardrobe source asset is missing.");

    GameObject root = new("HidingWardrobe");
    root.layer = 13; // HideSpot
    GameObject model = Object.Instantiate(source);
    model.name = "WardrobeModel";
    model.transform.SetParent(root.transform, false);
    // The JapanPalace FBX is authored in centimeters. Scene 3 scales both wardrobe variants by
    // 100, so preserve that established world scale in the reusable hiding prefab as well.
    model.transform.localScale = Vector3.one * 100f;

    Renderer modelRenderer = model.GetComponentInChildren<Renderer>(true);
    MeshFilter modelFilter = model.GetComponentInChildren<MeshFilter>(true);
    if (modelRenderer == null || modelFilter == null)
      throw new System.InvalidOperationException("The wardrobe prefab has no renderable mesh.");

    Material[] wardrobeMaterials = modelRenderer.sharedMaterials;
    if (wardrobeMaterials.Length > 0)
      wardrobeMaterials[0] = AssetDatabase.LoadAssetAtPath<Material>(WoodMaterialPath);
    if (wardrobeMaterials.Length > 1)
      wardrobeMaterials[1] = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
    modelRenderer.sharedMaterials = wardrobeMaterials;
    model.AddComponent<SelectiveColor>();

    Bounds bounds = modelRenderer.bounds;
    Vector3 localCenter = root.transform.InverseTransformPoint(bounds.center);

    BoxCollider interaction = root.AddComponent<BoxCollider>();
    interaction.isTrigger = true;
    interaction.center = localCenter;
    interaction.size = new Vector3(
      Mathf.Max(1.2f, bounds.size.x + 1.1f),
      Mathf.Max(1f, bounds.size.y + 0.15f),
      Mathf.Max(1.2f, bounds.size.z + 1.1f));

    Transform hidePoint = CreateAnchor(root.transform, "HidePoint", localCenter);
    Transform exitPoint = CreateAnchor(
      root.transform,
      "ExitPoint",
      new Vector3(localCenter.x, 0f, localCenter.z - bounds.extents.z - 0.4f));
    Transform effectPoint = CreateAnchor(root.transform, "InkEffectPoint", localCenter);

    WardrobeHidingSpot hidingSpot = root.AddComponent<WardrobeHidingSpot>();
    string inkPath = AssetDatabase.GUIDToAssetPath(InkPrefabGuid);
    hidingSpot.Configure(
      hidePoint,
      exitPoint,
      effectPoint,
      AssetDatabase.LoadAssetAtPath<GameObject>(inkPath));

    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    Object.DestroyImmediate(root);
    return prefab;
  }

  private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition) {
    GameObject anchor = new(name);
    anchor.transform.SetParent(parent, false);
    anchor.transform.localPosition = localPosition;
    return anchor.transform;
  }

  private static void EnsureFolder() {
    if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Palace");
  }

  private static T FindInScene<T>(Scene scene) where T : Component {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      T found = root.GetComponentInChildren<T>(true);
      if (found != null) return found;
    }
    return null;
  }

  private static Transform FindTransform(Scene scene, string name) {
    foreach (GameObject root in scene.GetRootGameObjects()) {
      Transform found = FindTransformRecursive(root.transform, name);
      if (found != null) return found;
    }
    return null;
  }

  private static Transform FindTransformRecursive(Transform current, string name) {
    if (current.name == name) return current;
    for (int i = 0; i < current.childCount; i++) {
      Transform found = FindTransformRecursive(current.GetChild(i), name);
      if (found != null) return found;
    }
    return null;
  }
}
