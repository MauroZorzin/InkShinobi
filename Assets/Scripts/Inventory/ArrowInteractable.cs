using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// IInteractable that toggles a GameObject active/inactive only one time, and optionally disables a component on the player while the target is active.
/// </summary>
public class ArrowInteractable : MonoBehaviour, IInteractable {
  [Header("Target")]
  [Tooltip("GameObject shown/hidden (SetActive) each time this is interacted with.")]
  [SerializeField] private GameObject targetObject;

  [Header("ToHide")]
  [Tooltip("GameObject hide permanently when interaction is triggered.")]
  [SerializeField] private GameObject toHideObject;
  [SerializeField] private GameObject toHideObjectText;

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
    if (componentToDisable != null) componentToDisable.enabled = !showing;
    if (toHideObject != null && showing == false) toHideObject.SetActive(false);
    if (toHideObjectText != null && showing == false) toHideObjectText.SetActive(false);
  }
}
