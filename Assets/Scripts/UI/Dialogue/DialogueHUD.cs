using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// Single shared HUD text element driven by three independent content sources, in priority order:
/// Dialogue (highest) > Interaction prompt (PlayerInteractor) > Information/tutorial hints
/// (InformationTrigger). Whichever active source has the highest priority owns the shared label;
/// when it clears, display falls back to the next highest-priority still-active source.
///
/// Each source can carry its own background element (informationBackground / dialogueBackground),
/// shown only while that source is the one currently displayed. Dialogue also reveals a portrait —
/// there is only ever one dialogue "speaker" in this game (the player), so the portrait is a fixed
/// image assigned once here rather than something callers pass in per message.
///
/// Whenever Information or Dialogue transitions from not-shown to shown (not on every repeated
/// message while already the active source), it plays its show sound; Dialogue additionally slides
/// its portrait in from offscreen. Re-showing while already the active source (e.g. a second line of
/// the same conversation) only swaps the text — the portrait stays put and nothing replays.
///
/// One instance is expected per scene. Callers reach it via the static Instance rather than a
/// per-caller Inspector reference.
/// </summary>
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

  [Header("Portrait Slide-In")]
  [Tooltip("How far offscreen (anchored-position units, local X) the portrait starts before sliding into its resting position.")]
  [SerializeField] private float portraitSlideOffset = 300f;

  [SerializeField] private float portraitSlideDuration = 0.35f;

  [Header("Sounds")]
  [SerializeField] private AudioClip informationShowSound;
  [SerializeField] private AudioClip dialogueShowSound;
  [SerializeField] private AudioMixerGroup uiMixerGroup;

  public static DialogueHUD Instance { get; private set; }

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
    }

    Refresh();
  }

  private void OnDestroy() {
    if (Instance == this) Instance = null;
  }

  /// <summary>Shows a plain hint (lowest priority). duration &lt;= 0 leaves it up until ClearInformation() is called.</summary>
  public void ShowInformation(string text, float duration = 0f) {
    StopTimer(ref _informationTimer);
    SetSlot(Source.Information, text);
    if (duration > 0f) _informationTimer = StartCoroutine(ClearAfter(Source.Information, duration));
  }

  public void ClearInformation() {
    StopTimer(ref _informationTimer);
    SetSlot(Source.Information, null);
  }

  /// <summary>Driven every frame by PlayerInteractor — no duration, it just tracks proximity.</summary>
  public void ShowInteractionPrompt(string text) => SetSlot(Source.Interaction, text);

  public void ClearInteractionPrompt() => SetSlot(Source.Interaction, null);

  /// <summary>Shows a dialogue line (highest priority). duration &lt;= 0 leaves it up until ClearDialogue() is called.</summary>
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

    if (active.HasValue) {
      int i = (int)active.Value;
      if (messageLabel != null) {
        messageLabel.gameObject.SetActive(true);
        messageLabel.text = _text[i];
      }
      SetBackgrounds(active);

      if (active != _lastActiveSource) PlayShowFeedback(active.Value);
    } else {
      if (messageLabel != null) messageLabel.gameObject.SetActive(false);
      SetBackgrounds(null);
    }

    _lastActiveSource = active;
  }

  private void SetBackgrounds(Source? active) {
    if (informationBackground != null) informationBackground.SetActive(active == Source.Information);
    if (dialogueBackground != null) dialogueBackground.SetActive(active == Source.Dialogue);
    if (dialoguePortrait != null) dialoguePortrait.gameObject.SetActive(active == Source.Dialogue);
  }

  private void PlayShowFeedback(Source source) {
    switch (source) {
      case Source.Information:
        SceneTransitionManager.PlayUiSound(informationShowSound, uiMixerGroup);
        break;
      case Source.Dialogue:
        SceneTransitionManager.PlayUiSound(dialogueShowSound, uiMixerGroup);
        PlayPortraitSlideIn();
        break;
    }
  }

  private void PlayPortraitSlideIn() {
    if (_portraitRect == null) return;
    if (_portraitAnimation != null) StopCoroutine(_portraitAnimation);
    _portraitAnimation = StartCoroutine(AnimatePortraitSlideIn());
  }

  private IEnumerator AnimatePortraitSlideIn() {
    Vector2 start = _portraitRestPosition + new Vector2(portraitSlideOffset, 0f);
    _portraitRect.anchoredPosition = start;

    float elapsed = 0f;
    while (elapsed < portraitSlideDuration) {
      yield return null;
      elapsed += Time.unscaledDeltaTime;
      float t = Mathf.Clamp01(elapsed / portraitSlideDuration);
      float eased = 1f - Mathf.Pow(1f - t, 3f);
      _portraitRect.anchoredPosition = Vector2.LerpUnclamped(start, _portraitRestPosition, eased);
    }

    _portraitRect.anchoredPosition = _portraitRestPosition;
    _portraitAnimation = null;
  }
}
