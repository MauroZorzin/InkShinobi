using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

/// <summary>
/// IInteractable that toggles a GameObject active/inactive and optionally disables a component on the player while the target is active.
/// </summary>
public class ArrowInteractable : MonoBehaviour, IInteractable {
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
  [FormerlySerializedAs("toHideObjectText")]
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

  private void Awake() {
    //SetShowing(false);
  }

  /// <summary>Toggles targetObject on/off each time this is interacted with.</summary>
  public void Interact(PlayerInventory inventory) {
    if (interactSound != null) OneShotAudio.PlayClipAtPoint(interactSound, transform.position, interactSoundVolume, mixerGroup);
    SetShowing(!_isShowing);
  }

  private void SetShowing(bool showing) {
    _isShowing = showing;
    if (targetObject != null) targetObject.SetActive(showing);
    if (targetText != null) targetText.SetActive(showing);
    if (textToHideOnInteraction != null && showing) textToHideOnInteraction.SetActive(false);
    if (textToToggleOnInteraction != null) textToToggleOnInteraction.SetActive(showing);
    if (componentToDisable != null) componentToDisable.enabled = !showing;
    if (toHideObject != null && showing == false) toHideObject.SetActive(false);
  }
}
