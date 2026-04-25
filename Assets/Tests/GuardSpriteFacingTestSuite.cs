using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public class GuardSpriteFacingTestSuite {
  private GameObject _guardGO;
  private GameObject _cameraGO;
  private GameObject _spriteVisualGO;
  private GameObject _navMeshFloorGO;
  private NavMeshDataInstance _navMeshDataInstance;

  private GuardSpriteFacing _guardSpriteFacing;
  private Camera _camera;
  private SpriteRenderer _spriteRenderer;

  private Sprite[] _frontFrames;
  private Sprite[] _backFrames;
  private Sprite[] _leftFrames;
  private Sprite[] _rightFrames;

  private Sprite _frontA;
  private Sprite _backA;
  private Sprite _leftA;
  private Sprite _leftB;
  private Sprite _rightA;
  private Sprite _rightB;
  private Sprite _rightC;

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
    _guardGO.AddComponent<NavMeshAgent>();

    _spriteVisualGO = new GameObject("SpriteVisual");
    _spriteVisualGO.transform.SetParent(_guardGO.transform);
    _spriteRenderer = _spriteVisualGO.AddComponent<SpriteRenderer>();

    _guardSpriteFacing = _guardGO.AddComponent<GuardSpriteFacing>();

    _frontA = CreateSprite("frontA");
    _backA = CreateSprite("backA");
    _leftA = CreateSprite("leftA");
    _leftB = CreateSprite("leftB");
    _rightA = CreateSprite("rightA");
    _rightB = CreateSprite("rightB");
    _rightC = CreateSprite("rightC");

    _frontFrames = new[] { _frontA };
    _backFrames = new[] { _backA };
    _leftFrames = new[] { _leftA, _leftB };
    _rightFrames = new[] { _rightA, _rightB, _rightC };

    SetPrivateField("gameCamera", _camera);
    SetPrivateField("spriteVisual", _spriteVisualGO.transform);
    SetPrivateField("spriteRenderer", _spriteRenderer);
    SetPrivateField("frontFrames", _frontFrames);
    SetPrivateField("backFrames", _backFrames);
    SetPrivateField("leftFrames", _leftFrames);
    SetPrivateField("rightFrames", _rightFrames);
    SetPrivateField("framesPerSecond", 10f);
    SetPrivateField("minimumMoveSpeed", 0.05f);
    SetPrivateField("rotateSpriteToFaceCamera", true);

    InvokePrivate("Awake");
  }

  [TearDown]
  public void TearDown() {
    DestroySprite(_frontA);
    DestroySprite(_backA);
    DestroySprite(_leftA);
    DestroySprite(_leftB);
    DestroySprite(_rightA);
    DestroySprite(_rightB);
    DestroySprite(_rightC);

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
  public void GetFramesForCameraRelativeDirection_ForwardMovement_UsesBackFrames() {
    SetPrivateField("lastMoveDirection", Vector3.forward);

    Sprite[] frames = InvokePrivateWithResult<Sprite[]>("GetFramesForCameraRelativeDirection");

    Assert.AreSame(_backFrames, frames, "Moving along camera forward should show back frames.");
  }

  [Test]
  public void GetFramesForCameraRelativeDirection_BackwardMovement_UsesFrontFrames() {
    SetPrivateField("lastMoveDirection", Vector3.back);

    Sprite[] frames = InvokePrivateWithResult<Sprite[]>("GetFramesForCameraRelativeDirection");

    Assert.AreSame(_frontFrames, frames, "Moving opposite camera forward should show front frames.");
  }

  [Test]
  public void GetFramesForCameraRelativeDirection_RightMovement_UsesRightFrames() {
    SetPrivateField("lastMoveDirection", Vector3.right);

    Sprite[] frames = InvokePrivateWithResult<Sprite[]>("GetFramesForCameraRelativeDirection");

    Assert.AreSame(_rightFrames, frames, "Moving along camera right should show right frames.");
  }

  [Test]
  public void GetFramesForCameraRelativeDirection_LeftMovement_UsesLeftFrames() {
    SetPrivateField("lastMoveDirection", Vector3.left);

    Sprite[] frames = InvokePrivateWithResult<Sprite[]>("GetFramesForCameraRelativeDirection");

    Assert.AreSame(_leftFrames, frames, "Moving opposite camera right should show left frames.");
  }

  [Test]
  public void Animate_WhenNotMoving_UsesFirstFrame() {
    SetPrivateField("frameIndex", 1);

    InvokePrivate("Animate", _leftFrames, false);

    var frameIndex = GetPrivateField<int>("frameIndex");
    Assert.AreEqual(0, frameIndex, "Idle animation should reset to the first frame.");
    Assert.AreSame(_leftA, _spriteRenderer.sprite, "Idle animation should render the first frame.");
  }

  [Test]
  public void Animate_WhenMoving_AdvancesFrameBasedOnTimer() {
    InvokePrivate("Animate", _rightFrames, false);
    SetPrivateField("frameTimer", 0.21f);

    InvokePrivate("Animate", _rightFrames, true);

    var frameIndex = GetPrivateField<int>("frameIndex");
    Assert.AreEqual(2, frameIndex, "Animation timer should advance multiple frames when enough time has accumulated.");
    Assert.AreSame(_rightC, _spriteRenderer.sprite, "Renderer should display the advanced frame.");
  }

  [Test]
  public void RotateVisualTowardCamera_MatchesCameraYaw() {
    _cameraGO.transform.rotation = Quaternion.Euler(25f, 73f, 0f);

    InvokePrivate("RotateVisualTowardCamera");

    var visualYaw = _spriteVisualGO.transform.eulerAngles.y;
    Assert.AreEqual(73f, visualYaw, 0.01f, "Sprite visual should rotate to match camera yaw.");
  }

  private static Sprite CreateSprite(string name) {
    var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    sprite.name = name;
    return sprite;
  }

  private static void DestroySprite(Sprite sprite) {
    if (sprite != null) {
      Object.Destroy(sprite);
    }
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