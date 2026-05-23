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
