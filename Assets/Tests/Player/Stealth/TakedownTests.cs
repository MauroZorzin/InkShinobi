using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// Play Mode tests — Takedown System
// Run via: Window > General > Test Runner > Play Mode > Run All
public class TakedownTests {
  const int PlayerLayer = 3;
  const int GuardLayer = 7;

  // -------------------------------------------------------------------------
  // Test Scene Management
  // -------------------------------------------------------------------------

  /// <summary>
  /// Creates a complete test scene with camera, ground, and NavMesh surface.
  /// Returns a reference to the root container for easy cleanup.
  /// </summary>
  static GameObject CreateTestScene() {
    Debug.Log("=== CreateTestScene START ===");

    // Root container
    var sceneRoot = new GameObject("TestScene");

    // === GROUND / NAVMESH SURFACE ===
    // Create a primitive plane with mesh for NavMesh building
    var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
    groundGo.name = "Ground";
    groundGo.transform.SetParent(sceneRoot.transform);
    groundGo.transform.position = Vector3.zero;
    groundGo.layer = LayerMask.NameToLayer("Default");

    // Scale the plane to be larger
    groundGo.transform.localScale = new Vector3(10f, 1f, 10f);
    Debug.Log($"Ground plane created at {groundGo.transform.position}, scale: {groundGo.transform.localScale}");

    // Remove the capsule collider that comes with the primitive
    Object.Destroy(groundGo.GetComponent<Collider>());

    // Build NavMesh using Unity 6 NavMeshBuilder API
    Mesh mesh = groundGo.GetComponent<MeshFilter>().sharedMesh;
    Debug.Log($"Mesh vertices: {mesh.vertices.Length}, triangles: {mesh.triangles.Length}");

    var sources = new List<NavMeshBuildSource> {
      new() {
        shape = NavMeshBuildSourceShape.Mesh,
        sourceObject = mesh,
        transform = groundGo.transform.localToWorldMatrix,
        area = 0
      }
    };

    NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByIndex(0);
    var bounds = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
    Debug.Log($"Building NavMesh with bounds: {bounds.center}, size: {bounds.size}");

    NavMeshData navMeshData = NavMeshBuilder.BuildNavMeshData(
      buildSettings,
      sources,
      bounds,
      Vector3.zero,
      Quaternion.identity);

    if (navMeshData != null) {
      var instance = NavMesh.AddNavMeshData(navMeshData);
      Debug.Log($"NavMesh added successfully: {instance.valid}");

      // Verify NavMesh is valid
      if (NavMesh.SamplePosition(Vector3.zero, out NavMeshHit hit, 5f, NavMesh.AllAreas)) {
        Debug.Log($"NavMesh sample successful at: {hit.position}");
      } else {
        Debug.LogError("NavMesh sample FAILED at Vector3.zero!");
      }
    } else {
      Debug.LogError("NavMeshData creation FAILED!");
    }

    // === CAMERA ===
    var cameraGo = new GameObject("TestCamera");
    cameraGo.transform.SetParent(sceneRoot.transform);
    cameraGo.transform.position = new Vector3(0f, 5f, 5f);
    cameraGo.transform.LookAt(Vector3.zero);
    var camera = cameraGo.AddComponent<Camera>();
    camera.enabled = true;

    Debug.Log("=== CreateTestScene END ===");
    return sceneRoot;
  }

  /// <summary>
  /// Gets a valid position on the NavMesh at the specified offset.
  /// </summary>
  static Vector3 GetNavMeshPosition(Vector3 targetPos) {
    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas)) {
      Debug.Log($"Sampled NavMesh position: {targetPos} -> {hit.position}");
      return hit.position;
    }
    Debug.LogError($"Failed to sample NavMesh at {targetPos}!");
    return targetPos;
  }

  /// <summary>
  /// Creates a test player with all necessary stealth components.
  /// Properly initializes references and settings.
  /// </summary>
  static TakedownController MakePlayer(Vector3 pos, float range = 2f, float angle = 120f) {
    // Get valid NavMesh position
    pos = GetNavMeshPosition(pos);
    Debug.Log($"[MakePlayer] Creating player at {pos}");

    var go = new GameObject("TestPlayer");
    go.layer = PlayerLayer;
    go.transform.position = pos;

    // Physics
    var capsule = go.AddComponent<CapsuleCollider>();
    capsule.height = 2f;
    capsule.radius = 0.5f;

    var rb = go.AddComponent<Rigidbody>();
    rb.isKinematic = true; // Don't fall through ground

    // NavMeshAgent for pathfinding
    var navAgent = go.AddComponent<NavMeshAgent>();
    navAgent.enabled = true;
    Debug.Log($"[MakePlayer] NavMeshAgent on mesh: {navAgent.isOnNavMesh}");

    // Stealth system
    var playerStealth = go.AddComponent<PlayerStealthController>();
    playerStealth.timeToHide = 0.1f; // Quick transition for tests

    var takedown = go.AddComponent<TakedownController>();
    takedown.enabledAtStart = true;
    takedown.verboseLogging = true;
    takedown.takedownRange = range;
    takedown.takedownAngle = angle;
    takedown.guardLayerMask = 1 << GuardLayer; // Layer mask for guard layer
    Debug.Log($"[MakePlayer] Guard layer mask set to: {takedown.guardLayerMask.value} (layer {GuardLayer})");

    // Link PlayerStealth to Takedown (required)
    playerStealth.takedownController = takedown;

    return takedown;
  }

  /// <summary>
  /// Creates a test guard with vision cone and controller.
  /// Properly initialized for testing without NavMesh dependencies.
  /// </summary>
  static GuardController MakeGuard(Vector3 pos, Vector3 forward) {
    // Get valid NavMesh position
    pos = GetNavMeshPosition(pos);
    Debug.Log($"[MakeGuard] Creating guard at {pos}");

    var go = new GameObject("TestGuard");
    go.layer = GuardLayer;
    go.transform.position = pos;
    go.transform.forward = forward;

    // Physics
    var capsule = go.AddComponent<CapsuleCollider>();
    capsule.height = 1.8f;
    capsule.radius = 0.4f;

    var rb = go.AddComponent<Rigidbody>();
    rb.isKinematic = true;

    // NavMeshAgent for pathfinding
    var navAgent = go.AddComponent<NavMeshAgent>();
    navAgent.enabled = true;
    Debug.Log($"[MakeGuard] NavMeshAgent on mesh: {navAgent.isOnNavMesh}");

    // Vision cone for detection
    var cone = go.AddComponent<GuardVisionCone>();
    cone.playerLayerMask = 1 << PlayerLayer; // Layer mask for player layer
    cone.showGizmos = false;
    cone.showRuntimeRay = false;
    cone.verboseLogging = false;
    Debug.Log($"[MakeGuard] Player layer mask set to: {cone.playerLayerMask.value} (layer {PlayerLayer})");

    // Guard controller (disabled to prevent NavMesh-dependent Update)
    var guard = go.AddComponent<GuardController>();
    guard.enabled = false; // We manually call PerformTakedown() in tests

    return guard;
  }

  // -------------------------------------------------------------------------
  // Tests
  // -------------------------------------------------------------------------

  /// <summary>
  /// Takedown succeeds when player is directly behind guard and within range.
  /// Full scene setup with camera and NavMesh.
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_Succeeds_WhenBehindGuardInRange() {

    // === SETUP COMPLETE TEST SCENE ===
    var sceneRoot = CreateTestScene();

    yield return null; // Let NavMesh settle

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);
    var guardGo = guard.gameObject;
    Debug.Log($"[Test] Guard created at {guardGo.transform.position}, layer: {guardGo.layer}");

    var takedown = MakePlayer(new Vector3(0f, 0f, -1.5f), range: 2f, angle: 60f);
    takedown.gameObject.transform.SetParent(sceneRoot.transform);
    var playerGo = takedown.gameObject;
    Debug.Log($"[Test] Player created at {playerGo.transform.position}, layer: {playerGo.layer}");

    // === VERIFY PREREQUISITES ===
    var playerStealth = takedown.GetComponent<PlayerStealthController>();
    Assert.IsNotNull(playerStealth, "Player must have PlayerStealthController");
    Assert.IsTrue(takedown.IsEnabled, "TakedownController must be enabled at start");

    // === LET STATE MACHINE UPDATE ===
    yield return null;
    Debug.Log($"[Test] Frame 1 - Player state: {playerStealth.CurrentState}, Hidden: {playerStealth.IsHidden}");

    yield return null;
    Debug.Log($"[Test] Frame 2 - Player state: {playerStealth.CurrentState}, Hidden: {playerStealth.IsHidden}");

    // Verify player is in Hidden state (prerequisite for takedown)
    var playerAgent = playerGo.GetComponent<NavMeshAgent>();
    var guardAgent = guardGo.GetComponent<NavMeshAgent>();
    Debug.Log($"[Test] Player NavMeshAgent on NavMesh: {playerAgent.isOnNavMesh}");
    Debug.Log($"[Test] Guard NavMeshAgent on NavMesh: {guardAgent.isOnNavMesh}");
    Debug.Log($"[Test] Distance between player and guard: {Vector3.Distance(playerGo.transform.position, guardGo.transform.position)}");

    Assert.IsTrue(playerStealth.IsHidden,
        $"Player must be in Hidden state, but is in {playerStealth.CurrentState}");

    // === ATTEMPT TAKEDOWN ===
    Debug.Log("[Test] Attempting takedown...");
    takedown.TryTakedown();

    // Wait for takedown animation/state to propagate
    yield return null;
    yield return null;
    yield return null;

    // === VERIFY RESULT ===
    Debug.Log($"[Test] Guard state after takedown: {guard.CurrentState}");
    Assert.AreEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must be TakenDown when player approaches from behind within range and angle.");

    // === CLEANUP ===
    Object.Destroy(sceneRoot);
  }

  /// <summary>
  /// Takedown fails when player is in front of guard (not behind).
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_Fails_WhenInFrontOfGuard() {
    var sceneRoot = CreateTestScene();

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);

    // Player in FRONT of guard (same direction as guard's forward)
    var takedown = MakePlayer(new Vector3(0f, 0f, 1f), range: 1.5f, angle: 60f);
    takedown.gameObject.transform.SetParent(sceneRoot.transform);

    var playerStealth = takedown.GetComponent<PlayerStealthController>();

    yield return null;
    yield return null;

    takedown.TryTakedown();
    yield return null;

    // Guard should NOT be taken down
    Assert.AreNotEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must NOT be TakenDown when player is in front.");

    Object.Destroy(sceneRoot);
  }

  /// <summary>
  /// Takedown fails when player is beyond range.
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_Fails_WhenBeyondRange() {
    var sceneRoot = CreateTestScene();

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);

    // Player behind guard but TOO FAR
    var takedown = MakePlayer(new Vector3(0f, 0f, -5f), range: 1.5f, angle: 60f);
    takedown.gameObject.transform.SetParent(sceneRoot.transform);

    var playerStealth = takedown.GetComponent<PlayerStealthController>();

    yield return null;
    yield return null;

    takedown.TryTakedown();
    yield return null;

    Assert.AreNotEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must NOT be TakenDown when player is beyond range.");

    Object.Destroy(sceneRoot);
  }

  /// <summary>
  /// Takedown fails when player is detected (Detected state).
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_LocksWhen_PlayerIsDetected() {
    var sceneRoot = CreateTestScene();

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);

    var takedown = MakePlayer(new Vector3(0f, 0f, -1f), range: 1.5f, angle: 60f);
    takedown.gameObject.transform.SetParent(sceneRoot.transform);

    var playerStealth = takedown.GetComponent<PlayerStealthController>();

    yield return null;

    // Manually trigger "detected" state
    playerStealth.OnGuardStartsDetecting();
    yield return null;

    Assert.AreEqual(PlayerStealthController.StealthState.Detected, playerStealth.CurrentState,
        "Player should be Detected");
    Assert.IsFalse(takedown.IsEnabled,
        "TakedownController should be disabled when player is Detected");

    takedown.TryTakedown();
    yield return null;

    Assert.AreNotEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard must NOT be TakenDown when takedown is locked (player detected).");

    Object.Destroy(sceneRoot);
  }

  /// <summary>
  /// GetCandidates returns the correct guards in range and behind the player.
  /// </summary>
  [UnityTest]
  public IEnumerator GetCandidates_ReturnsGuardsInRangeAndBehind() {

    var sceneRoot = CreateTestScene();
    yield return null;

    var takedown = MakePlayer(Vector3.zero, range: 3f, angle: 120f);
    takedown.gameObject.transform.SetParent(sceneRoot.transform);
    var playerGo = takedown.gameObject;

    // Ensure player faces forward (positive Z)
    playerGo.transform.forward = Vector3.forward;
    Debug.Log($"[Test] Player at {playerGo.transform.position}, facing: {playerGo.transform.forward}");

    // Guard 1: Behind, in range, in angle → SHOULD be candidate
    var guard1 = MakeGuard(new Vector3(0f, 0f, 2f), Vector3.forward);
    guard1.name = "Guard1_Behind_InRange";
    guard1.transform.SetParent(sceneRoot.transform);
    Debug.Log($"[Test] Guard1 at {guard1.gameObject.transform.position}");

    // Guard 2: In front → should NOT be candidate
    var guard2 = MakeGuard(new Vector3(0f, 0f, -2f), Vector3.forward);
    guard2.name = "Guard2_Front";
    guard2.transform.SetParent(sceneRoot.transform);
    Debug.Log($"[Test] Guard2 at {guard2.gameObject.transform.position}");

    // Guard 3: Behind but out of range → should NOT be candidate
    var guard3 = MakeGuard(new Vector3(0f, 0f, -5f), Vector3.forward);
    guard3.name = "Guard3_Behind_OutOfRange";
    guard3.transform.SetParent(sceneRoot.transform);
    Debug.Log($"[Test] Guard3 at {guard3.gameObject.transform.position}");

    yield return null;
    yield return null;

    IReadOnlyList<GuardController> candidates = takedown.GetCandidates();
    Debug.Log($"[Test] Found {candidates.Count} candidates");
    for (int i = 0; i < candidates.Count; i++) {
      Debug.Log($"[Test] Candidate {i}: {candidates[i].name}");
    }

    Assert.AreEqual(1, candidates.Count, "Only 1 guard should be a valid candidate");
    Assert.AreEqual(guard1, candidates[0], "Guard1 should be the candidate");

    Object.Destroy(sceneRoot);
    Debug.Log("=== TEST: GetCandidates_ReturnsGuardsInRangeAndBehind END ===\n");
  }

  /// <summary>
  /// Guard is destroyed when takedown is performed with destroyOnTakedown flag enabled.
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_WithDestroyFlag_DestroysGuardGameObject() {
    Debug.Log("\n=== TEST: Takedown_WithDestroyFlag_DestroysGuardGameObject START ===");

    var sceneRoot = CreateTestScene();
    yield return null;

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);
    var guardGo = guard.gameObject;
    guard.destroyOnTakedown = true;
    Debug.Log($"[Test] Guard created with destroyOnTakedown = true");

    // Guard should exist before takedown
    Assert.IsNotNull(guardGo, "Guard should exist before takedown");
    Debug.Log($"[Test] Guard exists before takedown: {guardGo != null}");

    // Perform takedown directly
    guard.PerformTakedown();
    Debug.Log($"[Test] PerformTakedown called");

    yield return null;

    // Guard GameObject should be destroyed
    Assert.IsTrue(guardGo == null || !guardGo.activeInHierarchy,
        "Guard GameObject should be destroyed when destroyOnTakedown is enabled");
    Debug.Log($"[Test] Guard destroyed after takedown: {guardGo == null}");

    Object.Destroy(sceneRoot);
    Debug.Log("=== TEST: Takedown_WithDestroyFlag_DestroysGuardGameObject END ===\n");
  }

  /// <summary>
  /// Guard is NOT destroyed when takedown is performed with destroyOnTakedown flag disabled.
  /// </summary>
  [UnityTest]
  public IEnumerator Takedown_WithoutDestroyFlag_GuardRemains() {
    Debug.Log("\n=== TEST: Takedown_WithoutDestroyFlag_GuardRemains START ===");

    var sceneRoot = CreateTestScene();
    yield return null;

    var guard = MakeGuard(Vector3.zero, Vector3.forward);
    guard.transform.SetParent(sceneRoot.transform);
    var guardGo = guard.gameObject;
    guard.destroyOnTakedown = false;
    Debug.Log($"[Test] Guard created with destroyOnTakedown = false (default)");

    // Guard should exist before takedown
    Assert.IsNotNull(guardGo, "Guard should exist before takedown");
    Debug.Log($"[Test] Guard exists before takedown: {guardGo != null}");

    // Perform takedown directly
    guard.PerformTakedown();
    Debug.Log($"[Test] PerformTakedown called");

    yield return null;

    // Guard GameObject should still exist
    Assert.IsNotNull(guardGo, "Guard GameObject should still exist when destroyOnTakedown is disabled");
    Assert.AreEqual(GuardController.GuardState.TakenDown, guard.CurrentState,
        "Guard should be in TakenDown state");
    Debug.Log($"[Test] Guard still exists after takedown: {guardGo != null}, State: {guard.CurrentState}");

    Object.Destroy(sceneRoot);
    Debug.Log("=== TEST: Takedown_WithoutDestroyFlag_GuardRemains END ===\n");
  }
}