using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Play Mode tests — Tutorial Trigger System
// Run via: Window > General > Test Runner > Play Mode > Run All
public class TutorialTriggerTests
{

  private GameObject _testScene;
  private GameObject _player;
  private GameObject _triggerZone;
  private GameObject _textUI;
  private TutorialTrigger _tutorialTrigger;

  [SetUp]
  public void SetUp()
  {
    // Create root container
    _testScene = new GameObject("TutorialTestScene");

    // Create player
    _player = new GameObject("Player");
    _player.tag = "Player";
    _player.transform.SetParent(_testScene.transform);
    _player.AddComponent<Rigidbody>();
    _player.AddComponent<Collider>();

    // Add a test component to player (we'll enable/disable this)
    _player.AddComponent<TestComponent>();

    // Create text UI GameObject
    _textUI = new GameObject("TutorialText");
    _textUI.transform.SetParent(_testScene.transform);
    _textUI.SetActive(false);

    // Create trigger zone
    _triggerZone = new GameObject("TutorialTrigger");
    _triggerZone.transform.SetParent(_testScene.transform);
    var col = _triggerZone.AddComponent<BoxCollider>();
    col.isTrigger = true;

    _tutorialTrigger = _triggerZone.AddComponent<TutorialTrigger>();

    Debug.Log("[TutorialTriggerTests] Setup complete");
  }

  [TearDown]
  public void TearDown()
  {
    Object.Destroy(_testScene);
  }

  /// <summary>
  /// Test 1: Enable text only
  /// </summary>
  [UnityTest]
  public IEnumerator EnableTextOnly_TextBecomesActive()
  {
    // Configure trigger to enable text only
    SetupTriggerAction(
      enableText: true,
      enableComponent: false,
      shouldEnable: false
    );

    // Verify text is initially off
    Assert.IsFalse(_textUI.activeInHierarchy, "Text should be inactive at start");

    // Execute trigger
    _tutorialTrigger.Execute();
    yield return null;

    // Verify text is now active
    Assert.IsTrue(_textUI.activeInHierarchy, "Text should be active after trigger");

    // Verify component is not affected
    TestComponent comp = _player.GetComponent<TestComponent>();
    Assert.IsTrue(comp.enabled, "Component should remain enabled (no action)");

    Debug.Log("[TutorialTriggerTests] Test 1 PASSED: Enable text only");
  }

  /// <summary>
  /// Test 2: Disable component only
  /// </summary>
  [UnityTest]
  public IEnumerator DisableComponentOnly_ComponentBecomesInactive()
  {
    // Configure trigger to disable component only
    SetupTriggerAction(
      enableText: false,
      enableComponent: false,
      shouldEnable: false,
      disableComponent: true
    );

    TestComponent comp = _player.GetComponent<TestComponent>();
    Assert.IsTrue(comp.enabled, "Component should be enabled at start");

    // Execute trigger
    _tutorialTrigger.Execute();
    yield return null;

    // Verify component is now disabled
    Assert.IsFalse(comp.enabled, "Component should be disabled after trigger");

    // Verify text is not affected
    Assert.IsFalse(_textUI.activeInHierarchy, "Text should remain inactive (no action)");

    Debug.Log("[TutorialTriggerTests] Test 2 PASSED: Disable component only");
  }

  /// <summary>
  /// Test 3: Enable component only
  /// </summary>
  [UnityTest]
  public IEnumerator EnableComponentOnly_ComponentBecomesActive()
  {
    // Setup: Start with component disabled
    TestComponent comp = _player.GetComponent<TestComponent>();
    comp.enabled = false;
    Assert.IsFalse(comp.enabled, "Component should be disabled at start");

    // Configure trigger to enable component only
    SetupTriggerAction(
      enableText: false,
      enableComponent: true,
      shouldEnable: true
    );

    // Execute trigger
    _tutorialTrigger.Execute();
    yield return null;

    // Verify component is now enabled
    Assert.IsTrue(comp.enabled, "Component should be enabled after trigger");

    // Verify text is not affected
    Assert.IsFalse(_textUI.activeInHierarchy, "Text should remain inactive (no action)");

    Debug.Log("[TutorialTriggerTests] Test 3 PASSED: Enable component only");
  }

  /// <summary>
  /// Test 4: Combination - Enable text AND disable component
  /// </summary>
  [UnityTest]
  public IEnumerator CombinationAction_BothTextAndComponentToggle()
  {
    // Setup: Component starts enabled
    TestComponent comp = _player.GetComponent<TestComponent>();
    Assert.IsTrue(comp.enabled, "Component should be enabled at start");
    Assert.IsFalse(_textUI.activeInHierarchy, "Text should be inactive at start");

    // Configure trigger to enable text AND disable component
    SetupTriggerAction(
      enableText: true,
      enableComponent: false,
      shouldEnable: false,
      disableComponent: true
    );

    // Execute trigger
    _tutorialTrigger.Execute();
    yield return null;

    // Verify BOTH actions occurred
    Assert.IsTrue(_textUI.activeInHierarchy, "Text should be active after trigger");
    Assert.IsFalse(comp.enabled, "Component should be disabled after trigger");

    Debug.Log("[TutorialTriggerTests] Test 4 PASSED: Combination action");
  }

  // -------------------------------------------------------------------------
  // Helper Methods
  // -------------------------------------------------------------------------

  private void SetupTriggerAction(
    bool enableText = false,
    bool enableComponent = false,
    bool shouldEnable = false,
    bool disableComponent = false)
  {

    // Setup text action
    if (enableText || disableComponent)
    {
      var textAction = new TutorialTrigger.TextAction
      {
        textObject = _textUI,
        action = enableText ? TutorialTrigger.ActionType.Enable : TutorialTrigger.ActionType.None
      };
      SetPrivateField(_tutorialTrigger, "textActions", new[] { textAction });
    }

    // Setup component action
    if (enableComponent || disableComponent)
    {
      var compAction = new TutorialTrigger.ComponentAction
      {
        componentName = "TestComponent",
        action = enableComponent
          ? TutorialTrigger.ActionType.Enable
          : (disableComponent ? TutorialTrigger.ActionType.Disable : TutorialTrigger.ActionType.None)
      };
      SetPrivateField(_tutorialTrigger, "componentActions", new[] { compAction });
    }

    // Set player reference
    SetPrivateField(_tutorialTrigger, "player", _player);
  }

  private void SetPrivateField(object obj, string fieldName, object value)
  {
    var field = obj.GetType().GetField(fieldName,
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (field != null)
    {
      field.SetValue(obj, value);
    }
  }
}

/// <summary>
/// Dummy component for testing enable/disable functionality
/// </summary>
public class TestComponent : MonoBehaviour
{
}
