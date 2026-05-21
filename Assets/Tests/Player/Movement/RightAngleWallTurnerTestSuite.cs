using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class RightAngleWallTurnerTestSuite {
  private readonly List<GameObject> _retaggedMainCameras = new();
  private GameObject _cameraPivotGO;
  private GameObject _cameraGO;
  private GameObject _playerGO;
  private PlayerMovementController _movementController;
  private RightAngleWallTurner _turner;

  [SetUp]
  public void Setup() {
    RetagExistingMainCameras();

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

    RestoreRetaggedMainCameras();
  }

  [Test]
  public void Awake_AssignsLocalMovementController_WhenReferenceMissing() {
    Assert.AreSame(_movementController, _turner.movementController,
      "Awake should auto-wire the PlayerMovementController on the same GameObject.");
  }

  [Test]
  public void Awake_AssignsCameraMainParentAsPivot_WhenReferenceMissing() {
    Assert.IsTrue(_turner.camPivot == _cameraPivotGO.transform,
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

  private void RetagExistingMainCameras() {
    _retaggedMainCameras.Clear();

    foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
      if (!camera.CompareTag("MainCamera")) {
        continue;
      }

      _retaggedMainCameras.Add(camera.gameObject);
      camera.tag = "Untagged";
    }
  }

  private void RestoreRetaggedMainCameras() {
    foreach (GameObject cameraGO in _retaggedMainCameras) {
      if (cameraGO != null) {
        cameraGO.tag = "MainCamera";
      }
    }

    _retaggedMainCameras.Clear();
  }
}

public class RightAngleWallTurnerLapTestSuite {
  private const string ProtoSceneName = "ProtoScene";
  private const float SceneLoadTimeoutSeconds = 5f;
  private const float LapTimeoutSeconds = 20f;
  private const float ReturnTolerance = 0.85f;
  private const float MustLeaveStartDistance = 2f;
  private const float MaxStallSeconds = 4f;
  private const float ProgressDistance = 0.1f;

  private readonly List<string> _observedTurnKinds = new();

  private GameObject _playerGO;
  private CharacterController _characterController;
  private PlayerMovementController _movementController;
  private RightAngleWallTurner _turner;
  private Vector3 _startPosition;
  private Vector3 _lastProgressPosition;
  private float _lastProgressTime;

  [UnitySetUp]
  public IEnumerator Setup() {
    _observedTurnKinds.Clear();
    Application.logMessageReceived += OnLogMessageReceived;

    yield return LoadScene(ProtoSceneName);

    _playerGO = GameObject.Find("Player");
    Assert.IsNotNull(_playerGO, "ProtoScene should contain a root Player object.");

    _characterController = _playerGO.GetComponent<CharacterController>();
    _movementController = _playerGO.GetComponent<PlayerMovementController>();
    _turner = _playerGO.GetComponent<RightAngleWallTurner>();

    Assert.IsNotNull(_characterController, "ProtoScene Player should have a CharacterController.");
    Assert.IsNotNull(_movementController, "ProtoScene Player should have a PlayerMovementController.");
    Assert.IsNotNull(_turner, "ProtoScene Player should have a RightAngleWallTurner.");

    _turner.drawRayGizmos = false;
    _turner.logRayHits = true;

    _startPosition = _playerGO.transform.position;
    _lastProgressPosition = _startPosition;
    _lastProgressTime = Time.realtimeSinceStartup;

    yield return null;
  }

  [TearDown]
  public void TearDown() {
    SetMoveInput(0f);
    Application.logMessageReceived -= OnLogMessageReceived;
  }

  [UnityTest]
  public IEnumerator LeftOnlyLap_ReachesNearStart_AfterInnerAndOuterCorners() {
    var deadline = Time.realtimeSinceStartup + LapTimeoutSeconds;
    var leftStartArea = false;

    SetMoveInput(-1f);

    while (Time.realtimeSinceStartup < deadline) {
      Vector3 currentPosition = _playerGO.transform.position;
      var currentDistance = HorizontalDistance(currentPosition, _startPosition);

      if (!leftStartArea && currentDistance >= MustLeaveStartDistance) {
        leftStartArea = true;
      }

      if (_turner.IsTurning || HorizontalDistance(currentPosition, _lastProgressPosition) >= ProgressDistance) {
        _lastProgressPosition = currentPosition;
        _lastProgressTime = Time.realtimeSinceStartup;
      }

      if (Time.realtimeSinceStartup >= _lastProgressTime + MaxStallSeconds) {
        Assert.Fail($"Player stalled during the ProtoScene lap. Position={currentPosition}, start={_startPosition}, observedTurns=[{string.Join(", ", _observedTurnKinds)}].");
      }

      if (leftStartArea && currentDistance <= ReturnTolerance && HasObservedTurnKind("inner") && HasObservedTurnKind("outer")) {
        SetMoveInput(0f);
        Assert.IsFalse(_turner.IsTurning, "Turner should not still be mid-turn when the lap closes near the start position.");
        yield break;
      }

      yield return null;
    }

    SetMoveInput(0f);
    Assert.Fail($"Timed out completing a left-only lap in ProtoScene. Start={_startPosition}, end={_playerGO.transform.position}, observedTurns=[{string.Join(", ", _observedTurnKinds)}].");
  }

  private void OnLogMessageReceived(string condition, string stackTrace, LogType type) {
    if (type != LogType.Log || !condition.Contains("[RightAngleWallTurner] Turn triggered kind=")) {
      return;
    }

    if (condition.Contains("kind=inner")) {
      _observedTurnKinds.Add("inner");
    }

    if (condition.Contains("kind=outer")) {
      _observedTurnKinds.Add("outer");
    }
  }

  private bool HasObservedTurnKind(string turnKind) {
    return _observedTurnKinds.Contains(turnKind);
  }

  private void SetMoveInput(float value) {
    if (_movementController == null) {
      return;
    }

    typeof(PlayerMovementController)
      .GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_movementController, value);
  }

  private static float HorizontalDistance(Vector3 a, Vector3 b) {
    a.y = 0f;
    b.y = 0f;
    return Vector3.Distance(a, b);
  }

  private static IEnumerator LoadScene(string sceneName) {
    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

    Assert.IsNotNull(loadOperation, $"Failed to start loading scene '{sceneName}'.");

    yield return loadOperation;
    yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
  }

  private static IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds) {
    var deadline = Time.realtimeSinceStartup + timeoutSeconds;

    while (Time.realtimeSinceStartup < deadline) {
      if (SceneManager.GetActiveScene().name == sceneName) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting {timeoutSeconds:0.##} seconds for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'.");
  }
}