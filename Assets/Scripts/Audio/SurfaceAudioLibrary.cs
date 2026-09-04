using UnityEngine;

/// <summary>Asset dati condiviso che mappa SurfaceType a un insieme di clip di passi, scelte a caso ad ogni passo.</summary>
[CreateAssetMenu(fileName = "SurfaceAudioLibrary", menuName = "Audio/Surface Audio Library")]
public class SurfaceAudioLibrary : ScriptableObject {
  [System.Serializable]
  public struct SurfaceClips {
    public SurfaceType surface;
    public AudioClip[] clips;
  }

  public SurfaceClips[] entries = System.Array.Empty<SurfaceClips>();

  [Tooltip("Used when no entry matches the requested surface, or the matching entry has no clips assigned.")]
  public AudioClip[] fallbackClips = System.Array.Empty<AudioClip>();

  public AudioClip GetRandomClip(SurfaceType surface) {
    foreach (SurfaceClips entry in entries) {
      if (entry.surface == surface && entry.clips != null && entry.clips.Length > 0) {
        return entry.clips[Random.Range(0, entry.clips.Length)];
      }
    }

    if (fallbackClips != null && fallbackClips.Length > 0) {
      return fallbackClips[Random.Range(0, fallbackClips.Length)];
    }

    return null;
  }
}
