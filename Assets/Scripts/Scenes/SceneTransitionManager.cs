using System;
using System.Collections;
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
/// Setup: put this on a GameObject in whichever scene loads first (e.g. the main menu, or a boot
/// scene). No per-scene marker component needed — it just finds whatever AudioSources happen to
/// be playing.
/// </summary>
public class SceneTransitionManager : MonoBehaviour {
  public static SceneTransitionManager Instance { get; private set; }

  private bool _isTransitioning;
  private ConfirmationModalView _pauseDialog;
  private float _timeScaleBeforePause = 1f;
  private CursorLockMode _cursorLockBeforePause;
  private bool _cursorVisibleBeforePause;
  private AudioSource _uiAudioSource;

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

  [Tooltip("Minimum seconds the loading overlay remains visible before scene activation.")]
  public float minimumLoadTime = 1.5f;

  [Tooltip("Seconds used to fade the loading backdrop in before scene loading.")]
  public float fadeInDuration = 0.35f;

  [Tooltip("Seconds used to fade the loading backdrop out after the next scene activates.")]
  public float fadeOutDuration = 0.35f;

  [Header("Audio Fade")]
  [Tooltip("Seconds to fade every playing AudioSource in the CURRENT scene out before loading.")]
  public float audioFadeOutDuration = 0.6f;

  [Tooltip("Seconds to fade every playing AudioSource in the NEW scene in after it loads.")]
  public float audioFadeInDuration = 0.6f;

  [Header("Debug")]
  public bool logTransitions = true;

  private void Awake() {
    if (Instance != null && Instance != this) {
      Destroy(gameObject);
      return;
    }

    Instance = this;
    DontDestroyOnLoad(gameObject);
  }

  private void Update() {
    if (_isTransitioning || !GameProgress.IsGameScene(SceneManager.GetActiveScene().name)) return;
    if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

    if (_pauseDialog == null) ShowMainMenuConfirmation();
  }

  private void LateUpdate() {
    if (_pauseDialog != null) Time.timeScale = 0f;
  }

  private void OnDestroy() {
    if (Instance != this) return;

    Time.timeScale = _timeScaleBeforePause;
    if (_pauseDialog != null) {
      Cursor.lockState = _cursorLockBeforePause;
      Cursor.visible = _cursorVisibleBeforePause;
    }
    Instance = null;
  }

  /// <summary>Loads a scene by name, fading through black unless explicitly disabled.</summary>
  public static void LoadScene(string sceneName, bool useFade = true) =>
    Begin(sceneName, () => SceneManager.LoadSceneAsync(sceneName), useFade);

  /// <summary>Loads a scene by build index, fading through black unless explicitly disabled.</summary>
  public static void LoadScene(int buildIndex, bool useFade = true) =>
    Begin(GameProgress.GetSceneName(buildIndex), () => SceneManager.LoadSceneAsync(buildIndex), useFade);

  /// <summary>Reloads the active scene, fading through black unless explicitly disabled.</summary>
  public static void ReloadCurrentScene(bool useFade = true) =>
    LoadScene(SceneManager.GetActiveScene().buildIndex, useFade);

  public static void SetSavingFont(TMP_FontAsset font) {
    if (font == null) return;

    EnsureInstance();
    Instance.savingFont = font;
  }

  public static void PlayUiSound(AudioClip clip, AudioMixerGroup mixerGroup, float startOffset = 0f) {
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
    Instance._uiAudioSource.clip = clip;
    Instance._uiAudioSource.time = Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, clip.length - 0.001f));
    Instance._uiAudioSource.Play();
  }

  private static void Begin(string destinationSceneName, Func<AsyncOperation> beginLoad, bool useFade) {
    EnsureInstance();

    if (Instance._isTransitioning) return;

    string sourceSceneName = SceneManager.GetActiveScene().name;
    bool shouldFade = useFade && !GameProgress.AreBothMenuScenes(sourceSceneName, destinationSceneName);
    bool shouldSave = shouldFade && GameProgress.ShouldSaveDuringTransition(sourceSceneName);
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

    if (!useFade) {
      yield return beginLoad();
      _isTransitioning = false;
      yield break;
    }

    Image backdrop = BuildOverlay(out GameObject overlayGo);
    yield return FadeBackdrop(backdrop, 0f, 1f, fadeInDuration);

    ShowTransitionLabel(overlayGo.transform, showSaving ? "Saving" : "Loading");
    yield return null;

    if (shouldSave) {
      GameProgress.SaveTransition(SceneManager.GetActiveScene().name, destinationSceneName);
    }

    ShowLoaderAnimation(overlayGo.transform);

    FadeAllPlayingAudio(audioFadeOutDuration, restoreOriginalVolume: false);

    AsyncOperation op = beginLoad();
    op.allowSceneActivation = false;

    var elapsed = 0f;
    while (elapsed < minimumLoadTime || op.progress < 0.9f) {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }

    op.allowSceneActivation = true;

    // Keep the screen fully black until activation and the new scene's initialization finish.
    yield return op;

    // Let the new scene's Awake/OnEnable run (including anything that starts playing on its own,
    // e.g. a playOnAwake music/ambience source) before we look for what's playing.
    yield return null;

    FadeAllPlayingAudio(audioFadeInDuration, restoreOriginalVolume: true);

    yield return FadeBackdrop(backdrop, 1f, 0f, fadeOutDuration);

    if (logTransitions) Debug.Log("[SceneTransitionManager] Transition complete.");
    Destroy(overlayGo);
    _isTransitioning = false;
  }

  /// <summary>
  /// Fades every currently-playing AudioSource in the scene (skipping this manager's own
  /// hierarchy, so the loading overlay/manager never gets swept up in its own fade). Idle sources
  /// (playOnAwake off, waiting for a gameplay-triggered PlayOneShot — footsteps, one-shot SFX,
  /// etc.) are left completely untouched either way, so this never silences or force-starts them.
  /// </summary>
  /// <param name="duration">Fade duration in seconds.</param>
  /// <param name="restoreOriginalVolume">False = fade playing sources down to 0 (leaving scene). True = snap to 0 then fade back up to each source's own current volume (entering scene).</param>
  private void FadeAllPlayingAudio(float duration, bool restoreOriginalVolume) {
    AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

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

  private void ShowLoaderAnimation(Transform overlayTransform) {
    if (loaderAnimationPrefab == null) return;

    GameObject instance = Instantiate(loaderAnimationPrefab, overlayTransform);
    instance.transform.SetAsLastSibling();
  }

  private void ShowMainMenuConfirmation() {
    if (_pauseDialog != null) return;

    _timeScaleBeforePause = Time.timeScale;
    _cursorLockBeforePause = Cursor.lockState;
    _cursorVisibleBeforePause = Cursor.visible;
    Time.timeScale = 0f;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;

    _pauseDialog = ConfirmationModalView.Create(
      "MainMenuConfirmation",
      "Return to Main Menu?",
      "Your progress is saved at the start of the latest scene reached.",
      "Resume",
      "Main Menu",
      ResumeGame,
      ReturnToMainMenu
    );

    if (_pauseDialog == null) {
      Time.timeScale = _timeScaleBeforePause;
      Cursor.lockState = _cursorLockBeforePause;
      Cursor.visible = _cursorVisibleBeforePause;
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
    onClosed?.Invoke();
  }

  private void ReturnToMainMenu() {
    ClosePauseDialog(() => {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      LoadScene("MainMenu");
    });
  }

  private void ShowTransitionLabel(Transform overlayTransform, string message) {
    var labelObject = new GameObject("TransitionLabel");
    labelObject.transform.SetParent(overlayTransform, false);

    TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
    label.text = message;
    if (savingFont != null) label.font = savingFont;
    label.fontSize = 50f;
    label.fontStyle = FontStyles.Normal;
    label.alignment = TextAlignmentOptions.Center;
    label.color = Color.white;

    RectTransform rectTransform = label.rectTransform;
    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rectTransform.sizeDelta = new Vector2(320f, 80f);
    rectTransform.anchoredPosition = Vector2.zero;
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
    string.Equals(sceneName, "MainMenu", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(sceneName, "SettingsMenu", StringComparison.OrdinalIgnoreCase);

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
