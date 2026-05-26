using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Handles main-menu button actions and shows a loading overlay while changing scenes.
/// </summary>
public class MenuManager : MonoBehaviour {
  [Header("Scene Names")]
  [Tooltip("Scene loaded when the player starts a new game.")]
  [SerializeField] private string firstSceneName = "Demo";

  [Tooltip("Scene loaded when the player opens settings.")]
  [SerializeField] private string settingsSceneName = "SettingsMenu";

  [Header("Loading Screen")]
  [Tooltip("Solid color used by the loading overlay backdrop.")]
  [SerializeField] private Color backdropColor = Color.black;

  [Tooltip("Optional prefab spawned above the loading backdrop while a scene is loading.")]
  [SerializeField] private GameObject loaderAnimationPrefab;

  [Tooltip("Minimum seconds the loading overlay remains visible before scene activation.")]
  [SerializeField] private float minimumLoadTime = 1.5f;

  [Tooltip("Seconds used to fade the loading backdrop in before scene loading.")]
  [SerializeField] private float fadeInDuration = 0.35f;

  [Tooltip("Seconds used to fade the loading backdrop out after the next scene activates.")]
  [SerializeField] private float fadeOutDuration = 0.35f;

  [Header("Audio Fade")]
  [Tooltip("Music audio source faded out while changing scenes.")]
  [SerializeField] private AudioSource musicSource;

  [Tooltip("Ambient audio source faded out while changing scenes.")]
  [SerializeField] private AudioSource ambientSource;

  [Tooltip("Seconds used to fade menu audio to silence.")]
  [SerializeField] private float audioFadeDuration = 0.6f;

  public void StartGame() => StartCoroutine(LoadScene(firstSceneName));

  public void OpenSettings() => StartCoroutine(LoadScene(settingsSceneName));

  /// <summary>
  /// Placeholder hook for future continue-game support.
  /// </summary>
  public void ContinueGame() {
    Debug.Log("Continue clicked");
  }

  /// <summary>
  /// Exits play mode in the editor or quits the application in builds.
  /// </summary>
  public void QuitGame() {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
  }

  /// <summary>
  /// Displays the loading overlay, fades audio, and activates the requested scene.
  /// </summary>
  /// <param name="sceneName">Name of the scene to load.</param>
  private IEnumerator LoadScene(string sceneName) {
    LoadingOverlayDriver overlay = BuildOverlay(out Image backdrop);

    yield return StartCoroutine(FadeBackdrop(backdrop, 0f, 1f, fadeInDuration));

    ShowLoaderAnimation(overlay);

    if (musicSource != null) {
      StartCoroutine(FadeAudio(musicSource, 0f, audioFadeDuration));
    }

    if (ambientSource != null) {
      StartCoroutine(FadeAudio(ambientSource, 0f, audioFadeDuration));
    }

    AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
    op.allowSceneActivation = false;

    var elapsed = 0f;
    while (elapsed < minimumLoadTime || op.progress < 0.9f) {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }

    op.allowSceneActivation = true;

    overlay.StartFadeOutAndDestroy(backdrop, fadeOutDuration);
  }

  /// <summary>
  /// Creates the persistent loading overlay canvas and backdrop.
  /// </summary>
  /// <param name="backdrop">The generated backdrop image.</param>
  /// <returns>The overlay driver that survives the scene transition.</returns>
  private LoadingOverlayDriver BuildOverlay(out Image backdrop) {
    var go = new GameObject("LoadingOverlay");
    DontDestroyOnLoad(go);

    Canvas canvas = go.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 999;

    CanvasScaler scaler = go.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    go.AddComponent<GraphicRaycaster>();

    var bgGo = new GameObject("Backdrop");
    bgGo.transform.SetParent(go.transform, false);
    backdrop = bgGo.AddComponent<Image>();
    backdrop.color = new Color(backdropColor.r, backdropColor.g, backdropColor.b, 0f);

    RectTransform rectTransform = backdrop.rectTransform;
    rectTransform.anchorMin = Vector2.zero;
    rectTransform.anchorMax = Vector2.one;
    rectTransform.offsetMin = Vector2.zero;
    rectTransform.offsetMax = Vector2.zero;

    return go.AddComponent<LoadingOverlayDriver>();
  }

  private void ShowLoaderAnimation(LoadingOverlayDriver overlay) {
    if (loaderAnimationPrefab == null) {
      return;
    }
    GameObject instance = Instantiate(loaderAnimationPrefab, overlay.transform);
    instance.transform.SetAsLastSibling();
  }

  /// <summary>
  /// Fades an audio source volume to the target value and stops it at silence.
  /// </summary>
  /// <param name="source">The audio source to fade.</param>
  /// <param name="target">The target volume.</param>
  /// <param name="duration">Fade duration in seconds.</param>
  private IEnumerator FadeAudio(AudioSource source, float target, float duration) {
    var start = source.volume;
    var elapsed = 0f;

    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
      yield return null;
    }

    source.volume = target;
    if (Mathf.Approximately(target, 0f)) {
      source.Stop();
    }
  }

  /// <summary>
  /// Fades the loading backdrop alpha between two values.
  /// </summary>
  /// <param name="image">The backdrop image to update.</param>
  /// <param name="from">Starting alpha.</param>
  /// <param name="to">Ending alpha.</param>
  /// <param name="duration">Fade duration in seconds.</param>
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
/// Owns the loading overlay after the menu scene unloads and destroys it after fade-out.
/// </summary>
public class LoadingOverlayDriver : MonoBehaviour {
  /// <summary>
  /// Starts fading out the loading backdrop, then destroys the overlay GameObject.
  /// </summary>
  /// <param name="backdrop">The backdrop image to fade out.</param>
  /// <param name="duration">Fade duration in seconds.</param>
  public void StartFadeOutAndDestroy(Image backdrop, float duration) {
    StartCoroutine(FadeOutRoutine(backdrop, duration));
  }

  /// <summary>
  /// Fades a backdrop to transparent before destroying the overlay.
  /// </summary>
  /// <param name="backdrop">The backdrop image to fade out.</param>
  /// <param name="duration">Fade duration in seconds.</param>
  private IEnumerator FadeOutRoutine(Image backdrop, float duration) {
    var elapsed = 0f;
    Color color = backdrop.color;
    var start = color.a;

    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      color.a = Mathf.Lerp(start, 0f, Mathf.Clamp01(elapsed / duration));
      backdrop.color = color;
      yield return null;
    }

    color.a = 0f;
    backdrop.color = color;
    Destroy(gameObject);
  }
}
