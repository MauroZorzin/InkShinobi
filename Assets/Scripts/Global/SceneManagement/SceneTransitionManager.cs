using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Generic scene-to-scene transition: loading overlay (fade in/out + optional loader animation),
/// minimum load time, and a fade-out/fade-in of every currently-playing AudioSource in the scene
/// (music, ambience, anything else mid-playback) — same pattern as MenuManager's main-menu
/// loading, but callable from anywhere (SceneTransitionTrigger, a UI button, a respawn point) via
/// the static LoadScene methods, not just menu buttons.
///
/// Unlike MenuManager (which hands its overlay off to a separate DontDestroyOnLoad'd driver
/// object so the fade-out survives the menu scene being unloaded), this manager is itself a
/// persistent singleton: it survives every scene load, so the whole transition — including the
/// post-load audio fade-in, which needs to run code IN the new scene — happens on one component
/// without needing a hand-off trick.
///
/// Setup lives in Resources/SceneTransitionManager.prefab. The prefab is instantiated before the
/// first scene loads and persists for the rest of the session, so no per-scene copy is required.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class SceneTransitionManager : MonoBehaviour {
  private const float MaximumAnimationDelta = 1f / 30f;

  public static SceneTransitionManager Instance { get; private set; }
  public static bool IsGamePaused => Instance != null
    && (Instance._pauseDialog != null || Instance._deathDialog != null);
  public static bool IsDeathSequenceActive => Instance != null && Instance._deathSequenceActive;
  private bool _isTransitioning;
  private bool _acceptSceneRevealHolds;
  private int _sceneRevealHoldCount;
  private ConfirmationModalView _pauseDialog;
  private ConfirmationModalView _deathDialog;
  private bool _deathSequenceActive;
  private float _timeScaleBeforeDeath = 1f;
  private CursorLockMode _cursorLockBeforeDeath;
  private bool _cursorVisibleBeforeDeath;
  private float _timeScaleBeforePause = 1f;
  private CursorLockMode _cursorLockBeforePause;
  private bool _cursorVisibleBeforePause;
  private PlayerInput _pausedPlayerInput;
  private string _actionMapBeforePause;
  private AudioSource _uiAudioSource;
  [SerializeField, Tooltip("Ink effect used to cover and reveal scenes.")]
  private InkTransition _inkTransition;
  [SerializeField, Tooltip("Mixer that receives the persisted Music and SFX volume settings.")]
  private AudioMixer settingsMixer;
  private GameObject _transitionOverlay;
  private Image _transitionBackdrop;
  private TextMeshProUGUI _transitionLabel;
  private RawImage _transitionIconImage;
  private GameObject _loaderAnimationInstance;

  private sealed class CanvasRenderState {
    public Canvas Canvas;
    public RenderMode RenderMode;
    public Camera WorldCamera;
    public float PlaneDistance;
  }

  private sealed class SceneContentCache {
    public Camera Camera;
    public Canvas[] Canvases;
    public AudioSource[] AudioSources;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    Instance = null;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void Bootstrap() {
    EnsureInstance();
  }

  [Header("Loading Screen")]
  [Tooltip("Solid color used by the loading overlay backdrop.")]
  public Color backdropColor = Color.black;

  [Tooltip("Optional prefab spawned above the loading backdrop while a scene is loading.")]
  public GameObject loaderAnimationPrefab;

  [Tooltip("Font used by the Saving label displayed while the screen is black.")]
  public TMP_FontAsset savingFont;

  [Tooltip("Icon shown above the Loading/Saving label.")]
  [SerializeField] private Texture2D transitionIcon;

  [Tooltip("Maximum displayed size of the transition icon in canvas units.")]
  [SerializeField] private Vector2 transitionIconSize = new Vector2(240f, 120f);

  [Min(0.1f)]
  [Tooltip("Seconds for the icon to brighten and dim once.")]
  [SerializeField] private float iconGlowCycleDuration = 1.2f;

  [Range(0.1f, 1f)]
  [Tooltip("Shapes the glow timing. Lower values make the icon brighten sooner and remain near peak brightness longer; 1 is an evenly timed pulse (sine).")]
  [SerializeField] private float iconGlowBrightnessBias = 0.45f;

  [Min(0.05f)]
  [Tooltip("Seconds between each additional dot in Loading/Saving.")]
  [SerializeField] private float labelDotInterval = 0.35f;

  [Tooltip("Minimum seconds the loading overlay remains visible before scene activation.")]
  public float minimumLoadTime = 1.5f;

  [Min(0)]
  [Tooltip("Frames kept fully covered after scene activation before the ink reveal begins.")]
  [SerializeField] private int postActivationSettleFrames = 3;

  [Tooltip("Seconds used to fade the loading backdrop in before scene loading.")]
  public float fadeInDuration = 0.35f;

  [Tooltip("Seconds used to fade the loading backdrop out after the next scene activates.")]
  public float fadeOutDuration = 0.35f;

  [Header("Audio Fade")]
  [Tooltip("Seconds to fade every playing AudioSource in the CURRENT scene out before loading.")]
  public float audioFadeOutDuration = 0.6f;

  [Tooltip("Seconds to fade every playing AudioSource in the NEW scene in after it loads.")]
  public float audioFadeInDuration = 0.6f;

  private void Awake() {
    if (Instance != null && Instance != this) {
      Destroy(gameObject);
      return;
    }

    Instance = this;
    DontDestroyOnLoad(gameObject);
    GameSettings.ApplySavedResolution();
    PrepareTransitionOverlay();
  }

  private void Start() {
    // AudioMixer values set during Awake can be discarded while Unity is still initializing
    // the audio graph. Applying persisted levels on the first Start guarantees that play-on-awake
    // music and ambience use the saved volume without waiting for a slider callback.
    GameSettings.ApplyAudio(settingsMixer);
  }

  private void Update() {
    if (_isTransitioning || _deathSequenceActive
        || !GameProgress.IsGameScene(SceneManager.GetActiveScene().name)) return;
    if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

    if (_pauseDialog == null) ShowMainMenuConfirmation();
  }

  private void LateUpdate() {
    // Death presentation runs on unscaled time. Keep the simulation frozen from the instant the
    // player dies, not merely after the recovery modal has appeared.
    if (_deathSequenceActive || _pauseDialog != null || _deathDialog != null) Time.timeScale = 0f;
  }

  private void OnDestroy() {
    if (Instance != this) return;

    Time.timeScale = _deathSequenceActive
      ? (Mathf.Approximately(_timeScaleBeforeDeath, 0f) ? 1f : _timeScaleBeforeDeath)
      : _timeScaleBeforePause;
    RestoreGameplayInput();
    if (_pauseDialog != null) {
      Cursor.lockState = _cursorLockBeforePause;
      Cursor.visible = _cursorVisibleBeforePause;
    } else if (_deathSequenceActive) {
      Cursor.lockState = _cursorLockBeforeDeath;
      Cursor.visible = _cursorVisibleBeforeDeath;
    }
    Instance = null;
  }

  /// <summary>Loads a scene with the configured transition unless explicitly disabled.</summary>
  public static void LoadScene(string sceneName, bool useFade = true) =>
    Begin(sceneName, () => SceneManager.LoadSceneAsync(sceneName), useFade);

  /// <summary>Loads a scene by build index with the configured transition unless disabled.</summary>
  public static void LoadScene(int buildIndex, bool useFade = true) =>
    Begin(GameProgress.GetSceneName(buildIndex), () => SceneManager.LoadSceneAsync(buildIndex), useFade);

  /// <summary>Reloads the active scene with the configured transition unless disabled.</summary>
  public static void ReloadCurrentScene(bool useFade = true) =>
    LoadScene(SceneManager.GetActiveScene().buildIndex, useFade);

  /// <summary>Reloads after a failed attempt without writing progress; the status reads Loading.</summary>
  public static void ReloadCurrentSceneWithoutSaving(bool useFade = true) =>
    Begin(
      SceneManager.GetActiveScene().name,
      () => SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex),
      useFade,
      false
    );

  /// <summary>Atomically reserves the one active player-death flow and suppresses pause input.</summary>
  public static bool BeginPlayerDeath() {
    EnsureInstance();
    if (Instance._isTransitioning || Instance._deathSequenceActive) return false;
    Instance._deathSequenceActive = true;
    Instance._timeScaleBeforeDeath = Time.timeScale;
    Time.timeScale = 0f;
    Instance._cursorLockBeforeDeath = Cursor.lockState;
    Instance._cursorVisibleBeforeDeath = Cursor.visible;
    Instance.SwitchToUiInput();
    return true;
  }

  /// <summary>Shows the shared parchment recovery modal after the player presentation finishes.</summary>
  public static void ShowPlayerDeathModal() {
    EnsureInstance();
    Instance.ShowDeathConfirmation();
  }

  public static void SetSavingFont(TMP_FontAsset font) {
    if (font == null) return;

    EnsureInstance();
    Instance.savingFont = font;
    if (Instance._transitionLabel != null) {
      Instance._transitionLabel.font = font;
      Instance.PrewarmTransitionText();
    }
  }

  public static void PlayUiSound(
    AudioClip clip,
    AudioMixerGroup mixerGroup,
    float startOffset = 0f,
    float volume = 1f
  ) {
    if (clip == null) return;

    EnsureInstance();
    if (Instance._uiAudioSource == null) {
      Instance._uiAudioSource = Instance.gameObject.AddComponent<AudioSource>();
      Instance._uiAudioSource.playOnAwake = false;
      Instance._uiAudioSource.loop = false;
      Instance._uiAudioSource.spatialBlend = 0f;
      Instance._uiAudioSource.ignoreListenerPause = true;
    }

    Instance._uiAudioSource.outputAudioMixerGroup = mixerGroup;
    Instance._uiAudioSource.volume = Mathf.Clamp01(volume);
    Instance._uiAudioSource.clip = clip;
    Instance._uiAudioSource.time = Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, clip.length - 0.001f));
    Instance._uiAudioSource.Play();
  }

  /// <summary>
  /// Lets a newly activated scene delay the ink reveal until its first visible frame is ready.
  /// Returns false when there is no covered scene transition to hold.
  /// </summary>
  public static bool TryHoldSceneReveal() {
    if (Instance == null || !Instance._acceptSceneRevealHolds) return false;
    Instance._sceneRevealHoldCount++;
    return true;
  }

  public static void ReleaseSceneReveal() {
    if (Instance == null || Instance._sceneRevealHoldCount <= 0) return;
    Instance._sceneRevealHoldCount--;
  }

  private static void Begin(
    string destinationSceneName,
    Func<AsyncOperation> beginLoad,
    bool useFade,
    bool allowSave = true
  ) {
    EnsureInstance();

    if (Instance._isTransitioning) return;

    string sourceSceneName = SceneManager.GetActiveScene().name;
    bool shouldFade = useFade && !GameProgress.AreBothMenuScenes(sourceSceneName, destinationSceneName);
    bool shouldSave = allowSave
      && shouldFade
      && GameProgress.ShouldSaveDuringTransition(sourceSceneName);
    bool showSaving = shouldSave && GameProgress.IsGameScene(destinationSceneName);

    Instance.StartCoroutine(Instance.LoadSceneRoutine(
      destinationSceneName,
      beginLoad,
      shouldFade,
      shouldSave,
      showSaving
    ));
  }

  private static void EnsureInstance() {
    if (Instance != null) return;

    SceneTransitionManager prefab = Resources.Load<SceneTransitionManager>(nameof(SceneTransitionManager));
    if (prefab != null) {
      Instantiate(prefab);
      if (Instance != null) return;
    }

    Debug.LogWarning(
      "[SceneTransitionManager] Resources/SceneTransitionManager prefab was not found; " +
      "using an unconfigured runtime fallback."
    );
    var managerObject = new GameObject(nameof(SceneTransitionManager));
    managerObject.AddComponent<SceneTransitionManager>();
  }

  private IEnumerator LoadSceneRoutine(
    string destinationSceneName,
    Func<AsyncOperation> beginLoad,
    bool useFade,
    bool shouldSave,
    bool showSaving
  ) {
    _isTransitioning = true;
    _acceptSceneRevealHolds = false;
    _sceneRevealHoldCount = 0;

    if (!useFade) {
      yield return beginLoad();
      CompleteDeathTransitionFreeze();
      _isTransitioning = false;
      yield break;
    }

    PrepareTransitionOverlay();
    GameObject overlayGo = _transitionOverlay;
    Image backdrop = _transitionBackdrop;
    ResetTransitionOverlay();

    SceneContentCache oldSceneContent = CaptureSceneContent();
    List<CanvasRenderState> oldCanvasStates = RouteOverlayCanvasesThroughCamera(
      oldSceneContent.Canvases,
      oldSceneContent.Camera,
      overlayGo
    );
    yield return _inkTransition.CoverScreen();

    string transitionMessage = showSaving ? "Saving" : "Loading";
    _transitionLabel.gameObject.SetActive(true);
    _transitionLabel.text = transitionMessage;
    if (_transitionIconImage != null) _transitionIconImage.gameObject.SetActive(true);
    Coroutine transitionStatusAnimation = StartCoroutine(
      AnimateTransitionStatus(_transitionLabel, _transitionIconImage, transitionMessage)
    );

    // Guarantee that the prewarmed status UI has rendered before any synchronous save work.
    yield return null;
    yield return null;

    if (shouldSave) {
      GameProgress.SaveTransition(SceneManager.GetActiveScene().name, destinationSceneName);
    }

    ShowLoaderAnimation();

    FadePlayingAudio(
      oldSceneContent.AudioSources,
      audioFadeOutDuration,
      restoreOriginalVolume: false
    );

    ThreadPriority previousLoadingPriority = Application.backgroundLoadingPriority;
    Application.backgroundLoadingPriority = ThreadPriority.Low;
    AsyncOperation op = beginLoad();
    op.priority = -1;
    op.allowSceneActivation = false;

    var elapsed = 0f;
    while (elapsed < minimumLoadTime || op.progress < 0.9f) {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }

    // Awakened components in the destination may hold the reveal while they prepare visual content
    // such as a VideoPlayer's first decoded frame.
    _acceptSceneRevealHolds = true;
    op.allowSceneActivation = true;

    // Keep the screen fully black until activation and the new scene's initialization finish.
    yield return op;
    Application.backgroundLoadingPriority = previousLoadingPriority;

    // Route canvases before the new scene's first visible frame so Screen Space Overlay UI cannot
    // briefly render above the full-screen ink pass.
    SceneContentCache newSceneContent = CaptureSceneContent();
    RetargetRoutedCanvases(oldCanvasStates, newSceneContent.Camera);
    List<CanvasRenderState> newCanvasStates = RouteOverlayCanvasesThroughCamera(
      newSceneContent.Canvases,
      newSceneContent.Camera,
      overlayGo
    );

    // Keep the expensive activation/initialization frames behind the fully covered transition.
    for (int frame = 0; frame < postActivationSettleFrames; frame++) yield return null;

    float revealHoldElapsed = 0f;
    while (_sceneRevealHoldCount > 0 && revealHoldElapsed < 20f) {
      revealHoldElapsed += Time.unscaledDeltaTime;
      yield return null;
    }
    if (_sceneRevealHoldCount > 0) {
      Debug.LogWarning("[SceneTransitionManager] Timed out waiting for the destination scene to release the ink reveal.");
      _sceneRevealHoldCount = 0;
    }
    _acceptSceneRevealHolds = false;

    FadePlayingAudio(
      newSceneContent.AudioSources,
      audioFadeInDuration,
      restoreOriginalVolume: true
    );
    StopCoroutine(transitionStatusAnimation);
    overlayGo.SetActive(false);
    yield return null;
    yield return _inkTransition.RevealScreen();

    if (oldCanvasStates != null) RestoreCanvasRenderStates(oldCanvasStates);
    if (newCanvasStates != null) RestoreCanvasRenderStates(newCanvasStates);

    CompleteDeathTransitionFreeze();
    _isTransitioning = false;
  }

  private void CompleteDeathTransitionFreeze() {
    if (!_deathSequenceActive) return;
    _deathSequenceActive = false;
    Time.timeScale = Mathf.Approximately(_timeScaleBeforeDeath, 0f) ? 1f : _timeScaleBeforeDeath;
  }

  private static SceneContentCache CaptureSceneContent() {
    Camera renderCamera = Camera.main;
    if (renderCamera == null) renderCamera = FindFirstObjectByType<Camera>();

    return new SceneContentCache {
      Camera = renderCamera,
      Canvases = FindObjectsByType<Canvas>(
        FindObjectsInactive.Exclude,
        FindObjectsSortMode.None
      ),
      AudioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
    };
  }

  private static List<CanvasRenderState> RouteOverlayCanvasesThroughCamera(
    Canvas[] canvases,
    Camera renderCamera,
    GameObject excludedRoot
  ) {
    var states = new List<CanvasRenderState>();
    if (renderCamera == null) return states;

    foreach (Canvas canvas in canvases) {
      if (canvas == null || !canvas.isRootCanvas) continue;
      if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
      if (excludedRoot != null && canvas.transform.IsChildOf(excludedRoot.transform)) continue;

      states.Add(new CanvasRenderState {
        Canvas = canvas,
        RenderMode = canvas.renderMode,
        WorldCamera = canvas.worldCamera,
        PlaneDistance = canvas.planeDistance
      });
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = renderCamera;
      canvas.planeDistance = renderCamera.nearClipPlane + 0.00003f;
    }

    return states;
  }

  private static void RestoreCanvasRenderStates(List<CanvasRenderState> states) {
    foreach (CanvasRenderState state in states) {
      if (state.Canvas == null) continue;
      state.Canvas.renderMode = state.RenderMode;
      state.Canvas.worldCamera = state.WorldCamera;
      state.Canvas.planeDistance = state.PlaneDistance;
    }
  }

  private static void RetargetRoutedCanvases(
    List<CanvasRenderState> states,
    Camera renderCamera
  ) {
    if (renderCamera == null) return;

    foreach (CanvasRenderState state in states) {
      if (state.Canvas == null) continue;
      state.Canvas.worldCamera = renderCamera;
      state.Canvas.planeDistance = renderCamera.nearClipPlane + 0.00003f;
    }
  }

  /// <summary>
  /// Fades every currently-playing AudioSource in the scene (skipping this manager's own
  /// hierarchy, so the loading overlay/manager never gets swept up in its own fade). Idle sources
  /// (playOnAwake off, waiting for a gameplay-triggered PlayOneShot — footsteps, one-shot SFX,
  /// etc.) are left completely untouched either way, so this never silences or force-starts them.
  /// </summary>
  /// <param name="duration">Fade duration in seconds.</param>
  /// <param name="restoreOriginalVolume">False = fade playing sources down to 0 (leaving scene). True = snap to 0 then fade back up to each source's own current volume (entering scene).</param>
  private void FadePlayingAudio(
    AudioSource[] sources,
    float duration,
    bool restoreOriginalVolume
  ) {
    foreach (AudioSource source in sources) {
      if (source == null || !source.isPlaying) continue;
      if (source.transform.IsChildOf(transform)) continue; // never fade the manager's own hierarchy

      if (restoreOriginalVolume) {
        float targetVolume = source.volume;
        source.volume = 0f;
        StartCoroutine(FadeAudio(source, targetVolume, duration));
      } else {
        StartCoroutine(FadeAudio(source, 0f, duration));
      }
    }
  }

  private void PrepareTransitionOverlay() {
    if (_transitionOverlay != null) return;

    _transitionBackdrop = BuildOverlay(out _transitionOverlay);
    _transitionLabel = ShowTransitionLabel(_transitionOverlay.transform, "Loading...");
    _transitionIconImage = ShowTransitionIcon(_transitionOverlay.transform);

    if (loaderAnimationPrefab != null) {
      _loaderAnimationInstance = Instantiate(
        loaderAnimationPrefab,
        _transitionOverlay.transform
      );
      _loaderAnimationInstance.transform.SetAsLastSibling();
      _loaderAnimationInstance.SetActive(false);
    }

    PrewarmTransitionText();
    if (transitionIcon != null) transitionIcon.GetNativeTexturePtr();
    Canvas.ForceUpdateCanvases();
    _transitionOverlay.SetActive(false);
  }

  private void PrewarmTransitionText() {
    if (_transitionLabel == null) return;

    string previousText = _transitionLabel.text;
    _transitionLabel.text = "Loading... Saving...";
    _transitionLabel.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
    _transitionLabel.text = previousText;
    _transitionLabel.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
  }

  private void ResetTransitionOverlay() {
    _transitionOverlay.SetActive(true);

    Color backdrop = _transitionBackdrop.color;
    backdrop.a = 0f;
    _transitionBackdrop.color = backdrop;

    _transitionLabel.text = string.Empty;
    _transitionLabel.gameObject.SetActive(false);
    if (_transitionIconImage != null) {
      _transitionIconImage.color = new Color(0.35f, 0.35f, 0.35f, 1f);
      _transitionIconImage.gameObject.SetActive(false);
    }
    if (_loaderAnimationInstance != null) _loaderAnimationInstance.SetActive(false);
  }

  /// <summary>Creates the loading overlay canvas and backdrop, parented under this persistent GameObject.</summary>
  private Image BuildOverlay(out GameObject overlayGo) {
    overlayGo = new GameObject("LoadingOverlay");
    overlayGo.transform.SetParent(transform, false);

    Canvas canvas = overlayGo.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 999;

    CanvasScaler scaler = overlayGo.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    overlayGo.AddComponent<GraphicRaycaster>();

    var bgGo = new GameObject("Backdrop");
    bgGo.transform.SetParent(overlayGo.transform, false);
    Image backdrop = bgGo.AddComponent<Image>();
    backdrop.color = new Color(backdropColor.r, backdropColor.g, backdropColor.b, 0f);

    RectTransform rectTransform = backdrop.rectTransform;
    rectTransform.anchorMin = Vector2.zero;
    rectTransform.anchorMax = Vector2.one;
    rectTransform.offsetMin = Vector2.zero;
    rectTransform.offsetMax = Vector2.zero;

    return backdrop;
  }

  private void ShowLoaderAnimation() {
    if (_loaderAnimationInstance == null) return;

    _loaderAnimationInstance.SetActive(true);
    _loaderAnimationInstance.transform.SetAsLastSibling();
  }

  private void ShowMainMenuConfirmation() {
    if (_pauseDialog != null) return;

    _timeScaleBeforePause = Time.timeScale;
    _cursorLockBeforePause = Cursor.lockState;
    _cursorVisibleBeforePause = Cursor.visible;
    Time.timeScale = 0f;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
    SwitchToUiInput();

    _pauseDialog = ConfirmationModalView.Create(
      "MainMenuConfirmation",
      "Abandon this stage?",
      "Progress is kept only from the start of this stage.",
      "Resume",
      "Main Menu",
      ResumeGame,
      ReturnToMainMenu
    );

    if (_pauseDialog == null) {
      Time.timeScale = _timeScaleBeforePause;
      Cursor.lockState = _cursorLockBeforePause;
      Cursor.visible = _cursorVisibleBeforePause;
      RestoreGameplayInput();
      return;
    }

  }

  private void ResumeGame() {
    if (_pauseDialog == null) return;

    ClosePauseDialog();
  }

  private void ClosePauseDialog(System.Action onClosed = null) {
    if (_pauseDialog == null) return;

    ConfirmationModalView dialog = _pauseDialog;
    dialog.Close(() => CompleteClosePauseDialog(dialog, onClosed));
  }

  private void CompleteClosePauseDialog(ConfirmationModalView dialog, System.Action onClosed) {
    if (_pauseDialog == dialog) _pauseDialog = null;
    Time.timeScale = _timeScaleBeforePause;
    Cursor.lockState = _cursorLockBeforePause;
    Cursor.visible = _cursorVisibleBeforePause;
    RestoreGameplayInput();
    onClosed?.Invoke();
  }

  private void SwitchToUiInput() {
    _pausedPlayerInput = null;
    _actionMapBeforePause = null;

    for (int index = 0; index < PlayerInput.all.Count; index++) {
      PlayerInput playerInput = PlayerInput.all[index];
      if (playerInput == null || !playerInput.isActiveAndEnabled) continue;

      InputActionMap uiMap = playerInput.actions?.FindActionMap("UI", false);
      if (uiMap == null) continue;

      _pausedPlayerInput = playerInput;
      _actionMapBeforePause = playerInput.currentActionMap?.name ?? "Player";
      playerInput.SwitchCurrentActionMap(uiMap.name);
      return;
    }

    Debug.LogWarning(
      "[SceneTransitionManager] No active PlayerInput with a UI action map was found."
    );
  }

  private void RestoreGameplayInput() {
    if (_pausedPlayerInput == null) return;

    string mapName = string.IsNullOrEmpty(_actionMapBeforePause)
      ? "Player"
      : _actionMapBeforePause;
    if (_pausedPlayerInput.actions?.FindActionMap(mapName, false) != null) {
      _pausedPlayerInput.SwitchCurrentActionMap(mapName);
    }

    _pausedPlayerInput = null;
    _actionMapBeforePause = null;
  }

  private void ReturnToMainMenu() {
    ClosePauseDialog(() => {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      LoadScene("MainMenu");
    });
  }

  private void ShowDeathConfirmation() {
    if (!_deathSequenceActive || _deathDialog != null || _isTransitioning) return;

    Time.timeScale = 0f;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
    _deathDialog = ConfirmationModalView.Create(
      "PlayerDeathConfirmation",
      "You Have Fallen",
      "Will the shinobi return to the shadows?",
      "Retry",
      "Main Menu",
      RetryAfterDeath,
      ReturnToMainMenuAfterDeath,
      false
    );

    if (_deathDialog == null) {
      // Keep the failed run frozen while the fallback reload covers and replaces it.
      Time.timeScale = 0f;
      ReloadCurrentSceneWithoutSaving();
    }
  }

  private void RetryAfterDeath() {
    CloseDeathDialog(() => {
      Cursor.lockState = _cursorLockBeforeDeath;
      Cursor.visible = _cursorVisibleBeforeDeath;
      ReloadCurrentSceneWithoutSaving();
    });
  }

  private void ReturnToMainMenuAfterDeath() {
    CloseDeathDialog(() => {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      LoadScene("MainMenu");
    });
  }

  private void CloseDeathDialog(Action onClosed) {
    if (_deathDialog == null) return;
    ConfirmationModalView dialog = _deathDialog;
    dialog.Close(() => {
      if (_deathDialog == dialog) _deathDialog = null;
      // The death freeze belongs to the full recovery transition, not only to the modal.
      Time.timeScale = 0f;
      onClosed?.Invoke();
    });
  }

  private TextMeshProUGUI ShowTransitionLabel(Transform overlayTransform, string message) {
    var labelObject = new GameObject("TransitionLabel");
    labelObject.transform.SetParent(overlayTransform, false);

    TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
    label.text = message;
    if (savingFont != null) label.font = savingFont;
    label.fontSize = 50f;
    label.fontStyle = FontStyles.SmallCaps;
    label.alignment = TextAlignmentOptions.Center;
    label.color = Color.white;

    RectTransform rectTransform = label.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.sizeDelta = new Vector2(420f, 80f);
    rectTransform.anchoredPosition = transitionIcon != null ? new Vector2(0f, -55f) : Vector2.zero;
    return label;
  }

  private RawImage ShowTransitionIcon(Transform overlayTransform) {
    if (transitionIcon == null) return null;

    var iconObject = new GameObject("TransitionIcon");
    iconObject.transform.SetParent(overlayTransform, false);

    RawImage icon = iconObject.AddComponent<RawImage>();
    icon.texture = transitionIcon;
    icon.color = Color.white;
    icon.raycastTarget = false;

    RectTransform rectTransform = icon.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    float textureAspect = transitionIcon.width / (float)transitionIcon.height;
    float boundsAspect = transitionIconSize.x / transitionIconSize.y;
    rectTransform.sizeDelta = textureAspect > boundsAspect
      ? new Vector2(transitionIconSize.x, transitionIconSize.x / textureAspect)
      : new Vector2(transitionIconSize.y * textureAspect, transitionIconSize.y);
    rectTransform.anchoredPosition = new Vector2(0f, 65f);
    return icon;
  }

  private IEnumerator AnimateTransitionStatus(TMP_Text label, RawImage icon, string baseMessage) {
    float elapsed = 0f;
    while (label != null) {
      elapsed += Mathf.Min(Time.unscaledDeltaTime, MaximumAnimationDelta);

      int dotCount = Mathf.FloorToInt(elapsed / labelDotInterval) % 4;
      label.text = baseMessage + new string('.', dotCount);

      if (icon != null) {
        float phase = elapsed / iconGlowCycleDuration * Mathf.PI * 2f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(phase - Mathf.PI * 0.5f);
        pulse = Mathf.SmoothStep(0f, 1f, pulse);
        pulse = Mathf.Pow(pulse, iconGlowBrightnessBias);
        float brightness = Mathf.Lerp(0.4f, 1f, pulse);
        icon.color = new Color(brightness, brightness, brightness, 1f);
      }

      yield return null;
    }
  }

  /// <summary>Fades an audio source's volume to the target value, stopping it if the target is silence.</summary>
  private IEnumerator FadeAudio(AudioSource source, float target, float duration) {
    var start = source.volume;
    var elapsed = 0f;

    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
      yield return null;
    }

    source.volume = target;
    if (Mathf.Approximately(target, 0f)) source.Stop();
  }

  /// <summary>Fades the loading backdrop alpha between two values.</summary>
  private IEnumerator FadeBackdrop(Image image, float from, float to, float duration) {
    var elapsed = 0f;
    Color color = image.color;

    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      color.a = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
      image.color = color;
      yield return null;
    }

    color.a = to;
    image.color = color;
  }
}

/// <summary>
/// Stores the furthest gameplay scene reached. The first scene alone does not count as
/// continue-able progress; reaching the following scene unlocks Continue.
/// </summary>
public static class GameProgress {
  public const string FirstSceneName = "1-Start";

  private const string SceneNameKey = "GameProgress.SceneName";
  private const string SceneBuildIndexKey = "GameProgress.SceneBuildIndex";

  public static bool HasContinueProgress {
    get {
      string sceneName = PlayerPrefs.GetString(SceneNameKey, string.Empty);
      return !string.IsNullOrEmpty(sceneName) &&
        sceneName != FirstSceneName &&
        IsGameScene(sceneName);
    }
  }

  public static string ContinueSceneName =>
    HasContinueProgress ? PlayerPrefs.GetString(SceneNameKey) : FirstSceneName;

  public static void Clear() {
    PlayerPrefs.DeleteKey(SceneNameKey);
    PlayerPrefs.DeleteKey(SceneBuildIndexKey);
    PlayerPrefs.Save();
  }

  public static void SaveTransition(string sourceSceneName, string destinationSceneName) {
    string reachedScene = IsGameScene(destinationSceneName) ? destinationSceneName : sourceSceneName;
    SaveReachedScene(reachedScene);
  }

  public static bool AreBothMenuScenes(string firstSceneName, string secondSceneName) =>
    IsMenuScene(firstSceneName) && IsMenuScene(secondSceneName);

  public static bool ShouldSaveDuringTransition(string sourceSceneName) =>
    !IsMenuScene(sourceSceneName);

  public static string GetSceneName(int buildIndex) {
    string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
    return Path.GetFileNameWithoutExtension(path);
  }

  public static bool IsGameScene(string sceneName) {
    int buildIndex = GetBuildIndex(sceneName);
    if (buildIndex < 0) return false;

    string path = SceneUtility.GetScenePathByBuildIndex(buildIndex).Replace('\\', '/');
    return path.Contains("/GameScenes/", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsMenuScene(string sceneName) =>
    string.Equals(sceneName, "MainMenu", StringComparison.OrdinalIgnoreCase);

  private static void SaveReachedScene(string sceneName) {
    if (!IsGameScene(sceneName) || sceneName == FirstSceneName) return;

    int buildIndex = GetBuildIndex(sceneName);
    int savedBuildIndex = PlayerPrefs.GetInt(SceneBuildIndexKey, -1);
    if (buildIndex < savedBuildIndex) return;

    PlayerPrefs.SetString(SceneNameKey, sceneName);
    PlayerPrefs.SetInt(SceneBuildIndexKey, buildIndex);
    PlayerPrefs.Save();
  }

  private static int GetBuildIndex(string sceneName) {
    for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++) {
      if (string.Equals(GetSceneName(i), sceneName, StringComparison.OrdinalIgnoreCase)) return i;
    }

    return -1;
  }
}
