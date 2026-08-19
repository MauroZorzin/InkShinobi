using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class InkTransition : MonoBehaviour {
  [Header("Visual")]
  [SerializeField] private Material inkMaterial;
  [SerializeField] private float duration = 1f;

  [Header("Audio")]
  [SerializeField] private AudioClip transitionSound;
  [SerializeField] private AudioMixerGroup mixerGroup;
  [Min(0f)]
  [SerializeField] private float revealSoundStartOffset = 0.12f;

  private static readonly int InkAmount = Shader.PropertyToID("_Ink_Amount");
  private static readonly int NoiseOffset = Shader.PropertyToID("_Noise_Offset");
  private static readonly int CoarseNoiseScale = Shader.PropertyToID("_Coarse_Noise_Scale");

  private static readonly int FineNoiseScale = Shader.PropertyToID("_Fine_Noise_Scale");
  private static readonly int CoarseDistortion = Shader.PropertyToID("_Coarse_Distortion");

  private static readonly int FineDistortion = Shader.PropertyToID("_Fine_Distortion");
  private SceneTransitionManager _owner;
  private AudioClip _reversedTransitionSound;

  private void Awake() {
    _owner = GetComponent<SceneTransitionManager>();
    if (_owner != null && SceneTransitionManager.Instance != _owner) return;

    inkMaterial.SetFloat(InkAmount, 0f);
    if (transitionSound != null) transitionSound.LoadAudioData();
  }

  private IEnumerator Start() {
    if (transitionSound == null) yield break;

    while (transitionSound.loadState == AudioDataLoadState.Loading) yield return null;
    CreateReversedTransitionSound();
  }

  public IEnumerator CoverScreen() {
    return ApplyToScreen();
  }

  public IEnumerator RevealScreen() {
    return ApplyToScreen(false);
  }

  private IEnumerator ApplyToScreen(bool cover = true) {
    AudioClip sound = cover ? transitionSound : GetReversedTransitionSound();
    SceneTransitionManager.PlayUiSound(sound, mixerGroup, cover ? 0f : revealSoundStartOffset);
    if (cover) RandomizeInk();
    float time = 0f;
    while (time < duration) {
      time += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(time / duration);
      inkMaterial.SetFloat(InkAmount, cover ? t : 1f - t);
      yield return null;
    }
    inkMaterial.SetFloat(InkAmount, cover ? 1f : 0f);
  }

  private AudioClip GetReversedTransitionSound() {
    if (_reversedTransitionSound == null) CreateReversedTransitionSound();
    return _reversedTransitionSound != null ? _reversedTransitionSound : transitionSound;
  }

  private void CreateReversedTransitionSound() {
    if (_reversedTransitionSound != null || transitionSound == null) return;
    if (transitionSound.loadState != AudioDataLoadState.Loaded) return;

    int channels = transitionSound.channels;
    int frameCount = transitionSound.samples;
    var samples = new float[frameCount * channels];
    if (!transitionSound.GetData(samples, 0)) return;

    for (int firstFrame = 0, lastFrame = frameCount - 1;
         firstFrame < lastFrame;
         firstFrame++, lastFrame--) {
      int firstSample = firstFrame * channels;
      int lastSample = lastFrame * channels;
      for (int channel = 0; channel < channels; channel++) {
        (samples[firstSample + channel], samples[lastSample + channel]) =
          (samples[lastSample + channel], samples[firstSample + channel]);
      }
    }

    _reversedTransitionSound = AudioClip.Create(
      $"{transitionSound.name} (Reversed)",
      frameCount,
      channels,
      transitionSound.frequency,
      false
    );
    _reversedTransitionSound.hideFlags = HideFlags.DontSave;
    if (!_reversedTransitionSound.SetData(samples, 0)) {
      Destroy(_reversedTransitionSound);
      _reversedTransitionSound = null;
    }
  }

  private void RandomizeInk() {
    // Randomize the sampled area of the noise.
    Vector2 offset = new Vector2(Random.Range(-100f, 100f), Random.Range(-100f, 100f));
    inkMaterial.SetVector(NoiseOffset, new Vector4(offset.x, offset.y, 0f, 0f));
    // Randomize the character of the ink edge.
    inkMaterial.SetFloat(FineNoiseScale, Random.Range(22, 28));
    inkMaterial.SetFloat(CoarseDistortion, Random.Range(0.12f, 0.18f));
    inkMaterial.SetFloat(FineDistortion, Random.Range(0.025f, 0.05f));
  }

  private void OnDestroy() {
    if (_reversedTransitionSound != null) Destroy(_reversedTransitionSound);
  }
}
