using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerTestSuit {
  private static readonly WaitForSeconds _waitForSeconds1 = new(1f);
  private static readonly WaitForSeconds _waitForSeconds0_3 = new(0.3f);
  private static readonly WaitForSeconds _waitForSeconds0_2 = new(0.2f);
  private GameObject _playerGO;
  private PlayerMovementController _controller;
  private SpriteRenderer _sr;

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

  private Vector3 GetVelocity() {
    return (Vector3)typeof(PlayerMovementController)
      .GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance)
      .GetValue(_controller);
  }

  // ── Setup / Teardown ──────────────────────────────────────────────────────
  [SetUp]
  public void Setup() {
    _playerGO = new GameObject("Player");

    CharacterController cc = _playerGO.AddComponent<CharacterController>();
    cc.height = 2f;
    cc.radius = 0.5f;

    _playerGO.AddComponent<Animator>();
    _sr = _playerGO.AddComponent<SpriteRenderer>();
    _controller = _playerGO.AddComponent<PlayerMovementController>();

    // Create a camPivot so HandleMovement doesn't throw a NullReferenceException
    GameObject camPivotGO = new GameObject("CamPivot");
    _controller.camPivot = camPivotGO.transform;

    _controller.moveSpeed = 5f;
    _controller.acceleration = 100f;
    _controller.deceleration = 100f;
    _controller.gravity = -20f;
  }

  [TearDown]
  public void TearDown() {
    Object.Destroy(_controller.camPivot.gameObject);
    Object.Destroy(_playerGO);
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
    float peakVelocity = GetVerticalVelocity();

    yield return _waitForSeconds0_3;
    float laterVelocity = GetVerticalVelocity();

    Assert.Less(laterVelocity, peakVelocity, "Vertical velocity should decrease after jump peak due to gravity.");
  }

  // 15. Jump is ignored when already airborne
  [UnityTest]
  public IEnumerator Jump_WhenAirborne_IsIgnored() {
    // Let player settle on ground first
    yield return _waitForSeconds1;

    // Trigger a real jump to get airborne
    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;
    float risingVelocity = GetVerticalVelocity();

    // Try to jump again while airborne
    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;
    float velocityAfterSecondJump = GetVerticalVelocity();

    // Gravity should have reduced velocity, not reset it to a fresh jump
    Assert.Less(velocityAfterSecondJump, risingVelocity, "A second jump while airborne should be ignored — velocity should not reset upward.");
  }

  // 16. Player Y position increases immediately after jump
  [UnityTest]
  public IEnumerator Jump_PlayerYPosition_IncreasesAfterJump() {
    yield return _waitForSeconds1;

    float preJumpY = _playerGO.transform.position.y;

    typeof(PlayerMovementController)
      .GetField("_jumpRequested", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_controller, true);

    yield return null;
    float postJumpY = _playerGO.transform.position.y;

    Assert.Greater(postJumpY, preJumpY, "Player Y should be higher the frame after jump is requested.");
  }
  // 17. RotateRight increments rotation index by 1
  [UnityTest]
  public IEnumerator RotateWorld_Right_IncrementsRotationIndex() {
    int startIndex = (int)typeof(PlayerMovementController)
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

    yield return new WaitForSeconds(0.5f); // longer than rotationDuration (0.3f)

    int newIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.AreEqual((startIndex + 1) % 4, newIndex, "Rotating right should increment rotation index by 1 (wrapping at 4).");
  }

  // 18. RotateLeft decrements rotation index by 1
  [UnityTest]
  public IEnumerator RotateWorld_Left_DecrementsRotationIndex() {
    int startIndex = (int)typeof(PlayerMovementController)
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

    yield return new WaitForSeconds(0.5f);

    int newIndex = (int)typeof(PlayerMovementController)
        .GetField("_currentRotationIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.AreEqual((startIndex + 3) % 4, newIndex, "Rotating left should decrement rotation index by 1 (wrapping at 0).");
  }

  // 19. _isRotating is true during rotation and false after completion
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
    yield return new WaitForSeconds(0.1f);

    bool duringRotation = (bool)typeof(PlayerMovementController)
        .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.IsTrue(duringRotation, "_isRotating should be true while rotation is in progress.");

    // After rotation completes
    yield return new WaitForSeconds(0.4f);

    bool afterRotation = (bool)typeof(PlayerMovementController)
        .GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(_controller);

    Assert.IsFalse(afterRotation, "_isRotating should be false after rotation completes.");
  }
}
