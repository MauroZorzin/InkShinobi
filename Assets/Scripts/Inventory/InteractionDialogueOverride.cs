using UnityEngine;

/// <summary>
/// Optional per-object override for the prompt selected by PlayerInteractionDialogue. Disable
/// Show Dialogue to explicitly suppress the prompt for this interactable.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionDialogueOverride : MonoBehaviour {
  [Tooltip("Disable this to explicitly show no interaction dialogue for this object.")]
  [SerializeField] private bool showDialogue = true;
  [TextArea]
  [SerializeField] private string dialogue = "[X] to interact";

  public bool ShowDialogue => showDialogue;
  public string Dialogue => dialogue;
}
