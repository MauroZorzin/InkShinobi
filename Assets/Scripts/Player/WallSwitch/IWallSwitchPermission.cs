/// <summary>Optional player-state gate for future hiding, death, and detection rules.</summary>
public interface IWallSwitchPermission {
  bool CanWallSwitch { get; }
}
