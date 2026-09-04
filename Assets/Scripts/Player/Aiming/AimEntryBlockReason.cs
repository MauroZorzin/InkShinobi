/// <summary>Authoritative reason an aim ability rejected an entry request.</summary>
public enum AimEntryBlockReason {
  None,
  Paused,
  Dead,
  InvalidConfiguration,
  CameraTransitioning,
  PlayerTurning,
  OtherAimModeActive,
  Concealed,
  VisibleOrDetected,
  NoCurrentPath,
  Cooldown
}

public static class AimEntryBlockReasonExtensions {
  /// <summary>Only ordinary, player-correctable gameplay restrictions produce rejection feedback.</summary>
  public static bool ShouldPlayFeedback(this AimEntryBlockReason reason) {
    return reason != AimEntryBlockReason.None
           && reason != AimEntryBlockReason.Paused
           && reason != AimEntryBlockReason.Dead
           && reason != AimEntryBlockReason.InvalidConfiguration;
  }
}
