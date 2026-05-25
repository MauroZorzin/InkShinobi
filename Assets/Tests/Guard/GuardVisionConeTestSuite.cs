using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GuardVisionConeTestSuite {
  private const int PlayerLayer = 8;
  private const int ObstacleLayer = 9;

  private GameObject _guardObject;
  private GameObject _playerObject;
  private GuardVisionCone _visionCone;
  private PlayerStealthController _playerStealth;
  private readonly List<GameObject> _extraObjects = new();

  [SetUp]
  public void SetUp() {
    _guardObject = new GameObject("VisionGuard");
    _guardObject.transform.position = Vector3.zero;
    _guardObject.transform.forward = Vector3.forward;

    _visionCone = _guardObject.AddComponent<GuardVisionCone>();
    _visionCone.eyeHeight = 0f;
    _visionCone.playerAimHeight = 0f;
    _visionCone.shortRange = 5f;
    _visionCone.shortAngle = 90f;
    _visionCone.longRange = 12f;
    _visionCone.longAngle = 40f;
    _visionCone.detectionTime = 0f;
    _visionCone.playerLayerMask = 1 << PlayerLayer;
    _visionCone.obstacleMask = 0;
    _visionCone.showGizmos = false;
    _visionCone.showRuntimeRay = false;
    _visionCone.verboseLogging = false;

    _playerObject = new GameObject("TestPlayer");
    _playerObject.layer = PlayerLayer;
    _playerObject.transform.position = new Vector3(0f, 0f, 3f);
    _playerObject.AddComponent<CapsuleCollider>();
    _playerObject.AddComponent<TakedownController>();
    _playerStealth = _playerObject.AddComponent<PlayerStealthController>();

    Physics.SyncTransforms();
  }

  [TearDown]
  public void TearDown() {
    if (_playerObject != null) {
      UnityEngine.Object.DestroyImmediate(_playerObject);
    }

    if (_guardObject != null) {
      UnityEngine.Object.DestroyImmediate(_guardObject);
    }

    foreach (GameObject extraObject in _extraObjects) {
      if (extraObject != null) {
        UnityEngine.Object.DestroyImmediate(extraObject);
      }
    }

    _extraObjects.Clear();
  }

  [Test]
  public void ScanForPlayer_DetectsPlayerInsideShortCone() {
    ScanForPlayer();

    Assert.IsTrue(_visionCone.PlayerDetected);
    Assert.AreSame(_playerStealth, _visionCone.DetectedPlayer);
    Assert.AreEqual(1, _playerStealth.DetectingGuardCount);
    Assert.AreEqual(1f, _visionCone.DetectionProgress);
  }

  [Test]
  public void ScanForPlayer_DoesNotDetectPlayerOutsideCone() {
    _playerObject.transform.position = new Vector3(4f, 0f, 0f);
    Physics.SyncTransforms();

    ScanForPlayer();

    Assert.IsFalse(_visionCone.PlayerDetected);
    Assert.IsNull(_visionCone.DetectedPlayer);
    Assert.AreEqual(0, _playerStealth.DetectingGuardCount);
  }

  [Test]
  public void ScanForPlayer_DetectsLitPlayerInsideLongCone() {
    GameObject lightZoneObject = new("LightZone");
    _extraObjects.Add(lightZoneObject);
    _playerObject.transform.position = new Vector3(0f, 0f, 9f);
    _playerStealth.EnterLight(lightZoneObject.AddComponent<LightZone>());
    Physics.SyncTransforms();

    ScanForPlayer();

    Assert.IsTrue(_visionCone.PlayerDetected);
  }

  [Test]
  public void ScanForPlayer_DoesNotDetectUnlitPlayerOutsideShortCone() {
    _playerObject.transform.position = new Vector3(0f, 0f, 9f);
    Physics.SyncTransforms();

    ScanForPlayer();

    Assert.IsFalse(_visionCone.PlayerDetected);
  }

  [Test]
  public void ScanForPlayer_DoesNotDetectWhenObstacleBlocksLineOfSight() {
    GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
    _extraObjects.Add(obstacle);
    obstacle.name = "VisionObstacle";
    obstacle.layer = ObstacleLayer;
    obstacle.transform.position = new Vector3(0f, 0f, 1.5f);
    obstacle.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
    _visionCone.obstacleMask = 1 << ObstacleLayer;
    Physics.SyncTransforms();

    ScanForPlayer();

    Assert.IsFalse(_visionCone.PlayerDetected);
  }

  private void ScanForPlayer() {
    typeof(GuardVisionCone)
      .GetMethod("ScanForPlayer", BindingFlags.Instance | BindingFlags.NonPublic)
      .Invoke(_visionCone, null);
  }
}
