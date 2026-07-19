using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the actual mechanics of a line switch: validating a candidate target LinePath
/// and moving the player onto it. Has no knowledge of the camera or of input — those are
/// owned by LineVisionController, which calls into this component. Mirrors WallSwitcher.
/// </summary>
[RequireComponent(typeof(LineFollowController))]
public class LineSwitcher : MonoBehaviour {
  [Header("References")]
  [Tooltip("The line-locked movement controller notified once the switch completes.")]
  public LineFollowController followController;

  [Header("Switch Transition")]
  [Tooltip("Seconds spent moving the player from its current position to the target point on the new line.")]
  public float switchDuration = 0.35f;

  [Tooltip("Height above the target line's own Y the player is placed at after switching — just enough to sit visibly on top of the line rather than exactly clipped into it.")]
  public float heightAboveLine = 0.05f;

  [Header("Debug")]
  public bool logSwitches = true;

  private const float SWITCH_COOLDOWN = 0.3f;

  private CharacterController _cc;
  private bool _isSwitching;
  private float _lastSwitchTime = -999f;

  /// <summary>True while the player is being moved onto the target line.</summary>
  public bool IsSwitching => _isSwitching;

  private void Awake() {
    _cc = GetComponent<CharacterController>();

    if (followController == null) {
      followController = GetComponent<LineFollowController>();
    }
  }

  /// <summary>
  /// A candidate strand is a valid switch target as long as it isn't the exact strand the
  /// player is already on. Two different strands on the SAME LinePath (disjoint sub-paths)
  /// count as different targets, same as strands on two different LinePath objects.
  /// </summary>
  public bool IsValidSwitchTarget(LinePath candidate, int strandIndex) {
    if (candidate == null || strandIndex < 0 || strandIndex >= candidate.StrandCount) return false;
    bool sameSpot = candidate == followController.currentLine && strandIndex == followController.currentStrand;
    return !sameSpot;
  }

  /// <summary>
  /// Starts moving the player onto the target line at the given point/distance, if valid and
  /// not already switching or on cooldown.
  /// </summary>
  /// <param name="targetLine">The LinePath to switch onto.</param>
  /// <param name="targetStrand">Which disjoint strand on targetLine to switch onto.</param>
  /// <param name="targetPoint">The exact aimed world point on that strand (from LineVisionController).</param>
  /// <param name="targetDistance">Distance-along-strand matching targetPoint, from LinePath.FindClosestDistance.</param>
  /// <param name="onComplete">Optional callback invoked once the move finishes.</param>
  /// <returns>True if a switch was started.</returns>
  public bool TrySwitchToLine(LinePath targetLine, int targetStrand, Vector3 targetPoint, float targetDistance, Action onComplete = null) {
    if (!enabled || _isSwitching || Time.time < _lastSwitchTime + SWITCH_COOLDOWN) {
      return false;
    }

    if (!IsValidSwitchTarget(targetLine, targetStrand)) {
      if (logSwitches) Debug.Log("[LineSwitcher] Switch denied: invalid target strand.");
      return false;
    }

    if (logSwitches) Debug.Log($"[LineSwitcher] Switch started. target={targetLine.name} strand={targetStrand} point={targetPoint:F3}");

    StartCoroutine(SwitchRoutine(targetLine, targetStrand, targetPoint, targetDistance, onComplete));
    return true;
  }

  private IEnumerator SwitchRoutine(LinePath targetLine, int targetStrand, Vector3 targetPoint, float targetDistance, Action onComplete) {
    _isSwitching = true;
    if (followController != null) followController.movementEnabled = false;

    Vector3 startPos = transform.position;

    // targetPoint is the exact aimed point ON the line, so its Y already matches the target
    // line's height at that spot — unlike WallSwitcher (which deliberately preserves the
    // player's old Y across a wall-to-wall move), here we WANT to adopt the new line's Y,
    // offset up slightly so the player visibly sits on top of the line instead of clipping into it.
    Vector3 huggedTarget = targetPoint + Vector3.up * Mathf.Max(0f, heightAboveLine);

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, switchDuration);

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      SetPlayerPosition(Vector3.Lerp(startPos, huggedTarget, t));
      yield return null;
    }

    SetPlayerPosition(huggedTarget);

    if (followController != null) {
      followController.SetLine(targetLine, targetStrand, targetDistance);
      followController.ResetVelocity();
      followController.movementEnabled = true;
    }

    _lastSwitchTime = Time.time;
    _isSwitching = false;

    if (logSwitches) Debug.Log($"[LineSwitcher] Switch completed onto '{targetLine.name}' strand={targetStrand}.");

    onComplete?.Invoke();
  }

  /// <summary>
  /// Temporarily disables the CharacterController so scripted placement isn't blocked by
  /// collision resolution, matching WallSwitcher's approach.
  /// </summary>
  private void SetPlayerPosition(Vector3 worldPos) {
    if (_cc == null) _cc = GetComponent<CharacterController>();

    if (_cc == null) {
      transform.position = worldPos;
      return;
    }

    var wasEnabled = _cc.enabled;
    _cc.enabled = false;
    transform.position = worldPos;
    _cc.enabled = wasEnabled;
  }
}
