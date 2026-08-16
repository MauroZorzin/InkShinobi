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
public class StrokeHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
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
  private bool _textWasEnabledBeforeSelection;
  private bool _hasStoredTextState;
  private TMP_Text _selectedTextOverlay;

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
    if (buttonLabel == null) {
      buttonLabel = GetComponentInChildren<TMP_Text>(includeInactive: true);
    }
    HideInstant();
  }

  private void OnEnable() {
    ClearSelectedTextOverlay();
    _selected = false;
    HideInstant();
  }

  private void OnDisable() {
    ClearSelectedTextOverlay();
    _selected = false;
    HideInstant();
  }

  private void LateUpdate() {
    if (_selectedTextOverlay == null || buttonLabel == null) return;

    SelectedMenuTextOverlay.Align(buttonLabel.rectTransform, _selectedTextOverlay.rectTransform);
  }

  public void OnPointerEnter(PointerEventData eventData) {
    if (!CanShow()) {
      return;
    }
    if (_selected) return;

    // A newly loaded scene or a closing modal can put an unchanged pointer over this button and
    // generate a synthetic enter event. Only actual pointer movement should produce hover audio.
    if (eventData.delta.sqrMagnitude > 0.01f) PlayHoverSound();
    StartAnimation(PaintIn());
  }

  public void OnPointerExit(PointerEventData eventData) {
    if (_selected) return;

    StartAnimation(FadeOut());
  }

  public void OnPointerClick(PointerEventData eventData) {
    if (!CanShow()) return;

    Select();
  }

  public void Deselect() {
    if (!_selected) return;

    _selected = false;
    ClearSelectedTextOverlay();

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

    _textColorBeforeSelection = buttonLabel.color;
    _textWasEnabledBeforeSelection = buttonLabel.enabled;
    _hasStoredTextState = true;
    _selectedTextOverlay = SelectedMenuTextOverlay.Create(buttonLabel);

    if (_selectedTextOverlay != null) {
      _selectedTextOverlay.color = Color.white;
      buttonLabel.enabled = false;
    } else {
      // Fallback if an overlay canvas cannot be created.
      buttonLabel.color = Color.white;
    }
  }

  private void ClearSelectedTextOverlay() {
    if (_selectedTextOverlay != null) {
      Destroy(_selectedTextOverlay.gameObject);
      _selectedTextOverlay = null;
    }

    if (buttonLabel == null || !_hasStoredTextState) return;

    buttonLabel.enabled = _textWasEnabledBeforeSelection;
    buttonLabel.color = _textColorBeforeSelection;
    _hasStoredTextState = false;
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

/// <summary>
/// Hosts selected menu labels after camera post-processing, leaving their unselected originals on
/// the camera canvas. Copies are visual only and never intercept pointer input.
/// </summary>
internal static class SelectedMenuTextOverlay {
  private const int SortingOrder = 900;

  private static RectTransform _overlayTransform;
  private static readonly Vector3[] WorldCorners = new Vector3[4];

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    _overlayTransform = null;
  }

  public static TMP_Text Create(TMP_Text source) {
    if (source == null) return null;

    EnsureCanvas();
    if (_overlayTransform == null) return null;

    GameObject copyObject = Object.Instantiate(source.gameObject, _overlayTransform, false);
    copyObject.name = $"{source.gameObject.name} (Selected Overlay)";

    TMP_Text copy = copyObject.GetComponent<TMP_Text>();
    if (copy == null) {
      Object.Destroy(copyObject);
      return null;
    }

    copy.raycastTarget = false;
    copy.enabled = true;
    Align(source.rectTransform, copy.rectTransform);
    return copy;
  }

  public static void Align(RectTransform source, RectTransform copy) {
    if (source == null || copy == null || _overlayTransform == null) return;

    Canvas sourceCanvas = source.GetComponentInParent<Canvas>();
    Camera sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
      ? sourceCanvas.worldCamera
      : null;

    source.GetWorldCorners(WorldCorners);
    Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(sourceCamera, WorldCorners[0]);
    Vector2 topRightScreen = RectTransformUtility.WorldToScreenPoint(sourceCamera, WorldCorners[2]);

    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
      _overlayTransform,
      bottomLeftScreen,
      null,
      out Vector2 bottomLeft
    )) return;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
      _overlayTransform,
      topRightScreen,
      null,
      out Vector2 topRight
    )) return;

    copy.anchorMin = new Vector2(0.5f, 0.5f);
    copy.anchorMax = new Vector2(0.5f, 0.5f);
    copy.pivot = source.pivot;
    copy.sizeDelta = topRight - bottomLeft;
    copy.anchoredPosition = new Vector2(
      Mathf.Lerp(bottomLeft.x, topRight.x, source.pivot.x),
      Mathf.Lerp(bottomLeft.y, topRight.y, source.pivot.y)
    );
    copy.localScale = Vector3.one;
    copy.localRotation = Quaternion.Euler(0f, 0f, source.eulerAngles.z);
  }

  private static void EnsureCanvas() {
    if (_overlayTransform != null) return;

    var overlayObject = new GameObject("SelectedMenuTextOverlay", typeof(RectTransform));
    Canvas canvas = overlayObject.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = SortingOrder;

    CanvasScaler scaler = overlayObject.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
    scaler.scaleFactor = 1f;

    _overlayTransform = overlayObject.GetComponent<RectTransform>();
    Canvas.ForceUpdateCanvases();
  }
}
