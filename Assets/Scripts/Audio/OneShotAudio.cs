using UnityEngine;
using UnityEngine.Audio;

/// <summary>Come AudioSource.PlayClipAtPoint, ma instrada attraverso un AudioMixerGroup invece di andare sempre al Master.</summary>
public static class OneShotAudio {
  public static AudioSource PlayClipAtPoint(
      AudioClip clip, Vector3 position, float volume = 1f, AudioMixerGroup mixerGroup = null, float pitchVariance = 0f) {
    if (clip == null) return null;

    var go = new GameObject($"OneShotAudio_{clip.name}");
    go.transform.position = position;

    AudioSource source = go.AddComponent<AudioSource>();
    source.clip = clip;
    source.volume = volume;
    source.spatialBlend = 1f;
    source.outputAudioMixerGroup = mixerGroup;
    source.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
    source.Play();

    Object.Destroy(go, clip.length);
    return source;
  }
}
