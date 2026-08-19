using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Handles main-menu button actions and confirmation dialogs.
/// </summary>
public class MenuManager : MonoBehaviour {
  [Header("Scene Names")]
  [Tooltip("Scene loaded when the player starts a new game.")]
  [SerializeField] private string firstSceneName = GameProgress.FirstSceneName;

  [Tooltip("Scene loaded when the player opens settings.")]
  [SerializeField] private string settingsSceneName = "SettingsMenu";

  [Header("Buttons")]
  [SerializeField] private Button continueButton;
  [SerializeField] private TMP_Text continueLabel;

  [Header("Transition")]
  [SerializeField] private TMP_FontAsset savingFont;

  [Header("Audio")]
  [SerializeField] private AudioSource rainAudio;
  [SerializeField] private AudioClip buttonClickSound;

  private ConfirmationModalView _newGameDialog;
  private ConfirmationModalView _quitDialog;
  private bool _restartRainAfterDialog;
  private int _rainPlaybackSample;

  private void Awake() {
    // Menus must not inherit a paused gameplay clock; particle effects such as rain use it.
    Time.timeScale = 1f;

    if (continueButton == null) {
      GameObject continueObject = GameObject.Find("Continue");
      if (continueObject != null) continueButton = continueObject.GetComponent<Button>();
    }

    bool canContinue = GameProgress.HasContinueProgress;
    if (continueButton != null) continueButton.interactable = canContinue;

    if (continueLabel == null && continueButton != null) {
      continueLabel = continueButton.GetComponentInChildren<TMP_Text>(includeInactive: true);
    }

    if (canContinue && continueLabel != null) continueLabel.color = Color.white;

    SceneTransitionManager.SetSavingFont(savingFont);
    ResolveRainAudio();
  }

  private void Update() {
    if (_newGameDialog != null || _quitDialog != null) return;
    if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

    ShowQuitConfirmation();
  }

  public void StartGame() {
    if (GameProgress.HasContinueProgress) {
      ShowNewGameConfirmation();
      return;
    }

    BeginNewGame();
  }

  public void OpenSettings() {
    SceneTransitionManager.LoadScene(settingsSceneName, useFade: false);
  }

  public void ContinueGame() {
    if (!GameProgress.HasContinueProgress) return;

    SceneTransitionManager.LoadScene(GameProgress.ContinueSceneName);
  }

  public void QuitGame() {
    ShowQuitConfirmation();
  }

  public void PlayMenuButtonClickSound() {
    SceneTransitionManager.PlayUiSound(
      buttonClickSound,
      rainAudio != null ? rainAudio.outputAudioMixerGroup : null
    );
  }

  private void ConfirmQuitGame() {
    CloseQuitConfirmation(resumeRain: false, QuitApplication);
  }

  private static void QuitApplication() {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
  }

  private void BeginNewGame() {
    if (_newGameDialog != null) {
      CloseNewGameConfirmation(resumeRain: false, StartNewGame);
      return;
    }

    StartNewGame();
  }

  private void StartNewGame() {
    GameProgress.Clear();
    SceneTransitionManager.LoadScene(firstSceneName);
  }

  private void ShowNewGameConfirmation() {
    if (_newGameDialog != null || _quitDialog != null) return;

    StopRainForDialog();
    _newGameDialog = ConfirmationModalView.Create(
      "NewGameConfirmation",
      "Overwrite progress?",
      "Starting a new game will overwrite all saved progress.",
      "Cancel",
      "New Game",
      CancelNewGameConfirmation,
      BeginNewGame
    );

    if (_newGameDialog == null) ResumeRainAfterDialog();
  }

  private void CloseNewGameConfirmation(bool resumeRain = true, System.Action onClosed = null) {
    if (_newGameDialog == null) return;

    ConfirmationModalView dialog = _newGameDialog;
    dialog.Close(() => CompleteCloseNewGameDialog(dialog, resumeRain, onClosed));
  }

  private void CompleteCloseNewGameDialog(
    ConfirmationModalView dialog,
    bool resumeRain,
    System.Action onClosed
  ) {
    if (_newGameDialog == dialog) _newGameDialog = null;

    if (resumeRain) ResumeRainAfterDialog();
    else _restartRainAfterDialog = false;
    onClosed?.Invoke();
  }

  private void ShowQuitConfirmation() {
    if (_quitDialog != null || _newGameDialog != null) return;

    StopRainForDialog();
    _quitDialog = ConfirmationModalView.Create(
      "QuitConfirmation",
      "Quit the game?",
      "Are you sure you want to quit?",
      "Cancel",
      "Quit Game",
      CancelQuitConfirmation,
      ConfirmQuitGame
    );

    if (_quitDialog == null) ResumeRainAfterDialog();
  }

  private void CloseQuitConfirmation(bool resumeRain = true, System.Action onClosed = null) {
    if (_quitDialog == null) return;

    ConfirmationModalView dialog = _quitDialog;
    dialog.Close(() => CompleteCloseQuitDialog(dialog, resumeRain, onClosed));
  }

  private void CompleteCloseQuitDialog(
    ConfirmationModalView dialog,
    bool resumeRain,
    System.Action onClosed
  ) {
    if (_quitDialog == dialog) _quitDialog = null;

    if (resumeRain) ResumeRainAfterDialog();
    else _restartRainAfterDialog = false;
    onClosed?.Invoke();
  }

  private void StopRainForDialog() {
    ResolveRainAudio();
    _restartRainAfterDialog = rainAudio != null && rainAudio.isPlaying;
    if (!_restartRainAfterDialog) return;

    _rainPlaybackSample = rainAudio.timeSamples;
    rainAudio.Stop();
  }

  private void ResumeRainAfterDialog() {
    if (_restartRainAfterDialog && rainAudio != null) {
      if (rainAudio.clip != null) {
        rainAudio.timeSamples = Mathf.Clamp(_rainPlaybackSample, 0, rainAudio.clip.samples - 1);
      }
      rainAudio.Play();
    }
    _restartRainAfterDialog = false;
  }

  private void ResolveRainAudio() {
    if (rainAudio != null) return;

    GameObject ambientObject = GameObject.Find("Ambient");
    if (ambientObject != null) rainAudio = ambientObject.GetComponent<AudioSource>();

    if (rainAudio != null) return;

    AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
    foreach (AudioSource source in sources) {
      if (source.clip == null || !source.clip.name.Contains("rain", System.StringComparison.OrdinalIgnoreCase)) continue;

      rainAudio = source;
      break;
    }
  }

  private void CancelNewGameConfirmation() {
    CloseNewGameConfirmation(onClosed: DeselectMenuHighlights);
  }

  private void CancelQuitConfirmation() {
    CloseQuitConfirmation(onClosed: DeselectMenuHighlights);
  }

  private static void DeselectMenuHighlights() {
    StrokeHighlight[] highlights = FindObjectsByType<StrokeHighlight>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None
    );
    foreach (StrokeHighlight highlight in highlights) highlight.Deselect();
  }
}
