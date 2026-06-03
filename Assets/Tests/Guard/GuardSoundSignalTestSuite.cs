using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

public class GuardSoundSignalTestSuite {
  private GameObject _signalGO;
  private GameObject _guardGO;
  private GameObject _navMeshFloorGO;
  private NavMeshDataInstance _navMeshDataInstance;
  private GuardSoundSignal _signal;
  private GuardController _guard;

  [SetUp]
  public void Setup() {
    CreateTestNavMesh();

    Assert.IsTrue(
      NavMesh.SamplePosition(Vector3.zero, out NavMeshHit hit, 5f, NavMesh.AllAreas),
      "Expected a sampled point on the test NavMesh."
    );

    // Create the sound signal with a trigger collider
    _signalGO = new GameObject("TestSoundSignal");
    _signalGO.transform.position = Vector3.zero;
    SphereCollider triggerCollider = _signalGO.AddComponent<SphereCollider>();
    triggerCollider.radius = 5f;
    triggerCollider.isTrigger = true;
    _signal = _signalGO.AddComponent<GuardSoundSignal>();

    // Create a guard with vision cone
    _guardGO = new GameObject("TestGuard");
    _guardGO.transform.position = hit.position;
    GuardVisionCone visionCone = new GameObject("VisionCone").AddComponent<GuardVisionCone>();
    visionCone.playerLayerMask = 1 << 3;
    visionCone.transform.SetParent(_guardGO.transform);
    _guard = _guardGO.AddComponent<GuardController>();
    _guard.takedownDestroyDelay = 10f;
  }

  [TearDown]
  public void TearDown() {
    if (_signalGO != null) {
      Object.DestroyImmediate(_signalGO);
    }

    if (_guardGO != null) {
      Object.DestroyImmediate(_guardGO);
    }

    if (_navMeshFloorGO != null) {
      Object.DestroyImmediate(_navMeshFloorGO);
    }

    if (_navMeshDataInstance.valid) {
      _navMeshDataInstance.Remove();
    }
  }

  [Test]
  public void Signal_StartsInactiveState() {
    Assert.IsFalse(_signal.IsActive, "Signal should start inactive.");
  }

  [Test]
  public void Activate_SetsIsActiveTrue() {
    _signal.Activate();
    Assert.IsTrue(_signal.IsActive, "Signal should be active after Activate() call.");
  }

  [Test]
  public void Activate_WithLifetime_SetsLifetimeValue() {
    float newLifetime = 2.5f;
    _signal.Activate(newLifetime);

    Assert.IsTrue(_signal.IsActive, "Signal should be active.");
    Assert.AreEqual(newLifetime, _signal.lifetime, "Lifetime should be updated to new value.");
  }

  [Test]
  public void Deactivate_SetsIsActiveFalse() {
    _signal.Activate();
    Assert.IsTrue(_signal.IsActive);

    _signal.Deactivate();
    Assert.IsFalse(_signal.IsActive, "Signal should be inactive after Deactivate() call.");
  }

  private void CreateTestNavMesh() {
    _navMeshFloorGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
    _navMeshFloorGO.name = "TestNavMeshFloor";
    _navMeshFloorGO.transform.position = Vector3.zero;

    Assert.Greater(NavMesh.GetSettingsCount(), 0, "Expected at least one NavMesh build setting.");

    Mesh mesh = _navMeshFloorGO.GetComponent<MeshFilter>().sharedMesh;
    var sources = new List<NavMeshBuildSource> {
      new() {
        shape = NavMeshBuildSourceShape.Mesh,
        sourceObject = mesh,
        transform = _navMeshFloorGO.transform.localToWorldMatrix,
        area = 0
      }
    };

    NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByIndex(0);
    var bounds = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
    NavMeshData navMeshData = NavMeshBuilder.BuildNavMeshData(
      buildSettings,
      sources,
      bounds,
      Vector3.zero,
      Quaternion.identity
    );

    Assert.IsNotNull(navMeshData, "Expected NavMeshBuilder to create test NavMeshData.");
    _navMeshDataInstance = NavMesh.AddNavMeshData(navMeshData);
  }
}
