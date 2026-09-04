using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls the main-menu settings interface and applies the selected audio, display, and input settings.
/// </summary>
public class SettingsManager : MonoBehaviour {
  private readonly struct ControlEntry {
    public readonly string Label;
    public readonly string Map;
    public readonly string Action;
    public readonly string BindingOverride;

    public ControlEntry(string label, string map, string action) {
      Label = label;
      Map = map;
      Action = action;
      BindingOverride = null;
    }

    public ControlEntry(string label, string bindingOverride) {
      Label = label;
      Map = null;
      Action = null;
      BindingOverride = bindingOverride;
    }
  }

  private static readonly ControlEntry[] DisplayedControls = {
    new("Move", "Player", "Move"),
    new("Interact / Hide", "Player", "Interact"),
    new("Switch Walls", "Space bar"),
    new("Confirm Aim", "Left Mouse Button"),
    new("Aim Distraction", "Right Mouse Button"),
    new("Pause / Back", "Player", "Exit")
  };

  [Header("Overlay")]
  [SerializeField, Tooltip("Main-menu controller used to close the settings overlay and play button sounds.")]
  private MenuManager menuManager;

  [Header("Audio")]
  [SerializeField, Tooltip("Mixer whose exposed music and sound-effect levels are updated by the sliders.")]
  private AudioMixer audioMixer;
  [SerializeField, Tooltip("Slider used to set music volume.")]
  private Slider musicSlider;
  [SerializeField, Tooltip("Slider used to set sound-effect volume.")]
  private Slider sfxSlider;
  [SerializeField, Tooltip("Text displaying the current music-volume percentage.")]
  private TMP_Text musicValueLabel;
  [SerializeField, Tooltip("Text displaying the current sound-effect-volume percentage.")]
  private TMP_Text sfxValueLabel;

  [Header("Display")]
  [SerializeField, Tooltip("Text displaying the selected screen resolution.")]
  private TMP_Text resolutionLabel;
  [SerializeField, Tooltip("Button selecting the previous available resolution.")]
  private Button previousResolutionButton;
  [SerializeField, Tooltip("Button selecting the next available resolution.")]
  private Button nextResolutionButton;

  [Header("Controls")]
  [SerializeField, Tooltip("Input actions used to read the displayed keyboard and mouse bindings.")]
  private InputActionAsset inputActions;
  [SerializeField, Tooltip("Text displaying control names.")]
  private TMP_Text controlNamesLabel;
  [SerializeField, Tooltip("Text displaying the bindings corresponding to each control name.")]
  private TMP_Text controlBindingsLabel;

  [Header("Actions")]
  [SerializeField, Tooltip("Button restoring every setting to its default value.")]
  private Button restoreDefaultsButton;
  [SerializeField, Tooltip("Button saving the settings and returning to the main menu.")]
  private Button backButton;

  private readonly List<Vector2Int> _resolutions = new();
  private int _resolutionIndex;
  private bool _suppressCallbacks;

  private void Awake() {
    ConfigureVolumeSlider(musicSlider);
    ConfigureVolumeSlider(sfxSlider);
    BindUi();
    BuildResolutionList();
    GameSettings.ApplyAudio(audioMixer);
    GameSettings.ApplySavedResolution();
    RefreshUi();
  }

  private void OnDestroy() {
    musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
    sfxSlider.onValueChanged.RemoveListener(SetSfxVolume);
    previousResolutionButton.onClick.RemoveListener(PreviousResolution);
    nextResolutionButton.onClick.RemoveListener(NextResolution);
    restoreDefaultsButton.onClick.RemoveListener(RestoreDefaults);
    backButton.onClick.RemoveListener(Done);
  }

  public void RefreshUi() {
    _suppressCallbacks = true;
    musicSlider.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.MusicVolume * 100f));
    sfxSlider.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.SfxVolume * 100f));
    UpdateVolumeLabels();
    SelectCurrentResolution();
    UpdateResolutionLabel();
    RefreshControls();
    _suppressCallbacks = false;
  }

  public void Done() {
    GameSettings.Save();
    PlayButtonSound();
    menuManager.CloseSettings();
  }

  public void RestoreDefaults() {
    GameSettings.RestoreDefaults(audioMixer);
    RefreshUi();
    PlayButtonSound();
  }

  public void PreviousResolution() {
    StepResolution(-1);
  }

  public void NextResolution() {
    StepResolution(1);
  }

  private void BindUi() {
    musicSlider.onValueChanged.AddListener(SetMusicVolume);
    sfxSlider.onValueChanged.AddListener(SetSfxVolume);
    previousResolutionButton.onClick.AddListener(PreviousResolution);
    nextResolutionButton.onClick.AddListener(NextResolution);
    restoreDefaultsButton.onClick.AddListener(RestoreDefaults);
    backButton.onClick.AddListener(Done);
  }

  private void SetMusicVolume(float value) {
    if (!_suppressCallbacks) {
      GameSettings.MusicVolume = value / 100f;
      GameSettings.ApplyAudio(audioMixer);
      UpdateVolumeLabels();
    }
  }

  private void SetSfxVolume(float value) {
    if (!_suppressCallbacks) {
      GameSettings.SfxVolume = value / 100f;
      GameSettings.ApplyAudio(audioMixer);
      UpdateVolumeLabels();
    }
  }

  private void UpdateVolumeLabels() {
    musicValueLabel.text = $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}%";
    sfxValueLabel.text = $"{Mathf.RoundToInt(GameSettings.SfxVolume * 100f)}%";
  }

  private void BuildResolutionList() {
    _resolutions.Clear();
    var seen = new HashSet<Vector2Int>();
    foreach (Resolution resolution in Screen.resolutions) {
      var size = new Vector2Int(resolution.width, resolution.height);
      if (GameSettings.IsSixteenByNine(size.x, size.y) && seen.Add(size)) {
        _resolutions.Add(size);
      }
    }
    var current = new Vector2Int(Screen.width, Screen.height);
    if (GameSettings.IsSixteenByNine(current.x, current.y) && seen.Add(current)) {
      _resolutions.Add(current);
    }
    _resolutions.Sort((left, right) => {
      var pixelComparison = (left.x * left.y).CompareTo(right.x * right.y);
      return pixelComparison != 0 ? pixelComparison : left.x.CompareTo(right.x);
    });
  }

  private void SelectCurrentResolution() {
    if (_resolutions.Count > 0) {
      Vector2Int target = GameSettings.HasSavedResolution ? GameSettings.SavedResolution : new Vector2Int(Screen.width, Screen.height);
      var index = _resolutions.IndexOf(target);
      _resolutionIndex = index >= 0 ? index : _resolutions.Count - 1;
    }
  }

  private void StepResolution(int direction) {
    if (_resolutions.Count > 0) {
      _resolutionIndex = (_resolutionIndex + direction + _resolutions.Count) % _resolutions.Count;
      Vector2Int resolution = _resolutions[_resolutionIndex];
      GameSettings.SetResolution(resolution.x, resolution.y);
      UpdateResolutionLabel();
      PlayButtonSound();
    }
  }

  private void UpdateResolutionLabel() {
    if (_resolutions.Count > 0) {
      Vector2Int resolution = _resolutions[_resolutionIndex];
      resolutionLabel.text = $"{resolution.x} x {resolution.y}";
    } else {
      resolutionLabel.text = $"{Screen.width} x {Screen.height}";
    }
  }

  private void RefreshControls() {
    var names = new List<string>();
    var bindings = new List<string>();
    foreach (ControlEntry entry in DisplayedControls) {
      names.Add(entry.Label);
      if (!string.IsNullOrEmpty(entry.BindingOverride)) {
        bindings.Add(entry.BindingOverride);
      } else {
        InputAction action = inputActions.FindActionMap(entry.Map, true).FindAction(entry.Action, true);
        bindings.Add(GetKeyboardMouseBindings(action));
      }
    }
    controlNamesLabel.text = string.Join("\n", names);
    controlBindingsLabel.text = string.Join("\n", bindings);
  }

  private void PlayButtonSound() {
    menuManager.PlayMenuButtonClickSound();
  }

  private static void ConfigureVolumeSlider(Slider slider) {
    slider.minValue = 0f;
    slider.maxValue = 100f;
    slider.wholeNumbers = true;
  }

  private static string GetKeyboardMouseBindings(InputAction action) {
    var displayStrings = new List<string>();
    for (var index = 0; index < action.bindings.Count; index++) {
      InputBinding binding = action.bindings[index];
      var usesKeyboardAndMouse = string.IsNullOrEmpty(binding.groups) || binding.groups.Contains("Keyboard&Mouse");
      if (!binding.isComposite && usesKeyboardAndMouse) {
        var display = action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        if (!string.IsNullOrEmpty(display) && !displayStrings.Contains(display)) {
          displayStrings.Add(display);
        }
      }
    }
    return displayStrings.Count > 0 ? string.Join(" / ", displayStrings) : "Unbound";
  }
}
