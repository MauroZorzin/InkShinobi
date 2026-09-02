/// <summary>Player-owned interaction categories used by reach and dialogue configuration.</summary>
public enum InteractionCategory {
  Default,
  Pickup,
  Door,
  HidingSpot
}

/// <summary>
/// Optional semantic category for an interactable whose Unity layer is shared with another category.
/// This communicates what the interaction is, without giving the object ownership of prompt text.
/// </summary>
public interface IInteractionCategoryProvider {
  InteractionCategory InteractionCategory { get; }
}
