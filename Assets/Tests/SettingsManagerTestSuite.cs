using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SettingsManagerTestSuite {
  private const string SettingsSceneName = "SettingsMenu";
  private const string PreviousSceneName = "MainMenu";
  private GameObject _settingsManagerGO;
  private SettingsManager _settingsManager;

  [UnitySetUp]
  public IEnumerator Setup() {
    yield return LoadScene(SettingsSceneName);

    _settingsManagerGO = new GameObject("SettingsManager");
    _settingsManager = _settingsManagerGO.AddComponent<SettingsManager>();

    typeof(SettingsManager)
      .GetField("previousSceneName", BindingFlags.NonPublic | BindingFlags.Instance)
      .SetValue(_settingsManager, PreviousSceneName);
  }

  [TearDown]
  public void TearDown() {
    if (_settingsManagerGO != null) {
      Object.Destroy(_settingsManagerGO);
    }
  }

  [UnityTest]
  public IEnumerator Done_LoadsConfiguredPreviousScene() {
    _settingsManager.Done();

    yield return WaitForActiveScene(PreviousSceneName);

    Assert.AreEqual(PreviousSceneName, SceneManager.GetActiveScene().name);
  }

  private static IEnumerator LoadScene(string sceneName) {
    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

    Assert.IsNotNull(loadOperation, $"Failed to start loading scene '{sceneName}'.");

    yield return loadOperation;
    yield return WaitForActiveScene(sceneName);
  }

  private static IEnumerator WaitForActiveScene(string sceneName, int maxFrames = 120) {
    for (int frame = 0; frame < maxFrames; frame++) {
      if (SceneManager.GetActiveScene().name == sceneName) {
        yield break;
      }

      yield return null;
    }

    Assert.Fail($"Timed out waiting for scene '{sceneName}'. Active scene: '{SceneManager.GetActiveScene().name}'.");
  }
}