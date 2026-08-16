using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles main-menu button actions and the new-game overwrite confirmation.
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

  private GameObject _newGameDialog;
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

  /// <summary>
  /// Exits play mode in the editor or quits the application in builds without a transition.
  /// </summary>
  public void QuitGame() {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
  }

  private void BeginNewGame() {
    CloseNewGameConfirmation(resumeRain: false);
    GameProgress.Clear();
    SceneTransitionManager.LoadScene(firstSceneName);
  }

  private void ShowNewGameConfirmation() {
    if (_newGameDialog != null) return;

    ResolveRainAudio();
    _restartRainAfterDialog = rainAudio != null && rainAudio.isPlaying;
    if (_restartRainAfterDialog) {
      _rainPlaybackSample = rainAudio.timeSamples;
      rainAudio.Stop();
    }

    _newGameDialog = new GameObject("NewGameConfirmation", typeof(RectTransform));

    Canvas canvas = _newGameDialog.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 1000;

    CanvasScaler scaler = _newGameDialog.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    _newGameDialog.AddComponent<GraphicRaycaster>();
    _newGameDialog.AddComponent<PopupBackgroundBlur>().Initialize(canvas);

    Image shade = CreateImage("Shade", _newGameDialog.transform, new Color(0f, 0f, 0f, 0.55f));
    Stretch(shade.rectTransform);

    Image panel = CreateImage("Panel", shade.transform, new Color(0.08f, 0.08f, 0.08f, 0.98f));
    RectTransform panelTransform = panel.rectTransform;
    panelTransform.anchorMin = new Vector2(0.5f, 0.5f);
    panelTransform.anchorMax = new Vector2(0.5f, 0.5f);
    panelTransform.sizeDelta = new Vector2(760f, 340f);
    panelTransform.anchoredPosition = Vector2.zero;

    CreateText(
      "Title",
      panel.transform,
      "Overwrite progress?",
      38f,
      new Vector2(0f, 100f),
      new Vector2(680f, 60f)
    );
    CreateText(
      "Message",
      panel.transform,
      "Starting a new game will overwrite all saved progress.",
      26f,
      new Vector2(0f, 30f),
      new Vector2(650f, 80f)
    );

    Button cancelButton = CreateButton(
      "Cancel",
      panel.transform,
      "Cancel",
      new Vector2(-175f, -105f)
    );
    cancelButton.onClick.AddListener(CancelNewGameConfirmation);

    Button confirmButton = CreateButton(
      "ConfirmStart",
      panel.transform,
      "Start New Game",
      new Vector2(175f, -105f)
    );
    confirmButton.onClick.AddListener(BeginNewGame);

    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
  }

  private void CloseNewGameConfirmation(bool resumeRain = true) {
    if (_newGameDialog == null) return;

    Destroy(_newGameDialog);
    _newGameDialog = null;

    if (resumeRain && _restartRainAfterDialog && rainAudio != null) {
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
    CloseNewGameConfirmation();

    StrokeHighlight[] highlights = FindObjectsByType<StrokeHighlight>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None
    );
    foreach (StrokeHighlight highlight in highlights) highlight.Deselect();
  }

  internal static Image CreateImage(string objectName, Transform parent, Color color) {
    var imageObject = new GameObject(objectName, typeof(RectTransform));
    imageObject.transform.SetParent(parent, false);
    Image image = imageObject.AddComponent<Image>();
    image.color = color;
    return image;
  }

  internal static TextMeshProUGUI CreateText(
    string objectName,
    Transform parent,
    string content,
    float fontSize,
    Vector2 position,
    Vector2 size
  ) {
    var textObject = new GameObject(objectName, typeof(RectTransform));
    textObject.transform.SetParent(parent, false);

    TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
    text.text = content;
    text.fontSize = fontSize;
    text.color = Color.white;
    text.alignment = TextAlignmentOptions.Center;

    RectTransform rectTransform = text.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.sizeDelta = size;
    rectTransform.anchoredPosition = position;
    return text;
  }

  internal static Button CreateButton(
    string objectName,
    Transform parent,
    string label,
    Vector2 position
  ) {
    Image image = CreateImage(objectName, parent, new Color(0.22f, 0.22f, 0.22f, 1f));
    RectTransform rectTransform = image.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.sizeDelta = new Vector2(280f, 70f);
    rectTransform.anchoredPosition = position;

    Button button = image.gameObject.AddComponent<Button>();
    button.targetGraphic = image;
    CreateText("Label", button.transform, label, 25f, Vector2.zero, rectTransform.sizeDelta);
    return button;
  }

  internal static void Stretch(RectTransform rectTransform) {
    rectTransform.anchorMin = Vector2.zero;
    rectTransform.anchorMax = Vector2.one;
    rectTransform.offsetMin = Vector2.zero;
    rectTransform.offsetMax = Vector2.zero;
  }
}
