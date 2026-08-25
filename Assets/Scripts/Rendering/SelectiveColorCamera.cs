using UnityEngine;

/// <summary>
/// Opts one camera into the selective-color renderer feature. Keeping the settings on the camera
/// allows the Palace to develop this art direction without changing every other scene.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class SelectiveColorCamera : MonoBehaviour {
  [Tooltip("Enables monochrome rendering for this camera only.")]
  public bool effectEnabled = true;

  [Tooltip("Overall blend of the effect. Zero leaves the scene untouched; one applies the configured background saturation.")]
  [Range(0f, 1f)] public float intensity = 1f;

  [Tooltip("Saturation retained by ordinary world objects. Zero is fully black-and-white; one keeps their original color.")]
  [Range(0f, 1f)] public float backgroundSaturation = 0f;

  [Tooltip("How strongly marked SelectiveColor objects recover their original color.")]
  [Range(0f, 1f)] public float preservedColorStrength = 1f;
}
