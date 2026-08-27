/// <summary>
/// Read-only illumination information for perception and presentation systems.
/// </summary>
public interface ILightExposureProvider {
  /// <summary>Current normalized illumination, from fully dark (0) to fully exposed (1).</summary>
  float Exposure { get; }

  /// <summary>Whether exposure has crossed the provider's authored gameplay threshold.</summary>
  bool IsExposed { get; }
}
