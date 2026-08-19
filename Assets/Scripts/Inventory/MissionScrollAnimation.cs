using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Lifts a rolled mission scroll into place, then reveals it downward from its rolled top.
/// Audio is split into pickup, unfold, and discard slots so each phase can receive its own clip.
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class MissionScrollAnimation : MonoBehaviour {
  private const float RolledFillAmount = 0.2f;
  private const float PickupLiftDistance = 35f;

  [Header("Animation")]
  [SerializeField] private Image scrollImage;
  [Min(0.01f)][SerializeField] private float unfoldDuration = 0.7f;

  [Header("Audio Phases")]
  [SerializeField] private AudioClip pickupSound;
  [SerializeField] private AudioClip unfoldSound;
  [SerializeField] private AudioClip discardSound;
  [SerializeField] private AudioMixerGroup mixerGroup;

  private AudioSource _audioSource;
  private Coroutine _animation;
  private readonly List<Material> _depthOverrideMaterials = new();
  private bool _configuredDepth;
  private RectTransform _contentMaskRect;
  private RectMask2D _contentMask;
  private GameObject _maskedContent;
  private float _contentMaskFullHeight;

  public bool IsAnimating => _animation != null;

  private void Awake() {
    if (scrollImage == null) scrollImage = GetComponent<Image>();

    _audioSource = GetComponent<AudioSource>();
    if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
    _audioSource.playOnAwake = false;
    _audioSource.loop = false;
    _audioSource.spatialBlend = 0f;
    _audioSource.ignoreListenerPause = true;
    _audioSource.outputAudioMixerGroup = mixerGroup;

    ConfigureTopDownFill();
  }

  public void PlayOpen(GameObject content) {
    if (_animation != null) StopCoroutine(_animation);
    ConfigureMissionText(content);
    ConfigureDepthIndependentRendering(content);
    _animation = StartCoroutine(OpenRoutine(content));
  }

  public void PlayClose(GameObject content, Action onComplete) {
    if (_animation != null) StopCoroutine(_animation);
    _animation = null;
    if (content != null) content.SetActive(false);
    SceneTransitionManager.PlayUiSound(discardSound, mixerGroup);
    scrollImage.fillAmount = RolledFillAmount;
    onComplete?.Invoke();
  }

  private IEnumerator OpenRoutine(GameObject content) {
    ConfigureTopDownFill();
    scrollImage.fillAmount = RolledFillAmount;
    if (content != null) content.SetActive(false);

    PlaySound(pickupSound);
    yield return PlayPickupMotion();
    PlaySound(unfoldSound);

    PrepareContentMask(content);
    float elapsed = 0f;

    while (elapsed < unfoldDuration) {
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / unfoldDuration);
      float eased = 1f - Mathf.Pow(1f - progress, 3f);
      scrollImage.fillAmount = Mathf.Lerp(RolledFillAmount, 1f, eased);
      SetContentMaskFill(scrollImage.fillAmount);
      yield return null;
    }

    scrollImage.fillAmount = 1f;
    SetContentMaskFill(1f);
    _animation = null;
  }

  private void PrepareContentMask(GameObject content) {
    if (content == null) return;

    CanvasGroup previousFade = content.GetComponent<CanvasGroup>();
    if (previousFade != null) previousFade.alpha = 1f;

    RectTransform scrollRect = scrollImage.rectTransform;
    if (_contentMaskRect == null) {
      var maskObject = new GameObject(
        "Mission Text Reveal Mask",
        typeof(RectTransform),
        typeof(RectMask2D)
      );
      _contentMaskRect = maskObject.GetComponent<RectTransform>();
      _contentMask = maskObject.GetComponent<RectMask2D>();
      _contentMaskRect.SetParent(scrollRect.parent, false);
      _contentMaskRect.SetSiblingIndex(scrollRect.GetSiblingIndex() + 1);
    }

    ConfigureContentMaskLayout(scrollRect);
    if (_maskedContent != content || content.transform.parent != _contentMaskRect) {
      content.transform.SetParent(_contentMaskRect, true);
      _maskedContent = content;
    }

    SetContentMaskFill(RolledFillAmount);
    content.SetActive(true);
  }

  private void ConfigureContentMaskLayout(RectTransform scrollRect) {
    _contentMaskFullHeight = scrollRect.rect.height;

    _contentMaskRect.anchorMin = scrollRect.anchorMin;
    _contentMaskRect.anchorMax = scrollRect.anchorMax;
    _contentMaskRect.pivot = scrollRect.pivot;
    _contentMaskRect.anchoredPosition = scrollRect.anchoredPosition;
    _contentMaskRect.sizeDelta = scrollRect.sizeDelta;
    _contentMaskRect.localRotation = scrollRect.localRotation;
    _contentMaskRect.localScale = scrollRect.localScale;
  }

  private void SetContentMaskFill(float fillAmount) {
    if (_contentMask == null) return;

    float hiddenFromBottom = _contentMaskFullHeight * (1f - Mathf.Clamp01(fillAmount));
    _contentMask.padding = new Vector4(0f, hiddenFromBottom, 0f, 0f);
  }

  private IEnumerator PlayPickupMotion() {
    RectTransform rect = scrollImage.rectTransform;
    Vector2 targetPosition = rect.anchoredPosition;
    Vector2 startPosition = targetPosition + Vector2.down * PickupLiftDistance;
    Vector3 targetScale = rect.localScale;
    Vector3 startScale = Vector3.Scale(targetScale, new Vector3(0.94f, 0.94f, 1f));
    Color targetColor = scrollImage.color;
    Color startColor = targetColor;
    startColor.a = 0f;

    rect.anchoredPosition = startPosition;
    rect.localScale = startScale;
    scrollImage.color = startColor;

    float duration = pickupSound != null ? pickupSound.length : 0f;
    float elapsed = 0f;
    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      float progress = Mathf.Clamp01(elapsed / duration);
      float eased = 1f - Mathf.Pow(1f - progress, 3f);
      rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, eased);
      rect.localScale = Vector3.LerpUnclamped(startScale, targetScale, eased);
      scrollImage.color = Color.LerpUnclamped(startColor, targetColor, eased);
      yield return null;
    }

    rect.anchoredPosition = targetPosition;
    rect.localScale = targetScale;
    scrollImage.color = targetColor;
  }

  private void ConfigureTopDownFill() {
    scrollImage.type = Image.Type.Filled;
    scrollImage.fillMethod = Image.FillMethod.Vertical;
    scrollImage.fillOrigin = (int)Image.OriginVertical.Top;
    scrollImage.fillClockwise = true;
  }

  private void PlaySound(AudioClip clip) {
    if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip);
  }

  private void ConfigureDepthIndependentRendering(GameObject content) {
    if (_configuredDepth) return;

    ConfigureGraphic(scrollImage);
    if (content != null) {
      Graphic[] contentGraphics = content.GetComponentsInChildren<Graphic>(true);
      foreach (Graphic graphic in contentGraphics) ConfigureGraphic(graphic);
    }

    _configuredDepth = true;
  }

  private static void ConfigureMissionText(GameObject content) {
    if (content == null) return;

    TMP_Text[] labels = content.GetComponentsInChildren<TMP_Text>(true);
    foreach (TMP_Text label in labels) {
      label.color = Color.black;
    }
  }

  private void ConfigureGraphic(Graphic graphic) {
    if (graphic == null) return;

    Material source = graphic.material != null ? graphic.material : graphic.defaultMaterial;
    if (source == null) return;

    Material material = new(source) {
      name = $"{source.name} (Mission Scroll Depth Override)",
      hideFlags = HideFlags.DontSave
    };

    if (material.HasProperty("unity_GUIZTestMode")) {
      material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
    }
    if (material.HasProperty("_ZTestMode")) {
      material.SetInt("_ZTestMode", (int)CompareFunction.Always);
    }
    if (material.HasProperty("_ZTest")) {
      material.SetInt("_ZTest", (int)CompareFunction.Always);
    }

    _depthOverrideMaterials.Add(material);
  }

  private void OnDestroy() {
    foreach (Material material in _depthOverrideMaterials) {
      if (material != null) Destroy(material);
    }
    _depthOverrideMaterials.Clear();
  }
}
