using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(AudioSource))]
public sealed class SettingsButtonHover : MonoBehaviour,
  IPointerEnterHandler,
  IPointerExitHandler {

  private const float HoverSoundStartOffset = 0.1f;
  private const string SaturationProperty = "_Saturation";

  private static AudioSource _activeHoverSource;

  [Header("References")]
  [SerializeField] private Button button;
  [SerializeField] private Graphic colorTarget;
  [SerializeField] private Image brushStroke;
  [SerializeField] private Shader selectiveColorShader;

  [Header("Animation")]
  [SerializeField, Min(0f)] private float colorDuration = 0.12f;
  [SerializeField, Min(0f)] private float tiltDuration = 0.12f;
  [SerializeField, Range(-10f, 10f)] private float tiltAngle = -2.5f;

  [Header("Audio")]
  [SerializeField] private AudioSource audioSource;
  [SerializeField] private AudioClip hoverSound;

  private Material _originalMaterial;
  private Material _selectiveColorMaterial;
  private RectTransform _rectTransform;
  private Quaternion _restingRotation;
  private Coroutine _colorRoutine;
  private Coroutine _tiltRoutine;
  private bool _pointerInside;
  private bool _showingHover;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    _activeHoverSource = null;
  }

  private void Reset() {
    button = GetComponent<Button>();
    audioSource = GetComponent<AudioSource>();
    colorTarget = button != null ? button.targetGraphic : GetComponent<Graphic>();
  }

  private void Awake() {
    ResolveReferences();
    CreateSelectiveColorMaterial();
    ResetVisuals();
  }

  private void OnEnable() {
    ResolveReferences();
    ResetVisuals();
  }

  private void OnDisable() {
    _pointerInside = false;
    _showingHover = false;
    StopAnimations();
    ResetVisuals();
  }

  private void OnDestroy() {
    if (colorTarget != null && colorTarget.material == _selectiveColorMaterial)
      colorTarget.material = _originalMaterial;

    if (_selectiveColorMaterial == null) return;

    if (Application.isPlaying) Destroy(_selectiveColorMaterial);
    else DestroyImmediate(_selectiveColorMaterial);
  }

  public void OnPointerEnter(PointerEventData eventData) {
    _pointerInside = true;
    RefreshHoverState();
  }

  public void OnPointerExit(PointerEventData eventData) {
    _pointerInside = false;
    RefreshHoverState();
  }

  private void ResolveReferences() {
    if (button == null) button = GetComponent<Button>();
    if (audioSource == null) audioSource = GetComponent<AudioSource>();
    if (colorTarget == null && button != null) colorTarget = button.targetGraphic;
    if (_rectTransform == null) _rectTransform = transform as RectTransform;

    if (_rectTransform != null && _tiltRoutine == null)
      _restingRotation = _rectTransform.localRotation;

    if (brushStroke != null) {
      brushStroke.fillAmount = 0f;
      brushStroke.enabled = false;
    }

    if (audioSource != null) {
      audioSource.playOnAwake = false;
      audioSource.spatialBlend = 0f;
      audioSource.ignoreListenerPause = true;
    }
  }

  private void CreateSelectiveColorMaterial() {
    if (_selectiveColorMaterial != null || colorTarget == null || selectiveColorShader == null)
      return;

    _originalMaterial = colorTarget.material;
    _selectiveColorMaterial = new Material(selectiveColorShader) {
      name = $"{name} UI Selective Color (Runtime)",
      hideFlags = HideFlags.HideAndDontSave
    };
    colorTarget.material = _selectiveColorMaterial;
  }

  private void RefreshHoverState() {
    bool shouldShow = _pointerInside && button != null && button.interactable;
    if (shouldShow == _showingHover) return;

    _showingHover = shouldShow;
    AnimateSaturation(shouldShow ? 1f : 0f);

    if (shouldShow) {
      PlayHoverSound();
      AnimateTilt(tiltAngle);
    } else {
      AnimateTilt(0f);
    }
  }

  private void AnimateSaturation(float target) {
    if (_selectiveColorMaterial == null) return;

    if (_colorRoutine != null) StopCoroutine(_colorRoutine);
    _colorRoutine = StartCoroutine(AnimateSaturationRoutine(target));
  }

  private IEnumerator AnimateSaturationRoutine(float target) {
    float start = _selectiveColorMaterial.GetFloat(SaturationProperty);
    if (colorDuration <= 0f) {
      _selectiveColorMaterial.SetFloat(SaturationProperty, target);
      _colorRoutine = null;
      yield break;
    }

    float elapsed = 0f;
    while (elapsed < colorDuration) {
      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(elapsed / colorDuration);
      t = 1f - Mathf.Pow(1f - t, 3f);
      _selectiveColorMaterial.SetFloat(SaturationProperty, Mathf.Lerp(start, target, t));
      yield return null;
    }

    _selectiveColorMaterial.SetFloat(SaturationProperty, target);
    _colorRoutine = null;
  }

  private void AnimateTilt(float targetAngle) {
    if (_rectTransform == null) return;

    if (_tiltRoutine != null) StopCoroutine(_tiltRoutine);
    _tiltRoutine = StartCoroutine(AnimateTiltRoutine(targetAngle));
  }

  private IEnumerator AnimateTiltRoutine(float targetAngle) {
    Quaternion start = _rectTransform.localRotation;
    Quaternion target = _restingRotation * Quaternion.Euler(0f, 0f, targetAngle);
    if (tiltDuration <= 0f) {
      _rectTransform.localRotation = target;
      _tiltRoutine = null;
      yield break;
    }

    float elapsed = 0f;
    while (elapsed < tiltDuration) {
      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(elapsed / tiltDuration);
      t = 1f - Mathf.Pow(1f - t, 3f);
      _rectTransform.localRotation = Quaternion.Slerp(start, target, t);
      yield return null;
    }

    _rectTransform.localRotation = target;
    _tiltRoutine = null;
  }

  private void StopTilt() {
    if (_tiltRoutine != null) {
      StopCoroutine(_tiltRoutine);
      _tiltRoutine = null;
    }

    if (_rectTransform != null) _rectTransform.localRotation = _restingRotation;
  }

  private void StopAnimations() {
    if (_colorRoutine != null) {
      StopCoroutine(_colorRoutine);
      _colorRoutine = null;
    }
    StopTilt();
  }

  private void ResetVisuals() {
    if (brushStroke != null) {
      brushStroke.fillAmount = 0f;
      brushStroke.enabled = false;
    }
    if (_selectiveColorMaterial != null)
      _selectiveColorMaterial.SetFloat(SaturationProperty, 0f);
    if (_rectTransform != null) _rectTransform.localRotation = _restingRotation;
  }

  private void PlayHoverSound() {
    if (audioSource == null || hoverSound == null) return;

    if (_activeHoverSource != null && _activeHoverSource != audioSource)
      _activeHoverSource.Stop();

    audioSource.Stop();
    audioSource.clip = hoverSound;
    audioSource.time = Mathf.Min(HoverSoundStartOffset, hoverSound.length - 0.001f);
    audioSource.Play();
    _activeHoverSource = audioSource;
  }
}
