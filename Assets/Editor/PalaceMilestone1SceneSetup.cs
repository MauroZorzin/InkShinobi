#if UNITY_EDITOR
#pragma warning disable UDR0001 // Editor-only menu utility; it owns no runtime state.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot authoring utility for the Palace Milestone 1 color tests. It creates ordinary,
/// editable scene objects; it does not generate or synchronize them at runtime.
/// </summary>
public static class PalaceMilestone1SceneSetup {
  private const string ScenePath = "Assets/Scenes/GameScenes/4-Palace/4-Palace.unity";
  private const string LightCeilingPendingPath = "Temp/PalaceLightCeilingRevision.pending";
  private const string InteriorLightPendingPath = "Temp/PalaceInteriorLightFixtureRevision.pending";
  private const string GuardVisionLightsPendingPath = "Temp/PalaceGuardVisionLightsRevision.pending";
  private const string CompactLayoutPendingPath = "Temp/PalaceCompactLayoutRevision.pending";
  private const string FixedLightFieldsPendingPath = "Temp/PalaceFixedLightFieldsRevision.pending";
  private const string LightReceiversPendingPath = "Temp/PalaceLightReceiversRevision.pending";
  private const string WallVisualsPendingPath = "Temp/PalaceWallVisualsRevision.pending";
  private const string AmbientFillPendingPath = "Temp/PalaceAmbientFillRevision.pending";
  private const string FixedLightSyncPendingPath = "Temp/PalaceFixedLightSyncRevision.pending";
  private const string GuardLightLookPendingPath = "Temp/PalaceGuardLightLookRevision.pending";
  private const string RootName = "VisualFoundation";
  private const string ProjectionShaderPath = "Assets/Art/Shaders/PalaceLightProjection.shader";
  private const string MaterialFolder = "Assets/Art/Materials/Palace";
  private const string ProjectionMaterialPath = MaterialFolder + "/PalaceLightProjection.mat";
  private const string CoreMaterialPath = MaterialFolder + "/PalaceLightCore.mat";
  private const string WaterMaterialPath = MaterialFolder + "/PalaceWaterPuddle.mat";
  private const string GuardMaterialPath = MaterialFolder + "/PalaceGuardOutline.mat";
  private const string WaterShaderPath = "Assets/Art/Shaders/PalaceWaterPuddle.shader";
  private const string GuardSourceMaterialPath = "Assets/Art/VFX/SpriteOutline/GuardOutline.mat";
  private const string WaterPrefabPath = "Assets/Geometry/Idyllic Fantasy Nature/Prefabs/Water.prefab";
  private const string GuardPrefabPath = "Assets/Prefabs/GuardPatrol.prefab";
  private const string InteriorLightPrefabPath = "Assets/Geometry/JapanPalace/Prefabs/SM_light.prefab";
  private const string PalaceWallPrefabPath = "Assets/Geometry/JapanPalace/Prefabs/SM_wall.prefab";

  private static readonly Color LightColor = new(1f, 0.92f, 0.08f, 1f);
  private static readonly Color FarVisionColor = new(1f, 0.97f, 0.62f, 1f);
  private static readonly Color GuardColor = new(0.28f, 0.72f, 1f, 1f);

  [MenuItem("Tools/Ink Shinobi/Palace/Rebuild Milestone 1 Color Tests")]
  public static void RebuildFromMenu() {
    BuildSceneContent();
  }

  [InitializeOnLoadMethod]
  private static void QueueLightCeilingRevision() {
    if (File.Exists(LightCeilingPendingPath))
      EditorApplication.delayCall += RunLightCeilingRevision;
    if (File.Exists(InteriorLightPendingPath))
      EditorApplication.delayCall += RunInteriorLightRevision;
    if (File.Exists(GuardVisionLightsPendingPath))
      EditorApplication.delayCall += RunGuardVisionLightsRevision;
    if (File.Exists(CompactLayoutPendingPath))
      EditorApplication.delayCall += RunCompactLayoutRevision;
    if (File.Exists(FixedLightFieldsPendingPath))
      EditorApplication.delayCall += RunFixedLightFieldsRevision;
    if (File.Exists(LightReceiversPendingPath))
      EditorApplication.delayCall += RunLightReceiversRevision;
    if (File.Exists(WallVisualsPendingPath))
      EditorApplication.delayCall += RunWallVisualsRevision;
    if (File.Exists(AmbientFillPendingPath))
      EditorApplication.delayCall += RunAmbientFillRevision;
    if (File.Exists(FixedLightSyncPendingPath))
      EditorApplication.delayCall += RunFixedLightSyncRevision;
    if (File.Exists(GuardLightLookPendingPath))
      EditorApplication.delayCall += RunGuardLightLookRevision;
  }

  private static void RunLightCeilingRevision() {
    if (!File.Exists(LightCeilingPendingPath)) return;
    try {
      ApplyLightAndCeilingRevision();
      File.Delete(LightCeilingPendingPath);
      Debug.Log("Palace warm-yellow lights and graybox ceiling updated successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunInteriorLightRevision() {
    if (!File.Exists(InteriorLightPendingPath)) return;
    try {
      ReplaceLightPlaceholdersWithPalaceFixtures();
      File.Delete(InteriorLightPendingPath);
      Debug.Log("Palace light placeholders replaced with hanging interior fixtures successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunGuardVisionLightsRevision() {
    if (!File.Exists(GuardVisionLightsPendingPath)) return;
    try {
      ApplyGuardVisionLights();
      File.Delete(GuardVisionLightsPendingPath);
      Debug.Log("Palace guard near and far vision lights created successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunCompactLayoutRevision() {
    if (!File.Exists(CompactLayoutPendingPath)) return;
    try {
      ApplyCompactLayout();
      File.Delete(CompactLayoutPendingPath);
      Debug.Log("Palace corridors and loop compacted successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunFixedLightFieldsRevision() {
    if (!File.Exists(FixedLightFieldsPendingPath)) return;
    try {
      ApplyFixedLightFields();
      File.Delete(FixedLightFieldsPendingPath);
      Debug.Log("Palace fixed lights converted to connected world-space fields successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunLightReceiversRevision() {
    if (!File.Exists(LightReceiversPendingPath)) return;
    try {
      ApplyPalaceLightReceivers();
      File.Delete(LightReceiversPendingPath);
      Debug.Log("Palace floor and wall light receivers configured successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunWallVisualsRevision() {
    if (!File.Exists(WallVisualsPendingPath)) return;
    try {
      ReplaceGrayboxWallsWithPalaceWalls();
      File.Delete(WallVisualsPendingPath);
      Debug.Log("Palace graybox walls replaced with tiled Machiya wall visuals successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunAmbientFillRevision() {
    if (!File.Exists(AmbientFillPendingPath)) return;
    try {
      ApplyPalaceAmbientFill();
      File.Delete(AmbientFillPendingPath);
      Debug.Log("Palace shadowless ambient fill light configured successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunFixedLightSyncRevision() {
    if (!File.Exists(FixedLightSyncPendingPath)) return;
    try {
      CopyNorthFixedLightSettings();
      File.Delete(FixedLightSyncPendingPath);
      Debug.Log("North fixed-light settings copied to West, East, and South successfully.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  private static void RunGuardLightLookRevision() {
    if (!File.Exists(GuardLightLookPendingPath)) return;
    try {
      ApplyGuardVisionLightLook();
      File.Delete(GuardLightLookPendingPath);
      Debug.Log("Palace guard vision lights updated to match the approved fixed-light look.");
    } catch (System.Exception exception) {
      Debug.LogException(exception);
    }
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Guard Vision Light Look")]
  public static void ApplyGuardVisionLightLook() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null) throw new System.InvalidOperationException($"{RootName} was not found.");
      Transform guard = FindDescendant(visualRoot.transform, "LoopPatrolGuard");
      GuardVisionLightRig rig = guard != null ? guard.GetComponentInChildren<GuardVisionLightRig>(true) : null;
      if (rig == null) throw new System.InvalidOperationException("LoopPatrolGuard vision-light rig was not found.");

      PalaceConeLightSource nearField = rig.transform.Find("NearVision")?.GetComponent<PalaceConeLightSource>();
      PalaceConeLightSource farField = rig.transform.Find("FarVision")?.GetComponent<PalaceConeLightSource>();
      if (nearField == null || farField == null)
        throw new System.InvalidOperationException("NearVision or FarVision field was not found.");

      ConfigureGuardFieldLook(nearField, LightColor, 1f, true);
      ConfigureGuardFieldLook(farField, FarVisionColor, 0f, false);
      rig.Synchronize();

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void ConfigureGuardFieldLook(
    PalaceConeLightSource field,
    Color fieldColor,
    float priority,
    bool masksLowerPriority) {
    var serializedField = new SerializedObject(field);
    SerializedProperty origin = serializedField.FindProperty("origin");
    SerializedProperty masksLower = serializedField.FindProperty("maskLowerPriorityCones");
    if (origin == null || masksLower == null)
      throw new System.InvalidOperationException("Guard field origin or priority mask property was not found.");

    origin.objectReferenceValue = field.transform;
    SetColor(serializedField, "color", fieldColor);
    SetFloat(serializedField, "rangeFeather", 0.05f);
    SetFloat(serializedField, "angleFeather", 0.5f);
    SetFloat(serializedField, "colorIntensity", 0.2f);
    SetFloat(serializedField, "projectedBrightness", 0.15f);
    SetFloat(serializedField, "visualPriority", priority);
    SetFloat(serializedField, "flickerAmount", 0.02f);
    SetFloat(serializedField, "flickerSpeed", 2.4f);
    SetFloat(serializedField, "flickerIrregularity", 0.75f);
    masksLower.boolValue = masksLowerPriority;
    serializedField.ApplyModifiedPropertiesWithoutUndo();
  }

  private static void SetFloat(SerializedObject target, string propertyName, float value) {
    SerializedProperty property = target.FindProperty(propertyName);
    if (property == null) throw new System.InvalidOperationException($"Property '{propertyName}' was not found.");
    property.floatValue = value;
  }

  private static void SetColor(SerializedObject target, string propertyName, Color value) {
    SerializedProperty property = target.FindProperty(propertyName);
    if (property == null) throw new System.InvalidOperationException($"Property '{propertyName}' was not found.");
    property.colorValue = value;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Copy North Fixed Light Settings")]
  public static void CopyNorthFixedLightSettings() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null) throw new System.InvalidOperationException($"{RootName} was not found.");
      Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
      if (lightsRoot == null) throw new System.InvalidOperationException("LightPoints was not found.");

      Transform north = RequireTransform(lightsRoot, "NorthLight");
      PalaceFixedLightSource northSource = north.GetComponent<PalaceFixedLightSource>();
      Light northLight = RequireTransform(north, "PointLight").GetComponent<Light>();
      if (northSource == null || northLight == null)
        throw new System.InvalidOperationException("NorthLight is missing its fixed-light source or Point Light.");

      string[] targetNames = { "WestLight", "EastLight", "SouthLight" };
      foreach (string targetName in targetNames) {
        Transform target = RequireTransform(lightsRoot, targetName);
        PalaceFixedLightSource targetSource = target.GetComponent<PalaceFixedLightSource>();
        Light targetLight = RequireTransform(target, "PointLight").GetComponent<Light>();
        if (targetSource == null || targetLight == null)
          throw new System.InvalidOperationException($"{targetName} is missing its fixed-light source or Point Light.");

        CopyFixedLightSourceLook(northSource, targetSource);
        EditorUtility.CopySerialized(northLight, targetLight);

        Component northAdditionalData = northLight.GetComponent("UniversalAdditionalLightData");
        Component targetAdditionalData = targetLight.GetComponent("UniversalAdditionalLightData");
        if (northAdditionalData != null && targetAdditionalData != null)
          EditorUtility.CopySerialized(northAdditionalData, targetAdditionalData);
      }

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void CopyFixedLightSourceLook(PalaceFixedLightSource source, PalaceFixedLightSource target) {
    var sourceObject = new SerializedObject(source);
    var targetObject = new SerializedObject(target);
    string[] propertyNames = {
      "color", "radius", "edgeFeather", "colorIntensity", "projectedBrightness",
      "flickerEnabled", "flickerAmount", "flickerSpeed", "flickerIrregularity"
    };
    foreach (string propertyName in propertyNames) {
      SerializedProperty sourceProperty = sourceObject.FindProperty(propertyName);
      if (sourceProperty == null || targetObject.FindProperty(propertyName) == null)
        throw new System.InvalidOperationException($"Fixed-light property '{propertyName}' was not found.");
      targetObject.CopyFromSerializedProperty(sourceProperty);
    }
    targetObject.ApplyModifiedPropertiesWithoutUndo();
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Ambient Fill Light")]
  public static void ApplyPalaceAmbientFill() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject fillObject = FindRoot(palaceScene, "PalaceAmbientFill")
                              ?? FindRoot(palaceScene, "Directional Light");
      if (fillObject == null) fillObject = new GameObject("PalaceAmbientFill");

      fillObject.name = "PalaceAmbientFill";
      fillObject.SetActive(true);

      Light fill = fillObject.GetComponent<Light>();
      if (fill == null) fill = fillObject.AddComponent<Light>();
      fill.type = LightType.Directional;
      fill.shadows = LightShadows.None;
      fill.enabled = true;

      // The removed minimum-luminance component becomes a missing script after compilation.
      GameObjectUtility.RemoveMonoBehavioursWithMissingScript(fillObject);

      ConfigureHorizontalWallFill(palaceScene, "PalaceWallFillA", 45f, 0.08f);
      ConfigureHorizontalWallFill(palaceScene, "PalaceWallFillB", 225f, 0.08f);

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void ConfigureHorizontalWallFill(Scene scene, string objectName, float yaw, float intensity) {
    GameObject fillObject = FindRoot(scene, objectName);
    if (fillObject == null) fillObject = new GameObject(objectName);
    fillObject.SetActive(true);
    fillObject.transform.position = Vector3.zero;
    fillObject.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    Light fill = fillObject.GetComponent<Light>();
    if (fill == null) fill = fillObject.AddComponent<Light>();
    fill.type = LightType.Directional;
    fill.color = Color.white;
    fill.intensity = intensity;
    fill.shadows = LightShadows.None;
    fill.renderMode = LightRenderMode.Auto;
    fill.cullingMask = ~0;
    fill.enabled = true;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Replace Graybox Walls With Palace Walls")]
  public static void ReplaceGrayboxWallsWithPalaceWalls() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PalaceWallPrefabPath);
      if (wallPrefab == null)
        throw new FileNotFoundException("Palace wall prefab not found.", PalaceWallPrefabPath);

      MeshFilter prefabMeshFilter = wallPrefab.GetComponent<MeshFilter>();
      if (prefabMeshFilter == null || prefabMeshFilter.sharedMesh == null)
        throw new System.InvalidOperationException("SM_wall has no usable MeshFilter.");
      Bounds moduleBounds = prefabMeshFilter.sharedMesh.bounds;
      if (moduleBounds.size.z <= 0f || moduleBounds.size.y <= 0f)
        throw new System.InvalidOperationException("SM_wall has invalid mesh bounds.");

      GameObject grayBox = FindRoot(palaceScene, "GrayBox");
      if (grayBox == null) throw new System.InvalidOperationException("GrayBox was not found.");
      Transform firstSection = RequireTransform(grayBox.transform, "FirstSection");
      Transform linePaths = RequireTransform(firstSection, "LinePaths");
      var navigationSamples = new System.Collections.Generic.List<Vector3>();
      foreach (Transform sample in linePaths.GetComponentsInChildren<Transform>(true)) {
        if (sample != linePaths && sample.name.StartsWith("Point"))
          navigationSamples.Add(sample.position);
      }
      if (navigationSamples.Count == 0)
        throw new System.InvalidOperationException("No LinePaths points were found for wall-facing orientation.");

      Transform existingVisuals = grayBox.transform.Find("PalaceWallVisuals");
      if (existingVisuals != null) Object.DestroyImmediate(existingVisuals.gameObject);

      var visuals = new GameObject("PalaceWallVisuals");
      visuals.transform.SetParent(grayBox.transform, false);

      int targetCount = 0;
      int moduleCount = 0;
      foreach (MeshRenderer grayboxRenderer in firstSection.GetComponentsInChildren<MeshRenderer>(true)) {
        Transform target = grayboxRenderer.transform;
        bool isBlock = target.name.StartsWith("Block") && target.name != "BlockedDoorPlaceholder";
        bool isWall = target.name.Contains("Wall");
        if (!isBlock && !isWall) continue;
        if (!target.TryGetComponent(out BoxCollider box)) continue;

        if (isBlock)
          moduleCount += SkinGrayboxBlock(target, box, visuals.transform, wallPrefab, moduleBounds);
        else
          moduleCount += SkinGrayboxWall(target, box, visuals.transform, wallPrefab, moduleBounds, navigationSamples);

        grayboxRenderer.enabled = false;
        targetCount++;
      }

      if (targetCount == 0)
        throw new System.InvalidOperationException("No graybox wall targets were found in FirstSection.");

      PalaceLightReceiver receiver = grayBox.GetComponent<PalaceLightReceiver>();
      if (receiver == null) receiver = grayBox.AddComponent<PalaceLightReceiver>();
      receiver.RefreshRenderers();

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
      Debug.Log($"Skinned {targetCount} graybox wall targets with {moduleCount} SM_wall modules.");
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static int SkinGrayboxWall(
    Transform target,
    BoxCollider box,
    Transform visualRoot,
    GameObject wallPrefab,
    Bounds moduleBounds,
    System.Collections.Generic.List<Vector3> navigationSamples) {
    Vector3 scaledSize = Vector3.Scale(box.size, Abs(target.lossyScale));
    bool runsAlongX = scaledSize.x >= scaledSize.z;
    Vector3 localAxis = runsAlongX ? Vector3.right : Vector3.forward;
    float length = runsAlongX ? scaledSize.x : scaledSize.z;
    float height = scaledSize.y;
    Vector3 center = target.TransformPoint(box.center);
    Vector3 direction = target.TransformDirection(localAxis).normalized;
    Vector3 facing = FindFacingTowardNearestPath(center, direction, navigationSamples);
    return TileWallRun(target.name, center, direction, facing, length, height,
      visualRoot, wallPrefab, moduleBounds, target.gameObject.isStatic);
  }

  private static int SkinGrayboxBlock(
    Transform target,
    BoxCollider box,
    Transform visualRoot,
    GameObject wallPrefab,
    Bounds moduleBounds) {
    Vector3 scaledSize = Vector3.Scale(box.size, Abs(target.lossyScale));
    float height = scaledSize.y;
    Vector3 center = target.TransformPoint(box.center);
    Vector3 right = target.TransformDirection(Vector3.right).normalized;
    Vector3 forward = target.TransformDirection(Vector3.forward).normalized;

    int count = 0;
    count += TileWallRun(target.name + "North", center + forward * (scaledSize.z * 0.5f),
      right, forward, scaledSize.x, height, visualRoot, wallPrefab, moduleBounds, target.gameObject.isStatic);
    count += TileWallRun(target.name + "South", center - forward * (scaledSize.z * 0.5f),
      right, -forward, scaledSize.x, height, visualRoot, wallPrefab, moduleBounds, target.gameObject.isStatic);
    count += TileWallRun(target.name + "East", center + right * (scaledSize.x * 0.5f),
      forward, right, scaledSize.z, height, visualRoot, wallPrefab, moduleBounds, target.gameObject.isStatic);
    count += TileWallRun(target.name + "West", center - right * (scaledSize.x * 0.5f),
      forward, -right, scaledSize.z, height, visualRoot, wallPrefab, moduleBounds, target.gameObject.isStatic);
    return count;
  }

  private static int TileWallRun(
    string runName,
    Vector3 center,
    Vector3 direction,
    Vector3 facing,
    float length,
    float height,
    Transform visualRoot,
    GameObject wallPrefab,
    Bounds moduleBounds,
    bool isStatic) {
    float heightScale = height / moduleBounds.size.y;
    // The Machiya wall module runs along its local Z axis, not local X.
    float naturalModuleLength = moduleBounds.size.z * heightScale;
    int count = Mathf.Max(1, Mathf.RoundToInt(length / naturalModuleLength));
    float segmentLength = length / count;
    Vector3 scale = new(heightScale, heightScale, segmentLength / moduleBounds.size.z);
    Vector3 alignedLength = direction.normalized;
    // SM_wall's visible side is its local -X face. Reversing the length direction rotates the
    // module 180 degrees around Y without changing the occupied wall run.
    Vector3 visibleFace = -Vector3.Cross(Vector3.up, alignedLength).normalized;
    if (Vector3.Dot(visibleFace, facing.normalized) < 0f) alignedLength = -alignedLength;
    Quaternion rotation = Quaternion.LookRotation(alignedLength, Vector3.up);

    for (int index = 0; index < count; index++) {
      float offset = -length * 0.5f + segmentLength * (index + 0.5f);
      Vector3 desiredBoundsCenter = center + direction * offset;
      GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, visualRoot.gameObject.scene);
      instance.name = $"{runName}_{index + 1:D2}";
      instance.transform.SetParent(visualRoot, true);
      instance.transform.rotation = rotation;
      instance.transform.localScale = scale;
      instance.transform.position = desiredBoundsCenter - rotation * Vector3.Scale(moduleBounds.center, scale);
      GameObjectUtility.SetStaticEditorFlags(instance,
        isStatic ? StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic : 0);
    }
    return count;
  }

  private static Vector3 FindFacingTowardNearestPath(
    Vector3 wallCenter,
    Vector3 wallDirection,
    System.Collections.Generic.List<Vector3> navigationSamples) {
    Vector3 nearest = navigationSamples[0];
    float nearestDistance = float.PositiveInfinity;
    foreach (Vector3 sample in navigationSamples) {
      Vector3 planarDelta = new(sample.x - wallCenter.x, 0f, sample.z - wallCenter.z);
      float distance = planarDelta.sqrMagnitude;
      if (distance >= nearestDistance) continue;
      nearestDistance = distance;
      nearest = sample;
    }

    Vector3 side = Vector3.Cross(Vector3.up, wallDirection).normalized;
    Vector3 towardPath = new(nearest.x - wallCenter.x, 0f, nearest.z - wallCenter.z);
    return Vector3.Dot(side, towardPath) >= 0f ? side : -side;
  }

  private static Vector3 Abs(Vector3 value) {
    return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Floor And Wall Light Receivers")]
  public static void ApplyPalaceLightReceivers() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject grayBox = FindRoot(palaceScene, "GrayBox");
      if (grayBox == null) throw new System.InvalidOperationException("GrayBox was not found.");

      PalaceLightReceiver receiver = grayBox.GetComponent<PalaceLightReceiver>();
      if (receiver == null) receiver = grayBox.AddComponent<PalaceLightReceiver>();
      receiver.RefreshRenderers();

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Connected Fixed Light Fields")]
  public static void ApplyFixedLightFields() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null)
        throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");
      Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
      if (lightsRoot == null) throw new System.InvalidOperationException("LightPoints was not found.");

      string[] lightNames = { "NorthLight", "WestLight", "EastLight", "SouthLight" };
      foreach (string lightName in lightNames)
        ConfigureFixedLightRoot(RequireTransform(lightsRoot, lightName));

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void ConfigureFixedLightRoot(Transform lightRoot) {
    Transform floorProjection = lightRoot.Find("FloorProjection");
    if (floorProjection != null) Object.DestroyImmediate(floorProjection.gameObject);
    Transform wallProjections = lightRoot.Find("WallProjections");
    if (wallProjections != null) Object.DestroyImmediate(wallProjections.gameObject);

    Transform pointLightTransform = lightRoot.Find("PointLight");
    if (pointLightTransform == null || !pointLightTransform.TryGetComponent(out Light pointLight))
      throw new System.InvalidOperationException($"{lightRoot.name} has no PointLight child.");

    ConfigureEnvironmentPointLight(pointLight);
    PalaceFixedLightSource source = lightRoot.GetComponent<PalaceFixedLightSource>();
    if (source == null) source = lightRoot.gameObject.AddComponent<PalaceFixedLightSource>();
    source.Configure(pointLightTransform, LightColor, 2f, 0.05f, 0.2f);
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Compact First Section Layout")]
  public static void ApplyCompactLayout() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject grayBox = FindRoot(palaceScene, "GrayBox");
      if (grayBox == null) throw new System.InvalidOperationException("GrayBox was not found.");
      Transform firstSection = RequireTransform(grayBox.transform, "FirstSection");

      CompactEntranceAndCorridors(firstSection);
      CompactLoop(firstSection);
      CompactLinePaths(firstSection);

      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null)
        throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");
      CompactVisualTests(visualRoot);

      foreach (LinePath path in firstSection.GetComponentsInChildren<LinePath>(true)) path.Rebuild();

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void CompactEntranceAndCorridors(Transform firstSection) {
    Transform entrance = RequireTransform(firstSection, "Entrance");
    SetLocalBox(RequireTransform(entrance, "WallLeft"), new Vector3(3.5f, 0.75f, 1f), new Vector3(7f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(entrance, "WallRight"), new Vector3(4.5f, 0.75f, -1f), new Vector3(9f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(entrance, "BlockedDoorPlaceholder"), new Vector3(9f, 0.75f, 0f), new Vector3(0.1f, 1.5f, 2f));

    Transform left = RequireTransform(firstSection, "LeftCorridor");
    SetLocalBox(RequireTransform(left, "OuterWall"), new Vector3(7f, 0.75f, 7f), new Vector3(0.1f, 1.5f, 12f));
    SetLocalBox(RequireTransform(left, "InnerWall"), new Vector3(9f, 0.75f, 6f), new Vector3(0.1f, 1.5f, 10f));

    Transform firstRight = RequireTransform(firstSection, "FirstRightCorridor");
    SetLocalBox(RequireTransform(firstRight, "LowerWall"), new Vector3(13f, 0.75f, 11f), new Vector3(8f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(firstRight, "UpperWall"), new Vector3(13f, 0.75f, 13f), new Vector3(12f, 1.5f, 0.1f));

    Transform secondRight = RequireTransform(firstSection, "SecondRightCorridor");
    SetLocalBox(RequireTransform(secondRight, "NearWall"), new Vector3(17f, 0.75f, 7f), new Vector3(0.1f, 1.5f, 8f));
    SetLocalBox(RequireTransform(secondRight, "FarWall"), new Vector3(19f, 0.75f, 8f), new Vector3(0.1f, 1.5f, 10f));
  }

  private static void CompactLoop(Transform firstSection) {
    Transform loop = RequireTransform(firstSection, "Loop");
    SetLocalBox(RequireTransform(loop, "OuterEntryWallNear"), new Vector3(15.5f, 0.75f, 3f), new Vector3(3f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(loop, "OuterEntryWallFar"), new Vector3(25f, 0.75f, 3f), new Vector3(12f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(loop, "OuterOppositeWall"), new Vector3(22.5f, 0.75f, -14f), new Vector3(17f, 1.5f, 0.1f));
    SetLocalBox(RequireTransform(loop, "OuterNearSideWall"), new Vector3(14f, 0.75f, -5.5f), new Vector3(0.1f, 1.5f, 17f));
    SetLocalBox(RequireTransform(loop, "OuterFarSideWall"), new Vector3(31f, 0.75f, -5.5f), new Vector3(0.1f, 1.5f, 17f));

    Vector3 blockScale = new(5.5f, 1.5f, 5.5f);
    SetLocalBox(RequireTransform(loop, "BlockNearUpper"), new Vector3(18.75f, 0.75f, -1.75f), blockScale);
    SetLocalBox(RequireTransform(loop, "BlockFarUpper"), new Vector3(26.25f, 0.75f, -1.75f), blockScale);
    SetLocalBox(RequireTransform(loop, "BlockNearLower"), new Vector3(18.75f, 0.75f, -9.25f), blockScale);
    SetLocalBox(RequireTransform(loop, "BlockFarLower"), new Vector3(26.25f, 0.75f, -9.25f), blockScale);
  }

  private static void CompactLinePaths(Transform firstSection) {
    Transform paths = RequireTransform(firstSection, "LinePaths");
    SetPathPoints(RequireTransform(paths, "OuterRoutePath"), new[] {
      new Vector3(0f, 0f, 0.85f), new Vector3(7.15f, 0f, 0.85f),
      new Vector3(7.15f, 0f, 12.85f), new Vector3(18.85f, 0f, 12.85f),
      new Vector3(18.85f, 0f, 2.85f), new Vector3(30.85f, 0f, 2.85f),
      new Vector3(30.85f, 0f, -13.85f), new Vector3(14.15f, 0f, -13.85f),
      new Vector3(14.15f, 0f, 2.85f), new Vector3(17.15f, 0f, 2.85f),
      new Vector3(17.15f, 0f, 11.15f), new Vector3(8.85f, 0f, 11.15f),
      new Vector3(8.85f, 0f, -0.85f), new Vector3(0f, 0f, -0.85f)
    });

    SetPathPoints(RequireTransform(paths, "NearUpperBlockPath"), RectanglePoints(15.9f, 21.6f, -4.6f, 1.1f));
    SetPathPoints(RequireTransform(paths, "FarUpperBlockPath"), RectanglePoints(23.4f, 29.1f, -4.6f, 1.1f));
    SetPathPoints(RequireTransform(paths, "NearLowerBlockPath"), RectanglePoints(15.9f, 21.6f, -12.1f, -6.4f));
    SetPathPoints(RequireTransform(paths, "FarLowerBlockPath"), RectanglePoints(23.4f, 29.1f, -12.1f, -6.4f));
  }

  private static Vector3[] RectanglePoints(float minX, float maxX, float minZ, float maxZ) {
    return new[] {
      new Vector3(minX, 0f, maxZ), new Vector3(maxX, 0f, maxZ),
      new Vector3(maxX, 0f, minZ), new Vector3(minX, 0f, minZ)
    };
  }

  private static void CompactVisualTests(GameObject visualRoot) {
    Material projectionMaterial = GetOrCreateProjectionMaterial();
    Material coreMaterial = GetOrCreateCoreMaterial();
    Transform puddle = FindDescendant(visualRoot.transform, "EntrancePuddle");
    if (puddle != null) SetWorldXZ(puddle, 3.5f, 0f);

    Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
    if (lightsRoot != null) {
      SetWorldXZ(RequireTransform(lightsRoot, "NorthLight"), 22.5f, 2f);
      SetWorldXZ(RequireTransform(lightsRoot, "WestLight"), 15f, -5.5f);
      SetWorldXZ(RequireTransform(lightsRoot, "EastLight"), 30f, -5.5f);
      SetWorldXZ(RequireTransform(lightsRoot, "SouthLight"), 22.5f, -13f);
      foreach (Light pointLight in lightsRoot.GetComponentsInChildren<Light>(true))
        ConfigureEnvironmentPointLight(pointLight);
      foreach (Transform child in lightsRoot.GetComponentsInChildren<Transform>(true)) {
        if (child.name == "FloorProjection" || child.parent != null && child.parent.name == "WallProjections") {
          if (child.TryGetComponent(out MeshRenderer projectionRenderer))
            projectionRenderer.sharedMaterial = projectionMaterial;
        } else if (child.name == "ColoredLightCore" && child.TryGetComponent(out MeshRenderer coreRenderer)) {
          coreRenderer.sharedMaterial = coreMaterial;
        }
      }
    }

    Transform guardGroup = FindDescendant(visualRoot.transform, "GuardColorTest");
    if (guardGroup == null) return;
    Transform route = RequireTransform(guardGroup, "SquarePatrolRoute");
    Vector3[] patrolPoints = {
      new(15f, 0f, 2f), new(30f, 0f, 2f),
      new(30f, 0f, -13f), new(15f, 0f, -13f)
    };
    SetPathPoints(route, patrolPoints);

    Transform guard = RequireTransform(guardGroup, "LoopPatrolGuard");
    SetWorldXZ(guard, 15f, -5.5f);
  }

  private static void SetPathPoints(Transform path, Vector3[] positions) {
    if (path.childCount != positions.Length)
      throw new System.InvalidOperationException($"{path.name} has {path.childCount} points; expected {positions.Length}.");
    for (int i = 0; i < positions.Length; i++) path.GetChild(i).localPosition = positions[i];
  }

  private static void SetLocalBox(Transform target, Vector3 position, Vector3 scale) {
    target.localPosition = position;
    target.localScale = scale;
  }

  private static void SetWorldXZ(Transform target, float x, float z) {
    Vector3 position = target.position;
    target.position = new Vector3(x, position.y, z);
  }

  private static Transform RequireTransform(Transform root, string relativePath) {
    Transform result = root.Find(relativePath);
    if (result == null)
      throw new System.InvalidOperationException($"{relativePath} was not found below {root.name}.");
    return result;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Guard Vision Lights")]
  public static void ApplyGuardVisionLights() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null)
        throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");

      Transform guard = FindDescendant(visualRoot.transform, "LoopPatrolGuard");
      if (guard == null) throw new System.InvalidOperationException("LoopPatrolGuard was not found.");

      GuardVisionCone vision = guard.GetComponent<GuardVisionCone>();
      if (vision == null) throw new System.InvalidOperationException("LoopPatrolGuard has no GuardVisionCone.");

      RemoveLegacyGuardVisionObjects(guard);
      Transform existingRig = guard.Find("VisionLightRig");
      if (existingRig != null) Object.DestroyImmediate(existingRig.gameObject);

      var rigObject = new GameObject("VisionLightRig");
      rigObject.transform.SetParent(guard, false);

      CreateGuardVisionCone("NearVision", rigObject.transform, out PalaceConeLightSource nearField);
      CreateGuardVisionCone("FarVision", rigObject.transform, out PalaceConeLightSource farField);

      GuardVisionLightRig rig = rigObject.AddComponent<GuardVisionLightRig>();
      rig.Configure(vision, nearField, farField);
      ConfigureGuardFieldLook(nearField, LightColor, 1f, true);
      ConfigureGuardFieldLook(farField, FarVisionColor, 0f, false);
      rig.Synchronize();

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void RemoveLegacyGuardVisionObjects(Transform guard) {
    string[] legacyNames = { "Spot Light", "Spot Light (1)", "Light", "Light (1)" };
    foreach (string legacyName in legacyNames) {
      Transform legacy = guard.Find(legacyName);
      if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
    }
  }

  private static void CreateGuardVisionCone(
    string objectName,
    Transform parent,
    out PalaceConeLightSource field) {
    var cone = new GameObject(objectName);
    cone.transform.SetParent(parent, false);
    cone.transform.localPosition = new Vector3(0f, 0f, 0.12f);
    field = cone.AddComponent<PalaceConeLightSource>();
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Replace Light Placeholders With Palace Fixtures")]
  public static void ReplaceLightPlaceholdersWithPalaceFixtures() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      GameObject fixturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InteriorLightPrefabPath);
      if (fixturePrefab == null)
        throw new FileNotFoundException("Palace interior light prefab not found.", InteriorLightPrefabPath);

      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null)
        throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");

      Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
      if (lightsRoot == null) throw new System.InvalidOperationException("LightPoints was not found.");

      string[] lightNames = { "NorthLight", "WestLight", "EastLight", "SouthLight" };
      foreach (string lightName in lightNames) {
        Transform lightRoot = FindDescendant(lightsRoot, lightName);
        if (lightRoot == null) continue;
        ReplaceLightFixture(lightRoot, fixturePrefab, palaceScene);
      }

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void ReplaceLightFixture(Transform lightRoot, GameObject fixturePrefab, Scene palaceScene) {
    Transform placeholder = lightRoot.Find("CeilingFixturePlaceholder");
    if (placeholder != null) Object.DestroyImmediate(placeholder.gameObject);

    Transform oldFixture = lightRoot.Find("CeilingFixture");
    if (oldFixture != null) Object.DestroyImmediate(oldFixture.gameObject);

    GameObject fixture = (GameObject)PrefabUtility.InstantiatePrefab(fixturePrefab, palaceScene);
    fixture.name = "CeilingFixture";
    fixture.transform.SetParent(lightRoot, false);
    fixture.transform.localPosition = Vector3.zero;
    fixture.transform.localRotation = Quaternion.identity;
    fixture.transform.localScale = Vector3.one;

    Bounds initialBounds = GetRendererBounds(fixture);
    const float targetHeight = 0.5f;
    const float targetWidth = 0.45f;
    float sourceHeight = Mathf.Max(initialBounds.size.y, 0.0001f);
    float sourceWidth = Mathf.Max(initialBounds.size.x, initialBounds.size.z, 0.0001f);
    float uniformScale = Mathf.Min(targetHeight / sourceHeight, targetWidth / sourceWidth);
    fixture.transform.localScale = Vector3.one * uniformScale;

    Bounds fittedBounds = GetRendererBounds(fixture);
    Vector3 horizontalOffset = new(
      lightRoot.position.x - fittedBounds.center.x,
      0f,
      lightRoot.position.z - fittedBounds.center.z);
    fixture.transform.position += horizontalOffset;

    fittedBounds = GetRendererBounds(fixture);
    const float ceilingUndersideY = 2f;
    fixture.transform.position += Vector3.up * (ceilingUndersideY - fittedBounds.max.y);

    fittedBounds = GetRendererBounds(fixture);
    float glowHeight = Mathf.Lerp(fittedBounds.min.y, fittedBounds.max.y, 0.38f);
    Transform core = lightRoot.Find("ColoredLightCore");
    if (core != null)
      core.position = new Vector3(lightRoot.position.x, glowHeight, lightRoot.position.z);
    Transform pointLight = lightRoot.Find("PointLight");
    if (pointLight != null)
      pointLight.position = new Vector3(lightRoot.position.x, glowHeight, lightRoot.position.z);
  }

  private static Bounds GetRendererBounds(GameObject root) {
    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
    if (renderers.Length == 0)
      throw new System.InvalidOperationException($"{root.name} contains no renderers and cannot be fitted.");

    Bounds bounds = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
    return bounds;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Warm Lights and Graybox Ceiling")]
  public static void ApplyLightAndCeilingRevision() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      Material projectionMaterial = GetOrCreateProjectionMaterial();
      Material coreMaterial = GetOrCreateCoreMaterial();
      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null) throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");

      Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
      if (lightsRoot == null) throw new System.InvalidOperationException("LightPoints was not found.");
      foreach (Light pointLight in lightsRoot.GetComponentsInChildren<Light>(true))
        ConfigureEnvironmentPointLight(pointLight);
      foreach (Transform child in lightsRoot.GetComponentsInChildren<Transform>(true)) {
        if (child.name == "FloorProjection" || child.parent != null && child.parent.name == "WallProjections") {
          if (child.TryGetComponent(out MeshRenderer projectionRenderer))
            projectionRenderer.sharedMaterial = projectionMaterial;
        } else if (child.name == "ColoredLightCore" && child.TryGetComponent(out MeshRenderer coreRenderer)) {
          coreRenderer.sharedMaterial = coreMaterial;
        }
      }

      AddOrUpdateGrayboxCeiling(palaceScene);
      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void AddOrUpdateGrayboxCeiling(Scene palaceScene) {
    GameObject grayBox = FindRoot(palaceScene, "GrayBox");
    if (grayBox == null) throw new System.InvalidOperationException("GrayBox was not found in the Palace scene.");

    Transform ceiling = grayBox.transform.Find("Ceiling");
    GameObject ceilingObject;
    if (ceiling == null) {
      ceilingObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
      ceilingObject.name = "Ceiling";
      ceilingObject.transform.SetParent(grayBox.transform, false);
    } else {
      ceilingObject = ceiling.gameObject;
    }

    Transform floor = grayBox.transform.Find("Floor");
    if (floor != null) ceilingObject.layer = floor.gameObject.layer;
    ceilingObject.transform.localPosition = new Vector3(0f, 2.1f, 0f);
    ceilingObject.transform.localRotation = Quaternion.identity;
    ceilingObject.transform.localScale = new Vector3(100f, 0.2f, 60f);

    Collider ceilingCollider = ceilingObject.GetComponent<Collider>();
    if (ceilingCollider != null) Object.DestroyImmediate(ceilingCollider);
    MeshRenderer renderer = ceilingObject.GetComponent<MeshRenderer>();
    if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.Off;
  }

  private static void ConfigureEnvironmentPointLight(Light pointLight) {
    pointLight.color = LightColor;
    pointLight.intensity = 3f;
    pointLight.range = 2.2f;
    pointLight.shadows = LightShadows.Hard;
  }

  [MenuItem("Tools/Ink Shinobi/Palace/Apply Water Light and Guard Revision")]
  public static void ApplyRequestedRevisions() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      Material projectionMaterial = GetOrCreateProjectionMaterial();
      Material coreMaterial = GetOrCreateCoreMaterial();
      Material waterMaterial = GetOrCreateWaterMaterial();
      Material guardMaterial = GetOrCreateGuardMaterial();

      GameObject visualRoot = FindRoot(palaceScene, RootName);
      if (visualRoot == null) throw new System.InvalidOperationException($"{RootName} was not found in the Palace scene.");

      UpdateWater(visualRoot, waterMaterial);
      UpdateLights(visualRoot, projectionMaterial, coreMaterial);
      UpdateGuard(visualRoot, guardMaterial);

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void UpdateWater(GameObject visualRoot, Material waterMaterial) {
    Transform puddle = FindDescendant(visualRoot.transform, "EntrancePuddle");
    if (puddle == null) throw new System.InvalidOperationException("EntrancePuddle was not found.");
    MeshRenderer renderer = puddle.GetComponent<MeshRenderer>();
    if (renderer == null) throw new System.InvalidOperationException("EntrancePuddle has no MeshRenderer.");
    renderer.sharedMaterial = waterMaterial;
  }

  private static void UpdateLights(GameObject visualRoot, Material projectionMaterial, Material coreMaterial) {
    Transform lightsRoot = FindDescendant(visualRoot.transform, "LightPoints");
    if (lightsRoot == null) throw new System.InvalidOperationException("LightPoints was not found.");

    string[] lightNames = { "NorthLight", "WestLight", "EastLight", "SouthLight" };
    foreach (string lightName in lightNames) {
      Transform lightRoot = FindDescendant(lightsRoot, lightName);
      if (lightRoot == null) continue;

      Transform core = FindDescendant(lightRoot, "ColoredLightCore");
      if (core != null && core.TryGetComponent(out MeshRenderer coreRenderer))
        coreRenderer.sharedMaterial = coreMaterial;

      foreach (Light pointLight in lightRoot.GetComponentsInChildren<Light>(true))
        ConfigureEnvironmentPointLight(pointLight);

      ConfigureFixedLightRoot(lightRoot);
    }
  }

  private static void CreateWallProjections(Transform lightRoot, Material projectionMaterial, bool wallsAlongZ) {
    var wallRoot = new GameObject("WallProjections");
    wallRoot.transform.SetParent(lightRoot, false);

    for (int side = -1; side <= 1; side += 2) {
      GameObject projection = GameObject.CreatePrimitive(PrimitiveType.Quad);
      projection.name = side < 0 ? "InnerWallProjection" : "OuterWallProjection";
      projection.transform.SetParent(wallRoot.transform, false);
      projection.transform.localScale = new Vector3(4.5f, 1.9f, 1f);

      if (wallsAlongZ) {
        projection.transform.localPosition = new Vector3(side * 0.985f, 0.95f, 0f);
        projection.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);
      } else {
        projection.transform.localPosition = new Vector3(0f, 0.95f, side * 0.985f);
        projection.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 0f : 180f, 0f);
      }

      Object.DestroyImmediate(projection.GetComponent<Collider>());
      projection.GetComponent<MeshRenderer>().sharedMaterial = projectionMaterial;
      projection.AddComponent<SelectiveColor>();
    }
  }

  private static void UpdateGuard(GameObject visualRoot, Material guardMaterial) {
    Transform guard = FindDescendant(visualRoot.transform, "StationaryColoredGuard")
                      ?? FindDescendant(visualRoot.transform, "LoopPatrolGuard");
    if (guard == null) throw new System.InvalidOperationException("Palace test guard was not found.");

    guard.name = "LoopPatrolGuard";
    Vector3 guardPosition = guard.position;
    guard.position = new Vector3(15f, guardPosition.y, -5.5f);

    SpriteRenderer spriteRenderer = guard.GetComponentInChildren<SpriteRenderer>(true);
    if (spriteRenderer == null) throw new System.InvalidOperationException("Palace test guard has no SpriteRenderer.");
    spriteRenderer.color = Color.white;
    spriteRenderer.sharedMaterial = guardMaterial;

    // Clear stale serialized selection bits first, then let the marker authoritatively reapply it.
    foreach (Renderer renderer in guard.GetComponentsInChildren<Renderer>(true))
      renderer.renderingLayerMask &= ~SelectiveColor.RenderingLayerMask;

    SelectiveColor selectiveColor = guard.GetComponent<SelectiveColor>();
    if (selectiveColor == null) selectiveColor = guard.gameObject.AddComponent<SelectiveColor>();
    selectiveColor.enabled = true;
    selectiveColor.RefreshRenderers();

    GuardPaletteTint paletteTint = guard.GetComponent<GuardPaletteTint>();
    if (paletteTint == null) paletteTint = guard.gameObject.AddComponent<GuardPaletteTint>();
    paletteTint.Configure(spriteRenderer, GuardColor);

    NavMeshAgent agent = guard.GetComponent<NavMeshAgent>();
    if (agent != null) agent.enabled = false;
    GuardSpriteFacing facing = guard.GetComponent<GuardSpriteFacing>();
    if (facing != null) facing.enabled = true;

    Transform guardGroup = guard.parent;
    Transform oldRoute = guardGroup.Find("SquarePatrolRoute");
    if (oldRoute != null) Object.DestroyImmediate(oldRoute.gameObject);

    var route = new GameObject("SquarePatrolRoute");
    route.transform.SetParent(guardGroup, false);
    Vector3[] routePositions = {
      new(15f, 0f, 2f),
      new(30f, 0f, 2f),
      new(30f, 0f, -13f),
      new(15f, 0f, -13f)
    };

    var routePoints = new Transform[routePositions.Length];
    for (int i = 0; i < routePositions.Length; i++) {
      var point = new GameObject($"Point{i + 1:00}");
      point.transform.SetParent(route.transform, false);
      point.transform.position = routePositions[i];
      routePoints[i] = point.transform;
    }

    GuardSquarePatrol patrol = guard.GetComponent<GuardSquarePatrol>();
    if (patrol == null) patrol = guard.gameObject.AddComponent<GuardSquarePatrol>();
    patrol.Configure(routePoints);
  }

  private static void BuildSceneContent() {
    Scene palaceScene = SceneManager.GetSceneByPath(ScenePath);
    bool openedTemporarily = !palaceScene.IsValid() || !palaceScene.isLoaded;
    if (openedTemporarily) palaceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

    try {
      Material projectionMaterial = GetOrCreateProjectionMaterial();
      Material coreMaterial = GetOrCreateCoreMaterial();

      GameObject existingRoot = FindRoot(palaceScene, RootName);
      if (existingRoot != null) Object.DestroyImmediate(existingRoot);

      var root = new GameObject(RootName);
      SceneManager.MoveGameObjectToScene(root, palaceScene);

      CreateWaterTest(root.transform);
      CreateLightTests(root.transform, projectionMaterial, coreMaterial);
      CreateGuardTest(root.transform);

      EditorSceneManager.MarkSceneDirty(palaceScene);
      EditorSceneManager.SaveScene(palaceScene);
      AssetDatabase.SaveAssets();
    } finally {
      if (openedTemporarily && palaceScene.IsValid() && palaceScene.isLoaded)
        EditorSceneManager.CloseScene(palaceScene, true);
    }
  }

  private static void CreateWaterTest(Transform root) {
    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);
    if (prefab == null) throw new FileNotFoundException("Water prefab not found.", WaterPrefabPath);

    var waterGroup = new GameObject("WaterColorTest");
    waterGroup.transform.SetParent(root, false);

    GameObject puddle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
    puddle.name = "EntrancePuddle";
    puddle.transform.SetParent(waterGroup.transform, true);
    puddle.transform.SetPositionAndRotation(new Vector3(3.5f, 0.012f, 0f), Quaternion.identity);
    puddle.transform.localScale = new Vector3(0.24f, 1f, 0.085f);
    puddle.AddComponent<SelectiveColor>();
  }

  private static void CreateLightTests(Transform root, Material projectionMaterial, Material coreMaterial) {
    var lightsRoot = new GameObject("LightPoints");
    lightsRoot.transform.SetParent(root, false);

    Vector3[] positions = {
      new(22.5f, 0f, 2f),
      new(15f, 0f, -5.5f),
      new(30f, 0f, -5.5f),
      new(22.5f, 0f, -13f)
    };

    string[] names = { "NorthLight", "WestLight", "EastLight", "SouthLight" };
    for (int i = 0; i < positions.Length; i++)
      CreateLightPoint(names[i], positions[i], lightsRoot.transform, projectionMaterial, coreMaterial);
  }

  private static void CreateLightPoint(
    string objectName,
    Vector3 position,
    Transform parent,
    Material projectionMaterial,
    Material coreMaterial) {

    var root = new GameObject(objectName);
    root.transform.SetParent(parent, false);
    root.transform.position = position;

    GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    fixture.name = "CeilingFixturePlaceholder";
    fixture.transform.SetParent(root.transform, false);
    fixture.transform.localPosition = new Vector3(0f, 1.82f, 0f);
    fixture.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);
    Object.DestroyImmediate(fixture.GetComponent<Collider>());

    GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    core.name = "ColoredLightCore";
    core.transform.SetParent(root.transform, false);
    core.transform.localPosition = new Vector3(0f, 1.67f, 0f);
    core.transform.localScale = Vector3.one * 0.09f;
    Object.DestroyImmediate(core.GetComponent<Collider>());
    core.GetComponent<MeshRenderer>().sharedMaterial = coreMaterial;
    core.AddComponent<SelectiveColor>();

    var lightObject = new GameObject("PointLight");
    lightObject.transform.SetParent(root.transform, false);
    lightObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
    Light pointLight = lightObject.AddComponent<Light>();
    pointLight.type = LightType.Point;
    pointLight.color = LightColor;
    pointLight.range = 3.2f;
    pointLight.intensity = 0.35f;
    pointLight.shadows = LightShadows.Hard;

    PalaceFixedLightSource source = root.AddComponent<PalaceFixedLightSource>();
    source.Configure(lightObject.transform, LightColor, 3.2f, 0.12f, 0.95f);
  }

  private static void CreateGuardTest(Transform root) {
    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPrefabPath);
    if (prefab == null) throw new FileNotFoundException("Guard prefab not found.", GuardPrefabPath);

    var guardGroup = new GameObject("GuardColorTest");
    guardGroup.transform.SetParent(root, false);

    GameObject guard = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
    guard.name = "StationaryColoredGuard";
    guard.transform.SetParent(guardGroup.transform, true);
    guard.transform.SetPositionAndRotation(new Vector3(15f, 0.05f, -5.5f), Quaternion.identity);

    foreach (NavMeshAgent agent in guard.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
    foreach (Collider collider in guard.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

    foreach (MonoBehaviour behaviour in guard.GetComponentsInChildren<MonoBehaviour>(true)) {
      if (behaviour is GuardSpriteFacing || behaviour is SelectiveColor) continue;
      behaviour.enabled = false;
    }

    foreach (Transform child in guard.GetComponentsInChildren<Transform>(true)) {
      if (child.name == "Light" || child.name == "Light (1)" || child.name.StartsWith("Spot Light"))
        child.gameObject.SetActive(false);
    }

    foreach (SpriteRenderer spriteRenderer in guard.GetComponentsInChildren<SpriteRenderer>(true))
      spriteRenderer.color = GuardColor;

    guard.AddComponent<SelectiveColor>();
  }

  private static Material GetOrCreateProjectionMaterial() {
    EnsureMaterialFolder();
    Material material = AssetDatabase.LoadAssetAtPath<Material>(ProjectionMaterialPath);
    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ProjectionShaderPath);
    if (shader == null) throw new FileNotFoundException("Palace light projection shader not found.", ProjectionShaderPath);

    if (material == null) {
      material = new Material(shader) { name = "PalaceLightProjection" };
      AssetDatabase.CreateAsset(material, ProjectionMaterialPath);
    } else {
      material.shader = shader;
    }

    material.SetColor("_Color", new Color(LightColor.r, LightColor.g, LightColor.b, 0.78f));
    material.SetFloat("_EdgeSoftness", 0.52f);
    material.SetFloat("_Intensity", 0.6f);
    EditorUtility.SetDirty(material);
    return material;
  }

  private static Material GetOrCreateCoreMaterial() {
    EnsureMaterialFolder();
    Material material = AssetDatabase.LoadAssetAtPath<Material>(CoreMaterialPath);
    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
    if (shader == null) throw new System.InvalidOperationException("URP Unlit shader was not found.");

    if (material == null) {
      material = new Material(shader) { name = "PalaceLightCore" };
      AssetDatabase.CreateAsset(material, CoreMaterialPath);
    } else {
      material.shader = shader;
    }

    material.SetColor("_BaseColor", LightColor);
    material.SetColor("_Color", LightColor);
    EditorUtility.SetDirty(material);
    return material;
  }

  private static Material GetOrCreateWaterMaterial() {
    EnsureMaterialFolder();
    Material material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(WaterShaderPath);
    if (shader == null) throw new FileNotFoundException("Palace puddle shader not found.", WaterShaderPath);

    if (material == null) {
      material = new Material(shader) { name = "PalaceWaterPuddle" };
      AssetDatabase.CreateAsset(material, WaterMaterialPath);
    } else {
      material.shader = shader;
    }

    material.SetColor("_DeepColor", new Color(0.015f, 0.42f, 0.72f, 0.9f));
    material.SetColor("_ShallowColor", new Color(0.16f, 0.78f, 0.95f, 0.72f));
    material.SetColor("_RippleColor", new Color(0.7f, 0.95f, 1f, 0.7f));
    material.SetFloat("_RippleSpeed", 0.45f);
    material.SetFloat("_EdgeSoftness", 0.12f);
    EditorUtility.SetDirty(material);
    return material;
  }

  private static Material GetOrCreateGuardMaterial() {
    EnsureMaterialFolder();
    Material material = AssetDatabase.LoadAssetAtPath<Material>(GuardMaterialPath);
    Material source = AssetDatabase.LoadAssetAtPath<Material>(GuardSourceMaterialPath);
    if (source == null) throw new FileNotFoundException("Guard outline material not found.", GuardSourceMaterialPath);

    if (material == null) {
      material = new Material(source) { name = "PalaceGuardOutline" };
      AssetDatabase.CreateAsset(material, GuardMaterialPath);
    } else {
      material.shader = source.shader;
    }

    material.SetFloat("_RegionRecolorEnabled", 0f);
    material.SetFloat("_RegionBlueThreshold", 0.08f);
    material.SetFloat("_RegionSoftness", 0.04f);
    material.SetFloat("_RegionReferenceLuminance", 0.62f);
    EditorUtility.SetDirty(material);
    return material;
  }

  private static void EnsureMaterialFolder() {
    if (!AssetDatabase.IsValidFolder("Assets/Art/Materials"))
      AssetDatabase.CreateFolder("Assets/Art", "Materials");
    if (!AssetDatabase.IsValidFolder(MaterialFolder))
      AssetDatabase.CreateFolder("Assets/Art/Materials", "Palace");
  }

  private static GameObject FindRoot(Scene scene, string objectName) {
    foreach (GameObject root in scene.GetRootGameObjects())
      if (root.name == objectName) return root;
    return null;
  }

  private static Transform FindDescendant(Transform root, string objectName) {
    if (root.name == objectName) return root;
    foreach (Transform child in root) {
      Transform result = FindDescendant(child, objectName);
      if (result != null) return result;
    }
    return null;
  }
}
#pragma warning restore UDR0001
#endif
