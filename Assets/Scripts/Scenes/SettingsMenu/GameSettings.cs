using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Stores user-facing settings independently from game progress and applies settings that must
/// survive scene changes. Values use PlayerPrefs because the data is small and platform-local.
/// </summary>
public static class GameSettings {
  public const float DefaultMusicVolume = 0.75f;
  public const float DefaultSfxVolume = 1f;

  private const string MusicVolumeKey = "Settings.Audio.Music";
  private const string SfxVolumeKey = "Settings.Audio.Sfx";
  private const string ResolutionWidthKey = "Settings.Display.Width";
  private const string ResolutionHeightKey = "Settings.Display.Height";

  // These accessors hold no static state; their values live entirely in PlayerPrefs.
#pragma warning disable UDR0001, UDR0002
  public static float MusicVolume {
    get => PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
    set => PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
  }

  public static float SfxVolume {
    get => PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume);
    set => PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
  }
#pragma warning restore UDR0001, UDR0002

  public static bool HasSavedResolution =>
    PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey);

  public static Vector2Int SavedResolution => new(
    PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width),
    PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height)
  );

  public static void SetResolution(int width, int height) {
    if (!IsSixteenByNine(width, height)) return;

    PlayerPrefs.SetInt(ResolutionWidthKey, width);
    PlayerPrefs.SetInt(ResolutionHeightKey, height);
    Screen.SetResolution(width, height, Screen.fullScreenMode);
    PlayerPrefs.Save();
  }

  public static void ApplyAudio(AudioMixer mixer) {
    if (mixer == null) return;

    mixer.SetFloat("musicVolume", SliderToDecibels(MusicVolume));
    mixer.SetFloat("fxVolume", SliderToDecibels(SfxVolume));
  }

  public static void ApplySavedResolution() {
    if (!HasSavedResolution) return;

    Vector2Int resolution = SavedResolution;
    if (!IsSixteenByNine(resolution.x, resolution.y)) {
      PlayerPrefs.DeleteKey(ResolutionWidthKey);
      PlayerPrefs.DeleteKey(ResolutionHeightKey);
      PlayerPrefs.Save();
      return;
    }
    if (Screen.width == resolution.x && Screen.height == resolution.y) return;

    Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
  }

  public static void RestoreDefaults(AudioMixer mixer) {
    MusicVolume = DefaultMusicVolume;
    SfxVolume = DefaultSfxVolume;
    PlayerPrefs.DeleteKey(ResolutionWidthKey);
    PlayerPrefs.DeleteKey(ResolutionHeightKey);
    ApplyAudio(mixer);
    PlayerPrefs.Save();
  }

  public static void Save() => PlayerPrefs.Save();

  public static bool IsSixteenByNine(int width, int height) {
    if (width <= 0 || height <= 0) return false;
    // Accept nominal 16:9 modes such as 1366x768 (sub-pixel ratio rounding), while rejecting
    // visibly different modes such as 1360x768.
    return Mathf.Abs(width * 9 - height * 16) <= 9;
  }

  /// <summary>
  /// Maps linear perceived volume to decibels. A logarithmic conversion keeps the upper part of
  /// the slider useful: 80% is about -1.9 dB and 50% is about -6 dB instead of -16/-40 dB.
  /// </summary>
  private static float SliderToDecibels(float value) {
    value = Mathf.Clamp01(value);
    return value <= 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(value));
  }
}
