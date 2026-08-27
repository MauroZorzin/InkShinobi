#if UNITY_EDITOR
#pragma warning disable UDR0001 // Editor-only InitializeOnLoad method; no runtime initializer is required.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot authoring utility for the right-turn corridor and four-corner door paths.
/// It creates ordinary scene objects which remain fully editable in the Inspector.
/// </summary>
public static class PalaceSecondSectionSceneSetup {
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";
  private const string PendingPath = "Temp/PalaceSecondSectionFirstCorridor.pending";
  private const string WallPrefabPath = "Assets/Geometry/JapanPalace/Prefabs/SM_wall.prefab";
  private const string DoorPrefabPath = "Assets/Prefabs/Doors/SlidingDoor.prefab";

  private const float DoorX = 9f;
  private const float DoorHalfWidth = 1f;
  private const float CorridorOuterX = 11f;
  private const float CorridorEndZ = -13f;
  private const float WallHeight = 1.5f;
  private const float WallThickness = 0.1f;

  private const StaticEditorFlags EnvironmentStaticFlags =
    StaticEditorFlags.ContributeGI |
    StaticEditorFlags.OccluderStatic |
    StaticEditorFlags.OccludeeStatic |
    StaticEditorFlags.BatchingStatic |
    StaticEditorFlags.ReflectionProbeStatic;

  [InitializeOnLoadMethod]
  private static void QueuePendingSetup() {
    if (File.Exists(PendingPath)) EditorApplication.delayCall += RunPendingSetup;
  }

  private static void RunPendingSetup() {
    if (!File.Exists(PendingPath)) return;

    try {
      BuildFirstCorridor();
      File.Delete(PendingPath);
      Debug.Log("Palace SecondSection/FirstCorridor added successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Build Second Section First Corridor")]
  public static void BuildFirstCorridor() {
    EnsureDoorPrefabLinePaths();

    Scene scene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !scene.IsValid() || !scene.isLoaded;
    if (openedTemporarily) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject grayBox = FindRoot(scene, "GrayBox");
      if (grayBox == null) throw new System.InvalidOperationException("GrayBox was not found.");

      Transform section = FindOrCreateContainer("SecondSection", grayBox.transform);
      RemoveExistingDoorTopology(grayBox.transform, section);

      Transform oldCorridor = section.Find("FirstCorridor");
      if (oldCorridor != null) Object.DestroyImmediate(oldCorridor.gameObject);
      Transform corridor = CreateContainer("FirstCorridor", section);

      // The entrance approaches the door along +X. In the layout, the route turns right
      // immediately after crossing it, which maps to world -Z in this scene.
      float innerLength = Mathf.Abs(CorridorEndZ + DoorHalfWidth);
      float innerCenterZ = (CorridorEndZ - DoorHalfWidth) * 0.5f;
      float outerLength = Mathf.Abs(CorridorEndZ - DoorHalfWidth);
      float outerCenterZ = (CorridorEndZ + DoorHalfWidth) * 0.5f;

      Transform turnWall = CreateGrayboxWall(
        "TurnWall",
        corridor,
        new Vector3((DoorX + CorridorOuterX) * 0.5f, WallHeight * 0.5f, DoorHalfWidth),
        new Vector3(CorridorOuterX - DoorX, WallHeight, WallThickness));
      Transform innerWall = CreateGrayboxWall(
        "InnerWall",
        corridor,
        new Vector3(DoorX, WallHeight * 0.5f, innerCenterZ),
        new Vector3(WallThickness, WallHeight, innerLength));
      Transform outerWall = CreateGrayboxWall(
        "OuterWall",
        corridor,
        new Vector3(CorridorOuterX, WallHeight * 0.5f, outerCenterZ),
        new Vector3(WallThickness, WallHeight, outerLength));

      Transform visuals = CreateContainer("WallVisuals", corridor);
      GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
      if (wallPrefab == null)
        throw new FileNotFoundException("Palace wall prefab was not found.", WallPrefabPath);

      MeshFilter prefabMesh = wallPrefab.GetComponent<MeshFilter>();
      if (prefabMesh == null || prefabMesh.sharedMesh == null)
        throw new System.InvalidOperationException("SM_wall has no usable MeshFilter.");

      TileWallRun(turnWall.name, turnWall.position, Vector3.right, Vector3.back,
        CorridorOuterX - DoorX, WallHeight, visuals, wallPrefab, prefabMesh.sharedMesh.bounds);
      TileWallRun(innerWall.name, innerWall.position, Vector3.forward, Vector3.right,
        innerLength, WallHeight, visuals, wallPrefab, prefabMesh.sharedMesh.bounds);
      TileWallRun(outerWall.name, outerWall.position, Vector3.forward, Vector3.left,
        outerLength, WallHeight, visuals, wallPrefab, prefabMesh.sharedMesh.bounds);

      BuildDoorLinePathTopology(grayBox.transform, section);

      PalaceLightReceiver receiver = grayBox.GetComponent<PalaceLightReceiver>();
      if (receiver != null) receiver.RefreshRenderers();

      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && scene.IsValid() && scene.isLoaded)
        EditorSceneManager.CloseScene(scene, true);
    }
  }

  private static Transform CreateContainer(string name, Transform parent) {
    var container = new GameObject(name);
    container.transform.SetParent(parent, false);
    GameObjectUtility.SetStaticEditorFlags(container, EnvironmentStaticFlags);
    return container.transform;
  }

  private static Transform FindOrCreateContainer(string name, Transform parent) {
    Transform existing = parent.Find(name);
    return existing != null ? existing : CreateContainer(name, parent);
  }

  private static Transform CreateGrayboxWall(
    string name,
    Transform parent,
    Vector3 position,
    Vector3 scale) {
    GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
    wall.name = name;
    wall.layer = 8;
    wall.transform.SetParent(parent, false);
    wall.transform.localPosition = position;
    wall.transform.localScale = scale;
    GameObjectUtility.SetStaticEditorFlags(wall, EnvironmentStaticFlags);

    MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
    if (renderer != null) renderer.enabled = false;
    return wall.transform;
  }

  private static void RemoveExistingDoorTopology(Transform grayBox, Transform section) {
    Transform linePaths = section.Find("LinePaths");

    WallSwitchPathNetwork network = grayBox.GetComponentInChildren<WallSwitchPathNetwork>(true);
    if (network != null) {
      var serializedNetwork = new SerializedObject(network);
      SerializedProperty paths = serializedNetwork.FindProperty("switchablePaths");
      if (paths != null) {
        for (int index = paths.arraySize - 1; index >= 0; index--) {
          Object existing = paths.GetArrayElementAtIndex(index).objectReferenceValue;
          LinePath existingPath = existing as LinePath;
          bool belongsToOldTopology = existingPath != null && linePaths != null &&
                                      existingPath.transform.IsChildOf(linePaths);
          if (existing != null && !belongsToOldTopology) continue;

          int previousSize = paths.arraySize;
          paths.DeleteArrayElementAtIndex(index);
          if (paths.arraySize == previousSize) paths.DeleteArrayElementAtIndex(index);
        }
        serializedNetwork.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(network);
      }
    }

    if (linePaths != null) Object.DestroyImmediate(linePaths.gameObject);
  }

  private static void BuildDoorLinePathTopology(Transform grayBox, Transform section) {
    Transform firstSection = grayBox.Find("FirstSection");
    if (firstSection == null) throw new System.InvalidOperationException("FirstSection was not found.");
    Transform firstSectionPaths = firstSection.Find("LinePaths");
    Transform outerRouteTransform = firstSectionPaths != null
      ? firstSectionPaths.Find("OuterRoutePath")
      : null;
    if (outerRouteTransform == null || !outerRouteTransform.TryGetComponent(out LinePath outerRoute))
      throw new System.InvalidOperationException("FirstSection/LinePaths/OuterRoutePath was not found.");

    EnsureDoorSplitStrands(outerRouteTransform, outerRoute);

    PassagewayDoor door = null;
    foreach (PassagewayDoor candidate in grayBox.GetComponentsInChildren<PassagewayDoor>(true)) {
      if (candidate.name != "FirstSectionLockedDoor") continue;
      door = candidate;
      break;
    }
    if (door == null) throw new System.InvalidOperationException("FirstSectionLockedDoor was not found.");

    DoorLinePathState topology = door.GetComponentInChildren<DoorLinePathState>(true);
    if (topology == null || topology.LongSidePaths.Length != 2 || topology.ShortSidePaths.Length != 2)
      throw new System.InvalidOperationException("SlidingDoor's four-corner LinePath topology is incomplete.");

    Transform pathsRoot = CreateContainer("LinePaths", section);

    LinePath corridorPath = CreatePath("FirstCorridorPath", pathsRoot, new[] {
      new[] {
        new Vector3(9.25f, 0f, -0.75f),
        new Vector3(9.25f, 0f, CorridorEndZ + 0.25f)
      },
      new[] {
        new Vector3(9.25f, 0f, 0.75f),
        new Vector3(CorridorOuterX - 0.25f, 0f, 0.75f),
        new Vector3(CorridorOuterX - 0.25f, 0f, CorridorEndZ + 0.25f)
      }
    });

    RegisterSwitchablePaths(
      grayBox,
      topology.LongSidePaths[0],
      topology.LongSidePaths[1],
      topology.ShortSidePaths[0],
      topology.ShortSidePaths[1],
      corridorPath);
  }

  private static void EnsureDoorPrefabLinePaths() {
    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(DoorPrefabPath);
    if (prefabRoot == null) throw new FileNotFoundException("SlidingDoor prefab was not found.", DoorPrefabPath);

    try {
      PassagewayDoor door = prefabRoot.GetComponent<PassagewayDoor>();
      if (door == null) throw new System.InvalidOperationException("SlidingDoor has no PassagewayDoor component.");

      Transform topologyRoot = prefabRoot.transform.Find("DoorLinePaths");
      if (topologyRoot == null) {
        var topologyObject = new GameObject("DoorLinePaths");
        topologyObject.transform.SetParent(prefabRoot.transform, false);
        topologyRoot = topologyObject.transform;
      }

      Transform frontLower = FindOrCreateLocalPoint(
        topologyRoot, "FrontLower", new Vector3(0.75f, 0f, -0.25f));
      Transform frontUpper = FindOrCreateLocalPoint(
        topologyRoot, "FrontUpper", new Vector3(-0.75f, 0f, -0.25f));
      Transform backLower = FindOrCreateLocalPoint(
        topologyRoot, "BackLower", new Vector3(0.75f, 0f, 0.25f));
      Transform backUpper = FindOrCreateLocalPoint(
        topologyRoot, "BackUpper", new Vector3(-0.75f, 0f, 0.25f));

      LinePath longFront = FindOrCreateEdgePath("LongSideFront", topologyRoot, frontLower, frontUpper);
      LinePath longBack = FindOrCreateEdgePath("LongSideBack", topologyRoot, backLower, backUpper);
      LinePath shortLower = FindOrCreateEdgePath("ShortSideLower", topologyRoot, frontLower, backLower);
      LinePath shortUpper = FindOrCreateEdgePath("ShortSideUpper", topologyRoot, frontUpper, backUpper);

      DoorLinePathState topology = topologyRoot.GetComponent<DoorLinePathState>();
      if (topology == null) topology = topologyRoot.gameObject.AddComponent<DoorLinePathState>();
      topology.Configure(door, new[] { longFront, longBack }, new[] { shortLower, shortUpper });

      PrefabUtility.SaveAsPrefabAsset(prefabRoot, DoorPrefabPath);
    } finally {
      PrefabUtility.UnloadPrefabContents(prefabRoot);
    }
  }

  private static Transform FindOrCreateLocalPoint(Transform parent, string name, Vector3 localPosition) {
    Transform point = parent.Find(name);
    if (point == null) {
      var pointObject = new GameObject(name);
      pointObject.transform.SetParent(parent, false);
      point = pointObject.transform;
    }
    point.localPosition = localPosition;
    point.localRotation = Quaternion.identity;
    point.localScale = Vector3.one;
    return point;
  }

  private static LinePath FindOrCreateEdgePath(
    string name,
    Transform parent,
    Transform firstPoint,
    Transform secondPoint) {
    Transform root = parent.Find(name);
    if (root == null) {
      var rootObject = new GameObject(name);
      rootObject.transform.SetParent(parent, false);
      root = rootObject.transform;
    }
    LinePath path = root.GetComponent<LinePath>();
    if (path == null) path = root.gameObject.AddComponent<LinePath>();
    path.closedLoop = false;
    path.gizmoColor = Color.cyan;
    path.ConfigureExternalPoints(firstPoint, secondPoint);
    return path;
  }

  private static void EnsureDoorSplitStrands(Transform routeRoot, LinePath route) {
    Transform existingMain = routeRoot.Find("MainRouteStrand");
    Transform existingEntrance = routeRoot.Find("EntranceSouthStrand");
    if (existingMain != null && existingEntrance != null) {
      route.Rebuild();
      return;
    }

    if (routeRoot.childCount != 14)
      throw new System.InvalidOperationException(
        "OuterRoutePath must contain its original 14 points before the door topology is authored.");

    var originalPoints = new Transform[14];
    for (int index = 0; index < originalPoints.Length; index++)
      originalPoints[index] = routeRoot.GetChild(index);

    Transform main = CreateContainer("MainRouteStrand", routeRoot);
    Transform entrance = CreateContainer("EntranceSouthStrand", routeRoot);
    for (int index = 0; index < 12; index++) originalPoints[index].SetParent(main, true);
    CreatePoint("DoorUpperPoint", main, new Vector3(8.75f, 0f, 0.75f));
    originalPoints[12].SetParent(entrance, true);
    originalPoints[13].SetParent(entrance, true);
    route.Rebuild();
  }

  private static LinePath CreatePath(string name, Transform parent, Vector3[][] strands) {
    Transform pathRoot = CreateContainer(name, parent);
    LinePath path = pathRoot.gameObject.AddComponent<LinePath>();
    path.closedLoop = false;
    path.gizmoColor = Color.cyan;

    for (int strandIndex = 0; strandIndex < strands.Length; strandIndex++) {
      Transform strand = CreateContainer($"Strand{strandIndex + 1:D2}", pathRoot);
      Vector3[] points = strands[strandIndex];
      for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
        CreatePoint($"Point{pointIndex + 1:D2}", strand, points[pointIndex]);
    }

    path.Rebuild();
    return path;
  }

  private static Transform CreatePoint(string name, Transform parent, Vector3 worldPosition) {
    var point = new GameObject(name);
    point.transform.SetParent(parent, false);
    point.transform.position = worldPosition;
    return point.transform;
  }

  private static void RegisterSwitchablePaths(Transform grayBox, params LinePath[] additions) {
    WallSwitchPathNetwork network = grayBox.GetComponentInChildren<WallSwitchPathNetwork>(true);
    if (network == null)
      throw new System.InvalidOperationException("The Palace WallSwitchPathNetwork was not found.");

    var serializedNetwork = new SerializedObject(network);
    SerializedProperty paths = serializedNetwork.FindProperty("switchablePaths");
    foreach (LinePath addition in additions) {
      bool exists = false;
      for (int index = 0; index < paths.arraySize; index++) {
        if (paths.GetArrayElementAtIndex(index).objectReferenceValue != addition) continue;
        exists = true;
        break;
      }
      if (exists) continue;
      int newIndex = paths.arraySize;
      paths.InsertArrayElementAtIndex(newIndex);
      paths.GetArrayElementAtIndex(newIndex).objectReferenceValue = addition;
    }
    serializedNetwork.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(network);
  }

  private static void TileWallRun(
    string runName,
    Vector3 center,
    Vector3 direction,
    Vector3 facing,
    float length,
    float height,
    Transform visualRoot,
    GameObject wallPrefab,
    Bounds moduleBounds) {
    float heightScale = height / moduleBounds.size.y;
    float naturalModuleLength = moduleBounds.size.z * heightScale;
    int count = Mathf.Max(1, Mathf.RoundToInt(length / naturalModuleLength));
    float segmentLength = length / count;
    Vector3 scale = new(heightScale, heightScale, segmentLength / moduleBounds.size.z);
    Vector3 alignedLength = direction.normalized;
    Vector3 visibleFace = -Vector3.Cross(Vector3.up, alignedLength).normalized;
    if (Vector3.Dot(visibleFace, facing.normalized) < 0f) alignedLength = -alignedLength;
    Quaternion rotation = Quaternion.LookRotation(alignedLength, Vector3.up);

    for (int index = 0; index < count; index++) {
      float offset = -length * 0.5f + segmentLength * (index + 0.5f);
      Vector3 desiredBoundsCenter = center + direction * offset;
      GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
        wallPrefab,
        visualRoot.gameObject.scene);
      instance.name = $"{runName}_{index + 1:D2}";
      instance.transform.SetParent(visualRoot, true);
      instance.transform.rotation = rotation;
      instance.transform.localScale = scale;
      instance.transform.position = desiredBoundsCenter - rotation * Vector3.Scale(moduleBounds.center, scale);
      GameObjectUtility.SetStaticEditorFlags(instance,
        StaticEditorFlags.ContributeGI |
        StaticEditorFlags.BatchingStatic |
        StaticEditorFlags.OccluderStatic |
        StaticEditorFlags.OccludeeStatic);

      foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        renderer.renderingLayerMask = 1073741825u;
    }
  }

  private static GameObject FindRoot(Scene scene, string name) {
    foreach (GameObject root in scene.GetRootGameObjects())
      if (root.name == name) return root;
    return null;
  }
}
#endif
