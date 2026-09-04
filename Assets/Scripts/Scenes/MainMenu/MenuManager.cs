using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls main-menu navigation, confirmation dialogs, settings visibility, and related audio.
/// </summary>
public class MenuManager : MonoBehaviour {
  [Header("Scene Names")]
  [SerializeField, Tooltip("Scene loaded when the player starts a new game.")]
  private string firstSceneName = GameProgress.FirstSceneName;

  [Header("Buttons")]
  [SerializeField, Tooltip("Button used to continue from saved progress.")]
  private Button continueButton;
  [SerializeField, Tooltip("Text displayed by the Continue button.")]
  private TMP_Text continueLabel;
  [SerializeField, Tooltip("Highlights reset when the menu view changes.")]
  private StrokeHighlight[] menuHighlights;

  [Header("Settings Overlay")]
  [SerializeField, Tooltip("Main-menu content hidden while settings are open.")]
  private GameObject mainMenuPanel;
  [SerializeField, Tooltip("Settings content shown over the main menu.")]
  private GameObject settingsPanel;
  [SerializeField, Tooltip("Controller used to refresh the settings interface.")]
  private SettingsManager settingsManager;

  [Header("Transition")]
  [SerializeField, Tooltip("Font used by the scene-transition status label.")]
  private TMP_FontAsset savingFont;

  [Header("Audio")]
  [SerializeField, Tooltip("Rain ambience paused while a confirmation dialog is open.")]
  private AudioSource rainAudio;
  [SerializeField, Tooltip("Sound played when a main-menu button is selected.")]
  private AudioClip buttonClickSound;

  private ConfirmationModalView _confirmationDialog;
  private bool _restartRainAfterDialog;
  private int _rainPlaybackSample;

  private void Awake() {
    Time.timeScale = 1f;
    var canContinue = GameProgress.HasContinueProgress;
    continueButton.interactable = canContinue;
    if (canContinue) {
      continueLabel.color = Color.white;
    }
    SceneTransitionManager.SetSavingFont(savingFont);
  }

  private void Update() {
    var canHandleEscape = _confirmationDialog == null && Keyboard.current != null;
    if (canHandleEscape && Keyboard.current.escapeKey.wasPressedThisFrame) {
      if (settingsPanel.activeSelf) {
        CloseSettings();
      } else {
        ShowQuitConfirmation();
      }
    }
  }

  public void StartGame() {
    if (GameProgress.HasContinueProgress) {
      ShowNewGameConfirmation();
    } else {
      StartNewGame();
    }
  }

  public void OpenSettings() {
    mainMenuPanel.SetActive(false);
    settingsPanel.SetActive(true);
    settingsManager.RefreshUi();
    DeselectMenuHighlights();
  }

  public void CloseSettings() {
    GameSettings.Save();
    settingsPanel.SetActive(false);
    mainMenuPanel.SetActive(true);
    DeselectMenuHighlights();
  }

  public void ContinueGame() {
    if (GameProgress.HasContinueProgress) {
      SceneTransitionManager.LoadScene(GameProgress.ContinueSceneName);
    }
  }

  public void QuitGame() {
    ShowQuitConfirmation();
  }

  public void PlayMenuButtonClickSound() {
    SceneTransitionManager.PlayUiSound(buttonClickSound, rainAudio.outputAudioMixerGroup);
  }

  private void BeginNewGame() {
    if (_confirmationDialog != null) {
      CloseConfirmation(resumeRain: false, StartNewGame);
    } else {
      StartNewGame();
    }
  }

  private void StartNewGame() {
    GameProgress.Clear();
    SceneTransitionManager.LoadScene(firstSceneName);
  }

  private void ShowNewGameConfirmation() {
    if (_confirmationDialog == null) {
      OpenConfirmation("NewGameConfirmation", "Begin a new mission?", "This will erase the current path and all saved progress.", "New Game", BeginNewGame);
    }
  }

  private void ShowQuitConfirmation() {
    if (_confirmationDialog == null) {
      OpenConfirmation("QuitConfirmation", "Leave the shadows?", "Are you sure you want to quit?", "Quit Game", ConfirmQuitGame);
    }
  }

  private void OpenConfirmation(string objectName, string title, string message, string confirmText, Action onConfirm) {
    StopRainForDialog();
    _confirmationDialog = ConfirmationModalView.Create(objectName, title, message, "Cancel", confirmText, CancelConfirmation, onConfirm);
    if (_confirmationDialog == null) {
      ResumeRainAfterDialog();
    }
  }

  private void ConfirmQuitGame() {
    CloseConfirmation(resumeRain: false, QuitApplication);
  }

  private void CancelConfirmation() {
    CloseConfirmation(onClosed: DeselectMenuHighlights);
  }

  private void CloseConfirmation(bool resumeRain = true, Action onClosed = null) {
    if (_confirmationDialog != null) {
      ConfirmationModalView dialog = _confirmationDialog;
      dialog.Close(() => CompleteCloseConfirmation(dialog, resumeRain, onClosed));
    }
  }

  private void CompleteCloseConfirmation(ConfirmationModalView dialog, bool resumeRain, Action onClosed) {
    if (_confirmationDialog == dialog) {
      _confirmationDialog = null;
    }
    if (resumeRain) {
      ResumeRainAfterDialog();
    } else {
      _restartRainAfterDialog = false;
    }
    onClosed?.Invoke();
  }

  private void StopRainForDialog() {
    _restartRainAfterDialog = rainAudio.isPlaying;
    if (_restartRainAfterDialog) {
      _rainPlaybackSample = rainAudio.timeSamples;
      rainAudio.Stop();
    }
  }

  private void ResumeRainAfterDialog() {
    if (_restartRainAfterDialog) {
      rainAudio.timeSamples = Mathf.Clamp(_rainPlaybackSample, 0, rainAudio.clip.samples - 1);
      rainAudio.Play();
    }
    _restartRainAfterDialog = false;
  }

  private void DeselectMenuHighlights() {
    foreach (StrokeHighlight highlight in menuHighlights) {
      highlight.Deselect();
    }
  }

  private static void QuitApplication() {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
  }
}
