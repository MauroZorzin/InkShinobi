using System;
using System.Collections;
using UnityEngine;
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

  [Header("Loading Screen")]
  [Tooltip("Solid color used by the loading overlay backdrop.")]
  public Color backdropColor = Color.black;

  [Tooltip("Optional prefab spawned above the loading backdrop while a scene is loading.")]
  public GameObject loaderAnimationPrefab;

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

  /// <summary>Loads a scene by name with the loading overlay + audio fade.</summary>
  public static void LoadScene(string sceneName) => Begin(() => SceneManager.LoadSceneAsync(sceneName));

  /// <summary>Loads a scene by build index with the loading overlay + audio fade.</summary>
  public static void LoadScene(int buildIndex) => Begin(() => SceneManager.LoadSceneAsync(buildIndex));

  /// <summary>Reloads the currently active scene with the loading overlay + audio fade.</summary>
  public static void ReloadCurrentScene() => LoadScene(SceneManager.GetActiveScene().buildIndex);

  private static void Begin(Func<AsyncOperation> beginLoad) {
    if (Instance == null) {
      Debug.LogError("[SceneTransitionManager] No instance in the scene — add a SceneTransitionManager component to a persistent/boot GameObject before calling LoadScene.");
      return;
    }

    Instance.StartCoroutine(Instance.LoadSceneRoutine(beginLoad));
  }

  private IEnumerator LoadSceneRoutine(Func<AsyncOperation> beginLoad) {
    Image backdrop = BuildOverlay(out GameObject overlayGo);
    yield return FadeBackdrop(backdrop, 0f, 1f, fadeInDuration);
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

    // Let the new scene's Awake/OnEnable run (including anything that starts playing on its own,
    // e.g. a playOnAwake music/ambience source) before we look for what's playing.
    yield return null;

    FadeAllPlayingAudio(audioFadeInDuration, restoreOriginalVolume: true);

    yield return FadeBackdrop(backdrop, 1f, 0f, fadeOutDuration);

    if (logTransitions) Debug.Log("[SceneTransitionManager] Transition complete.");
    Destroy(overlayGo);
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
    AudioSource[] sources = FindObjectsOfType<AudioSource>();

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
