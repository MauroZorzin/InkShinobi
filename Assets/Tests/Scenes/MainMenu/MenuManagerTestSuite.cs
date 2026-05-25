using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class MenuManagerTestSuite {
  private const string MainMenuSceneName = "MainMenu";
  private const string SettingsSceneName = "SettingsMenu";
  private const string FirstSceneName = "SettingsMenu";
  private const float SceneLoadTimeoutSeconds = 5f;
  private readonly List<Object> _createdObjects = new();
  private GameObject _menuManagerGO;
  private MenuManager _menuManager;

  [UnitySetUp]
  public IEnumerator Setup() {
    yield return LoadScene(MainMenuSceneName);

    _menuManagerGO = new GameObject("MenuManager");
    _menuManager = _menuManagerGO.AddComponent<MenuManager>();

    SetPrivateField("firstSceneName", FirstSceneName);
    SetPrivateField("settingsSceneName", SettingsSceneName);
    SetPrivateField("minimumLoadTime", 0f);
    SetPrivateField("fadeInDuration", 0f);
    SetPrivateField("fadeOutDuration", 0f);
    SetPrivateField("audioFadeDuration", 0f);
  }

  [TearDown]
  public void TearDown() {
    if (_menuManagerGO != null) {
      Object.Destroy(_menuManagerGO);
    }

    foreach (Object createdObject in _createdObjects) {
      if (createdObject != null) {
        Object.DestroyImmediate(createdObject);
      }
    }

    _createdObjects.Clear();
  }

  [UnityTest]
  public IEnumerator StartGame_LoadsConfiguredFirstScene() {
    _menuManager.StartGame();

    yield return WaitForActiveScene(FirstSceneName, SceneLoadTimeoutSeconds);

    Assert.AreEqual(FirstSceneName, SceneManager.GetActiveScene().name);
  }

  [UnityTest]
  public IEnumerator OpenSettings_LoadsConfiguredSettingsScene() {
    _menuManager.OpenSettings();

    yield return WaitForActiveScene(SettingsSceneName, SceneLoadTimeoutSeconds);

    Assert.AreEqual(SettingsSceneName, SceneManager.GetActiveScene().name);
  }

  [Test]
  public void ContinueGame_LogsPlaceholderMessage() {
    LogAssert.Expect(LogType.Log, "Continue clicked");

    _menuManager.ContinueGame();
  }

  [Test]
  public void BuildOverlay_CreatesPersistentCanvasAndTransparentBackdrop() {
    Color backdropColor = new(0.2f, 0.3f, 0.4f, 1f);
    SetPrivateField("backdropColor", backdropColor);

    object[] parameters = { null };
    LoadingOverlayDriver overlay = (LoadingOverlayDriver)InvokePrivate("BuildOverlay", parameters);
    _createdObjects.Add(overlay.gameObject);

    Image backdrop = (Image)parameters[0];
    Canvas canvas = overlay.GetComponent<Canvas>();
    CanvasScaler canvasScaler = overlay.GetComponent<CanvasScaler>();

    Assert.IsNotNull(backdrop);
    Assert.IsNotNull(canvas);
    Assert.IsNotNull(canvasScaler);
    Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
    Assert.AreEqual(999, canvas.sortingOrder);
    Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, canvasScaler.uiScaleMode);
    Assert.AreEqual(0f, backdrop.color.a, 0.001f);
    Assert.AreEqual(backdropColor.r, backdrop.color.r, 0.001f);
    Assert.AreEqual(backdropColor.g, backdrop.color.g, 0.001f);
    Assert.AreEqual(backdropColor.b, backdrop.color.b, 0.001f);
  }

  [UnityTest]
  public IEnumerator FadeAudio_ReachesTargetVolumeAndStopsAtZero() {
    AudioSource audioSource = CreateGameObject("AudioSource").AddComponent<AudioSource>();
    audioSource.volume = 1f;

    IEnumerator routine = (IEnumerator)InvokePrivate("FadeAudio", audioSource, 0f, 0.01f);
    _menuManager.StartCoroutine(routine);

    yield return new WaitForSecondsRealtime(0.05f);

    Assert.AreEqual(0f, audioSource.volume, 0.001f);
    Assert.IsFalse(audioSource.isPlaying);
  }

  [UnityTest]
  public IEnumerator FadeBackdrop_UpdatesBackdropAlpha() {
    Image backdrop = CreateGameObject("Backdrop").AddComponent<Image>();
    backdrop.color = Color.clear;

    IEnumerator routine = (IEnumerator)InvokePrivate("FadeBackdrop", backdrop, 0f, 1f, 0.01f);
    _menuManager.StartCoroutine(routine);

    yield return new WaitForSecondsRealtime(0.05f);

    Assert.AreEqual(1f, backdrop.color.a, 0.001f);
  }

  [UnityTest]
  public IEnumerator LoadingOverlayDriver_StartFadeOutAndDestroy_FadesAndDestroysOverlay() {
    GameObject overlayGO = CreateGameObject("OverlayDriver");
    LoadingOverlayDriver overlay = overlayGO.AddComponent<LoadingOverlayDriver>();
    Image backdrop = CreateGameObject("Backdrop").AddComponent<Image>();
    backdrop.transform.SetParent(overlay.transform, false);
    backdrop.color = Color.white;

    overlay.StartFadeOutAndDestroy(backdrop, 0.01f);

    yield return new WaitForSecondsRealtime(0.05f);

    Assert.IsTrue(overlay == null, "The overlay driver should destroy itself after fade-out.");
  }

  private GameObject CreateGameObject(string name) {
    GameObject gameObject = new(name);
    _createdObjects.Add(gameObject);
    return gameObject;
  }

  private void SetPrivateField(string fieldName, object value) {
    typeof(MenuManager)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_menuManager, value);
  }

  private object InvokePrivate(string methodName, params object[] parameters) {
    return typeof(MenuManager)
      .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
      .Invoke(_menuManager, parameters);
  }

  private static IEnumerator LoadScene(string sceneName) {
    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

    Assert.IsNotNull(loadOperation, $"Failed to start loading scene '{sceneName}'.");

    yield return loadOperation;
    yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
  }

  private static IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds) {
    var deadline = Time.realtimeSinceStartup + timeoutSeconds;

    while (Time.realtimeSinceStartup < deadline) {
      if (SceneManager.GetActiveScene().name == sceneName) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting {timeoutSeconds:0.##} seconds for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'.");
  }
}
