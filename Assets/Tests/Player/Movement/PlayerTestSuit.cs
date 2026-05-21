using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerTestSuit {
  private static readonly WaitForSeconds _waitForSeconds0_1 = new(0.1f);
  private static readonly WaitForSeconds _waitForSeconds0_2 = new(0.2f);
  private static readonly WaitForSeconds _waitForSeconds0_3 = new(0.3f);
  private static readonly WaitForSeconds _waitForSeconds0_4 = new(0.4f);
  private static readonly WaitForSeconds _waitForSeconds0_5 = new(0.5f);
  private static readonly WaitForSeconds _waitForSeconds1 = new(1f);
  private const string PlayerAnimatorControllerPath = "Assets/Animators/PlayerAnimatorController.controller";
  private GameObject _groundGO;
  private GameObject _playerGO;
  private GameObject _switchWallGO;
  private PlayerMovementController _controller;
  private RightAngleWallTurner _turner;
  private WallSwitcher _wallSwitcher;
  private SpriteRenderer _sr;
  private InputAction _switchAction;
  private InputSettings.BackgroundBehavior _oldBackgroundBehavior;

  // ── Reflection helpers to access private fields ───────────────────────────
  private void SetMoveInput(float value) {
    typeof(PlayerMovementController)
      .GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, value);
  }

  private float GetVerticalVelocity() {
    return (float)typeof(PlayerMovementController)
      .GetField("_verticalVelocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_controller);
  }

  private void SetVerticalVelocity(float value) {
    typeof(PlayerMovementController)
      .GetField("_verticalVelocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, value);
  }

  private Vector3 GetVelocity() {
    return (Vector3)typeof(PlayerMovementController)
      .GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_controller);
  }

  // ── Setup / Teardown ──────────────────────────────────────────────────────
  [SetUp]
  public void Setup() {
    _oldBackgroundBehavior = InputSystem.settings.backgroundBehavior;
    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

    _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
    _groundGO.name = "Ground";
    _groundGO.transform.position = new Vector3(0f, -0.5f, 0f);
    _groundGO.transform.localScale = new Vector3(20f, 1f, 20f);

    _playerGO = new GameObject("Player");
    _playerGO.transform.position = Vector3.up;

    CharacterController cc = _playerGO.AddComponent<CharacterController>();
    cc.height = 2f;
    cc.radius = 0.5f;

    Animator animator = _playerGO.AddComponent<Animator>();
    RuntimeAnimatorController animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorControllerPath);
    Assert.IsNotNull(animatorController, $"Expected animator controller at '{PlayerAnimatorControllerPath}'.");
    animator.runtimeAnimatorController = animatorController;

    _sr = _playerGO.AddComponent<SpriteRenderer>();
    _controller = _playerGO.AddComponent<PlayerMovementController>();
    _turner = _playerGO.GetComponent<RightAngleWallTurner>();
    _wallSwitcher = _playerGO.GetComponent<WallSwitcher>();

    // Create a camPivot so HandleMovement doesn't throw a NullReferenceException
    var camPivotGO = new GameObject("CamPivot");
    _controller.camPivot = camPivotGO.transform;
    _turner.camPivot = camPivotGO.transform;
    _wallSwitcher.camPivot = camPivotGO.transform;

    _controller.moveSpeed = 5f;
    _controller.acceleration = 100f;
    _controller.deceleration = 100f;
    _controller.gravity = -20f;

    _turner.wallLayer = 1 << MovementTestFixture.WallLayer;
    _turner.logRayHits = false;
    _turner.drawRayGizmos = false;
    _wallSwitcher.wallLayer = 1 << MovementTestFixture.WallLayer;
    _wallSwitcher.frontRayLength = 2f;
    _wallSwitcher.frontRayCenterOffset = 0.2f;
    _wallSwitcher.firstNinetyRotationDuration = 0.05f;
    _wallSwitcher.switchObservationDuration = 0.05f;
    _wallSwitcher.finalNinetyRotationDuration = 0.05f;
    _wallSwitcher.logRayHits = false;
    _wallSwitcher.drawDebugGizmos = false;
  }

  [TearDown]
  public void TearDown() {
    if (_controller != null && _controller.camPivot != null) {
      Object.Destroy(_controller.camPivot.gameObject);
    }

    if (_playerGO != null) {
      Object.Destroy(_playerGO);
    }

    if (_groundGO != null) {
      Object.Destroy(_groundGO);
    }

    if (_switchWallGO != null) {
      Object.Destroy(_switchWallGO);
    }

    if (_switchAction != null) {
      _switchAction.Dispose();
      _switchAction = null;
    }

    InputSystem.settings.backgroundBehavior = _oldBackgroundBehavior;
  }

  // ── Tests ─────────────────────────────────────────────────────────────────

  // 1. Moving right builds positive X velocity
  [UnityTest]
  public IEnumerator HandleMovement_PositiveInput_BuildsPositiveVelocity() {
    SetMoveInput(1f);
    yield return _waitForSeconds0_2;

    Assert.Greater(GetVelocity().x, 0f, "Positive move input should produce positive X velocity.");
  }

  // 2. Moving left builds negative X velocity
  [UnityTest]
  public IEnumerator HandleMovement_NegativeInput_BuildsNegativeVelocity() {
    SetMoveInput(-1f);
    yield return _waitForSeconds0_2;

    Assert.Less(GetVelocity().x, 0f, "Negative move input should produce negative X velocity.");
  }

  // 3. No input decelerates velocity toward zero
  [UnityTest]
  public IEnumerator HandleMovement_ZeroInput_DeceleratesVelocity() {
    // Get up to speed first
    SetMoveInput(1f);
    yield return _waitForSeconds0_2;

    // Then release
    SetMoveInput(0f);
    yield return _waitForSeconds0_3;

    Assert.AreEqual(0f, GetVelocity().x, 0.05f, "Zero input should decelerate velocity to ~0.");
  }

  // 4. Velocity does not exceed moveSpeed
  [UnityTest]
  public IEnumerator HandleMovement_VelocityNeverExceedsMoveSpeed() {
    SetMoveInput(1f);
    yield return _waitForSeconds1;

    Assert.LessOrEqual(Mathf.Abs(GetVelocity().x), _controller.moveSpeed + 0.01f, "Velocity should never exceed moveSpeed.");
  }

  // 5. Gravity increases vertical velocity over time (makes it more negative)
  [UnityTest]
  public IEnumerator ApplyGravity_IncreasesNegativeVerticalVelocity() {
    _playerGO.transform.position = new Vector3(0f, 5f, 0f);
    SetVerticalVelocity(0f);

    var initial = GetVerticalVelocity();
    yield return _waitForSeconds0_2;

    Assert.Less(GetVerticalVelocity(), initial, "Vertical velocity should decrease (more negative) due to gravity.");
  }

  // 6. Moving right flips sprite to face right (flipX = false)
  [UnityTest]
  public IEnumerator HandleMovement_PositiveInput_SpriteNotFlipped() {
    SetMoveInput(1f);
    yield return null;

    Assert.IsFalse(_sr.flipX, "Moving right should set flipX to false.");
  }

  // 7. Moving left flips sprite to face left (flipX = true)
  [UnityTest]
  public IEnumerator HandleMovement_NegativeInput_SpriteFlipped() {
    SetMoveInput(-1f);
    yield return null;

    Assert.IsTrue(_sr.flipX, "Moving left should set flipX to true.");
  }

  // 8. Sprite flip persists correctly when changing direction
  [UnityTest]
  public IEnumerator HandleMovement_DirectionChange_SpriteFlipUpdates() {
    SetMoveInput(1f);
    yield return null;
    Assert.IsFalse(_sr.flipX, "Moving right: flipX should be false.");

    SetMoveInput(-1f);
    yield return null;
    Assert.IsTrue(_sr.flipX, "Moving left: flipX should be true.");
  }

  // 9. Player position moves right over time with positive input
  [UnityTest]
  public IEnumerator HandleMovement_PositiveInput_PlayerMovesRight() {
    var startX = _playerGO.transform.position.x;
    SetMoveInput(1f);
    yield return _waitForSeconds0_3;

    Assert.Greater(_playerGO.transform.position.x, startX, "Player should move in +X direction with positive input.");
  }

  // 10. Player position moves left over time with negative input
  [UnityTest]
  public IEnumerator HandleMovement_NegativeInput_PlayerMovesLeft() {
    var startX = _playerGO.transform.position.x;
    SetMoveInput(-1f);
    yield return _waitForSeconds0_3;

    Assert.Less(_playerGO.transform.position.x, startX, "Player should move in -X direction with negative input.");
  }

  // 11. Player falls down due to gravity with no input
  [UnityTest]
  public IEnumerator ApplyGravity_PlayerFallsDown() {
    _playerGO.transform.position = new Vector3(0f, 5f, 0f);
    var startY = _playerGO.transform.position.y;

    yield return _waitForSeconds0_3;

    Assert.Less(_playerGO.transform.position.y, startY, "Player Y position should decrease due to gravity.");
  }

  // 12. No movement without input (X axis stays put)
  [UnityTest]
  public IEnumerator HandleMovement_NoInput_PlayerDoesNotMoveHorizontally() {
    var startX = _playerGO.transform.position.x;
    yield return _waitForSeconds0_2;

    Assert.AreEqual(startX, _playerGO.transform.position.x, 0.001f, "X position should not change with no horizontal input.");
  }
  // 13. Jump sets positive vertical velocity when grounded
  [UnityTest]
  public IEnumerator Jump_WhenGrounded_SetsPositiveVerticalVelocity() {
    // Let the player settle on the ground first
    yield return _waitForSeconds1;

    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;

    Assert.Greater(GetVerticalVelocity(), 0f, "Jumping should set a positive vertical velocity.");
  }

  // 14. Jump vertical velocity peaks then decreases due to gravity
  [UnityTest]
  public IEnumerator Jump_VerticalVelocity_PeaksThenDecreases() {
    yield return _waitForSeconds1;

    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;
    var peakVelocity = GetVerticalVelocity();

    yield return _waitForSeconds0_3;
    var laterVelocity = GetVerticalVelocity();

    Assert.Less(laterVelocity, peakVelocity, "Vertical velocity should decrease after jump peak due to gravity.");
  }

  // 15. Player Y position increases immediately after jump
  [UnityTest]
  public IEnumerator Jump_PlayerYPosition_IncreasesAfterJump() {
    yield return _waitForSeconds1;

    var preJumpY = _playerGO.transform.position.y;

    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;
    var postJumpY = _playerGO.transform.position.y;

    Assert.Greater(postJumpY, preJumpY, "Player Y should be higher the frame after jump is requested.");
  }

  // 16. RotateRight increments rotation index by 1
  [UnityTest]
  public IEnumerator RotateWorld_Right_IncrementsRotationIndex() {
    var startIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    typeof(PlayerMovementController)
        .GetMethod("StartCoroutine", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(IEnumerator) }, null)
        .Invoke(_controller, new object[] {
            (IEnumerator)typeof(PlayerMovementController)
                .GetMethod("RotateWorld", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_controller, new object[] { 1 })
        });

    yield return _waitForSeconds0_5; // longer than rotationDuration (0.3f)

    var newIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.AreEqual((startIndex + 1) % 4, newIndex, "Rotating right should increment rotation index by 1 (wrapping at 4).");
  }

  // 17. RotateLeft decrements rotation index by 1
  [UnityTest]
  public IEnumerator RotateWorld_Left_DecrementsRotationIndex() {
    var startIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    typeof(PlayerMovementController)
        .GetMethod("StartCoroutine", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(IEnumerator) }, null)
        .Invoke(_controller, new object[] {
            (IEnumerator)typeof(PlayerMovementController)
                .GetMethod("RotateWorld", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_controller, new object[] { -1 })
        });

    yield return _waitForSeconds0_5;

    var newIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.AreEqual((startIndex + 3) % 4, newIndex, "Rotating left should decrement rotation index by 1 (wrapping at 0).");
  }

  // 18. _isRotating is true during rotation and false after completion
  [UnityTest]
  public IEnumerator RotateWorld_IsRotating_TrueWhileRotating_FalseAfter() {
    typeof(PlayerMovementController)
        .GetMethod("StartCoroutine", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(IEnumerator) }, null)
        .Invoke(_controller, new object[] {
            (IEnumerator)typeof(PlayerMovementController)
                .GetMethod("RotateWorld", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_controller, new object[] { 1 })
        });

    // Mid-rotation: _isRotating should be true
    yield return _waitForSeconds0_1;

    var duringRotation = (bool)typeof(PlayerMovementController)
        .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.IsTrue(duringRotation, "_isRotating should be true while rotation is in progress.");

    // After rotation completes
    yield return _waitForSeconds0_4;

    var afterRotation = (bool)typeof(PlayerMovementController)
        .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.IsFalse(afterRotation, "_isRotating should be false after rotation completes.");
  }

  [Test]
  public void GetRotationIndex_ReturnsCurrentRotationIndex() {
    typeof(PlayerMovementController)
      .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, 2);

    Assert.AreEqual(2, _controller.GetRotationIndex());
  }

  [Test]
  public void IsRotating_ReturnsCurrentRotationState() {
    typeof(PlayerMovementController)
      .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    Assert.IsTrue(_controller.IsRotating());

    typeof(PlayerMovementController)
      .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, false);

    Assert.IsFalse(_controller.IsRotating());
  }

  [Test]
  public void ReorientHorizontalVelocity_RotatesVelocityByQuarterTurns() {
    typeof(PlayerMovementController)
      .GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, Vector3.forward);

    _controller.ReorientHorizontalVelocity(1);

    Vector3 velocity = GetVelocity();
    Assert.AreEqual(1f, velocity.x, 0.0001f);
    Assert.AreEqual(0f, velocity.y, 0.0001f);
    Assert.AreEqual(0f, velocity.z, 0.0001f);
  }

  [Test]
  public void ResetHorizontalVelocity_ClearsVelocity() {
    typeof(PlayerMovementController)
      .GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, new Vector3(1f, 2f, 3f));

    _controller.ResetHorizontalVelocity();

    Assert.AreEqual(Vector3.zero, GetVelocity());
  }

  [UnityTest]
  public IEnumerator OnSwitch_WhenInputActionIsPressed_StartsWallSwitch() {
    _switchWallGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
    _switchWallGO.name = "InputSwitchFrontWall";
    _switchWallGO.layer = MovementTestFixture.WallLayer;
    _switchWallGO.transform.position = new Vector3(0f, 1f, -1f);
    _switchWallGO.transform.localScale = new Vector3(2f, 2f, 0.1f);
    Physics.SyncTransforms();

    var inputFixture = new InputTestFixture();
    inputFixture.Setup();

    try {
      var testKeyboard = InputSystem.AddDevice<Keyboard>();
      _switchAction = new InputAction("Switch", InputActionType.Button, "<Keyboard>/space");
      var callbackInvoked = false;

      typeof(PlayerMovementController)
        .GetField("_wallSwitcher", BindingFlags.NonPublic | BindingFlags.Instance)
        .SetValue(_controller, _wallSwitcher);

      _switchAction.performed += context => {
        callbackInvoked = true;
        var inputValue = new InputValue();
        typeof(InputValue)
          .GetField("m_Context", BindingFlags.NonPublic | BindingFlags.Instance)
          .SetValue(inputValue, context);

        typeof(PlayerMovementController)
          .GetMethod("OnSwitch", BindingFlags.NonPublic | BindingFlags.Instance)
          .Invoke(_controller, new object[] { inputValue });
      };
      _switchAction.Enable();

      yield return null;

      inputFixture.Press(testKeyboard.spaceKey);

      yield return null;

      Assert.IsTrue(testKeyboard.spaceKey.isPressed, "The generated keyboard should receive the queued space key press.");
      Assert.IsTrue(callbackInvoked, "The generated Switch InputAction should receive the test keyboard press.");
      Assert.IsTrue(_wallSwitcher.IsSwitching, "Pressing the Switch action should route through OnSwitch and start WallSwitcher.");
    } finally {
      _switchAction?.Dispose();
      _switchAction = null;
      inputFixture.TearDown();
    }
  }
}
