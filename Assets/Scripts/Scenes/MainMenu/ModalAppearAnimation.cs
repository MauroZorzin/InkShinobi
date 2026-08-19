using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public sealed class ModalAppearAnimation : MonoBehaviour {
  private const float Duration = 0.5f;
  private static readonly Vector3 StartScale = new(0.97f, 0.08f, 1f);

  private CanvasGroup _canvasGroup;
  private Vector3 _targetScale;
  private Coroutine _animation;

  public bool IsClosing { get; private set; }

  private void Awake() {
    _targetScale = transform.localScale;
    _canvasGroup = GetComponent<CanvasGroup>();
    Prepare();
  }

  private void Prepare() {
    transform.localScale = Vector3.Scale(_targetScale, StartScale);
    _canvasGroup.alpha = 0f;
    _canvasGroup.interactable = false;
    _canvasGroup.blocksRaycasts = false;
  }

  public void Play() {
    if (_animation != null) StopCoroutine(_animation);
    IsClosing = false;
    Prepare();
    _animation = StartCoroutine(AnimateOpen());
  }

  public void PlayReverse(Action onComplete) {
    if (IsClosing) return;

    if (_animation != null) StopCoroutine(_animation);
    IsClosing = true;
    _canvasGroup.interactable = false;
    _canvasGroup.blocksRaycasts = false;
    _animation = StartCoroutine(AnimateClosed(onComplete));
  }

  private IEnumerator AnimateOpen() {
    float elapsed = 0f;
    Vector3 initialScale = transform.localScale;

    while (elapsed < Duration) {
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / Duration);
      float eased = 1f - Mathf.Pow(1f - progress, 3f);

      transform.localScale = Vector3.LerpUnclamped(initialScale, _targetScale, eased);
      _canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 1.5f));
      yield return null;
    }

    transform.localScale = _targetScale;
    _canvasGroup.alpha = 1f;
    _canvasGroup.interactable = true;
    _canvasGroup.blocksRaycasts = true;
    _animation = null;
  }

  private IEnumerator AnimateClosed(Action onComplete) {
    float elapsed = 0f;
    Vector3 foldedScale = Vector3.Scale(_targetScale, StartScale);

    while (elapsed < Duration) {
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / Duration);
      float reverseProgress = 1f - progress;
      float eased = 1f - Mathf.Pow(progress, 3f);

      transform.localScale = Vector3.LerpUnclamped(foldedScale, _targetScale, eased);
      _canvasGroup.alpha = Mathf.SmoothStep(
        0f,
        1f,
        Mathf.Clamp01(reverseProgress * 1.5f)
      );
      yield return null;
    }

    transform.localScale = foldedScale;
    _canvasGroup.alpha = 0f;
    _animation = null;
    onComplete?.Invoke();
  }
}
