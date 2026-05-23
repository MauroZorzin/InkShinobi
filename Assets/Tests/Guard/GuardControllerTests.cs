using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Simple tests for GuardController state changes.
/// </summary>
public class GuardControllerTests {
  private GameObject _guardGO;
  private GuardController _guard;

  [SetUp]
  public void Setup() {
    _guardGO = new GameObject("TestGuard");
    _guard = _guardGO.AddComponent<GuardController>();
  }

  [TearDown]
  public void TearDown() {
    if (_guardGO != null) {
      Object.Destroy(_guardGO);
    }
  }

  /// <summary>
  /// Guard initializes in Patrol state.
  /// </summary>
  [Test]
  public void Guard_StartsInPatrolState() {
    Debug.Log("[Test] Guard_StartsInPatrolState");
    Assert.AreEqual(GuardController.GuardState.Patrol, _guard.CurrentState);
    Debug.Log("[Test] PASSED");
  }

  /// <summary>
  /// Guard transitions to TakenDown state when PerformTakedown is called.
  /// </summary>
  [Test]
  public void Guard_TransitionsToTakenDownState() {
    Debug.Log("[Test] Guard_TransitionsToTakenDownState");

    _guard.PerformTakedown();

    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);
    Debug.Log("[Test] PASSED");
  }

  /// <summary>
  /// Guard stays in TakenDown state and doesn't transition out.
  /// </summary>
  [Test]
  public void Guard_RemainsInTakenDownState() {
    Debug.Log("[Test] Guard_RemainsInTakenDownState");

    _guard.PerformTakedown();
    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);

    _guard.PerformTakedown(); // Call again
    Assert.AreEqual(GuardController.GuardState.TakenDown, _guard.CurrentState);

    Debug.Log("[Test] PASSED");
  }
}
