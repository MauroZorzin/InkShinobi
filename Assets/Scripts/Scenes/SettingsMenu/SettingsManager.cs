using UnityEngine;
using UnityEngine.SceneManagement;

public class SettingsManager : MonoBehaviour {
  [SerializeField] private string previousSceneName = "MainMenu";

  public void Done() {
    SceneManager.LoadSceneAsync(previousSceneName);
  }
}
