using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public class GuardSpriteFacingTestSuite {
  private const string GuardAnimatorControllerPath = "Assets/Animators/GuardAnimatorController.controller";

  private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
  private static readonly int FacingHash = Animator.StringToHash("Facing");

  private GameObject _guardGO;
  private GameObject _cameraGO;
  private GameObject _spriteVisualGO;
  private GameObject _navMeshFloorGO;
  private NavMeshDataInstance _navMeshDataInstance;

  private GuardSpriteFacing _guardSpriteFacing;
  private Camera _camera;
  private NavMeshAgent _agent;
  private Animator _spriteAnimator;

  [SetUp]
  public void Setup() {
    CreateTestNavMesh();

    _cameraGO = new GameObject("GameCamera");
    _camera = _cameraGO.AddComponent<Camera>();
    _cameraGO.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

    Assert.IsTrue(
      NavMesh.SamplePosition(Vector3.zero, out NavMeshHit hit, 5f, NavMesh.AllAreas),
      "Expected a sampled point on the test NavMesh."
    );

    _guardGO = new GameObject("Guard");
    _guardGO.transform.position = hit.position;
    _agent = _guardGO.AddComponent<NavMeshAgent>();

    _spriteVisualGO = new GameObject("SpriteVisual");
    _spriteVisualGO.transform.SetParent(_guardGO.transform);
    _spriteAnimator = _spriteVisualGO.AddComponent<Animator>();

    RuntimeAnimatorController guardController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GuardAnimatorControllerPath);
    Assert.IsNotNull(guardController, $"Expected animator controller at '{GuardAnimatorControllerPath}'.");
    _spriteAnimator.runtimeAnimatorController = guardController;

    _guardSpriteFacing = _guardGO.AddComponent<GuardSpriteFacing>();
    _guardSpriteFacing.enabled = false;

    SetPrivateField("gameCamera", _camera);
    SetPrivateField("spriteVisual", _spriteVisualGO.transform);
    SetPrivateField("spriteAnimator", _spriteAnimator);
    SetPrivateField("minimumMoveSpeed", 0.05f);
    SetPrivateField("idleDelay", 0.1f);
    SetPrivateField("rotateSpriteToFaceCamera", true);
  }

  [TearDown]
  public void TearDown() {
    if (_cameraGO != null) {
      Object.Destroy(_cameraGO);
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
  public void GetCameraRelativeDirection_ForwardMovement_ReturnsBack() {
    SetPrivateField("lastMoveDirection", Vector3.forward);

    var direction = GetFacingDirectionValue();

    Assert.AreEqual(1, direction, "Moving along camera forward should map to Back.");
  }

  [Test]
  public void GetCameraRelativeDirection_BackwardMovement_ReturnsFront() {
    SetPrivateField("lastMoveDirection", Vector3.back);

    var direction = GetFacingDirectionValue();

    Assert.AreEqual(0, direction, "Moving opposite camera forward should map to Front.");
  }

  [Test]
  public void GetCameraRelativeDirection_RightMovement_ReturnsRight() {
    SetPrivateField("lastMoveDirection", Vector3.right);

    var direction = GetFacingDirectionValue();

    Assert.AreEqual(3, direction, "Moving along camera right should map to Right.");
  }

  [Test]
  public void GetCameraRelativeDirection_LeftMovement_ReturnsLeft() {
    SetPrivateField("lastMoveDirection", Vector3.left);

    var direction = GetFacingDirectionValue();

    Assert.AreEqual(2, direction, "Moving opposite camera right should map to Left.");
  }

  [UnityTest]
  public IEnumerator UpdateLastMoveDirection_WhenVelocityAboveThreshold_UpdatesDirectionAndTimestamp() {
    SetPrivateField("minimumMoveSpeed", 0.05f);
    SetPrivateField("lastMoveDirection", Vector3.forward);
    SetPrivateField("lastMovingTime", -999f);

    _agent.Warp(Vector3.zero);
    _agent.isStopped = false;
    _agent.speed = 6f;
    _agent.acceleration = 100f;
    _agent.angularSpeed = 0f;

    yield return null;
    Assert.IsTrue(_agent.SetDestination(new Vector3(5f, 0f, 0f)), "Expected test agent destination to be accepted.");

    yield return WaitForAgentVelocity(0.2f, 60);

    var moved = InvokePrivateWithResult<bool>("UpdateLastMoveDirection");

    Assert.IsTrue(moved, "Velocity above threshold should be treated as moving.");
    Assert.AreEqual(Vector3.right, GetPrivateField<Vector3>("lastMoveDirection"), "lastMoveDirection should track normalized velocity.");
    Assert.GreaterOrEqual(GetPrivateField<float>("lastMovingTime"), 0f, "A moving update should refresh lastMovingTime.");
  }

  [UnityTest]
  public IEnumerator UpdateLastMoveDirection_WhenVelocityBelowThreshold_LeavesDirectionUnchanged() {
    SetPrivateField("lastMoveDirection", Vector3.left);

    _agent.Warp(Vector3.zero);
    _agent.isStopped = false;
    _agent.speed = 2f;
    _agent.acceleration = 100f;
    _agent.angularSpeed = 0f;

    yield return null;
    Assert.IsTrue(_agent.SetDestination(new Vector3(5f, 0f, 0f)), "Expected test agent destination to be accepted.");

    yield return WaitForAgentVelocity(0.2f, 60);
    var currentSpeed = _agent.velocity.magnitude;
    SetPrivateField("minimumMoveSpeed", currentSpeed + 0.5f);

    var moved = InvokePrivateWithResult<bool>("UpdateLastMoveDirection");

    Assert.IsFalse(moved, "Velocity below threshold should be treated as not moving.");
    Assert.AreEqual(Vector3.left, GetPrivateField<Vector3>("lastMoveDirection"), "lastMoveDirection should remain unchanged when not moving.");
  }

  [Test]
  public void Update_WhenWithinIdleDelay_KeepsWalkAnimationTrue() {
    SetPrivateField("idleDelay", 0.5f);
    SetPrivateField("lastMovingTime", Time.time);
    SetPrivateField("lastMoveDirection", Vector3.right);
    _agent.velocity = Vector3.zero;

    InvokePrivate("Update");

    Assert.IsTrue(_spriteAnimator.GetBool(IsMovingHash), "Recent movement should keep IsMoving true during idle delay.");
    Assert.AreEqual(3, _spriteAnimator.GetInteger(FacingHash), "Facing should reflect the last movement direction.");
  }

  [Test]
  public void Update_WhenIdleDelayElapsed_SetsWalkAnimationFalse() {
    SetPrivateField("idleDelay", 0.1f);
    SetPrivateField("lastMovingTime", Time.time - 1f);
    SetPrivateField("lastMoveDirection", Vector3.left);
    _agent.velocity = Vector3.zero;

    InvokePrivate("Update");

    Assert.IsFalse(_spriteAnimator.GetBool(IsMovingHash), "When idle delay has elapsed, IsMoving should be false.");
    Assert.AreEqual(2, _spriteAnimator.GetInteger(FacingHash), "Facing should still follow lastMoveDirection while idle.");
  }

  [Test]
  public void Update_WhenBillboardDisabled_DoesNotRotateSpriteVisual() {
    SetPrivateField("rotateSpriteToFaceCamera", false);
    _spriteVisualGO.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
    _cameraGO.transform.rotation = Quaternion.Euler(0f, 135f, 0f);

    InvokePrivate("Update");

    Assert.AreEqual(20f, _spriteVisualGO.transform.eulerAngles.y, 0.01f, "Sprite visual yaw should remain unchanged when billboard mode is disabled.");
  }

  [Test]
  public void RotateVisualTowardCamera_MatchesCameraYaw() {
    _cameraGO.transform.rotation = Quaternion.Euler(25f, 73f, 0f);

    InvokePrivate("RotateVisualTowardCamera");

    var visualYaw = _spriteVisualGO.transform.eulerAngles.y;
    Assert.AreEqual(73f, visualYaw, 0.01f, "Sprite visual should rotate to match camera yaw.");
  }

  private void SetPrivateField(string fieldName, object value) {
    typeof(GuardSpriteFacing)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_guardSpriteFacing, value);
  }

  private T GetPrivateField<T>(string fieldName) {
    return (T)typeof(GuardSpriteFacing)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_guardSpriteFacing);
  }

  private void InvokePrivate(string methodName, params object[] args) {
    typeof(GuardSpriteFacing)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_guardSpriteFacing, args);
  }

  private T InvokePrivateWithResult<T>(string methodName, params object[] args) {
    return (T)typeof(GuardSpriteFacing)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_guardSpriteFacing, args);
  }

  private int GetFacingDirectionValue() {
    var facing = InvokePrivateWithResult<object>("GetCameraRelativeDirection");
    return System.Convert.ToInt32(facing);
  }

  private IEnumerator WaitForAgentVelocity(float minimumMagnitude, int maxFrames) {
    for (var i = 0; i < maxFrames; i++) {
      yield return null;

      if (_agent != null && _agent.velocity.magnitude >= minimumMagnitude) {
        yield break;
      }
    }

    Assert.Fail($"Agent velocity did not reach {minimumMagnitude} within {maxFrames} frames.");
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
