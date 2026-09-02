using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Fully self-contained footstep sounds — attach to any moving object/character and it works on
/// its own: it measures its own movement by comparing position frame to frame, raycasts straight
/// down to find the SurfaceMarker on whatever it's standing on, and plays a random clip from
/// SurfaceAudioLibrary every stepDistance world units traveled. Nothing external needs to feed it
/// speed or surface, and no other script needs to know it exists.
/// </summary>
public class FootstepPlayer : MonoBehaviour {
  [Header("Surface Detection")]
  [Tooltip("Shared surface -> clip mapping.")]
  public SurfaceAudioLibrary library;

  [Tooltip("How far above this object's pivot the downward ground raycast starts.")]
  public float raycastHeight = 0.5f;

  [Tooltip("How far below the raycast start point to search for ground.")]
  public float raycastDistance = 2f;

  [Tooltip("Layers considered walkable ground for surface detection.")]
  public LayerMask groundMask = ~0;

  [Header("Step Timing")]
  [Tooltip("World units of movement between footstep sounds.")]
  public float stepDistance = 1.2f;

  [Tooltip("Movement speed below this (world units/second) counts as standing still — no footsteps.")]
  public float minSpeedToStep = 0.05f;

  [Header("Sound")]
  [Range(0f, 1f)] public float volume = 1f;

  [Tooltip("Random pitch variance applied to each step, as a +/- fraction of 1.0.")]
  [Range(0f, 0.5f)] public float pitchVariance = 0.08f;

  [Tooltip("Mixer group footsteps are routed through (e.g. your \"FX\" group). Leave empty to go straight to Master.")]
  public AudioMixerGroup mixerGroup;

  private AudioSource _source;
  private Vector3 _lastPosition;
  private float _distanceSinceLastStep;
  private bool _warnedNoLibrary;

  private void Awake() {
    _source = GetComponent<AudioSource>();
    if (_source == null) {
      _source = gameObject.AddComponent<AudioSource>();
      _source.playOnAwake = false;
      _source.spatialBlend = 1f;
    }

    _source.outputAudioMixerGroup = mixerGroup;
    _lastPosition = transform.position;

    if (library == null) {
      Debug.LogWarning($"[FootstepPlayer] '{name}': no SurfaceAudioLibrary assigned — footsteps will never play.", this);
    }
  }

  private void Update() {
    float distanceThisFrame = Vector3.Distance(transform.position, _lastPosition);
    _lastPosition = transform.position;

    float speed = Time.deltaTime > 0f ? distanceThisFrame / Time.deltaTime : 0f;
    if (speed < minSpeedToStep) {
      _distanceSinceLastStep = 0f;
      return;
    }

    _distanceSinceLastStep += distanceThisFrame;
    if (_distanceSinceLastStep < stepDistance) return;

    _distanceSinceLastStep = 0f;
    PlayStep();
  }

  private void PlayStep() {
    if (library == null) {
      if (!_warnedNoLibrary) {
        Debug.LogWarning($"[FootstepPlayer] '{name}': step triggered but no SurfaceAudioLibrary is assigned.", this);
        _warnedNoLibrary = true;
      }
      return;
    }

    SurfaceType surface = SurfaceDetection.DetectBelow(transform.position, raycastHeight, raycastDistance, groundMask);
    AudioClip clip = library.GetRandomClip(surface);

    if (clip == null) return;

    _source.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
    _source.PlayOneShot(clip, volume);
  }

#if UNITY_EDITOR
  private void OnDrawGizmosSelected() {
    Vector3 origin = transform.position + Vector3.up * raycastHeight;
    Gizmos.color = Color.yellow;
    Gizmos.DrawLine(origin, origin + Vector3.down * raycastDistance);
  }
#endif
}
