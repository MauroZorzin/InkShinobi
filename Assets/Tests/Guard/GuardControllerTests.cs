using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

public class GuardControllerTests {
  private GameObject _guardGO;
  private GameObject _navMeshFloorGO;
  private NavMeshDataInstance _navMeshDataInstance;
  private GuardController _guard;

  [SetUp]
  public void Setup() {
    CreateTestNavMesh();

    Assert.IsTrue(
      NavMesh.SamplePosition(Vector3.zero, out NavMeshHit hit, 5f, NavMesh.AllAreas),
      "Expected a sampled point on the test NavMesh."
    );

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
  public void Guard_StartsInPatrolState() {
    Assert.AreEqual(GuardController.GuardState.Patrol, _guard.CurrentState);
  }

  [Test]
  public void Guard_TransitionsToTakenDownState() {
    _guard.PerformTakedown();

    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);
  }

  [Test]
  public void Guard_RemainsInTakenDownState() {
    _guard.PerformTakedown();
    _guard.PerformTakedown();

    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);
  }

  [Test]
  public void Awake_AutoFindsVisionConeInChildren() {
    GameObject guardGO = new("GuardWithVision");
    GameObject visionGO = new("VisionCone");
    visionGO.transform.SetParent(guardGO.transform);
    GuardVisionCone visionCone = visionGO.AddComponent<GuardVisionCone>();

    GuardController guard = guardGO.AddComponent<GuardController>();

    Assert.AreSame(visionCone, guard.visionCone);

    Object.DestroyImmediate(guardGO);
  }

  [Test]
  public void InvestigateSound_FromPatrol_TransitionsToInvestigating() {
    Vector3 soundPosition = _guardGO.transform.position + Vector3.forward;

    _guard.InvestigateSound(soundPosition);

    Assert.AreEqual(GuardController.GuardState.Investigating, _guard.CurrentState);
  }

  [Test]
  public void InvestigateSound_DoesNotOverrideTakenDownState() {
    _guard.PerformTakedown();

    _guard.InvestigateSound(_guardGO.transform.position + Vector3.forward);

    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);
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
