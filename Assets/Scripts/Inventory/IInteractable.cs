/// <summary>
/// Defines an object that can react to the player's inventory interaction.
/// </summary>
public interface IInteractable {
  /// <summary>
  /// Runs the interaction using the inventory owned by the interacting player.
  /// </summary>
  /// <param name="inventory">The inventory that can receive or spend items during the interaction.</param>
  void Interact(PlayerInventory inventory);
}

/// <summary>Optional dynamic prompt supplied by an interactable instead of its layer fallback.</summary>
public interface IInteractionPrompt {
  string GetPromptText(PlayerInventory inventory);
}

/// <summary>Optional focus feedback driven while an interactable is the player's current target.</summary>
public interface IInteractionFocus {
  void SetInteractionFocused(bool focused, PlayerInventory inventory);
}

/// <summary>
/// Optional per-object interaction distance. Values at or below zero use the player's default.
/// </summary>
public interface IInteractionRange {
  float InteractionRange { get; }
}

/// <summary>
/// Optional interaction priority. When multiple interactables are in range, the highest priority
/// wins regardless of distance; distance only breaks ties within the same priority. Interactables
/// without this interface implicitly use priority 0.
/// </summary>
public interface IInteractionPriority {
  int Priority { get; }
}
