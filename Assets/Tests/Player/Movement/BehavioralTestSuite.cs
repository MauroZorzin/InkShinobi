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
    _cameraGO = new GameObject("Main Camera") {
      tag = "MainCamera"
    };
    _cameraGO.transform.SetParent(_cameraPivotGO.transform, false);
    _cameraGO.AddComponent<Camera>();

    _playerGO = new GameObject("Player");
    _playerGO.AddComponent<CharacterController>();
    _playerGO.AddComponent<SpriteRenderer>();
    _playerGO.AddComponent<Animator>();
    _movementController = _playerGO.AddComponent<PlayerMovementController>();
    _turner = _playerGO.GetComponent<RightAngleWallTurner>();
    if (_turner == null) {
      _turner = _playerGO.AddComponent<RightAngleWallTurner>();
    }
    InvokeAwake();
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
    _turner.movementController = null;
    InvokeAwake();

    Assert.AreSame(_movementController, _turner.movementController,
      "Awake should auto-wire the PlayerMovementController on the same GameObject.");
  }

  [Test]
  public void Awake_AssignsCameraMainParentAsPivot_WhenReferenceMissing() {
    _turner.camPivot = null;
    InvokeAwake();

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

  [Test]
  public void InnerCornerResolution_CarriesHitColliderNormalAndContact() {
    var fixture = new MovementTestFixture();
    try {
      var wall = fixture.CreateWall("InnerTargetWall", new Vector3(1f, 0.6f, 0f), new Vector3(0.1f, 2f, 2f));
      Assert.IsTrue(Physics.Raycast(fixture.PlayerGO.transform.position, Vector3.right, out RaycastHit hit, 2f, 1 << MovementTestFixture.WallLayer));

      object[] args = { true, hit, true, Vector3.zero, Vector3.zero, false, null };
      var resolved = (bool)MovementTestFixture.InvokePrivate(fixture.Turner, "TryGetInnerCornerFromSideHit", args);

      Assert.IsTrue(resolved);
      Assert.AreEqual(Vector3.left, (Vector3)args[3]);
      Assert.AreEqual(hit.point, (Vector3)args[4]);
      Assert.IsTrue((bool)args[5]);
      Assert.AreSame(wall.GetComponent<Collider>(), args[6]);
    } finally {
      fixture.Dispose();
    }
  }

  [Test]
  public void OuterTargetResolution_CarriesResolvedWallCollider() {
    var fixture = new MovementTestFixture();
    try {
      var wall = fixture.CreateWall("OuterTargetWall", new Vector3(0.2f, 0.6f, 0f), new Vector3(0.1f, 2f, 2f));

      object[] args = { Vector3.left, fixture.PlayerGO.transform.position, Vector3.zero, Vector3.zero, Vector3.zero, null };
      var resolved = (bool)MovementTestFixture.InvokePrivate(fixture.Turner, "TryResolveOuterTargetForSide", args);

      Assert.IsTrue(resolved);
      Assert.AreEqual(Vector3.left, (Vector3)args[3]);
      Assert.AreSame(wall.GetComponent<Collider>(), args[5]);
    } finally {
      fixture.Dispose();
    }
  }

  [UnityTest]
  public IEnumerator DoCornerTurn_DisablesMovementDuringTurn_ReenablesAfterward_AndRestoresHugDistance() {
    var fixture = new MovementTestFixture();
    try {
      var wall = fixture.CreateWall("TurnTargetWall", new Vector3(-0.1f, 0.6f, 0.25f), new Vector3(0.1f, 2f, 2f));
      Assert.IsTrue(Physics.Raycast(new Vector3(1f, 0.6f, 0.25f), Vector3.left, out RaycastHit hit, 2f, 1 << MovementTestFixture.WallLayer));

      var turn = MovementTestFixture.InvokeCoroutine(
        fixture.Turner,
        "DoCornerTurn",
        Vector3.right,
        hit.point,
        Vector3.forward,
        false,
        wall.GetComponent<Collider>());

      fixture.Turner.StartCoroutine(turn);

      Assert.IsTrue(fixture.Turner.IsTurning);
      Assert.IsFalse(fixture.MovementController.enabled);

      var deadline = Time.realtimeSinceStartup + 2f;
      while (fixture.Turner.IsTurning && Time.realtimeSinceStartup < deadline) {
        yield return null;
      }

      Assert.IsFalse(fixture.Turner.IsTurning);
      Assert.IsTrue(fixture.MovementController.enabled);
      Assert.AreEqual(90f, NormalizeYaw(fixture.CameraPivotGO.transform.eulerAngles.y), 0.5f);
      Assert.AreEqual(0.2f, fixture.PlayerGO.transform.position.x, fixture.Turner.postTurnHugDistanceTolerance + 0.03f);
    } finally {
      fixture.Dispose();
    }
  }

  [Test]
  public void StrictHugCorrection_UsesKnownTargetColliderInsteadOfOppositeParallelWall() {
    var fixture = new MovementTestFixture(new Vector3(0.3f, 0.6f, 0f));
    try {
      var intendedWall = fixture.CreateWall("IntendedCorridorWall", new Vector3(0f, 0.6f, 0f), new Vector3(0.1f, 2f, 2f));
      fixture.CreateWall("OppositeCorridorWall", new Vector3(1f, 0.6f, 0f), new Vector3(0.1f, 2f, 2f));

      object[] args = {
        fixture.PlayerGO.transform.position,
        Vector3.right,
        intendedWall.GetComponent<Collider>(),
        new Vector3(0.05f, 0.6f, 0f),
        Vector3.zero
      };

      var restored = (bool)MovementTestFixture.InvokePrivate(fixture.Turner, "TryComputeHuggedPositionStrict", args);

      Assert.IsTrue(restored);
      Assert.AreEqual(0.3f, ((Vector3)args[4]).x, 0.02f);
    } finally {
      fixture.Dispose();
    }
  }

  [Test]
  public void StrictHugCorrection_RejectsBroadHitsFarFromExpectedContact_WhenNoColliderKnown() {
    var fixture = new MovementTestFixture(new Vector3(0.3f, 0.6f, 0f));
    try {
      fixture.CreateWall("FarParallelWall", new Vector3(1f, 0.6f, 0f), new Vector3(0.1f, 2f, 2f));

      object[] args = {
        fixture.PlayerGO.transform.position,
        Vector3.right,
        null,
        new Vector3(0.05f, 0.6f, 0f),
        Vector3.zero
      };

      var restored = (bool)MovementTestFixture.InvokePrivate(fixture.Turner, "TryComputeHuggedPositionStrict", args);

      Assert.IsFalse(restored);
    } finally {
      fixture.Dispose();
    }
  }

  [Test]
  public void RestoreHugDistance_DoesNotStepPlayer_WhenConstrainedWallCannotBeReacquired() {
    var fixture = new MovementTestFixture(new Vector3(0.3f, 0.6f, 0f));
    try {
      var startPosition = fixture.PlayerGO.transform.position;

      object[] args = { Vector3.right, null, new Vector3(0.05f, 0.6f, 0f) };
      var restored = (bool)MovementTestFixture.InvokePrivate(fixture.Turner, "RestoreHugDistance", args);

      Assert.IsFalse(restored);
      Assert.AreEqual(startPosition, fixture.PlayerGO.transform.position);
    } finally {
      fixture.Dispose();
    }
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

  private void InvokeAwake() {
    typeof(RightAngleWallTurner)
      .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_turner, null);
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

  private static float NormalizeYaw(float yaw) {
    yaw %= 360f;
    return yaw < 0f ? yaw + 360f : yaw;
  }
}

internal sealed class MovementTestFixture {
  public const int WallLayer = 8;

  private readonly List<GameObject> _createdObjects = new();
  private readonly List<GameObject> _retaggedMainCameras = new();

  public GameObject PlayerGO { get; }
  public GameObject CameraPivotGO { get; }
  public GameObject CameraGO { get; }
  public CharacterController CharacterController { get; }
  public PlayerMovementController MovementController { get; }
  public RightAngleWallTurner Turner { get; }
  public WallSwitcher Switcher { get; }

  public MovementTestFixture(Vector3? playerPosition = null) {
    RetagExistingMainCameras();

    CameraPivotGO = new GameObject("TestCameraPivot");
    _createdObjects.Add(CameraPivotGO);

    CameraGO = new GameObject("Main Camera") {
      tag = "MainCamera"
    };
    CameraGO.transform.SetParent(CameraPivotGO.transform, false);
    CameraGO.AddComponent<Camera>();
    _createdObjects.Add(CameraGO);

    PlayerGO = new GameObject("Player");
    PlayerGO.transform.position = playerPosition ?? new Vector3(0f, 0.6f, 0f);
    _createdObjects.Add(PlayerGO);

    CharacterController = PlayerGO.AddComponent<CharacterController>();
    CharacterController.height = 1f;
    CharacterController.radius = 0.1f;
    CharacterController.center = Vector3.zero;

    PlayerGO.AddComponent<SpriteRenderer>();
    PlayerGO.AddComponent<Animator>();

    MovementController = PlayerGO.AddComponent<PlayerMovementController>();
    Turner = PlayerGO.GetComponent<RightAngleWallTurner>();
    if (Turner == null) {
      Turner = PlayerGO.AddComponent<RightAngleWallTurner>();
    }
    Switcher = PlayerGO.GetComponent<WallSwitcher>();
    if (Switcher == null) {
      Switcher = PlayerGO.AddComponent<WallSwitcher>();
    }

    MovementController.camPivot = CameraPivotGO.transform;
    MovementController.moveSpeed = 4f;
    MovementController.acceleration = 100f;
    MovementController.deceleration = 100f;
    MovementController.gravity = 0f;
    MovementController.rotationDuration = 0.05f;

    Turner.camPivot = CameraPivotGO.transform;
    Turner.movementController = MovementController;
    Turner.wallSwitcher = Switcher;
    Turner.wallLayer = 1 << WallLayer;
    Turner.passagewayLayer = 0;
    Turner.cameraTurnDuration = 0.05f;
    Turner.postTurnCorrectionFrames = 4;
    Turner.postTurnBacktrackStep = 0.05f;
    Turner.postTurnBacktrackMaxDistance = 0.5f;
    Turner.postTurnWallSearchDistance = 2.5f;
    Turner.wallHugDistance = 0.25f;
    Turner.targetContactTolerance = 0.25f;
    Turner.logRayHits = false;
    Turner.drawRayGizmos = false;

    Switcher.camPivot = CameraPivotGO.transform;
    Switcher.movementController = MovementController;
    Switcher.rightAngleWallTurner = Turner;
    Switcher.wallLayer = 1 << WallLayer;
    Switcher.frontRayLength = 2f;
    Switcher.frontRayCenterOffset = 0.2f;
    Switcher.firstNinetyRotationDuration = 0.05f;
    Switcher.switchObservationDuration = 0.05f;
    Switcher.finalNinetyRotationDuration = 0.05f;
    Switcher.wallHugDistance = 0.25f;
    Switcher.logRayHits = false;
    Switcher.drawDebugGizmos = false;
  }

  public GameObject CreateWall(string name, Vector3 position, Vector3 scale) {
    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
    wall.name = name;
    wall.layer = WallLayer;
    wall.transform.position = position;
    wall.transform.localScale = scale;
    _createdObjects.Add(wall);
    Physics.SyncTransforms();
    return wall;
  }

  public void Dispose() {
    for (var i = _createdObjects.Count - 1; i >= 0; i--) {
      if (_createdObjects[i] != null) {
        Object.DestroyImmediate(_createdObjects[i]);
      }
    }

    _createdObjects.Clear();
    RestoreRetaggedMainCameras();
  }

  public static void SetPrivateField<TTarget>(TTarget target, string fieldName, object value) {
    typeof(TTarget)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(target, value);
  }

  public static TField GetPrivateField<TTarget, TField>(TTarget target, string fieldName) {
    return (TField)typeof(TTarget)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(target);
  }

  public static IEnumerator InvokeCoroutine<TTarget>(TTarget target, string methodName, params object[] args) {
    return (IEnumerator)typeof(TTarget)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(target, args);
  }

  public static object InvokePrivate<TTarget>(TTarget target, string methodName, params object[] args) {
    return typeof(TTarget)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(target, args);
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

public class WallSwitcherTestSuite {
  private MovementTestFixture _fixture;

  [SetUp]
  public void Setup() {
    _fixture = new MovementTestFixture();
  }

  [TearDown]
  public void TearDown() {
    _fixture?.Dispose();
  }

  [Test]
  public void Awake_AssignsLocalReferences_AndCameraPivot() {
    Assert.AreSame(_fixture.MovementController, _fixture.Switcher.movementController);
    Assert.AreSame(_fixture.Turner, _fixture.Switcher.rightAngleWallTurner);
    Assert.AreSame(_fixture.CameraPivotGO.transform, _fixture.Switcher.camPivot);
  }

  [Test]
  public void RequestSwitch_ReturnsFalse_WhenNoFrontWallExists() {
    Assert.IsFalse(_fixture.Switcher.RequestSwitch());
    Assert.IsFalse(_fixture.Switcher.IsSwitching);
  }

  [Test]
  public void RequestSwitch_ReturnsFalse_WhenOnlyOneFrontRayHits() {
    _fixture.CreateWall("SingleRayWall", new Vector3(-0.2f, 0.6f, -1f), new Vector3(0.08f, 2f, 0.1f));

    Assert.IsFalse(_fixture.Switcher.RequestSwitch());
    Assert.IsFalse(_fixture.Switcher.IsSwitching);
  }

  [UnityTest]
  public IEnumerator RequestSwitch_DisablesDependenciesDuringSwitch_ThenRestoresThem() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));

    Assert.IsTrue(_fixture.Switcher.RequestSwitch());
    Assert.IsTrue(_fixture.Switcher.IsSwitching);
    Assert.IsFalse(_fixture.MovementController.enabled);
    Assert.IsFalse(_fixture.Turner.enabled);

    yield return WaitUntilSwitchEnds();

    Assert.IsFalse(_fixture.Switcher.IsSwitching);
    Assert.IsTrue(_fixture.MovementController.enabled);
    Assert.IsTrue(_fixture.Turner.enabled);
  }

  [UnityTest]
  public IEnumerator RequestSwitch_PlacesPlayerAtTargetHugDistance_AndRotatesCamera180Degrees() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));
    var startY = _fixture.PlayerGO.transform.position.y;

    Assert.IsTrue(_fixture.Switcher.RequestSwitch());
    yield return WaitUntilSwitchEnds();

    Assert.AreEqual(-0.7f, _fixture.PlayerGO.transform.position.z, 0.04f);
    Assert.AreEqual(0f, _fixture.PlayerGO.transform.position.x, 0.04f);
    Assert.AreEqual(startY, _fixture.PlayerGO.transform.position.y, 0.001f);
    Assert.AreEqual(180f, NormalizeYaw(_fixture.CameraPivotGO.transform.eulerAngles.y), 0.5f);
  }

  [UnityTest]
  public IEnumerator RequestSwitch_ReturnsFalse_WhileSwitchAlreadyRunning() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));

    Assert.IsTrue(_fixture.Switcher.RequestSwitch());
    Assert.IsFalse(_fixture.Switcher.RequestSwitch());

    yield return WaitUntilSwitchEnds();
  }

  [Test]
  public void RequestSwitch_ReturnsFalse_WhenMovementControllerIsRotating() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));
    MovementTestFixture.SetPrivateField(_fixture.MovementController, "_isRotating", true);

    Assert.IsFalse(_fixture.Switcher.RequestSwitch());
  }

  [Test]
  public void RequestSwitch_ReturnsFalse_WhenRightAngleTurnerIsTurning() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));
    MovementTestFixture.SetPrivateField(_fixture.Turner, "_isTurning", true);

    Assert.IsFalse(_fixture.Switcher.RequestSwitch());
  }

  [UnityTest]
  public IEnumerator RequestSwitch_NotifiesTurnerWithTargetWallNormal() {
    _fixture.CreateWall("FrontWall", new Vector3(0f, 0.6f, -1f), new Vector3(2f, 2f, 0.1f));

    Assert.IsTrue(_fixture.Switcher.RequestSwitch());
    yield return WaitUntilSwitchEnds();

    Assert.IsTrue(MovementTestFixture.GetPrivateField<RightAngleWallTurner, bool>(_fixture.Turner, "_hasCachedWall"));
    Assert.AreEqual(Vector3.forward, MovementTestFixture.GetPrivateField<RightAngleWallTurner, Vector3>(_fixture.Turner, "_cachedWallNormal"));
    Assert.IsTrue(MovementTestFixture.GetPrivateField<RightAngleWallTurner, bool>(_fixture.Turner, "_awaitingPostSwitchInputDecision"));
  }

  private IEnumerator WaitUntilSwitchEnds() {
    var deadline = Time.realtimeSinceStartup + 2f;

    while (_fixture.Switcher.IsSwitching && Time.realtimeSinceStartup < deadline) {
      yield return null;
    }

    Assert.IsFalse(_fixture.Switcher.IsSwitching, "Switch should finish within the deterministic test timeout.");
  }

  private static float NormalizeYaw(float yaw) {
    yaw %= 360f;
    return yaw < 0f ? yaw + 360f : yaw;
  }
}

public class RightAngleWallTurnerLapTestSuite {
  private const string ProtoSceneName = "ProtoScene";
  private const string ProtoScenePath = "Assets/Scenes/ProtoScene.unity";
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
#if UNITY_EDITOR
    if (sceneName == ProtoSceneName) {
      Scene scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
        ProtoScenePath,
        new LoadSceneParameters(LoadSceneMode.Single)
      );

      Assert.IsTrue(scene.IsValid(), $"Failed to load scene asset '{ProtoScenePath}'.");
      yield return null;
      yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
      yield break;
    }
#endif

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

public class WallSwitcherProtoSceneTestSuite {
  private const string ProtoSceneName = "ProtoScene";
  private const string ProtoScenePath = "Assets/Scenes/ProtoScene.unity";
  private const float SceneLoadTimeoutSeconds = 5f;

  private readonly List<GameObject> _createdWalls = new();

  private GameObject _playerGO;
  private CharacterController _characterController;
  private PlayerMovementController _movementController;
  private RightAngleWallTurner _turner;
  private WallSwitcher _switcher;

  [UnitySetUp]
  public IEnumerator Setup() {
    yield return LoadScene(ProtoSceneName);

    _playerGO = GameObject.Find("Player");
    Assert.IsNotNull(_playerGO, "ProtoScene should contain a root Player object.");

    _characterController = _playerGO.GetComponent<CharacterController>();
    _movementController = _playerGO.GetComponent<PlayerMovementController>();
    _turner = _playerGO.GetComponent<RightAngleWallTurner>();
    _switcher = _playerGO.GetComponent<WallSwitcher>();

    Assert.IsNotNull(_characterController, "ProtoScene Player should have a CharacterController.");
    Assert.IsNotNull(_movementController, "ProtoScene Player should have a PlayerMovementController.");
    Assert.IsNotNull(_turner, "ProtoScene Player should have a RightAngleWallTurner.");
    Assert.IsNotNull(_switcher, "ProtoScene Player should have a WallSwitcher.");

    _movementController.gravity = 0f;
    _turner.drawRayGizmos = false;
    _turner.logRayHits = false;
    _switcher.drawDebugGizmos = false;
    _switcher.logRayHits = false;
  }

  [TearDown]
  public void TearDown() {
    foreach (GameObject wall in _createdWalls) {
      if (wall != null) {
        Object.DestroyImmediate(wall);
      }
    }

    _createdWalls.Clear();
  }

  [UnityTest]
  public IEnumerator ProtoScenePlayer_CanSwitchWall_ThenSwitchBack() {
    var startPosition = new Vector3(50f, 0.6f, -0.3f);
    var farWall = CreateSceneWall("ProtoSceneSwitchFarWall", new Vector3(50f, 0.6f, -1f));
    CreateSceneWall("ProtoSceneSwitchNearWall", new Vector3(50f, 0.6f, 0f));

    _switcher.wallLayer = 1 << MovementTestFixture.WallLayer;
    _turner.wallLayer = 1 << MovementTestFixture.WallLayer;
    _switcher.firstNinetyRotationDuration = 0.05f;
    _switcher.switchObservationDuration = 0.05f;
    _switcher.finalNinetyRotationDuration = 0.05f;
    _switcher.wallHugDistance = 0.25f;

    SetPlayerPosition(startPosition);
    _movementController.camPivot.eulerAngles = Vector3.zero;
    _switcher.camPivot = _movementController.camPivot;
    _turner.camPivot = _movementController.camPivot;
    Physics.SyncTransforms();

    Assert.IsTrue(_switcher.RequestSwitch(), "The positioned ProtoScene player should be able to switch to the front test wall.");
    yield return WaitUntilSwitchEnds();

    var switchedPosition = _playerGO.transform.position;
    Assert.AreEqual(farWall.transform.position.z + 0.05f + _switcher.wallHugDistance, switchedPosition.z, 0.06f);
    Assert.AreEqual(180f, NormalizeYaw(_movementController.camPivot.eulerAngles.y), 0.5f);

    yield return new WaitForSeconds(1.05f);

    Assert.IsTrue(_switcher.RequestSwitch(), "After cooldown, the ProtoScene player should be able to switch back to the opposite test wall.");
    yield return WaitUntilSwitchEnds();

    Assert.AreEqual(startPosition.z, _playerGO.transform.position.z, 0.08f);
    Assert.AreEqual(0f, Mathf.DeltaAngle(0f, _movementController.camPivot.eulerAngles.y), 0.5f);
  }

  private GameObject CreateSceneWall(string name, Vector3 position) {
    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
    wall.name = name;
    wall.layer = MovementTestFixture.WallLayer;
    wall.transform.position = position;
    wall.transform.localScale = new Vector3(4f, 2f, 0.1f);
    _createdWalls.Add(wall);
    Physics.SyncTransforms();
    return wall;
  }

  private void SetPlayerPosition(Vector3 position) {
    var wasEnabled = _characterController.enabled;
    _characterController.enabled = false;
    _playerGO.transform.position = position;
    _characterController.enabled = wasEnabled;
  }

  private IEnumerator WaitUntilSwitchEnds() {
    var deadline = Time.realtimeSinceStartup + 2f;

    while (_switcher.IsSwitching && Time.realtimeSinceStartup < deadline) {
      yield return null;
    }

    Assert.IsFalse(_switcher.IsSwitching, "Switch should finish within the deterministic test timeout.");
  }

  private static IEnumerator LoadScene(string sceneName) {
#if UNITY_EDITOR
    if (sceneName == ProtoSceneName) {
      Scene scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
        ProtoScenePath,
        new LoadSceneParameters(LoadSceneMode.Single)
      );

      Assert.IsTrue(scene.IsValid(), $"Failed to load scene asset '{ProtoScenePath}'.");
      yield return null;
      yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
      yield break;
    }
#endif

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

    Assert.Fail($"Timed out waiting {timeoutSeconds:0.##} seconds for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}.");
  }

  private static float NormalizeYaw(float yaw) {
    yaw %= 360f;
    return yaw < 0f ? yaw + 360f : yaw;
  }
}
