using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns all standard interaction-prompt wording for the player. Interactables report only their
/// gameplay state; an InteractionDialogueOverride can replace or suppress the resulting prompt on
/// an exceptional object.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerInteractionDialogue : MonoBehaviour {
  [Header("Default")]
  [SerializeField] private string defaultDialogue = "[X] to interact";

  [Header("Pickup")]
  [SerializeField] private string pickupDialogue = "[X] to pick up";

  [Header("Door")]
  [SerializeField] private string openDoorDialogue = "[X] to open";
  [SerializeField] private string closeDoorDialogue = "[X] to close";
  [SerializeField] private string unavailableDoorDialogue = "The way is blocked.";
  [Tooltip("{0} is replaced by the door's colour name, tinted with its authored key colour.")]
  [SerializeField] private string lockedDoorDialogueFormat = "Requires {0} key";

  [Header("Hiding Spot")]
  [SerializeField] private string enterHidingDialogue = "[X] to hide";
  [SerializeField] private string unavailableHidingDialogue = "Can't hide now";
  [SerializeField] private string exitHidingDialogue = "[X] to exit";

  private readonly HashSet<int> suppressionOwners = new();

  public bool IsSuppressed => suppressionOwners.Count > 0;

  /// <summary>Suppresses interaction prompts while an owning player mode (such as aim) is active.</summary>
  public void SetSuppressed(Object owner, bool suppressed) {
    if (owner == null) return;
    int ownerId = owner.GetInstanceID();
    if (suppressed) suppressionOwners.Add(ownerId);
    else suppressionOwners.Remove(ownerId);
    if (IsSuppressed) Clear();
  }

  public void Show(Collider targetCollider, IInteractable interactable, PlayerInventory inventory,
      InteractionCategory layerCategory) {
    if (IsSuppressed || !IsUnityInterfaceAlive(interactable)) {
      Clear();
      return;
    }

    InteractionDialogueOverride promptOverride = FindOverride(targetCollider, interactable);
    if (promptOverride != null) {
      SetPrompt(promptOverride.ShowDialogue ? promptOverride.Dialogue : null);
      return;
    }

    InteractionCategory category = interactable is IInteractionCategoryProvider categoryProvider
      ? categoryProvider.InteractionCategory
      : layerCategory;
    string dialogue = category switch {
      InteractionCategory.Pickup => pickupDialogue,
      InteractionCategory.Door => DoorDialogue(interactable as PassagewayDoor, inventory),
      InteractionCategory.HidingSpot => HidingDialogue(interactable as HidingSpot, inventory),
      _ => defaultDialogue
    };
    SetPrompt(dialogue);
  }

  public void Clear() => DialogueHUD.Instance?.ClearInteractionPrompt();

  private string DoorDialogue(PassagewayDoor door, PlayerInventory inventory) {
    if (door == null) return defaultDialogue;
    return door.GetInteractionState(inventory) switch {
      PassagewayDoor.InteractionState.Open => openDoorDialogue,
      PassagewayDoor.InteractionState.Close => closeDoorDialogue,
      PassagewayDoor.InteractionState.Locked => LockedDoorDialogue(door),
      _ => unavailableDoorDialogue
    };
  }

  private string HidingDialogue(HidingSpot hidingSpot, PlayerInventory inventory) {
    if (hidingSpot == null) return defaultDialogue;
    return hidingSpot.GetInteractionState(inventory) switch {
      HidingSpot.InteractionState.Enter => enterHidingDialogue,
      HidingSpot.InteractionState.Exit => exitHidingDialogue,
      _ => unavailableHidingDialogue
    };
  }

  private string LockedDoorDialogue(PassagewayDoor door) {
    string colourName = string.IsNullOrWhiteSpace(door.RequiredKeyColorName)
      ? "Unknown"
      : door.RequiredKeyColorName.Trim();
    string colourHex = ColorUtility.ToHtmlStringRGB(door.RequiredKeyColor);
    string tintedColour = $"<color=#{colourHex}>{colourName}</color>";
    return string.IsNullOrEmpty(lockedDoorDialogueFormat)
      ? null
      : lockedDoorDialogueFormat.Replace("{0}", tintedColour);
  }

  private static InteractionDialogueOverride FindOverride(
      Collider targetCollider,
      IInteractable interactable) {
    InteractionDialogueOverride promptOverride = targetCollider != null
      ? targetCollider.GetComponentInParent<InteractionDialogueOverride>()
      : null;
    if (promptOverride == null && interactable is Component component)
      promptOverride = component.GetComponent<InteractionDialogueOverride>();
    return promptOverride != null && promptOverride.isActiveAndEnabled ? promptOverride : null;
  }

  private static bool IsUnityInterfaceAlive(object value) {
    if (value == null) return false;
    return value is not Object unityObject || unityObject != null;
  }

  private static void SetPrompt(string dialogue) {
    if (DialogueHUD.Instance == null) return;
    if (string.IsNullOrEmpty(dialogue)) DialogueHUD.Instance.ClearInteractionPrompt();
    else DialogueHUD.Instance.ShowInteractionPrompt(dialogue);
  }

  private void OnDisable() {
    suppressionOwners.Clear();
    Clear();
  }
}
