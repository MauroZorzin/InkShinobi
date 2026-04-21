using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MenuManagerTestSuite {
  private const string MainMenuSceneName = "MainMenu";
  private const string SettingsSceneName = "SettingsMenu";
  private const string FirstSceneName = "ProtoScene";
  private const float SceneLoadTimeoutSeconds = 5f;
  private GameObject _menuManagerGO;
  private MenuManager _menuManager;

  [UnitySetUp]
  public IEnumerator Setup() {
    yield return LoadScene(MainMenuSceneName);

    _menuManagerGO = new GameObject("MenuManager");
    _menuManager = _menuManagerGO.AddComponent<MenuManager>();

    SetPrivateField("firstSceneName", FirstSceneName);
    SetPrivateField("settingsSceneName", SettingsSceneName);
  }

  [TearDown]
  public void TearDown() {
    if (_menuManagerGO != null) {
      Object.Destroy(_menuManagerGO);
    }
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
  public void QuitGame_DoesNotThrow() {
    Assert.DoesNotThrow(() => _menuManager.QuitGame());
  }

  private void SetPrivateField(string fieldName, string value) {
    typeof(MenuManager)
      .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_menuManager, value);
  }

  private static IEnumerator LoadScene(string sceneName) {
    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

    Assert.IsNotNull(loadOperation, $"Failed to start loading scene '{sceneName}'.");

    yield return loadOperation;
    yield return WaitForActiveScene(sceneName, SceneLoadTimeoutSeconds);
  }

  private static IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds) {
    float deadline = Time.realtimeSinceStartup + timeoutSeconds;

    while (Time.realtimeSinceStartup < deadline) {
      if (SceneManager.GetActiveScene().name == sceneName) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting {timeoutSeconds:0.##} seconds for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'.");
  }
}