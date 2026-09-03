using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Drives the main-menu settings overlay.</summary>
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
    new("Switch Walls", "Player", "Switch"),
    new("Confirm Aim", "Left Mouse Button"),
    new("Aim Distraction", "Right Mouse Button"),
    new("Pause / Back", "Player", "Exit")
  };

  [Header("Overlay")]
  [SerializeField] private MenuManager menuManager;

  [Header("Audio")]
  [SerializeField] private AudioMixer audioMixer;
  [SerializeField] private Slider musicSlider;
  [SerializeField] private Slider sfxSlider;
  [SerializeField] private TMP_Text musicValueLabel;
  [SerializeField] private TMP_Text sfxValueLabel;

  [Header("Display")]
  [SerializeField] private TMP_Text resolutionLabel;
  [SerializeField] private Button previousResolutionButton;
  [SerializeField] private Button nextResolutionButton;

  [Header("Controls")]
  [SerializeField] private InputActionAsset inputActions;
  [SerializeField] private TMP_Text controlNamesLabel;
  [SerializeField] private TMP_Text controlBindingsLabel;

  [Header("Actions")]
  [SerializeField] private Button restoreDefaultsButton;
  [SerializeField] private Button backButton;

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
    if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
    if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(SetSfxVolume);
    if (previousResolutionButton != null)
      previousResolutionButton.onClick.RemoveListener(PreviousResolution);
    if (nextResolutionButton != null)
      nextResolutionButton.onClick.RemoveListener(NextResolution);
    if (restoreDefaultsButton != null)
      restoreDefaultsButton.onClick.RemoveListener(RestoreDefaults);
    if (backButton != null) backButton.onClick.RemoveListener(Done);
  }

  public void RefreshUi() {
    _suppressCallbacks = true;
    if (musicSlider != null)
      musicSlider.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.MusicVolume * 100f));
    if (sfxSlider != null)
      sfxSlider.SetValueWithoutNotify(Mathf.RoundToInt(GameSettings.SfxVolume * 100f));
    UpdateVolumeLabels();
    SelectCurrentResolution();
    UpdateResolutionLabel();
    RefreshControls();
    _suppressCallbacks = false;
  }

  public void Done() {
    GameSettings.Save();
    if (menuManager == null) {
      Debug.LogError("[SettingsManager] MenuManager is not assigned.", this);
      return;
    }

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
    if (musicSlider != null) musicSlider.onValueChanged.AddListener(SetMusicVolume);
    if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(SetSfxVolume);
    if (previousResolutionButton != null)
      previousResolutionButton.onClick.AddListener(PreviousResolution);
    if (nextResolutionButton != null)
      nextResolutionButton.onClick.AddListener(NextResolution);
    if (restoreDefaultsButton != null)
      restoreDefaultsButton.onClick.AddListener(RestoreDefaults);
    if (backButton != null) backButton.onClick.AddListener(Done);
  }

  private void SetMusicVolume(float value) {
    if (_suppressCallbacks) return;
    GameSettings.MusicVolume = value / 100f;
    GameSettings.ApplyAudio(audioMixer);
    UpdateVolumeLabels();
  }

  private void SetSfxVolume(float value) {
    if (_suppressCallbacks) return;
    GameSettings.SfxVolume = value / 100f;
    GameSettings.ApplyAudio(audioMixer);
    UpdateVolumeLabels();
  }

  private void UpdateVolumeLabels() {
    if (musicValueLabel != null)
      musicValueLabel.text = $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}%";
    if (sfxValueLabel != null)
      sfxValueLabel.text = $"{Mathf.RoundToInt(GameSettings.SfxVolume * 100f)}%";
  }

  private static void ConfigureVolumeSlider(Slider slider) {
    if (slider == null) return;

    slider.minValue = 0f;
    slider.maxValue = 100f;
    slider.wholeNumbers = true;
  }

  private void BuildResolutionList() {
    _resolutions.Clear();
    var seen = new HashSet<Vector2Int>();
    foreach (Resolution resolution in Screen.resolutions) {
      var size = new Vector2Int(resolution.width, resolution.height);
      if (!GameSettings.IsSixteenByNine(size.x, size.y)) continue;
      if (seen.Add(size)) _resolutions.Add(size);
    }

    var current = new Vector2Int(Screen.width, Screen.height);
    if (GameSettings.IsSixteenByNine(current.x, current.y) && seen.Add(current))
      _resolutions.Add(current);
    _resolutions.Sort((left, right) => {
      int pixels = (left.x * left.y).CompareTo(right.x * right.y);
      return pixels != 0 ? pixels : left.x.CompareTo(right.x);
    });
  }

  private void SelectCurrentResolution() {
    if (_resolutions.Count == 0) return;

    Vector2Int target = GameSettings.HasSavedResolution
      ? GameSettings.SavedResolution
      : new Vector2Int(Screen.width, Screen.height);
    int index = _resolutions.IndexOf(target);
    _resolutionIndex = index >= 0 ? index : _resolutions.Count - 1;
  }

  private void StepResolution(int direction) {
    if (_resolutions.Count == 0) return;

    _resolutionIndex = (_resolutionIndex + direction + _resolutions.Count) % _resolutions.Count;
    Vector2Int resolution = _resolutions[_resolutionIndex];
    GameSettings.SetResolution(resolution.x, resolution.y);
    UpdateResolutionLabel();
    PlayButtonSound();
  }

  private void UpdateResolutionLabel() {
    if (resolutionLabel == null) return;
    if (_resolutions.Count == 0) {
      resolutionLabel.text = $"{Screen.width} x {Screen.height}";
      return;
    }

    Vector2Int resolution = _resolutions[_resolutionIndex];
    resolutionLabel.text = $"{resolution.x} x {resolution.y}";
  }

  private void RefreshControls() {
    if (controlNamesLabel == null || controlBindingsLabel == null) return;

    var names = new List<string>();
    var bindings = new List<string>();
    foreach (ControlEntry entry in DisplayedControls) {
      if (!string.IsNullOrEmpty(entry.BindingOverride)) {
        names.Add(entry.Label);
        bindings.Add(entry.BindingOverride);
        continue;
      }

      InputAction action = inputActions?.FindActionMap(entry.Map, false)?.FindAction(entry.Action, false);
      if (action == null) continue;

      names.Add(entry.Label);
      bindings.Add(GetKeyboardMouseBindings(action));
    }

    controlNamesLabel.text = string.Join("\n", names);
    controlBindingsLabel.text = string.Join("\n", bindings);
  }

  private static string GetKeyboardMouseBindings(InputAction action) {
    var displayStrings = new List<string>();
    for (int index = 0; index < action.bindings.Count; index++) {
      InputBinding binding = action.bindings[index];
      if (binding.isComposite) continue;
      if (!string.IsNullOrEmpty(binding.groups)
          && !binding.groups.Contains("Keyboard&Mouse")) continue;

      string display = action.GetBindingDisplayString(
        index,
        InputBinding.DisplayStringOptions.DontIncludeInteractions
      );
      if (!string.IsNullOrEmpty(display) && !displayStrings.Contains(display))
        displayStrings.Add(display);
    }

    return displayStrings.Count > 0 ? string.Join(" / ", displayStrings) : "Unbound";
  }

  private void PlayButtonSound() {
    menuManager?.PlayMenuButtonClickSound();
  }
}
