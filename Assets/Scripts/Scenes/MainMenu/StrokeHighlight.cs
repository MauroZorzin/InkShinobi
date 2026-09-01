using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
[RequireComponent(typeof(Button))]

/// <summary>
/// Animates a brush-stroke image and optional hover sound for a menu button.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StrokeHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
  private const float HoverSoundStartOffset = 0.1f;

  private static AudioSource _activeHoverSource;

  [Header("References")]
  [Tooltip("Button whose interactable state controls whether the highlight can appear.")]
  [SerializeField] private Button button;

  [Tooltip("Filled image used as the animated brush-stroke highlight.")]
  [SerializeField] private Image brushStroke;

  [Tooltip("Button label optionally rendered as white while hovered.")]
  [SerializeField] private TMP_Text buttonLabel;

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
  [SerializeField] private bool _useWhiteTextOnHover;
  private bool _hasHoverTextColor;
  private Color _textColorBeforeHover;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    _activeHoverSource = null;
  }

  private void Reset() {
    button = GetComponent<Button>();
    audioSource = GetComponent<AudioSource>();
  }

  private void Awake() {
    EnsureStyleReferences();
    HideInstant();
  }

  internal void EnsureStyleReferences() {
    if (button == null) {
      button = GetComponent<Button>();
    }
    if (audioSource == null) {
      audioSource = GetComponent<AudioSource>();
    }
    if (audioSource != null) {
      audioSource.playOnAwake = false;
      audioSource.spatialBlend = 0f;
      audioSource.ignoreListenerPause = true;
    }
    if (buttonLabel == null) {
      buttonLabel = GetComponentInChildren<TMP_Text>(includeInactive: true);
    }
  }

  private void OnEnable() {
    HideInstant();
  }

  private void OnDisable() {
    RestoreTextAfterHover();
    HideInstant();
  }

  public void OnPointerEnter(PointerEventData eventData) {
    if (!CanShow()) {
      return;
    }

    if (_useWhiteTextOnHover && buttonLabel != null && !_hasHoverTextColor) {
      _textColorBeforeHover = buttonLabel.color;
      _hasHoverTextColor = true;
      buttonLabel.color = Color.white;
    }

    PlayHoverSound();
    StartAnimation(PaintIn());
  }

  public void OnPointerExit(PointerEventData eventData) {
    RestoreTextAfterHover();
    StartAnimation(FadeOut());
  }

  public void Deselect() {
    RestoreTextAfterHover();
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
      if (_activeHoverSource != null && _activeHoverSource != audioSource) {
        _activeHoverSource.Stop();
      }

      audioSource.Stop();
      audioSource.clip = hoverSound;
      audioSource.time = Mathf.Min(HoverSoundStartOffset, hoverSound.length - 0.001f);
      audioSource.Play();
      _activeHoverSource = audioSource;
    }
  }

  private void RestoreTextAfterHover() {
    if (!_hasHoverTextColor) return;

    if (buttonLabel != null) buttonLabel.color = _textColorBeforeHover;
    _hasHoverTextColor = false;
  }

  /// <summary>
  /// Replaces the currently running highlight animation.
  /// </summary>
  /// <param name="routine">The animation routine to start.</param>
  private void StartAnimation(IEnumerator routine) {
    // Menu panels are deactivated before their highlights are deselected. Coroutines cannot be
    // started on inactive GameObjects, so finish the visual reset synchronously in that case.
    if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
      animationRoutine = null;
      HideInstant();
      return;
    }

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
