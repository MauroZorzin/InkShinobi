using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public class GuardPatrolTestSuite {
  private GameObject _guardGO;
  private GuardPatrol _guardPatrol;
  private NavMeshAgent _agent;
  private GameObject _navMeshFloorGO;
  private NavMeshDataInstance _navMeshDataInstance;

  private GameObject _patrolAreaGO;
  private PatrolArea _patrolArea;

  [SetUp]
  public void Setup() {
    CreateTestNavMesh();

    Assert.IsTrue(
      NavMesh.SamplePosition(Vector3.zero, out NavMeshHit hit, 5f, NavMesh.AllAreas),
      "Expected a sampled point on the test NavMesh.");

    _guardGO = new GameObject("Guard");
    _guardGO.transform.position = hit.position;
    _agent = _guardGO.AddComponent<NavMeshAgent>();
    _guardPatrol = _guardGO.AddComponent<GuardPatrol>();

    // Prevent Unity from auto-running Start before each test has finished arranging state.
    _guardPatrol.enabled = false;
  }

  [TearDown]
  public void TearDown() {
    if (_patrolAreaGO != null) {
      Object.Destroy(_patrolAreaGO);
    }

    if (_guardGO != null) {
      Object.Destroy(_guardGO);
    }

    if (_navMeshFloorGO != null) {
      Object.Destroy(_navMeshFloorGO);
    }

    if (_navMeshDataInstance.valid) {
      _navMeshDataInstance.Remove();
    }

    LogAssert.NoUnexpectedReceived();
  }

  [Test]
  public void Awake_InitializesAgentAndPathState() {
    NavMeshPath path = GetPrivateField<NavMeshPath>("path");

    Assert.IsNotNull(path, "Awake should create a reusable NavMeshPath instance.");
    Assert.IsTrue(_agent.updatePosition, "Awake should keep NavMeshAgent position updates enabled.");
    Assert.IsFalse(_agent.updateRotation, "Awake should disable NavMeshAgent rotation updates for sprite facing control.");
  }

  [Test]
  public void Start_WithoutPatrolArea_LogsErrorAndDisablesComponent() {
    SetPrivateField("patrolArea", null);

    LogAssert.Expect(LogType.Error, "Guard: No PatrolArea assigned.");

    InvokePrivate("Start");

    Assert.IsFalse(_guardPatrol.enabled, "Start should disable component when no patrol area is assigned.");
  }

  [Test]
  public void Start_WithPatrolAreaButNotOnNavMesh_LogsErrorAndDisablesComponent() {
    _patrolAreaGO = new GameObject("PatrolArea");
    _patrolAreaGO.AddComponent<BoxCollider>();
    _patrolArea = _patrolAreaGO.AddComponent<PatrolArea>();
    SetPrivateField("patrolArea", _patrolArea);

    // Disabled agent is guaranteed to report not on NavMesh.
    _agent.enabled = false;

    Assert.IsFalse(_agent.isOnNavMesh, "Test precondition failed: guard should be off NavMesh before Start.");

    LogAssert.Expect(LogType.Error, "Guard: This guard is not on the NavMesh.");

    InvokePrivate("Start");

    Assert.IsFalse(_guardPatrol.enabled, "Start should disable component when guard is not on a NavMesh.");
  }

  [Test]
  public void Update_WhenWaitingAndDeadlineNotReached_RemainsWaiting() {
    SetPrivateField("waiting", true);
    SetPrivateField("waitUntilTime", Time.time + 10f);

    InvokePrivate("Update");

    var waiting = GetPrivateField<bool>("waiting");
    Assert.IsTrue(waiting, "Update should keep waiting state until waitUntilTime is reached.");
  }

  private void SetPrivateField(string fieldName, object value) {
    typeof(GuardPatrol)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_guardPatrol, value);
  }

  private T GetPrivateField<T>(string fieldName) {
    return (T)typeof(GuardPatrol)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_guardPatrol);
  }

  private void InvokePrivate(string methodName) {
    typeof(GuardPatrol)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_guardPatrol, null);
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
      Quaternion.identity);

    Assert.IsNotNull(navMeshData, "Expected NavMeshBuilder to create test NavMeshData.");
    _navMeshDataInstance = NavMesh.AddNavMeshData(navMeshData);
  }
}
