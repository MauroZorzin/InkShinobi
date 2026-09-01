using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

/// <summary>
/// Sequences the authored objects in 5-Finale. The camera, video surface, blood VFX, title, prompt,
/// VideoPlayer, and AudioSource all live in the scene hierarchy; this component only controls them.
/// </summary>
public sealed class FinaleSequence : MonoBehaviour {
  [Header("Playback")]
  [SerializeField] private VideoPlayer videoPlayer;
  [SerializeField] private AudioSource audioSource;
  [SerializeField] private AudioClip finaleSoundtrack;
  [SerializeField] private AudioClip takedownSound;
  [SerializeField] private AudioClip shogunDeathSound;

  [Header("Authored Presentation")]
  [SerializeField] private CanvasGroup videoGroup;
  [SerializeField] private GameObject bloodGushRoot;
  [SerializeField] private CanvasGroup titleGroup;
  [SerializeField] private CanvasGroup promptGroup;

  [Header("Timing")]
  [SerializeField, Min(0f)] private float strikeDelay = 0.06f;
  [SerializeField, Min(0f)] private float titleDelay = 0.18f;
  [SerializeField, Min(0.05f)] private float titleFadeDuration = 0.65f;
  [SerializeField, Min(0f)] private float inputDelay = 1.25f;

  [Header("Destination")]
  [SerializeField] private string menuSceneName = "MainMenu";

  private bool videoEnded;
  private bool leaving;
  private bool holdingSceneReveal;

  private void Awake() {
    // When entered through the shared ink transition, keep the screen covered until the movie has
    // actually rendered a frame. Direct scene loads do not acquire a hold.
    holdingSceneReveal = SceneTransitionManager.TryHoldSceneReveal();
  }

  private void Start() {
    if (!HasRequiredReferences()) {
      ReleaseSceneReveal();
      enabled = false;
      return;
    }

    Cursor.lockState = CursorLockMode.Locked;
    Cursor.visible = false;
    ResetPresentation();

    videoPlayer.loopPointReached += HandleVideoEnded;
    videoPlayer.errorReceived += HandleVideoError;
    StartCoroutine(PlayFinale());
  }

  private void OnDestroy() {
    ReleaseSceneReveal();
    if (videoPlayer == null) return;
    videoPlayer.loopPointReached -= HandleVideoEnded;
    videoPlayer.errorReceived -= HandleVideoError;
  }

  private bool HasRequiredReferences() {
    bool valid = videoPlayer != null
      && audioSource != null
      && finaleSoundtrack != null
      && shogunDeathSound != null
      && videoGroup != null
      && bloodGushRoot != null
      && titleGroup != null
      && promptGroup != null;
    if (!valid) {
      Debug.LogError(
        "[FinaleSequence] One or more authored scene references are missing. "
        + "Use Tools/Ink Shinobi/Rebuild Finale Scene to restore the hierarchy.",
        this
      );
    }
    return valid;
  }

  private void ResetPresentation() {
    videoEnded = false;
    audioSource.Stop();
    audioSource.clip = null;
    videoGroup.alpha = 1f;
    titleGroup.alpha = 0f;
    promptGroup.alpha = 0f;
    bloodGushRoot.SetActive(false);
  }

  private IEnumerator PlayFinale() {
    VideoClip finaleVideo = videoPlayer.clip;
    bool videoPrepared = false;
    if (finaleVideo == null) {
      Debug.LogWarning("[FinaleSequence] The authored VideoPlayer has no clip; continuing to the final strike.", this);
    } else {
      videoPlayer.Prepare();
      float prepareElapsed = 0f;
      while (!videoPlayer.isPrepared && prepareElapsed < 15f) {
        prepareElapsed += Time.unscaledDeltaTime;
        yield return null;
      }

      videoPrepared = videoPlayer.isPrepared;
    }

    if (finaleVideo != null) {
      if (videoPrepared) {
        audioSource.clip = finaleSoundtrack;
        audioSource.Play();
        videoPlayer.Play();

        // Wait for playback to submit its first frame to the RenderTexture before allowing the ink
        // transition to uncover the scene. The movie is already advancing when the reveal begins.
        float firstFrameElapsed = 0f;
        while (videoPlayer.frame < 0 && firstFrameElapsed < 1.5f) {
          firstFrameElapsed += Time.unscaledDeltaTime;
          yield return null;
        }
        yield return new WaitForEndOfFrame();
        ReleaseSceneReveal();

        float playbackElapsed = 0f;
        float playbackTimeout = Mathf.Max((float)videoPlayer.length, (float)finaleVideo.length) + 2f;
        while (!videoEnded && playbackElapsed < playbackTimeout) {
          playbackElapsed += Time.unscaledDeltaTime;
          yield return null;
        }
      } else {
        Debug.LogWarning("[FinaleSequence] Video preparation timed out; continuing to the final strike.", this);
        ReleaseSceneReveal();
      }
    } else {
      ReleaseSceneReveal();
    }

    audioSource.Stop();
    audioSource.clip = null;
    yield return Fade(videoGroup, videoGroup.alpha, 0f, 0.16f);
    if (takedownSound != null) audioSource.PlayOneShot(takedownSound);
    if (strikeDelay > 0f) yield return new WaitForSecondsRealtime(strikeDelay);

    PlayBloodGush();
    if (titleDelay > 0f) yield return new WaitForSecondsRealtime(titleDelay);
    FreezeBloodGush();
    yield return Fade(titleGroup, 0f, 1f, titleFadeDuration);
    yield return new WaitForSecondsRealtime(inputDelay);
    yield return Fade(promptGroup, 0f, 1f, 0.35f);

    while (!ReturnPressed()) yield return null;
    ReturnToMenu();
  }

  private void PlayBloodGush() {
    audioSource.PlayOneShot(shogunDeathSound);
    bloodGushRoot.SetActive(true);
    ParticleSystem[] particles = bloodGushRoot.GetComponentsInChildren<ParticleSystem>(true);

    // Clear any editor preview/runtime residue first. Playing only the prefab's root system avoids
    // triggering its child twice because Play(true) already propagates through the hierarchy.
    foreach (ParticleSystem particle in particles) {
      particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    foreach (ParticleSystem particle in particles) {
      if (particle.transform.parent == bloodGushRoot.transform) particle.Play(true);
    }
  }

  private void FreezeBloodGush() {
    // The package's one-shot particles normally expire after roughly two seconds. Pausing after
    // their initial impact keeps that authored impact frame rendered for the rest of the finale.
    foreach (ParticleSystem particle in
      bloodGushRoot.GetComponentsInChildren<ParticleSystem>(true)) {
      particle.Pause(false);
    }
  }

  private void HandleVideoEnded(VideoPlayer _) => videoEnded = true;

  private void HandleVideoError(VideoPlayer _, string message) {
    Debug.LogWarning($"[FinaleSequence] Video playback error: {message}", this);
    if (audioSource != null) audioSource.Stop();
    ReleaseSceneReveal();
    videoEnded = true;
  }

  private void ReleaseSceneReveal() {
    if (!holdingSceneReveal) return;
    holdingSceneReveal = false;
    SceneTransitionManager.ReleaseSceneReveal();
  }

  private void ReturnToMenu() {
    if (leaving) return;
    leaving = true;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
    SceneTransitionManager.LoadScene(menuSceneName);
  }

  private static bool ReturnPressed() {
    if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
    if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
    if (Gamepad.current == null) return false;

    return Gamepad.current.buttonSouth.wasPressedThisFrame
      || Gamepad.current.buttonNorth.wasPressedThisFrame
      || Gamepad.current.buttonEast.wasPressedThisFrame
      || Gamepad.current.buttonWest.wasPressedThisFrame
      || Gamepad.current.startButton.wasPressedThisFrame;
  }

  private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration) {
    float elapsed = 0f;
    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
      yield return null;
    }
    group.alpha = to;
  }
}
