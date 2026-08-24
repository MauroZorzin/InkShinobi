using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Handles the actual mechanics of a line switch: validating a candidate target LinePath
/// and moving the player onto it. Has no knowledge of the camera or of input — those are
/// owned by AimSwitch, which calls into this component. Mirrors WallSwitcher.
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
  [Tooltip("Instantiated at the player's position where the switch starts (where the player vanishes from).")]
  public ParticleSystem departParticlesPrefab;

  [Tooltip("Seconds after the switch starts before Depart Particles Prefab spawns. 0 = instant, matching the old behavior.")]
  public float departParticleDelay = 0f;

  [Tooltip("Instantiated at the target position where the switch finishes (where the player arrives).")]
  public ParticleSystem arriveParticlesPrefab;

  [Tooltip("Seconds after the switch finishes before Arrive Particles Prefab spawns. 0 = instant, matching the old behavior.")]
  public float arriveParticleDelay = 0f;

  [Header("Visibility")]
  [Tooltip("If true, hides the player's sprite for the duration of the switch (it's a fast/teleport-like move) — TakedownController briefly shows it again mid-switch to play the takedown animation, then this hides it again until arrival.")]
  public bool hidePlayerDuringSwitch = false;

  [Tooltip("Defaults to this GameObject's SpriteRenderer if left empty.")]
  public SpriteRenderer spriteRenderer;

  [Tooltip("Seconds after the switch finishes before the player's sprite reappears (only relevant when Hide Player During Switch is on). Movement re-enables immediately regardless — this only delays the visual. 0 = instant, matching the old behavior.")]
  public float spriteReappearDelay = 0f;

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


  public void SetSpriteVisible(bool visible) {
    if (!hidePlayerDuringSwitch || spriteRenderer == null) return;
    spriteRenderer.enabled = visible;
  }


  public bool IsValidSwitchTarget(LinePath candidate, int strandIndex) {
    if (candidate == null || strandIndex < 0 || strandIndex >= candidate.StrandCount) return false;
    bool sameSpot = candidate == followController.currentLine && strandIndex == followController.currentStrand;
    return !sameSpot;
  }

  private Vector3 GetHuggedTarget(Vector3 targetPoint) {
    float hugHeight = followController != null ? followController.heightAboveLine : 0f;
    return targetPoint + Vector3.up * Mathf.Max(0f, hugHeight);
  }

  public bool TrySwitchToLine(LinePath targetLine, int targetStrand, Vector3 targetPoint, float targetDistance, Action onComplete = null) {
    if (!enabled || _isSwitching || Time.time < _lastSwitchTime + SWITCH_COOLDOWN) {
      return false;
    }

    if (!IsValidSwitchTarget(targetLine, targetStrand)) {
      if (logSwitches) Debug.Log("[LineSwitcher] Switch denied: invalid target strand.");
      return false;
    }

    Vector3 huggedTarget = GetHuggedTarget(targetPoint);

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

    StartCoroutine(SpawnParticlesDelayed(departParticlesPrefab, startPos, departParticleDelay));
    SetSpriteVisible(false);

    Vector3 huggedTarget = GetHuggedTarget(targetPoint);

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
      lastPos = transform.position;

      if (rotatingCamera) {
        UpdateOrbitingCamera(camStartOffset, t * 180f);
      }

      yield return null;
    }

    MoveTowards(huggedTarget - lastPos);

    if (rotatingCamera) {
      UpdateOrbitingCamera(camStartOffset, 180f);
    }

    // huggedTarget, not transform.position — with a nonzero arriveParticleDelay the player (now
    // free to move again below) could already be elsewhere by the time this actually fires.
    StartCoroutine(SpawnParticlesDelayed(arriveParticlesPrefab, huggedTarget, arriveParticleDelay));
    StartCoroutine(ShowSpriteDelayed(spriteReappearDelay));

    if (followController != null) {
      followController.SetLine(targetLine, targetStrand, targetDistance);
      followController.SnapFacingToLine();
      followController.ResetVelocity();
      followController.movementEnabled = true;
    }

    _lastSwitchTime = Time.time;
    _isSwitching = false;

    if (logSwitches) Debug.Log($"[LineSwitcher] Switch completed onto '{targetLine.name}' strand={targetStrand}.");

    onComplete?.Invoke();
  }

  private void UpdateOrbitingCamera(Vector3 camStartOffset, float yawDegrees) {
    Vector3 pivot = transform.position;
    Vector3 orbitedOffset = Quaternion.AngleAxis(yawDegrees, Vector3.up) * camStartOffset;
    cameraTransform.position = pivot + orbitedOffset;
    cameraTransform.rotation = Quaternion.LookRotation((pivot - cameraTransform.position).normalized, Vector3.up);
  }

  private void SpawnParticles(ParticleSystem prefab, Vector3 position) {
    if (prefab == null) return;

    ParticleSystem instance = Instantiate(prefab, position, Quaternion.identity);
    ParticleSystem.MainModule main = instance.main;
    float lifetime = Mathf.Max(main.startLifetime.constant, main.startLifetime.constantMax);
    Destroy(instance.gameObject, main.duration + lifetime);
  }

  private IEnumerator SpawnParticlesDelayed(ParticleSystem prefab, Vector3 position, float delay) {
    if (delay > 0f) yield return new WaitForSeconds(delay);
    SpawnParticles(prefab, position);
  }

  private IEnumerator ShowSpriteDelayed(float delay) {
    if (delay > 0f) yield return new WaitForSeconds(delay);
    SetSpriteVisible(true);
  }

  private void MoveTowards(Vector3 delta) {
    if (_cc == null) _cc = GetComponent<CharacterController>();

    if (_cc == null || !_cc.enabled) {
      transform.position += delta;
      return;
    }

    _cc.Move(delta);
  }
}
