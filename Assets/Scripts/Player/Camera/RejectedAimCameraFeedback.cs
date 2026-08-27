using System.Collections;
using UnityEngine;

/// <summary>
/// Applies a small additive camera impulse to its own identity pivot. Camera controllers continue
/// to author the child camera pose, so rejected-action feedback cannot overwrite that pose.
/// </summary>
[DisallowMultipleComponent]
public sealed class RejectedAimCameraFeedback : MonoBehaviour {
  [SerializeField, Min(0f)] private float duration = 0.14f;
  [SerializeField, Min(0f)] private float positionAmplitude = 0.012f;
  [SerializeField, Min(0f)] private float rotationAmplitude = 0.25f;
  [SerializeField, Min(0.01f)] private float frequency = 22f;
  [SerializeField, Min(0f)] private float retriggerInterval = 0.18f;

  private Vector3 restingLocalPosition;
  private Quaternion restingLocalRotation;
  private Coroutine impulseRoutine;
  private float nextAllowedImpulseTime;

  private void Awake() {
    CaptureRestingPose();
  }

  private void OnEnable() {
    CaptureRestingPose();
  }

  private void OnDisable() {
    if (impulseRoutine != null) StopCoroutine(impulseRoutine);
    impulseRoutine = null;
    RestoreRestingPose();
  }

#if UNITY_EDITOR
  private void OnValidate() {
    duration = Mathf.Max(0f, duration);
    positionAmplitude = Mathf.Max(0f, positionAmplitude);
    rotationAmplitude = Mathf.Max(0f, rotationAmplitude);
    frequency = Mathf.Max(0.01f, frequency);
    retriggerInterval = Mathf.Max(0f, retriggerInterval);
  }
#endif

  public void PlayRejectedAction() {
    if (!isActiveAndEnabled || duration <= 0f || Time.unscaledTime < nextAllowedImpulseTime) return;
    if (impulseRoutine != null) return;

    nextAllowedImpulseTime = Time.unscaledTime + retriggerInterval;
    impulseRoutine = StartCoroutine(ImpulseRoutine());
  }

  private IEnumerator ImpulseRoutine() {
    float elapsed = 0f;
    while (elapsed < duration) {
      if (SceneTransitionManager.IsGamePaused || SceneTransitionManager.IsDeathSequenceActive) {
        RestoreRestingPose();
        impulseRoutine = null;
        yield break;
      }

      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / duration);
      float envelope = 1f - Mathf.SmoothStep(0f, 1f, progress);
      float phase = elapsed * frequency * Mathf.PI * 2f;
      float horizontal = Mathf.Sin(phase);
      float vertical = Mathf.Sin(phase * 1.61f + 0.8f);

      transform.localPosition = restingLocalPosition
                                + new Vector3(horizontal, vertical, 0f) * (positionAmplitude * envelope);
      transform.localRotation = restingLocalRotation * Quaternion.Euler(
        vertical * rotationAmplitude * 0.35f * envelope,
        horizontal * rotationAmplitude * 0.35f * envelope,
        horizontal * rotationAmplitude * envelope);
      yield return null;
    }

    RestoreRestingPose();
    impulseRoutine = null;
  }

  private void CaptureRestingPose() {
    restingLocalPosition = transform.localPosition;
    restingLocalRotation = transform.localRotation;
  }

  private void RestoreRestingPose() {
    transform.localPosition = restingLocalPosition;
    transform.localRotation = restingLocalRotation;
  }
}
