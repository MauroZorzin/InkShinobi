using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Aggregates every active guard into one Palace threat vignette.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public sealed class GuardThreatFeedback : MonoBehaviour {
  [SerializeField] private VolumeProfile profile;
  [SerializeField] private float priority = 20f;

  [Header("Noticing")]
  [SerializeField] private Color noticingColor = new(0.55f, 0.18f, 0.02f, 1f);
  [SerializeField, Range(0f, 1f)] private float noticingIntensity = 0.24f;

  [Header("Searching")]
  [SerializeField] private Color searchingColor = new(0.5f, 0.3f, 0.02f, 1f);
  [SerializeField, Range(0f, 1f)] private float searchingIntensity = 0.3f;

  [Header("Chasing")]
  [SerializeField] private Color chasingColor = new(0.55f, 0.005f, 0.005f, 1f);
  [SerializeField, Range(0f, 1f)] private float chasingIntensity = 0.48f;
  [SerializeField, Range(0f, 0.5f)] private float chasePulseAmplitude = 0.08f;
  [SerializeField, Min(0f)] private float chasePulseSpeed = 1.8f;
  [SerializeField, Range(0f, 0.5f)] private float detectionFlash = 0.14f;
  [SerializeField, Min(0.01f)] private float detectionFlashDecay = 2.5f;

  [Header("Blend")]
  [SerializeField, Min(0f)] private float fadeSpeed = 3.5f;
  [SerializeField, Range(0f, 1f)] private float smoothness = 0.32f;

  private Volume volume;
  private VolumeProfile runtimeProfile;
  private Vignette vignette;
  private bool wasChased;
  private float flash;

  public void Configure(VolumeProfile sourceProfile) {
    profile = sourceProfile;
  }

  private void Awake() {
    volume = GetComponent<Volume>();
    volume.isGlobal = true;
    volume.priority = priority;
    volume.weight = 0f;
    if (profile == null) {
      Debug.LogError("[GuardThreatFeedback] Assign a dedicated vignette VolumeProfile.", this);
      enabled = false;
      return;
    }
    runtimeProfile = Instantiate(profile);
    volume.profile = runtimeProfile;
    if (!runtimeProfile.TryGet(out vignette)) vignette = runtimeProfile.Add<Vignette>(true);
    vignette.color.overrideState = true;
    vignette.intensity.overrideState = true;
    vignette.smoothness.overrideState = true;
    vignette.rounded.overrideState = true;
    vignette.rounded.value = true;
  }

  private void Update() {
    if (vignette == null) return;
    float maxDetection = 0f;
    bool chased = false;
    bool searched = false;
    foreach (GuardController guard in GuardController.ActiveGuards) {
      if (guard == null || !guard.isActiveAndEnabled) continue;
      maxDetection = Mathf.Max(maxDetection, guard.DetectionProgress);
      chased |= guard.CurrentState == GuardController.GuardState.Chasing;
      searched |= guard.CurrentState == GuardController.GuardState.Searching;
    }

    if (chased && !wasChased) flash = detectionFlash;
    wasChased = chased;
    flash = Mathf.MoveTowards(flash, 0f, detectionFlashDecay * Time.deltaTime);

    Color color;
    float intensity;
    float targetWeight;
    if (chased) {
      float pulse = Mathf.Sin(Time.time * chasePulseSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
      color = chasingColor;
      intensity = chasingIntensity + chasePulseAmplitude * pulse + flash;
      targetWeight = 1f;
    } else if (searched) {
      color = searchingColor;
      intensity = searchingIntensity;
      targetWeight = 1f;
    } else {
      color = noticingColor;
      intensity = noticingIntensity;
      targetWeight = maxDetection;
    }

    volume.weight = Mathf.MoveTowards(volume.weight, targetWeight, fadeSpeed * Time.deltaTime);
    vignette.color.value = color;
    vignette.intensity.value = Mathf.Clamp01(intensity);
    vignette.smoothness.value = smoothness;
  }

  private void OnDestroy() {
    if (runtimeProfile != null) Destroy(runtimeProfile);
  }
}
