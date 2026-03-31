using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerTestSuit
{
    private GameObject _playerGO;
    private PlayerMovementController _controller;
    private SpriteRenderer _sr;

    // ── Reflection helpers to access private fields ───────────────────────────
    private void SetMoveInput(float value)
    {
        typeof(PlayerMovementController)
            .GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(_controller, value);
    }

    private float GetVerticalVelocity()
    {
        return (float)typeof(PlayerMovementController)
            .GetField("_verticalVelocity", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(_controller);
    }

    private Vector3 GetVelocity()
    {
        return (Vector3)typeof(PlayerMovementController)
            .GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(_controller);
    }

    // ── Setup / Teardown ──────────────────────────────────────────────────────
    [SetUp]
    public void Setup()
    {
        _playerGO = new GameObject("Player");

        var cc = _playerGO.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;

        _playerGO.AddComponent<Animator>();
        _sr = _playerGO.AddComponent<SpriteRenderer>();
        _controller = _playerGO.AddComponent<PlayerMovementController>();

        _controller.moveSpeed    = 5f;
        _controller.acceleration = 100f;
        _controller.deceleration = 100f;
        _controller.gravity      = -20f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(_playerGO);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    // 1. Moving right builds positive X velocity
    [UnityTest]
    public IEnumerator HandleMovement_PositiveInput_BuildsPositiveVelocity()
    {
        SetMoveInput(1f);
        yield return new WaitForSeconds(0.2f);

        Assert.Greater(GetVelocity().x, 0f,
            "Positive move input should produce positive X velocity.");
    }

    // 2. Moving left builds negative X velocity
    [UnityTest]
    public IEnumerator HandleMovement_NegativeInput_BuildsNegativeVelocity()
    {
        SetMoveInput(-1f);
        yield return new WaitForSeconds(0.2f);

        Assert.Less(GetVelocity().x, 0f,
            "Negative move input should produce negative X velocity.");
    }

    // 3. No input decelerates velocity toward zero
    [UnityTest]
    public IEnumerator HandleMovement_ZeroInput_DeceleratesVelocity()
    {
        // Get up to speed first
        SetMoveInput(1f);
        yield return new WaitForSeconds(0.2f);

        // Then release
        SetMoveInput(0f);
        yield return new WaitForSeconds(0.3f);

        Assert.AreEqual(0f, GetVelocity().x, 0.05f,
            "Zero input should decelerate velocity to ~0.");
    }

    // 4. Velocity does not exceed moveSpeed
    [UnityTest]
    public IEnumerator HandleMovement_VelocityNeverExceedsMoveSpeed()
    {
        SetMoveInput(1f);
        yield return new WaitForSeconds(1f);

        Assert.LessOrEqual(Mathf.Abs(GetVelocity().x), _controller.moveSpeed + 0.01f,
            "Velocity should never exceed moveSpeed.");
    }

    // 5. Gravity increases vertical velocity over time (makes it more negative)
    [UnityTest]
    public IEnumerator ApplyGravity_IncreasesNegativeVerticalVelocity()
    {
        float initial = GetVerticalVelocity();
        yield return new WaitForSeconds(0.2f);

        Assert.Less(GetVerticalVelocity(), initial,
            "Vertical velocity should decrease (more negative) due to gravity.");
    }

    // 6. Moving right flips sprite to face right (flipX = false)
    [UnityTest]
    public IEnumerator HandleMovement_PositiveInput_SpriteNotFlipped()
    {
        SetMoveInput(1f);
        yield return null;

        Assert.IsFalse(_sr.flipX, "Moving right should set flipX to false.");
    }

    // 7. Moving left flips sprite to face left (flipX = true)
    [UnityTest]
    public IEnumerator HandleMovement_NegativeInput_SpriteFlipped()
    {
        SetMoveInput(-1f);
        yield return null;

        Assert.IsTrue(_sr.flipX, "Moving left should set flipX to true.");
    }

    // 8. Sprite flip persists correctly when changing direction
    [UnityTest]
    public IEnumerator HandleMovement_DirectionChange_SpriteFlipUpdates()
    {
        SetMoveInput(1f);
        yield return null;
        Assert.IsFalse(_sr.flipX, "Moving right: flipX should be false.");

        SetMoveInput(-1f);
        yield return null;
        Assert.IsTrue(_sr.flipX, "Moving left: flipX should be true.");
    }

    // 9. Player position moves right over time with positive input
    [UnityTest]
    public IEnumerator HandleMovement_PositiveInput_PlayerMovesRight()
    {
        float startX = _playerGO.transform.position.x;
        SetMoveInput(1f);
        yield return new WaitForSeconds(0.3f);

        Assert.Greater(_playerGO.transform.position.x, startX,
            "Player should move in +X direction with positive input.");
    }

    // 10. Player position moves left over time with negative input
    [UnityTest]
    public IEnumerator HandleMovement_NegativeInput_PlayerMovesLeft()
    {
        float startX = _playerGO.transform.position.x;
        SetMoveInput(-1f);
        yield return new WaitForSeconds(0.3f);

        Assert.Less(_playerGO.transform.position.x, startX,
            "Player should move in -X direction with negative input.");
    }

    // 11. Player falls down due to gravity with no input
    [UnityTest]
    public IEnumerator ApplyGravity_PlayerFallsDown()
    {
        _playerGO.transform.position = new Vector3(0f, 5f, 0f);
        float startY = _playerGO.transform.position.y;

        yield return new WaitForSeconds(0.3f);

        Assert.Less(_playerGO.transform.position.y, startY,
            "Player Y position should decrease due to gravity.");
    }

    // 12. No movement without input (X axis stays put)
    [UnityTest]
    public IEnumerator HandleMovement_NoInput_PlayerDoesNotMoveHorizontally()
    {
        float startX = _playerGO.transform.position.x;
        yield return new WaitForSeconds(0.2f);

        Assert.AreEqual(startX, _playerGO.transform.position.x, 0.001f,
            "X position should not change with no horizontal input.");
    }
}