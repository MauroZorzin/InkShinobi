using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Public surface of the takedown subsystem.
/// Both the stealth controller and range highlighter talk through this interface
/// so neither depends on the concrete <see cref="TakedownController"/> class.
/// </summary>
public interface ITakedownSystem {
  /// <summary>Runtime toggle. When false the takedown action is silently ignored.</summary>
  bool IsEnabled { get; set; }

  float TakedownRange { get; set; }
  float TakedownAngle { get; set; }
  LayerMask GuardLayerMask { get; set; }

  /// <summary>
  /// Returns every guard that is currently within range AND within the allowed
  /// angle behind them — the same set the highlighter will illuminate.
  /// </summary>
  IReadOnlyList<GuardController> GetCandidates();
}
