using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Reloads the active scene when a collider on the configured player layer enters this trigger.
/// </summary>
public class KillCone : MonoBehaviour {
  [Header("Settings")]
  [Tooltip("Layer mask containing colliders that should trigger a scene reload.")]
  [SerializeField] private LayerMask playerLayer;

  [Tooltip("Optional delay in seconds before reloading the active scene.")]
  [SerializeField] private float reloadDelay = 0f;

  [Header("Audio")]
  [Tooltip("Played at the player's position the instant this kill trigger fires (i.e. the player dies).")]
  [SerializeField] private AudioClip deathSound;
  [Tooltip("Mixer group deathSound is routed through (e.g. your \"FX\" group). Leave empty to go straight to Master.")]
  [SerializeField] private AudioMixerGroup mixerGroup;

  private void OnTriggerEnter(Collider other) {
    Debug.Log($"KillCone triggered by {other.gameObject.name} (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");
    if (((1 << other.gameObject.layer) & playerLayer) == 0) {
      return;
    }

    if (deathSound != null) OneShotAudio.PlayClipAtPoint(deathSound, other.transform.position, 1f, mixerGroup);

    if (reloadDelay > 0f) {
      Invoke(nameof(ReloadScene), reloadDelay);
    } else {
      ReloadScene();
    }
  }

  private void ReloadScene() {
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }
}
