using UnityEngine;

/// <summary>
/// Public surface of the takedown subsystem. Takedowns happen as a side effect of a wall switch
/// (see <see cref="TakedownController"/>) rather than their own action, so this only exposes the
/// settings that shape that: whether it's active at all, how close a guard must be to the
/// switch's path to be caught by it, and which layer counts as a guard.
/// </summary>
public interface ITakedownSystem {
  /// <summary>Runtime toggle. When false, switches never take down guards.</summary>
  bool IsEnabled { get; set; }

  /// <summary>Max distance from a switch's path (start -> aimed target) a guard can be and still be taken down by it.</summary>
  float TakedownRange { get; set; }

  LayerMask GuardLayerMask { get; set; }

  /// <summary>True for the duration of a switch that is going to (or just did) take down a guard.</summary>
  bool IsTakingDown { get; }
}
