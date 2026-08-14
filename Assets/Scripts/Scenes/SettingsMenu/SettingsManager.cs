using UnityEngine;

public class SettingsManager : MonoBehaviour {
  [SerializeField] private string previousSceneName = "MainMenu";

  public void Done() {
    SceneTransitionManager.LoadScene(previousSceneName, useFade: false);
  }
}
