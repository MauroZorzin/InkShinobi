using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;
[RequireComponent(typeof(Button))]

/// <summary>
/// Animates a brush-stroke image and optional hover sound for a menu button.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StrokeHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
  private const float HoverSoundStartOffset = 0.1f;

  private static AudioSource _activeHoverSource;

  [Header("References")]
  [Tooltip("Button whose interactable state controls whether the highlight can appear.")]
  [SerializeField] private Button button;

  [Tooltip("Filled image used as the animated brush-stroke highlight.")]
  [SerializeField] private Image brushStroke;

  [Tooltip("Button label rendered as neutral white after selection.")]
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
  private bool _selected;
  private Color _textColorBeforeSelection;
  private bool _hasStoredTextState;
  private bool _useWhiteTextOnHover;
  private bool _useWhiteTextOnSelection = true;
  private bool _hasHoverTextColor;
  private Color _textColorBeforeHover;

  internal TMP_FontAsset StyleFont => buttonLabel != null ? buttonLabel.font : null;
  internal Material StyleFontMaterial => buttonLabel != null ? buttonLabel.fontSharedMaterial : null;
  internal FontStyles StyleFontStyle => buttonLabel != null ? buttonLabel.fontStyle : FontStyles.Normal;
  internal Sprite StyleBrushSprite => brushStroke != null ? brushStroke.sprite : null;
  internal Color StyleBrushColor => brushStroke != null ? brushStroke.color : Color.white;
  internal Sprite StylePanelSprite {
    get {
      Image[] images = GetComponentsInChildren<Image>(includeInactive: true);
      foreach (Image image in images) {
        if (image.gameObject.name == "Background" && image.sprite != null) return image.sprite;
      }
      return null;
    }
  }
  internal AudioClip StyleHoverSound => hoverSound;
  internal AudioMixerGroup StyleMixerGroup => audioSource != null ? audioSource.outputAudioMixerGroup : null;
  internal float StylePaintInDuration => paintInDuration;
  internal float StyleFadeOutDuration => fadeOutDuration;

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

  internal void Configure(
    Button configuredButton,
    Image configuredBrushStroke,
    TMP_Text configuredLabel,
    AudioSource configuredAudioSource,
    AudioClip configuredHoverSound,
    float configuredPaintInDuration,
    float configuredFadeOutDuration,
    bool useWhiteTextOnHover,
    bool useWhiteTextOnSelection
  ) {
    button = configuredButton;
    brushStroke = configuredBrushStroke;
    buttonLabel = configuredLabel;
    audioSource = configuredAudioSource;
    hoverSound = configuredHoverSound;
    paintInDuration = configuredPaintInDuration;
    fadeOutDuration = configuredFadeOutDuration;
    _useWhiteTextOnHover = useWhiteTextOnHover;
    _useWhiteTextOnSelection = useWhiteTextOnSelection;
    HideInstant();
  }

  private void OnEnable() {
    RestoreTextAfterSelection();
    _selected = false;
    HideInstant();
  }

  private void OnDisable() {
    RestoreTextAfterSelection();
    RestoreTextAfterHover();
    _selected = false;
    HideInstant();
  }

  public void OnPointerEnter(PointerEventData eventData) {
    if (!CanShow()) {
      return;
    }
    if (_selected) return;

    if (_useWhiteTextOnHover && buttonLabel != null && !_hasHoverTextColor) {
      _textColorBeforeHover = buttonLabel.color;
      _hasHoverTextColor = true;
      buttonLabel.color = Color.white;
    }

    PlayHoverSound();
    StartAnimation(PaintIn());
  }

  public void OnPointerExit(PointerEventData eventData) {
    if (_selected) return;

    RestoreTextAfterHover();
    StartAnimation(FadeOut());
  }

  public void OnPointerClick(PointerEventData eventData) {
    if (!CanShow()) return;

    Select();
  }

  public void Deselect() {
    if (!_selected) return;

    _selected = false;
    RestoreTextAfterSelection();

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

  private void Select() {
    if (_selected) return;

    _selected = true;

    if (animationRoutine != null) {
      StopCoroutine(animationRoutine);
      animationRoutine = null;
    }

    brushStroke.enabled = true;
    brushStroke.type = Image.Type.Filled;
    brushStroke.fillMethod = Image.FillMethod.Horizontal;
    brushStroke.fillOrigin = (int)Image.OriginHorizontal.Left;
    brushStroke.fillAmount = 1f;

    if (buttonLabel == null) return;

    _textColorBeforeSelection = _hasHoverTextColor ? _textColorBeforeHover : buttonLabel.color;
    _hasHoverTextColor = false;
    _hasStoredTextState = true;
    buttonLabel.color = _useWhiteTextOnSelection ? Color.white : _textColorBeforeSelection;
  }

  private void RestoreTextAfterSelection() {
    if (buttonLabel == null || !_hasStoredTextState) return;

    buttonLabel.color = _textColorBeforeSelection;
    _hasStoredTextState = false;
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
