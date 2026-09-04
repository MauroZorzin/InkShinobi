using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Paints a brush-stroke highlight and plays a hover sound for an interactable UI button.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class StrokeHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
  private const float HoverSoundStartOffset = 0.1f;
  private static AudioSource _activeHoverSource;

  [Header("References")]
  [SerializeField, Tooltip("Filled image animated as the button's brush-stroke highlight.")]
  private Image brushStroke;

  [Header("Animation")]
  [SerializeField, Min(0f), Tooltip("Seconds used to paint the brush stroke in on pointer enter.")]
  private float paintInDuration = 0.14f;

  [Header("Audio")]
  [SerializeField, Tooltip("Audio source used to play the button's hover sound.")]
  private AudioSource audioSource;
  [SerializeField, Tooltip("Sound played when the pointer enters the button.")]
  private AudioClip hoverSound;

  private Button _button;
  private Coroutine _paintRoutine;

  private void Awake() {
    _button = GetComponent<Button>();
    brushStroke.type = Image.Type.Filled;
    brushStroke.fillMethod = Image.FillMethod.Horizontal;
    brushStroke.fillOrigin = (int)Image.OriginHorizontal.Left;
    audioSource.playOnAwake = false;
    audioSource.loop = false;
    audioSource.spatialBlend = 0f;
    audioSource.ignoreListenerPause = true;
  }

  private void OnEnable() {
    HideInstant();
  }

  private void OnDisable() {
    HideInstant();
  }

  public void OnPointerEnter(PointerEventData eventData) {
    if (_button.interactable) {
      PlayHoverSound();
      if (_paintRoutine != null) {
        StopCoroutine(_paintRoutine);
      }
      _paintRoutine = StartCoroutine(PaintIn());
    }
  }

  public void OnPointerExit(PointerEventData eventData) {
    HideInstant();
  }

  public void Deselect() {
    HideInstant();
  }

  private IEnumerator PaintIn() {
    brushStroke.enabled = true;
    brushStroke.fillAmount = 0f;
    var elapsed = 0f;
    while (elapsed < paintInDuration) {
      elapsed += Time.unscaledDeltaTime;
      var progress = Mathf.Clamp01(elapsed / paintInDuration);
      brushStroke.fillAmount = 1f - Mathf.Pow(1f - progress, 3f);
      yield return null;
    }
    brushStroke.fillAmount = 1f;
    _paintRoutine = null;
  }

  private void PlayHoverSound() {
    if (_activeHoverSource != null && _activeHoverSource != audioSource) {
      _activeHoverSource.Stop();
    }
    audioSource.Stop();
    audioSource.clip = hoverSound;
    audioSource.time = Mathf.Clamp(HoverSoundStartOffset, 0f, Mathf.Max(0f, hoverSound.length - 0.001f));
    audioSource.Play();
    _activeHoverSource = audioSource;
  }

  private void HideInstant() {
    if (_paintRoutine != null) {
      StopCoroutine(_paintRoutine);
      _paintRoutine = null;
    }
    brushStroke.fillAmount = 0f;
    brushStroke.enabled = false;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    _activeHoverSource = null;
  }
}
