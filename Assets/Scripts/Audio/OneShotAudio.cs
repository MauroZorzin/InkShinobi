using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// AudioSource.PlayClipAtPoint has no way to route through an AudioMixerGroup — it always plays
/// straight to Master. This reimplements the same "temporary GameObject + AudioSource, auto-
/// destroyed once the clip finishes" pattern, but with a mixer group, so one-shot stingers (guard
/// barks, switch/death sounds) can be routed to a channel (e.g. "FX") instead of bypassing the
/// mixer entirely.
/// </summary>
public static class OneShotAudio {
  public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume = 1f, AudioMixerGroup mixerGroup = null) {
    if (clip == null) return;

    var go = new GameObject($"OneShotAudio_{clip.name}");
    go.transform.position = position;

    AudioSource source = go.AddComponent<AudioSource>();
    source.clip = clip;
    source.volume = volume;
    source.spatialBlend = 1f;
    source.outputAudioMixerGroup = mixerGroup;
    source.Play();

    Object.Destroy(go, clip.length);
  }
}
