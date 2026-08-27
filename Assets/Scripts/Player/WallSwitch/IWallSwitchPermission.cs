/// <summary>Optional player-state gate that reports why wall-switch aiming is unavailable.</summary>
public interface IWallSwitchPermission {
  bool CanWallSwitch { get; }
  AimEntryBlockReason WallSwitchBlockReason { get; }
}
