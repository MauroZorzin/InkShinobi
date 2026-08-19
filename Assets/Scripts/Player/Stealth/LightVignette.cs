using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LightVignette : MonoBehaviour {
  [Tooltip("Player whose IsInLight state drives the vignette. Leave empty to auto-find in the scene.")]
  public PlayerStealthController player;

  [Tooltip("Global Volume whose profile receives the vignette override. Leave empty to auto-find the scene's global Volume.")]
  public Volume globalVolume;

  [Header("Vignette")]
  public Color color = Color.black;
  [Range(0f, 1f)] public float intensity = 0.4f;
  [Range(0f, 1f)] public float smoothness = 0.3f;
  public bool rounded = true;
  [Tooltip("Seconds to fade the vignette in/out.")]
  public float fadeSpeed = 3f;

  private Vignette _vignette;
  private float _currentIntensity;

  private void Awake() {
    if (globalVolume == null) {
      globalVolume = FindGlobalVolume();
    }

    if (globalVolume == null || globalVolume.profile == null) {
      Debug.LogError($"[LightVignette] {name}: No global Volume with a profile found in the scene.", this);
      enabled = false;
      return;
    }

    if (!globalVolume.profile.TryGet(out _vignette)) {
      _vignette = globalVolume.profile.Add<Vignette>(true);
    }

    _vignette.color.overrideState = true;
    _vignette.intensity.overrideState = true;
    _vignette.smoothness.overrideState = true;
    _vignette.rounded.overrideState = true;

    if (player == null) {
      player = FindFirstObjectByType<PlayerStealthController>();
    }
  }

  private static Volume FindGlobalVolume() {
    foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsSortMode.None)) {
      if (volume.isGlobal) {
        return volume;
      }
    }
    return null;
  }

  private void Update() {
    if (player == null || _vignette == null) {
      return;
    }

    float target = player.IsInLight ? intensity : 0f;
    _currentIntensity = Mathf.Lerp(_currentIntensity, target, fadeSpeed * Time.deltaTime);

    _vignette.color.value = color;
    _vignette.intensity.value = _currentIntensity;
    _vignette.smoothness.value = smoothness;
    _vignette.rounded.value = rounded;
  }
}
