using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Play Mode tests for the Stealth System.
// Run via: Window > General > Test Runner > Play Mode > Run All
//
// Layers 8 (Player) and 9 (Guard) must exist in Project Settings > Tags and Layers.
public class StealthSystemTests
{
  private const int PlayerLayer = 8;
  private const int GuardLayer = 9;

  // -- Helpers -----------------------------------------------------------------

  // pos defaults to origin so stealth/light tests can call MakePlayer() with no args
  private static PlayerStealthController MakePlayer(Vector3 pos = default,
      float range = 2f, float angle = 120f)
  {
    var go = new GameObject("TestPlayer");
    go.layer = PlayerLayer;
    go.transform.position = pos;
    go.AddComponent<CapsuleCollider>();

    var p = go.AddComponent<PlayerStealthController>();
    p.takedownRange = range;
    p.takedownAngle = angle;
    p.guardLayerMask = 1 << GuardLayer;
    p.verboseLogging = false;
    return p;
  }

  private static LightZone MakeLightZone()
  {
    var go = new GameObject("TestLightZone");
    var col = go.AddComponent<BoxCollider>();
    col.isTrigger = true;
    col.enabled = false; // disabled — Enter/Exit are called directly in tests
    return go.AddComponent<LightZone>();
  }

  // Vision-cone only guard — no GuardController, no NavMeshAgent, no NavMesh needed.
  private static (GuardVisionCone cone, GameObject go)
      MakeGuardCone(Vector3 pos, Vector3 forward)
  {
    var go = new GameObject("TestGuard");
    go.layer = GuardLayer;
    go.transform.position = pos;
    go.transform.forward = forward;
    go.AddComponent<CapsuleCollider>();

    var cone = go.AddComponent<GuardVisionCone>();
    cone.playerLayerMask = 1 << PlayerLayer;
    cone.obstacleMask = 0;    // no walls
    cone.detectionTime = 0f;   // instant
    cone.shortRange = 10f;
    cone.shortAngle = 90f;
    cone.longRange = 20f;
    cone.longAngle = 60f;
    cone.eyeHeight = 0f;   // flat world
    cone.playerAimHeight = 0f;
    cone.showGizmos = false;
    cone.showRuntimeRay = false;
    cone.verboseLogging = false;

    return (cone, go);
  }

  // Minimal takedown target: collider on the guard layer + GuardController.
  // No NavMeshAgent — GuardController.SetState now guards all agent calls.
  private static GuardController MakeGuard(Vector3 pos, Vector3 forward)
  {
    var go = new GameObject("TestGuard");
    go.layer = GuardLayer;
    go.transform.position = pos;
    go.transform.forward = forward;
    go.AddComponent<CapsuleCollider>();

    // GuardController.Awake searches for GuardVisionCone; add a minimal one
    var cone = go.AddComponent<GuardVisionCone>();
    cone.playerLayerMask = 1 << PlayerLayer;
    cone.showGizmos = false;
    cone.showRuntimeRay = false;
    cone.verboseLogging = false;

    var guard = go.AddComponent<GuardController>();
    guard.enabled = false; // stop Update running — no NavMesh in test scene
    return guard;
  }

  // -- 1. Stealth State --------------------------------------------------------

  // 01 — player starts hidden with no guards around
  [UnityTest]
  public IEnumerator Stealth_StartsHidden_WithNoGuardsDetecting()
  {
    var player = MakePlayer();
    yield return null;

    Assert.IsTrue(player.IsHidden, "Player should start hidden when no guard is detecting.");

    Object.Destroy(player.gameObject);
  }

  // 02 — player re-hides after guard stops detecting and timeToHide elapses
  [UnityTest]
  public IEnumerator Stealth_BecomesHiddenAgain_AfterGuardStopsDetecting()
  {
    var player = MakePlayer();
    player.timeToHide = 0.05f;
    yield return null;

    player.OnGuardStartsDetecting();
    Assert.IsFalse(player.IsHidden, "Player must be spotted while guard detects.");

    player.OnGuardStopsDetecting();
    yield return new WaitForSeconds(0.15f);

    Assert.IsTrue(player.IsHidden, "Player should be hidden again after timeToHide elapses.");

    Object.Destroy(player.gameObject);
  }

  // -- 2. Light Zone -----------------------------------------------------------

  // 03 — entering a zone sets IsInLight, exiting clears it
  [UnityTest]
  public IEnumerator LightZone_EnterSetsInLight_ExitClearsIt()
  {
    var player = MakePlayer();
    var zone = MakeLightZone();
    yield return null;

    Assert.IsFalse(player.IsInLight, "Player should not be in light before entering zone.");

    player.EnterLight(zone);
    Assert.IsTrue(player.IsInLight, "IsInLight must be true after EnterLight.");

    player.ExitLight(zone);
    Assert.IsFalse(player.IsInLight, "IsInLight must be false after ExitLight.");

    Object.Destroy(player.gameObject);
    Object.Destroy(zone.gameObject);
  }

  // 04 — exiting a zone the player never entered must not clear the light state
  [UnityTest]
  public IEnumerator LightZone_ExitDifferentZone_DoesNotClearLight()
  {
    var player = MakePlayer();
    var zone1 = MakeLightZone();
    var zone2 = MakeLightZone();
    yield return null;

    player.EnterLight(zone1);
    player.ExitLight(zone2); // wrong zone — should be a no-op

    Assert.IsTrue(player.IsInLight, "Light state must not change when exiting a different zone.");

    Object.Destroy(player.gameObject);
    Object.Destroy(zone1.gameObject);
    Object.Destroy(zone2.gameObject);
  }

  // -- 3. Vision Cone ----------------------------------------------------------

  // 05 — player directly in front within short range is detected
  [UnityTest]
  public IEnumerator VisionCone_DetectsPlayer_DirectlyInFront()
  {
    var player = MakePlayer(new Vector3(0f, 0f, 4f));
    var (cone, guardGO) = MakeGuardCone(Vector3.zero, Vector3.forward);

    yield return null;
    yield return null; // two frames so OverlapSphere registers the collider

    Assert.IsTrue(cone.PlayerDetected, "Guard must detect player directly in front within short range.");

    Object.Destroy(player.gameObject);
    Object.Destroy(guardGO);
  }

  // 06 — player directly behind the guard is not detected
  [UnityTest]
  public IEnumerator VisionCone_DoesNotDetect_PlayerBehindGuard()
  {
    var player = MakePlayer(new Vector3(0f, 0f, -4f));
    var (cone, guardGO) = MakeGuardCone(Vector3.zero, Vector3.forward);

    yield return null;
    yield return null;

    Assert.IsFalse(cone.PlayerDetected, "Guard must NOT detect a player behind it.");

    Object.Destroy(player.gameObject);
    Object.Destroy(guardGO);
  }

  // 07 — lit player beyond short range is detected via long cone
  [UnityTest]
  public IEnumerator VisionCone_DetectsLitPlayer_BeyondShortRange_ViaLongCone()
  {
    var player = MakePlayer(new Vector3(0f, 0f, 15f)); // beyond shortRange=10
    var zone = MakeLightZone();
    player.EnterLight(zone);

    var (cone, guardGO) = MakeGuardCone(Vector3.zero, Vector3.forward);

    yield return null;
    yield return null;

    Assert.IsTrue(cone.PlayerDetected, "Guard must detect a lit player beyond short range via long cone.");

    Object.Destroy(player.gameObject);
    Object.Destroy(guardGO);
    Object.Destroy(zone.gameObject);
  }

  // 08 — unlit player beyond short range is NOT detected
  [UnityTest]
  public IEnumerator VisionCone_DoesNotDetect_UnlitPlayer_BeyondShortRange()
  {
    var player = MakePlayer(new Vector3(0f, 0f, 15f)); // beyond shortRange, no light
    var (cone, guardGO) = MakeGuardCone(Vector3.zero, Vector3.forward);

    yield return null;
    yield return null;

    Assert.IsFalse(cone.PlayerDetected, "Guard must NOT detect an unlit player beyond short range.");

    Object.Destroy(player.gameObject);
    Object.Destroy(guardGO);
  }

  // -- 4. Takedown -------------------------------------------------------------

  // 09 — takedown succeeds when player is directly behind guard and within range
  [UnityTest]
  public IEnumerator Takedown_Succeeds_WhenBehindGuardInRange()
  {
    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    var player = MakePlayer(new Vector3(0f, 0f, -0.8f)); // directly behind
    yield return null;
    yield return null;

    player.SendMessage("TryTakedown", SendMessageOptions.DontRequireReceiver);
    yield return null;

    Assert.AreEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must be TakenDown when player approaches from behind.");

    Object.Destroy(guard.gameObject);
    Object.Destroy(player.gameObject);
  }

  // 10 — takedown fails when player is in front of the guard
  [UnityTest]
  public IEnumerator Takedown_Fails_WhenInFrontOfGuard()
  {
    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    var player = MakePlayer(new Vector3(0f, 0f, 0.8f)); // directly in front
    yield return null;
    yield return null;

    player.SendMessage("TryTakedown", SendMessageOptions.DontRequireReceiver);
    yield return null;

    Assert.AreNotEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must NOT be taken down when player approaches from the front.");

    Object.Destroy(guard.gameObject);
    Object.Destroy(player.gameObject);
  }

  // 11 — takedown fails when player is behind but out of range
  [UnityTest]
  public IEnumerator Takedown_Fails_WhenBehindGuardButOutOfRange()
  {
    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    var player = MakePlayer(new Vector3(0f, 0f, -5f), range: 1f); // 5m away, range=1
    yield return null;
    yield return null;

    player.SendMessage("TryTakedown", SendMessageOptions.DontRequireReceiver);
    yield return null;

    Assert.AreNotEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must NOT be taken down when player is out of takedown range.");

    Object.Destroy(guard.gameObject);
    Object.Destroy(player.gameObject);
  }
}