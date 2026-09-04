using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>Unico elemento di testo HUD condiviso, guidato da tre fonti di contenuto in ordine di priorità: Dialogo, poi Prompt di interazione, poi Informazione.</summary>
public class DialogueHUD : MonoBehaviour {
  private enum Source { Information = 0, Interaction = 1, Dialogue = 2 }

  [Header("Shared Label")]
  [SerializeField] private TextMeshProUGUI messageLabel;

  [Header("Information Background")]
  [Tooltip("Shown only while Information is the source currently displayed.")]
  [SerializeField] private GameObject informationBackground;

  [Header("Dialogue Background + Portrait")]
  [Tooltip("Shown only while Dialogue is the source currently displayed.")]
  [SerializeField] private GameObject dialogueBackground;

  [Tooltip("Fixed portrait shown alongside every dialogue line — there is only one dialogue speaker (the player), so this isn't set per message.")]
  [SerializeField] private Image dialoguePortrait;

  [Header("Portrait Slide")]
  [Tooltip("How far offscreen (anchored-position units, local X) the portrait sits when hidden, before sliding in / after sliding out.")]
  [SerializeField] private float portraitSlideOffset = 300f;

  [SerializeField] private float portraitSlideDuration = 0.35f;

  [Header("Sounds")]
  [SerializeField] private AudioClip informationShowSound;
  [SerializeField] private AudioClip informationHideSound;
  [SerializeField] private AudioClip dialogueShowSound;
  [SerializeField] private AudioClip dialogueHideSound;
  [SerializeField] private AudioMixerGroup uiMixerGroup;
  [SerializeField, Range(0f, 1f)] private float soundVolume = 0.5f;

  public static DialogueHUD Instance { get; private set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    Instance = null;
  }

  private const int SourceCount = 3;
  private readonly string[] _text = new string[SourceCount];
  private readonly bool[] _active = new bool[SourceCount];
  private Coroutine _informationTimer;
  private Coroutine _dialogueTimer;
  private Source? _lastActiveSource;

  private RectTransform _portraitRect;
  private Vector2 _portraitRestPosition;
  private Coroutine _portraitAnimation;

  private void Awake() {
    if (Instance != null && Instance != this) {
      Debug.LogWarning($"[DialogueHUD] Multiple instances in scene — '{Instance.name}' already registered, ignoring '{name}'.", this);
      return;
    }

    Instance = this;

    if (dialoguePortrait != null) {
      _portraitRect = dialoguePortrait.rectTransform;
      _portraitRestPosition = _portraitRect.anchoredPosition;
      // Parcheggiato fuori schermo, così ogni scorrimento in entrata parte da una posizione coerente.
      _portraitRect.anchoredPosition = _portraitRestPosition + new Vector2(portraitSlideOffset, 0f);
    }

    if (dialogueBackground != null) dialogueBackground.SetActive(false);
    if (dialoguePortrait != null) dialoguePortrait.gameObject.SetActive(false);

    Refresh();
  }

  private void OnDestroy() {
    if (Instance == this) Instance = null;
  }

  public void ShowInformation(string text, float duration = 0f) {
    StopTimer(ref _informationTimer);
    SetSlot(Source.Information, text);
    if (duration > 0f) _informationTimer = StartCoroutine(ClearAfter(Source.Information, duration));
  }

  public void ClearInformation() {
    StopTimer(ref _informationTimer);
    SetSlot(Source.Information, null);
  }

  public void ClearInformationIfMatches(string expectedText) {
    int i = (int)Source.Information;
    if (!_active[i] || _text[i] != expectedText) return;
    ClearInformation();
  }

  public void ShowInteractionPrompt(string text) => SetSlot(Source.Interaction, text);

  public void ClearInteractionPrompt() => SetSlot(Source.Interaction, null);

  public void ShowDialogue(string text, float duration = 0f) {
    StopTimer(ref _dialogueTimer);
    SetSlot(Source.Dialogue, text);
    if (duration > 0f) _dialogueTimer = StartCoroutine(ClearAfter(Source.Dialogue, duration));
  }

  public void ClearDialogue() {
    StopTimer(ref _dialogueTimer);
    SetSlot(Source.Dialogue, null);
  }

  private void StopTimer(ref Coroutine timer) {
    if (timer == null) return;
    StopCoroutine(timer);
    timer = null;
  }

  private IEnumerator ClearAfter(Source source, float duration) {
    yield return new WaitForSeconds(duration);
    SetSlot(source, null);
  }

  private void SetSlot(Source source, string text) {
    int i = (int)source;
    _active[i] = !string.IsNullOrEmpty(text);
    _text[i] = text;
    Refresh();
  }

  private void Refresh() {
    Source? active = null;
    for (int i = SourceCount - 1; i >= 0; i--) {
      if (_active[i]) { active = (Source)i; break; }
    }

    bool changed = active != _lastActiveSource;

    if (changed && _lastActiveSource.HasValue) PlayHideFeedback(_lastActiveSource.Value);

    if (active.HasValue) {
      int i = (int)active.Value;
      if (messageLabel != null) {
        messageLabel.gameObject.SetActive(true);
        messageLabel.text = _text[i];
      }
    } else if (messageLabel != null) {
      messageLabel.gameObject.SetActive(false);
    }

    SetBackgrounds(active);

    if (changed && active.HasValue) PlayShowFeedback(active.Value);

    _lastActiveSource = active;
  }

  private void SetBackgrounds(Source? active) {
    if (informationBackground != null) informationBackground.SetActive(active == Source.Information);
    if (dialogueBackground != null) dialogueBackground.SetActive(active == Source.Dialogue);

    if (active == Source.Dialogue && dialoguePortrait != null) {
      dialoguePortrait.gameObject.SetActive(true);
    }
    // Nascondere il ritratto è rimandato a HidePortraitAfterSlideOut, così può scorrere via prima di sparire.
  }

  private void PlayShowFeedback(Source source) {
    switch (source) {
      case Source.Information:
        SceneTransitionManager.PlayUiSound(informationShowSound, uiMixerGroup, volume: soundVolume);
        break;
      case Source.Dialogue:
        SceneTransitionManager.PlayUiSound(dialogueShowSound, uiMixerGroup, volume: soundVolume);
        PlayPortraitSlideIn();
        break;
    }
  }

  private void PlayHideFeedback(Source source) {
    switch (source) {
      case Source.Information:
        SceneTransitionManager.PlayUiSound(informationHideSound, uiMixerGroup, volume: soundVolume);
        break;
      case Source.Dialogue:
        SceneTransitionManager.PlayUiSound(dialogueHideSound, uiMixerGroup, volume: soundVolume);
        PlayPortraitSlideOut();
        break;
    }
  }

  private void PlayPortraitSlideIn() {
    if (_portraitRect == null) return;
    if (_portraitAnimation != null) StopCoroutine(_portraitAnimation);
    // Si posiziona prima sul lato di entrata, perché entrata e uscita usano lati opposti fuori schermo.
    Vector2 entrySide = _portraitRestPosition + new Vector2(portraitSlideOffset, 0f);
    _portraitAnimation = StartCoroutine(AnimatePortraitSlide(entrySide, _portraitRestPosition, null));
  }

  private void PlayPortraitSlideOut() {
    if (_portraitRect == null) return;
    if (_portraitAnimation != null) StopCoroutine(_portraitAnimation);
    Vector2 exitSide = _portraitRestPosition - new Vector2(portraitSlideOffset, 0f);
    _portraitAnimation = StartCoroutine(AnimatePortraitSlide(_portraitRect.anchoredPosition, exitSide, HidePortraitAfterSlideOut));
  }

  private void HidePortraitAfterSlideOut() {
    if (dialoguePortrait != null) dialoguePortrait.gameObject.SetActive(false);
  }

  private IEnumerator AnimatePortraitSlide(Vector2 from, Vector2 to, Action onComplete) {
    float elapsed = 0f;
    while (elapsed < portraitSlideDuration) {
      yield return null;
      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(elapsed / portraitSlideDuration);
      float eased = 1f - Mathf.Pow(1f - t, 3f);
      _portraitRect.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
    }

    _portraitRect.anchoredPosition = to;
    _portraitAnimation = null;
    onComplete?.Invoke();
  }
}
