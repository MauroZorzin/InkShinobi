using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class PlayerDetectedIndicator : MonoBehaviour {
  [Tooltip("Player whose detection state drives the indicator. Leave empty to auto-find in the scene.")]
  public PlayerStealthController player;

  [Tooltip("Dedicated profile for this indicator (PlayerDetectedVolumeProfile.asset). Kept separate from the scene's environment profile so it never overwrites unrelated overrides.")]
  public VolumeProfile profile;

  [Tooltip("Blend priority relative to the scene's base Volume(s) — must be higher so this one wins while active.")]
  public float priority = 10f;

  [Header("Vignette")]
  public Color color = Color.black;
  [Range(0f, 1f)] public float baseIntensity = 0.35f;
  [Tooltip("How far intensity swings above baseIntensity on each pulse.")]
  [Range(0f, 1f)] public float pulseAmplitude = 0.15f;
  [Tooltip("Pulses per second while detected.")]
  public float pulseSpeed = 1.5f;
  [Range(0f, 1f)] public float smoothness = 0.3f;
  public bool rounded = true;
  [Tooltip("How quickly the volume's weight fades in/out when detection starts/stops.")]
  public float fadeSpeed = 3f;

  [Header("Audio")]
  public AudioClip detectedStartSound;
  public AudioClip detectedEndSound;
  [Tooltip("Played once per pulse cycle while detected.")]
  public AudioClip pulseSound;
  [Range(0f, 1f)] public float soundVolume = 1f;
  public AudioMixerGroup mixerGroup;

  private Volume _volume;
  private Vignette _vignette;
  private float _pulsePhase;
  private bool _wasDetected;

  private void Awake() {
    _volume = GetComponent<Volume>();
    _volume.isGlobal = true;
    _volume.priority = priority;
    _volume.weight = 0f;

    if (profile == null) {
      Debug.LogError($"[PlayerDetectedIndicator] {name}: No Volume Profile assigned — drag PlayerDetectedVolumeProfile.asset into the Profile field.", this);
      enabled = false;
      return;
    }

    _volume.profile = profile;

    if (!profile.TryGet(out _vignette)) {
      _vignette = profile.Add<Vignette>(true);
    }

    _vignette.color.overrideState = true;
    _vignette.intensity.overrideState = true;
    _vignette.smoothness.overrideState = true;
    _vignette.rounded.overrideState = true;

    if (player == null) {
      player = FindFirstObjectByType<PlayerStealthController>();
    }
  }

  private void Update() {
    if (player == null || _vignette == null) {
      return;
    }

    bool detected = player.DetectingGuardCount > 0;

    if (detected != _wasDetected) {
      _wasDetected = detected;
      AudioClip cue = detected ? detectedStartSound : detectedEndSound;
      if (cue != null) {
        OneShotAudio.PlayClipAtPoint(cue, player.transform.position, soundVolume, mixerGroup);
      }
      if (!detected) {
        _pulsePhase = 0f;
      }
    }

    float pulse = 0f;
    if (detected) {
      float previousPhase = _pulsePhase;
      _pulsePhase += pulseSpeed * Time.deltaTime;

      if (Mathf.Floor(_pulsePhase) > Mathf.Floor(previousPhase) && pulseSound != null) {
        OneShotAudio.PlayClipAtPoint(pulseSound, player.transform.position, soundVolume, mixerGroup);
      }

      pulse = Mathf.Sin(_pulsePhase * Mathf.PI * 2f) * 0.5f + 0.5f;
    }

    _volume.weight = Mathf.MoveTowards(_volume.weight, detected ? 1f : 0f, fadeSpeed * Time.deltaTime);

    _vignette.color.value = color;
    _vignette.intensity.value = baseIntensity + pulseAmplitude * pulse;
    _vignette.smoothness.value = smoothness;
    _vignette.rounded.value = rounded;
  }
}
