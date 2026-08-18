using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles main-menu button actions and confirmation dialogs.
/// </summary>
public class MenuManager : MonoBehaviour {
  private const float ModalButtonHighlightXOffset = 35f;
  private const float ModalOpenSoundStartOffset = 0.08f;

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

  [Header("Modal Style")]
  [SerializeField] private Sprite modalPanelSprite;
  [SerializeField] private AudioClip modalOpenSound;

  private GameObject _newGameDialog;
  private GameObject _quitDialog;
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
    CaptureModalButtonStyle();
    if (buttonClickSound != null) ModalButtonStyle.SetClickSound(buttonClickSound);
    if (modalPanelSprite != null) ModalButtonStyle.SetPanelSprite(modalPanelSprite, 2f);
    if (modalOpenSound != null) ModalButtonStyle.SetModalOpenSound(modalOpenSound);
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
    PlayButtonClickSound();
  }

  private void ConfirmQuitGame() {
    CloseQuitConfirmation(resumeRain: false);

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
    if (_newGameDialog != null || _quitDialog != null) return;

    PlayModalOpenSound(transform);
    StopRainForDialog();

    _newGameDialog = new GameObject("NewGameConfirmation", typeof(RectTransform));

    Canvas canvas = _newGameDialog.AddComponent<Canvas>();
    ConfigureModalCanvas(canvas);

    CanvasScaler scaler = _newGameDialog.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    _newGameDialog.AddComponent<GraphicRaycaster>();
    _newGameDialog.AddComponent<PopupBackgroundBlur>().Initialize(canvas);

    Image shade = CreateImage("Shade", _newGameDialog.transform, new Color(0f, 0f, 0f, 0.55f));
    Stretch(shade.rectTransform);

    Image panel = CreateModalPanel(shade.transform);
    RectTransform panelTransform = panel.rectTransform;
    panelTransform.anchorMin = new Vector2(0.5f, 0.5f);
    panelTransform.anchorMax = new Vector2(0.5f, 0.5f);
    panelTransform.sizeDelta = new Vector2(940f, 480f);
    panelTransform.anchoredPosition = Vector2.zero;

    CreateText(
      "Title",
      panel.transform,
      "Overwrite progress?",
      72f,
      new Vector2(0f, 130f),
      new Vector2(840f, 110f),
      FontStyles.Bold | FontStyles.SmallCaps
    );
    CreateText(
      "Message",
      panel.transform,
      "Starting a new game will overwrite all saved progress.",
      42f,
      new Vector2(0f, 20f),
      new Vector2(820f, 150f),
      FontStyles.Bold | FontStyles.SmallCaps
    );

    Button cancelButton = CreateButton(
      "Cancel",
      panel.transform,
      "Cancel",
      new Vector2(-205f, -145f)
    );
    cancelButton.onClick.AddListener(CancelNewGameConfirmation);

    Button confirmButton = CreateButton(
      "ConfirmStart",
      panel.transform,
      "New Game",
      new Vector2(205f, -145f)
    );
    confirmButton.onClick.AddListener(BeginNewGame);

    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
  }

  private void CloseNewGameConfirmation(bool resumeRain = true) {
    if (_newGameDialog == null) return;

    Destroy(_newGameDialog);
    _newGameDialog = null;

    if (resumeRain) ResumeRainAfterDialog();
    else _restartRainAfterDialog = false;
  }

  private void ShowQuitConfirmation() {
    if (_quitDialog != null || _newGameDialog != null) return;

    PlayModalOpenSound(transform);
    StopRainForDialog();

    _quitDialog = new GameObject("QuitConfirmation", typeof(RectTransform));

    Canvas canvas = _quitDialog.AddComponent<Canvas>();
    ConfigureModalCanvas(canvas);

    CanvasScaler scaler = _quitDialog.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    _quitDialog.AddComponent<GraphicRaycaster>();
    _quitDialog.AddComponent<PopupBackgroundBlur>().Initialize(canvas);

    Image shade = CreateImage("Shade", _quitDialog.transform, new Color(0f, 0f, 0f, 0.55f));
    Stretch(shade.rectTransform);

    Image panel = CreateModalPanel(shade.transform);
    RectTransform panelTransform = panel.rectTransform;
    panelTransform.anchorMin = new Vector2(0.5f, 0.5f);
    panelTransform.anchorMax = new Vector2(0.5f, 0.5f);
    panelTransform.sizeDelta = new Vector2(940f, 480f);
    panelTransform.anchoredPosition = Vector2.zero;

    CreateText(
      "Title",
      panel.transform,
      "Quit the game?",
      72f,
      new Vector2(0f, 130f),
      new Vector2(840f, 110f),
      FontStyles.Bold | FontStyles.SmallCaps
    );
    CreateText(
      "Message",
      panel.transform,
      "Are you sure you want to quit?",
      42f,
      new Vector2(0f, 20f),
      new Vector2(820f, 150f),
      FontStyles.Bold | FontStyles.SmallCaps
    );

    Button cancelButton = CreateButton(
      "Cancel",
      panel.transform,
      "Cancel",
      new Vector2(-205f, -145f)
    );
    cancelButton.onClick.AddListener(CancelQuitConfirmation);

    Button quitButton = CreateButton(
      "ConfirmQuit",
      panel.transform,
      "Quit Game",
      new Vector2(205f, -145f)
    );
    quitButton.onClick.AddListener(ConfirmQuitGame);

    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
  }

  private void CloseQuitConfirmation(bool resumeRain = true) {
    if (_quitDialog == null) return;

    Destroy(_quitDialog);
    _quitDialog = null;

    if (resumeRain) ResumeRainAfterDialog();
    else _restartRainAfterDialog = false;
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

  private static void CaptureModalButtonStyle() {
    StrokeHighlight[] highlights = FindObjectsByType<StrokeHighlight>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None
    );

    foreach (StrokeHighlight highlight in highlights) {
      highlight.EnsureStyleReferences();
      if (highlight.StyleFont == null || highlight.StyleBrushSprite == null) continue;

      ModalButtonStyle.Capture(highlight);
      break;
    }
  }

  private void CancelNewGameConfirmation() {
    CloseNewGameConfirmation();

    DeselectMenuHighlights();
  }

  private void CancelQuitConfirmation() {
    CloseQuitConfirmation();

    DeselectMenuHighlights();
  }

  private static void DeselectMenuHighlights() {
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
    Vector2 size,
    FontStyles fontStyle = FontStyles.Normal
  ) {
    var textObject = new GameObject(objectName, typeof(RectTransform));
    textObject.transform.SetParent(parent, false);

    TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
    text.text = content;
    if (ModalButtonStyle.IsConfigured) {
      text.font = ModalButtonStyle.Font;
      if (ModalButtonStyle.FontMaterial != null) {
        text.fontSharedMaterial = ModalButtonStyle.FontMaterial;
      }
    }
    text.fontSize = fontSize;
    text.fontStyle = fontStyle;
    text.color = Color.black;
    text.outlineColor = Color.white;
    text.outlineWidth = 0.18f;
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
    Image hitArea = CreateImage(
      objectName,
      parent,
      ModalButtonStyle.IsConfigured ? Color.clear : new Color(0.22f, 0.22f, 0.22f, 1f)
    );
    RectTransform rectTransform = hitArea.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.sizeDelta = ModalButtonStyle.IsConfigured
      ? new Vector2(320f, 80f)
      : new Vector2(280f, 70f);
    rectTransform.anchoredPosition = position;

    Button button = hitArea.gameObject.AddComponent<Button>();
    button.transition = Selectable.Transition.None;
    button.targetGraphic = hitArea;

    if (!ModalButtonStyle.IsConfigured) {
      CreateText("Label", button.transform, label, 25f, Vector2.zero, rectTransform.sizeDelta);
      return button;
    }

    Image brushStroke = CreateImage("Highlight", button.transform, ModalButtonStyle.BrushColor);
    Stretch(brushStroke.rectTransform);
    brushStroke.rectTransform.anchoredPosition = new Vector2(ModalButtonHighlightXOffset, 0f);
    brushStroke.sprite = ModalButtonStyle.BrushSprite;
    brushStroke.type = Image.Type.Filled;
    brushStroke.fillMethod = Image.FillMethod.Horizontal;
    brushStroke.fillOrigin = (int)Image.OriginHorizontal.Left;
    brushStroke.fillAmount = 0f;
    brushStroke.raycastTarget = false;

    TextMeshProUGUI buttonText = CreateText(
      "Label",
      button.transform,
      label,
      46f,
      Vector2.zero,
      rectTransform.sizeDelta
    );
    buttonText.fontStyle = ModalButtonStyle.FontStyle;
    buttonText.enableAutoSizing = true;
    buttonText.fontSizeMin = 24f;
    buttonText.fontSizeMax = 46f;

    AudioSource hoverSource = button.gameObject.AddComponent<AudioSource>();
    hoverSource.playOnAwake = false;
    hoverSource.loop = false;
    hoverSource.spatialBlend = 0f;
    hoverSource.ignoreListenerPause = true;
    hoverSource.outputAudioMixerGroup = ModalButtonStyle.MixerGroup;

    StrokeHighlight highlight = button.gameObject.AddComponent<StrokeHighlight>();
    highlight.Configure(
      button,
      brushStroke,
      buttonText,
      hoverSource,
      ModalButtonStyle.HoverSound,
      ModalButtonStyle.PaintInDuration,
      ModalButtonStyle.FadeOutDuration,
      useWhiteTextOnHover: false,
      useWhiteTextOnSelection: false
    );
    button.onClick.AddListener(() => PlayButtonClickSound());

    return button;
  }

  private static void PlayButtonClickSound() {
    SceneTransitionManager.PlayUiSound(
      ModalButtonStyle.ClickSound,
      ModalButtonStyle.MixerGroup
    );
  }

  internal static void Stretch(RectTransform rectTransform) {
    rectTransform.anchorMin = Vector2.zero;
    rectTransform.anchorMax = Vector2.one;
    rectTransform.offsetMin = Vector2.zero;
    rectTransform.offsetMax = Vector2.zero;
  }

  internal static Image CreateModalPanel(Transform parent) {
    bool usePaperPanel = ModalButtonStyle.PanelSprite != null;
    Image panel = CreateImage(
      "Panel",
      parent,
      usePaperPanel ? Color.white : new Color(0.08f, 0.08f, 0.08f, 0.98f)
    );

    if (usePaperPanel) {
      panel.sprite = ModalButtonStyle.PanelSprite;
      panel.type = Image.Type.Sliced;
      panel.pixelsPerUnitMultiplier = ModalButtonStyle.PanelPixelsPerUnitMultiplier;
    }

    panel.gameObject.AddComponent<ModalAppearAnimation>();
    return panel;
  }

  internal static void ConfigureModalCanvas(Canvas canvas) {
    if (canvas == null) return;

    Camera renderCamera = Camera.main;
    if (renderCamera == null) renderCamera = FindFirstObjectByType<Camera>();

    if (renderCamera != null) {
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = renderCamera;
      canvas.planeDistance = Mathf.Max(1f, renderCamera.nearClipPlane + 0.01f);
    } else {
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    canvas.sortingOrder = 1000;
  }

  internal static void PlayModalOpenSound(Transform modalRoot) {
    if (modalRoot == null || ModalButtonStyle.ModalOpenSound == null) return;

    AudioSource source = modalRoot.gameObject.AddComponent<AudioSource>();
    source.playOnAwake = false;
    source.loop = false;
    source.spatialBlend = 0f;
    source.ignoreListenerPause = true;
    source.outputAudioMixerGroup = ModalButtonStyle.MixerGroup;
    source.clip = ModalButtonStyle.ModalOpenSound;
    source.time = Mathf.Min(ModalOpenSoundStartOffset, source.clip.length - 0.001f);
    source.Play();
  }
}

internal sealed class ModalAppearAnimation : MonoBehaviour {
  private const float Duration = 0.5f;
  private static readonly Vector3 StartScale = new(0.97f, 0.08f, 1f);

  private CanvasGroup _canvasGroup;
  private Vector3 _targetScale;
  private Coroutine _animation;

  private void Awake() {
    _targetScale = transform.localScale;
    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    Prepare();
  }

  private void Prepare() {
    transform.localScale = Vector3.Scale(_targetScale, StartScale);
    _canvasGroup.alpha = 0f;
    _canvasGroup.interactable = false;
    _canvasGroup.blocksRaycasts = false;
  }

  public void Play() {
    if (_animation != null) StopCoroutine(_animation);
    Prepare();
    _animation = StartCoroutine(Animate());
  }

  private System.Collections.IEnumerator Animate() {
    float elapsed = 0f;
    Vector3 initialScale = transform.localScale;

    while (elapsed < Duration) {
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / Duration);
      float eased = 1f - Mathf.Pow(1f - progress, 3f);

      transform.localScale = Vector3.LerpUnclamped(initialScale, _targetScale, eased);
      _canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 1.5f));
      yield return null;
    }

    transform.localScale = _targetScale;
    _canvasGroup.alpha = 1f;
    _canvasGroup.interactable = true;
    _canvasGroup.blocksRaycasts = true;
    _animation = null;
  }
}

internal static class ModalButtonStyle {
  public static bool IsConfigured { get; private set; }
  public static TMP_FontAsset Font { get; private set; }
  public static Material FontMaterial { get; private set; }
  public static FontStyles FontStyle { get; private set; }
  public static Sprite BrushSprite { get; private set; }
  public static Color BrushColor { get; private set; }
  public static Sprite PanelSprite { get; private set; }
  public static float PanelPixelsPerUnitMultiplier { get; private set; }
  public static AudioClip HoverSound { get; private set; }
  public static AudioClip ClickSound { get; private set; }
  public static AudioClip ModalOpenSound { get; private set; }
  public static AudioMixerGroup MixerGroup { get; private set; }
  public static float PaintInDuration { get; private set; }
  public static float FadeOutDuration { get; private set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    IsConfigured = false;
    Font = null;
    FontMaterial = null;
    FontStyle = FontStyles.Normal;
    BrushSprite = null;
    BrushColor = Color.white;
    PanelSprite = null;
    PanelPixelsPerUnitMultiplier = 8f;
    HoverSound = null;
    ClickSound = null;
    ModalOpenSound = null;
    MixerGroup = null;
    PaintInDuration = 0.14f;
    FadeOutDuration = 0.1f;
  }

  public static void Capture(StrokeHighlight source) {
    if (source == null) return;

    Font = source.StyleFont;
    FontMaterial = source.StyleFontMaterial;
    FontStyle = source.StyleFontStyle;
    BrushSprite = source.StyleBrushSprite;
    BrushColor = source.StyleBrushColor;
    PanelSprite = source.StylePanelSprite;
    PanelPixelsPerUnitMultiplier = 8f;
    HoverSound = source.StyleHoverSound;
    MixerGroup = source.StyleMixerGroup;
    PaintInDuration = source.StylePaintInDuration;
    FadeOutDuration = source.StyleFadeOutDuration;
    IsConfigured = Font != null && BrushSprite != null;
  }

  public static void SetPanelSprite(Sprite sprite, float pixelsPerUnitMultiplier) {
    if (sprite == null) return;

    PanelSprite = sprite;
    PanelPixelsPerUnitMultiplier = Mathf.Max(0.01f, pixelsPerUnitMultiplier);
  }

  public static void SetModalOpenSound(AudioClip clip) {
    if (clip != null) ModalOpenSound = clip;
  }

  public static void SetClickSound(AudioClip clip) {
    if (clip != null) ClickSound = clip;
  }
}
