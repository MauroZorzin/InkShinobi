/// <summary>
/// Optional companion to IInteractable — implement on an interactable that needs its prompt text to
/// depend on its own state and/or the player's inventory (e.g. a door showing "Apri"/"Chiudi"/"Serve
/// una chiave"). Return null/empty to fall back to PlayerInteractor's layer-based default text.
/// </summary>
public interface IInteractionPrompt {
  string GetPromptText(PlayerInventory inventory);
}
