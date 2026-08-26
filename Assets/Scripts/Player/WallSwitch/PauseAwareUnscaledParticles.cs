using UnityEngine;

/// <summary>
/// Lets switch/death particles animate while gameplay time is stopped, but still freezes them
/// when the authoritative pause modal is open.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class PauseAwareUnscaledParticles : MonoBehaviour {
  private ParticleSystem[] particles = System.Array.Empty<ParticleSystem>();
  private bool pausedByModal;

  public static void Configure(GameObject root) {
    if (root == null) return;
    PauseAwareUnscaledParticles playback = root.GetComponent<PauseAwareUnscaledParticles>();
    if (playback == null) playback = root.AddComponent<PauseAwareUnscaledParticles>();
    playback.Prepare();
  }

  private void Awake() {
    Prepare();
  }

  private void Update() {
    bool shouldPause = SceneTransitionManager.IsGamePaused;
    if (shouldPause == pausedByModal) return;

    pausedByModal = shouldPause;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem particle = particles[i];
      if (particle == null) continue;
      if (pausedByModal) particle.Pause(true);
      else particle.Play(true);
    }
  }

  private void Prepare() {
    particles = GetComponentsInChildren<ParticleSystem>(true);
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      main.useUnscaledTime = true;
    }
  }
}
