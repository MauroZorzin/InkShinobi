using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Handles the actual mechanics of a line switch: validating a candidate target LinePath
/// and moving the player onto it. Has no knowledge of the camera or of input — those are
/// owned by LineAimSwitchController, which calls into this component. Mirrors WallSwitcher.
/// </summary>
[RequireComponent(typeof(LineFollowController))]
public class LineSwitcher : MonoBehaviour {
  [Header("References")]
  [Tooltip("The line-locked movement controller notified once the switch completes.")]
  public LineFollowController followController;

  [Header("Switch Transition")]
  [Tooltip("Seconds spent moving the player from its current position to the target point on the new line.")]
  public float switchDuration = 0.35f;

  [Header("Camera 180° Flip")]
  [Tooltip("If true, the camera orbits 180 degrees around the player over the course of the switch.")]
  public bool rotateCamera180OnSwitch = false;

  [Tooltip("Camera orbited when rotateCamera180OnSwitch is true. Defaults to Camera.main if left empty.")]
  public Transform cameraTransform;

  [Header("Audio")]
  [Tooltip("Played once, at the player's position, the instant a switch is confirmed and starts moving.")]
  public AudioClip switchSound;
  [Range(0f, 1f)] public float switchSoundVolume = 1f;
  [Tooltip("Mixer group switchSound is routed through (e.g. your \"FX\" group). Leave empty to go straight to Master.")]
  public AudioMixerGroup mixerGroup;

  [Header("Particles")]
  [Tooltip("Instantiated at the player's position the instant a switch starts (where the player vanishes from).")]
  public ParticleSystem departParticlesPrefab;

  [Tooltip("Instantiated at the target position the instant a switch finishes (where the player arrives).")]
  public ParticleSystem arriveParticlesPrefab;

  [Header("Visibility")]
  [Tooltip("If true, hides the player's sprite for the duration of the switch (it's a fast/teleport-like move) — TakedownController briefly shows it again mid-switch to play the takedown animation, then this hides it again until arrival.")]
  public bool hidePlayerDuringSwitch = false;

  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

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

    if (cameraTransform == null && Camera.main != null) {
      cameraTransform = Camera.main.transform;
    }

    if (spriteRenderer == null) {
      spriteRenderer = GetComponent<SpriteRenderer>();
    }
  }

  /// <summary>
  /// Shows/hides the player's sprite, but only when hidePlayerDuringSwitch is enabled — a no-op
  /// otherwise, so callers (e.g. TakedownController briefly showing the player mid-switch to play
  /// its takedown animation) don't need to know whether the feature is even turned on.
  /// </summary>
  public void SetSpriteVisible(bool visible) {
    if (!hidePlayerDuringSwitch || spriteRenderer == null) return;
    spriteRenderer.enabled = visible;
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
  /// <param name="targetPoint">The exact aimed world point on that strand (from LineAimSwitchController).</param>
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

    if (switchSound != null) OneShotAudio.PlayClipAtPoint(switchSound, transform.position, switchSoundVolume, mixerGroup);

    StartCoroutine(SwitchRoutine(targetLine, targetStrand, targetPoint, targetDistance, onComplete));
    return true;
  }

  private IEnumerator SwitchRoutine(LinePath targetLine, int targetStrand, Vector3 targetPoint, float targetDistance, Action onComplete) {
    _isSwitching = true;
    if (followController != null) followController.movementEnabled = false;
    if (_cc == null) _cc = GetComponent<CharacterController>();

    Vector3 startPos = transform.position;

    SpawnParticles(departParticlesPrefab, startPos);
    SetSpriteVisible(false);

    // targetPoint is the exact aimed point ON the line, so its Y already matches the target
    // line's height at that spot — unlike WallSwitcher (which deliberately preserves the
    // player's old Y across a wall-to-wall move), here we WANT to adopt the new line's Y.
    // Read the hug offset from followController rather than keeping our own copy: if this value
    // disagreed with what LineFollowController's snap-correction considers "on the line", the
    // very next frame after the switch would immediately yank the player to the OTHER height,
    // which is the "player not set correctly on the line" bug — they must always agree.
    float hugHeight = followController != null ? followController.heightAboveLine : 0f;
    Vector3 huggedTarget = targetPoint + Vector3.up * Mathf.Max(0f, hugHeight);

    // Orbit the camera's POSITION 180 degrees around the player over the switch (pivot = the
    // player, not the camera) — not just spinning the camera's facing in place. Computed once
    // up front from the camera's starting offset, then re-applied around the player's CURRENT
    // (possibly still moving) position each frame, so the orbit and the player move land together.
    bool rotatingCamera = rotateCamera180OnSwitch && cameraTransform != null;
    Vector3 camStartOffset = rotatingCamera ? cameraTransform.position - startPos : Vector3.zero;

    var elapsed = 0f;
    var duration = Mathf.Max(0.01f, switchDuration);
    Vector3 lastPos = startPos;

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
      Vector3 nextPos = Vector3.Lerp(startPos, huggedTarget, t);
      MoveTowards(nextPos - lastPos);
      lastPos = transform.position; // collision may have clamped the actual move short — track the real position

      if (rotatingCamera) {
        UpdateOrbitingCamera(camStartOffset, t * 180f);
      }

      yield return null;
    }

    MoveTowards(huggedTarget - lastPos);

    if (rotatingCamera) {
      UpdateOrbitingCamera(camStartOffset, 180f);
    }

    SpawnParticles(arriveParticlesPrefab, transform.position);
    SetSpriteVisible(true);

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
  /// Orbits cameraTransform to a given yaw (degrees, around world up) away from camStartOffset,
  /// pivoting around the player's CURRENT position — so the camera actually swings around the
  /// player rather than spinning in place — and keeps it looking at the player throughout.
  /// </summary>
  private void UpdateOrbitingCamera(Vector3 camStartOffset, float yawDegrees) {
    Vector3 pivot = transform.position;
    Vector3 orbitedOffset = Quaternion.AngleAxis(yawDegrees, Vector3.up) * camStartOffset;
    cameraTransform.position = pivot + orbitedOffset;
    cameraTransform.rotation = Quaternion.LookRotation((pivot - cameraTransform.position).normalized, Vector3.up);
  }

  /// <summary>Instantiates a one-shot particle prefab at a position, auto-destroyed once it finishes playing. No-op if prefab is null.</summary>
  private void SpawnParticles(ParticleSystem prefab, Vector3 position) {
    if (prefab == null) return;

    ParticleSystem instance = Instantiate(prefab, position, Quaternion.identity);
    ParticleSystem.MainModule main = instance.main;
    float lifetime = Mathf.Max(main.startLifetime.constant, main.startLifetime.constantMax);
    Destroy(instance.gameObject, main.duration + lifetime);
  }

  /// <summary>
  /// Moves the player by delta THROUGH the CharacterController (collision-aware), instead of
  /// teleporting the raw transform with the controller disabled. A disabled-CC teleport lerps
  /// in a straight line with no collision at all, which can clip through floors/walls when the
  /// old and new line points aren't on an unobstructed line of sight — this way the switch
  /// still slides to a stop against geometry instead of tunnelling through it.
  /// </summary>
  private void MoveTowards(Vector3 delta) {
    if (_cc == null) _cc = GetComponent<CharacterController>();

    if (_cc == null || !_cc.enabled) {
      transform.position += delta;
      return;
    }

    _cc.Move(delta);
  }
}
