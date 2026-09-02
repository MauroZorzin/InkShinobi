using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// IInteractable that toggles a GameObject active/inactive and optionally disables a component on
/// the player while the target is active. Its standard prompt is owned by the player.
/// </summary>
public class ArrowInteractable : MonoBehaviour, IInteractable, IInteractionCategoryProvider {
  [Header("Target")]
  [Tooltip("GameObject shown/hidden (SetActive) each time this is interacted with.")]
  [SerializeField] private GameObject targetObject;
  [Tooltip("Text shown and hidden together with the target object.")]
  [SerializeField] private GameObject targetText;

  [Header("ToHide")]
  [Tooltip("GameObject hide permanently when interaction is triggered.")]
  [SerializeField] private GameObject toHideObject;

  [Header("Interaction Texts")]
  [Tooltip("Text hidden the first time this object is interacted with.")]
  [SerializeField] private GameObject textToHideOnInteraction;
  [Tooltip("Text shown on the first interaction and hidden on the next interaction.")]
  [SerializeField] private GameObject textToToggleOnInteraction;

  [Header("Player Lock")]
  [Tooltip("Component disabled on the player while targetObject is shown, re-enabled when it's hidden again. Leave empty to skip locking anything. Don't point this at whatever drives the Interact input itself, or the player won't be able to press Interact again to close it.")]
  [SerializeField] private MonoBehaviour componentToDisable;

  [Header("Audio")]
  [Tooltip("Played at this object's position every time it's interacted with.")]
  [SerializeField] private AudioClip interactSound;
  [Range(0f, 1f)][SerializeField] private float interactSoundVolume = 1f;
  [Tooltip("Mixer group interactSound is routed through (e.g. your \"FX\" group). Leave empty to go straight to Master.")]
  [SerializeField] private AudioMixerGroup mixerGroup;

  private bool _isShowing;
  private MissionScrollAnimation _scrollAnimation;

  public InteractionCategory InteractionCategory => InteractionCategory.Default;

  private void Awake() {
    if (targetObject != null) _scrollAnimation = targetObject.GetComponent<MissionScrollAnimation>();
  }

  /// <summary>Toggles targetObject on/off each time this is interacted with.</summary>
  public void Interact(PlayerInventory inventory) {
    if (_scrollAnimation != null && _scrollAnimation.IsAnimating) return;
    if (_scrollAnimation == null && interactSound != null) {
      OneShotAudio.PlayClipAtPoint(
        interactSound,
        transform.position,
        interactSoundVolume,
        mixerGroup
      );
    }
    SetShowing(!_isShowing);
  }

  private void SetShowing(bool showing) {
    _isShowing = showing;
    if (textToHideOnInteraction != null && showing) textToHideOnInteraction.SetActive(false);
    if (textToToggleOnInteraction != null) textToToggleOnInteraction.SetActive(showing);
    if (componentToDisable != null) componentToDisable.enabled = !showing;

    if (showing) {
      if (targetObject != null) targetObject.SetActive(true);
      if (_scrollAnimation != null) _scrollAnimation.PlayOpen(targetText);
      else if (targetText != null) targetText.SetActive(true);
      return;
    }

    if (_scrollAnimation != null) {
      _scrollAnimation.PlayClose(targetText, CompleteHide);
    } else {
      CompleteHide();
    }
  }

  private void CompleteHide() {
    if (targetObject != null) targetObject.SetActive(false);
    if (targetText != null) targetText.SetActive(false);
    if (toHideObject != null) toHideObject.SetActive(false);
  }
}
