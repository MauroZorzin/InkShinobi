using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class RightAngleWallTurnerTestSuite {
  private GameObject _cameraPivotGO;
  private GameObject _cameraGO;
  private GameObject _playerGO;
  private PlayerMovementController _movementController;
  private RightAngleWallTurner _turner;

  [SetUp]
  public void Setup() {
    _cameraPivotGO = new GameObject("TestCameraPivot");
    _cameraGO = new GameObject("Main Camera");
    _cameraGO.tag = "MainCamera";
    _cameraGO.transform.SetParent(_cameraPivotGO.transform, false);
    _cameraGO.AddComponent<Camera>();

    _playerGO = new GameObject("Player");
    _playerGO.AddComponent<CharacterController>();
    _playerGO.AddComponent<SpriteRenderer>();
    _playerGO.AddComponent<Animator>();
    _movementController = _playerGO.AddComponent<PlayerMovementController>();
    _turner = _playerGO.AddComponent<RightAngleWallTurner>();
    _turner.logRayHits = false;
    _turner.drawRayGizmos = false;
  }

  [TearDown]
  public void TearDown() {
    if (_playerGO != null) {
      Object.DestroyImmediate(_playerGO);
    }

    if (_cameraPivotGO != null) {
      Object.DestroyImmediate(_cameraPivotGO);
    }
  }

  [Test]
  public void Awake_AssignsLocalMovementController_WhenReferenceMissing() {
    Assert.AreSame(_movementController, _turner.movementController,
      "Awake should auto-wire the PlayerMovementController on the same GameObject.");
  }

  [Test]
  public void Awake_AssignsCameraMainParentAsPivot_WhenReferenceMissing() {
    Assert.That(_turner.camPivot, Is.EqualTo(_cameraPivotGO.transform),
      "Awake should default camPivot to the parent of the main camera.");
  }

  [Test]
  public void NotifyWallSwitchCompleted_ResetsTurnState_AndCachesFlattenedNormal() {
    SetPrivateField("_isTurning", true);
    SetPrivateField("_movementInputLocked", true);
    SetPrivateField("_moveInput", 1f);
    SetPrivateField("_awaitingPostTurnReady", true);
    SetPrivateField("_awaitingPostSwitchInputDecision", false);

    _turner.NotifyWallSwitchCompleted(new Vector3(2f, 5f, 0f));

    Assert.IsFalse(_turner.IsTurning, "Turn completion should clear the turning flag.");
    Assert.IsFalse(GetPrivateField<bool>("_movementInputLocked"), "Turn completion should unlock movement input.");
    Assert.AreEqual(0f, GetPrivateField<float>("_moveInput"), 0.0001f, "Turn completion should clear cached move input.");
    Assert.IsFalse(GetPrivateField<bool>("_awaitingPostTurnReady"), "Turn completion should leave post-turn ready state.");
    Assert.IsTrue(GetPrivateField<bool>("_awaitingPostSwitchInputDecision"), "Turn completion should wait for the next post-switch input decision.");
    Assert.IsTrue(GetPrivateField<bool>("_hasCachedWall"), "A horizontal wall normal should be cached for future wall detection.");
    Assert.AreEqual(Vector3.right, GetPrivateField<Vector3>("_cachedWallNormal"), "The cached wall normal should be flattened and normalized.");
  }

  [Test]
  public void NotifyWallSwitchCompleted_ClearsCachedWall_WhenNormalHasNoPlanarComponent() {
    SetPrivateField("_hasCachedWall", true);
    SetPrivateField("_cachedWallNormal", Vector3.right);

    _turner.NotifyWallSwitchCompleted(Vector3.up);

    Assert.IsFalse(GetPrivateField<bool>("_hasCachedWall"), "A vertical-only normal should not remain cached as a wall.");
  }

  private void SetPrivateField(string fieldName, object value) {
    typeof(RightAngleWallTurner)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_turner, value);
  }

  private T GetPrivateField<T>(string fieldName) {
    return (T)typeof(RightAngleWallTurner)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_turner);
  }
}