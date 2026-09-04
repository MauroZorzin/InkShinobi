using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class InkTransition : MonoBehaviour {
  private const float MaximumAnimationDelta = 1f / 30f;

  [Header("Visual")]
  [SerializeField] private Material inkMaterial;
  [SerializeField] private float duration = 1f;

  [Header("Audio")]
  [SerializeField] private AudioClip fadeInSound;
  [SerializeField] private AudioClip fadeOutSound;
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

  private void Awake() {
    _owner = GetComponent<SceneTransitionManager>();
    if (_owner != null && SceneTransitionManager.Instance != _owner) return;

    inkMaterial.SetFloat(InkAmount, 0f);
    inkMaterial.SetPass(0);
    if (fadeInSound != null) fadeInSound.LoadAudioData();
    if (fadeOutSound != null) fadeOutSound.LoadAudioData();
  }

  public IEnumerator CoverScreen() {
    return ApplyToScreen();
  }

  public IEnumerator RevealScreen() {
    return ApplyToScreen(false);
  }

  private IEnumerator ApplyToScreen(bool cover = true) {
    AudioClip sound = cover ? fadeInSound : fadeOutSound;
    SceneTransitionManager.PlayUiSound(sound, mixerGroup, cover ? 0f : revealSoundStartOffset);
    if (cover) RandomizeInk();
    float time = 0f;
    while (time < duration) {
      time += Mathf.Min(Time.unscaledDeltaTime, MaximumAnimationDelta);
      float t = Mathf.Clamp01(time / duration);
      inkMaterial.SetFloat(InkAmount, cover ? t : 1f - t);
      yield return null;
    }
    inkMaterial.SetFloat(InkAmount, cover ? 1f : 0f);
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
}
