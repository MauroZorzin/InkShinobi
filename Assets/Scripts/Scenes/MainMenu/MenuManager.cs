using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour {
  [SerializeField] private string firstSceneName = "ProtoScene";
  [SerializeField] private string settingsSceneName = "SettingsMenu";

  public void StartGame() {
    SceneManager.LoadSceneAsync(firstSceneName);
  }

  public void ContinueGame() {
    // Temporary behavior: later it can replaced with save/load logic
    Debug.Log("Continue clicked");
  }

  public void OpenSettings() {
    SceneManager.LoadSceneAsync(settingsSceneName);
  }

  public void QuitGame() {
    Application.Quit();
  }
}
