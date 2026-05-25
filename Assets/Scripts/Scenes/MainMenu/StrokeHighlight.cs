using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
[RequireComponent(typeof(Button))]

/// <summary>
/// Animates a brush-stroke image and optional hover sound for a menu button.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StrokeHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
  [Header("References")]
  [Tooltip("Button whose interactable state controls whether the highlight can appear.")]
  [SerializeField] private Button button;

  [Tooltip("Filled image used as the animated brush-stroke highlight.")]
  [SerializeField] private Image brushStroke;

  [Header("Animation")]
  [Tooltip("Seconds used to paint the brush stroke in on pointer enter.")]
  [SerializeField] private float paintInDuration = 0.14f;

  [Tooltip("Seconds used to fade the brush stroke out on pointer exit.")]
  [SerializeField] private float fadeOutDuration = 0.10f;

  [Header("Audio")]
  [Tooltip("Audio source used to play the hover sound.")]
  [SerializeField] private AudioSource audioSource;

  [Tooltip("Optional sound played when the pointer enters an interactable button.")]
  [SerializeField] private AudioClip hoverSound;

  private Coroutine animationRoutine;

  private void Reset() {
    button = GetComponent<Button>();
    audioSource = GetComponent<AudioSource>();
  }

  private void Awake() {
    if (button == null) {
      button = GetComponent<Button>();
    }
    if (audioSource == null) {
      audioSource = GetComponent<AudioSource>();
    }
    HideInstant();
  }

  private void OnEnable() {
    HideInstant();
  }

  public void OnPointerEnter(PointerEventData eventData) {
    if (!CanShow()) {
      return;
    }

    PlayHoverSound();
    StartAnimation(PaintIn());
  }

  public void OnPointerExit(PointerEventData eventData) {
    StartAnimation(FadeOut());
  }

  /// <summary>
  /// Checks whether all dependencies are available and the button can be highlighted.
  /// </summary>
  /// <returns>True when the highlight animation can run.</returns>
  private bool CanShow() {
    return button != null && button.interactable && brushStroke != null;
  }

  private void PlayHoverSound() {
    if (audioSource != null && hoverSound != null) {
      audioSource.PlayOneShot(hoverSound);
    }
  }

  /// <summary>
  /// Replaces the currently running highlight animation.
  /// </summary>
  /// <param name="routine">The animation routine to start.</param>
  private void StartAnimation(IEnumerator routine) {
    if (animationRoutine != null) {
      StopCoroutine(animationRoutine);
    }
    animationRoutine = StartCoroutine(routine);
  }

  /// <summary>
  /// Paints the brush stroke from empty to full.
  /// </summary>
  private IEnumerator PaintIn() {
    brushStroke.enabled = true;
    brushStroke.type = Image.Type.Filled;
    brushStroke.fillMethod = Image.FillMethod.Horizontal;
    brushStroke.fillOrigin = (int)Image.OriginHorizontal.Left;
    brushStroke.fillAmount = 0f;
    Color color = brushStroke.color;
    brushStroke.color = color;
    var elapsed = 0f;
    while (elapsed < paintInDuration) {
      elapsed += Time.unscaledDeltaTime;
      var t = Mathf.Clamp01(elapsed / paintInDuration);
      var eased = 1f - Mathf.Pow(1f - t, 3f);
      brushStroke.fillAmount = eased;
      yield return null;
    }
    brushStroke.fillAmount = 1f;
  }

  /// <summary>
  /// Fades the brush stroke back to hidden.
  /// </summary>
  private IEnumerator FadeOut() {
    if (brushStroke == null) {
      yield break;
    }
    brushStroke.enabled = true;
    brushStroke.type = Image.Type.Filled;
    brushStroke.fillMethod = Image.FillMethod.Horizontal;
    brushStroke.fillOrigin = (int)Image.OriginHorizontal.Left;
    Color color = brushStroke.color;
    brushStroke.color = color;
    var elapsed = 0f;
    var startFill = brushStroke.fillAmount;
    while (elapsed < fadeOutDuration) {
      elapsed += Time.unscaledDeltaTime;
      var t = Mathf.Clamp01(elapsed / fadeOutDuration);
      var eased = t * t * t;
      brushStroke.fillAmount = Mathf.Lerp(startFill, 0f, eased);
      yield return null;
    }
    brushStroke.fillAmount = 0f;
    brushStroke.enabled = false;
  }

  /// <summary>
  /// Hides the brush stroke without animation.
  /// </summary>
  private void HideInstant() {
    if (brushStroke == null) {
      return;
    }
    brushStroke.fillAmount = 0f;
    brushStroke.enabled = false;
  }
}
