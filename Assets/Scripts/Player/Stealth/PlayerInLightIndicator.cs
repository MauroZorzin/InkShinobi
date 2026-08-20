using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class PlayerInLightIndicator : MonoBehaviour {
  [Tooltip("Player whose IsInLight state drives the indicator. Leave empty to auto-find in the scene.")]
  public PlayerStealthController player;

  [Tooltip("Dedicated profile for this indicator (PlayerInLightVolumeProfile.asset). Kept separate from the scene's environment profile so it never overwrites unrelated overrides.")]
  public VolumeProfile profile;

  [Tooltip("Blend priority relative to the scene's base Volume(s) — must be higher so this one wins while active.")]
  public float priority = 10f;

  [Header("Look")]
  [Tooltip("Saturation applied at full strength while in light (negative = washed out).")]
  [Range(-100f, 100f)] public float saturation = -25f;
  [Tooltip("Color tint applied at full strength while in light.")]
  public Color colorFilter = new Color(1f, 0.95f, 0.8f);
  [Tooltip("How quickly this volume's weight fades in/out.")]
  public float fadeSpeed = 4f;

  [Header("Audio")]
  public AudioClip enterSound;
  public AudioClip exitSound;
  [Range(0f, 1f)] public float soundVolume = 1f;
  public AudioMixerGroup mixerGroup;

  private Volume _volume;
  private ColorAdjustments _colorAdjustments;
  private bool _wasInLight;

  private void Awake() {
    _volume = GetComponent<Volume>();
    _volume.isGlobal = true;
    _volume.priority = priority;
    _volume.weight = 0f;

    if (profile == null) {
      Debug.LogError($"[PlayerInLightIndicator] {name}: No Volume Profile assigned — drag PlayerInLightVolumeProfile.asset into the Profile field.", this);
      enabled = false;
      return;
    }

    _volume.profile = profile;

    if (!profile.TryGet(out _colorAdjustments)) {
      _colorAdjustments = profile.Add<ColorAdjustments>(true);
    }

    _colorAdjustments.saturation.overrideState = true;
    _colorAdjustments.colorFilter.overrideState = true;

    if (player == null) {
      player = FindFirstObjectByType<PlayerStealthController>();
    }
  }

  private void Update() {
    if (player == null || _colorAdjustments == null) {
      return;
    }

    bool inLight = player.IsInLight;

    if (inLight != _wasInLight) {
      _wasInLight = inLight;
      AudioClip cue = inLight ? enterSound : exitSound;
      if (cue != null) {
        OneShotAudio.PlayClipAtPoint(cue, player.transform.position, soundVolume, mixerGroup);
      }
    }

    _volume.weight = Mathf.MoveTowards(_volume.weight, inLight ? 1f : 0f, fadeSpeed * Time.deltaTime);

    _colorAdjustments.saturation.value = saturation;
    _colorAdjustments.colorFilter.value = colorFilter;
  }
}
