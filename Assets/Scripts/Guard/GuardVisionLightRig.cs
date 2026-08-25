using UnityEngine;

/// <summary>
/// Presentation layer for a guard's two gameplay vision cones. The detector remains the
/// authoritative source for shape and occlusion; this component only mirrors it visually.
/// </summary>
[ExecuteAlways]
public sealed class GuardVisionLightRig : MonoBehaviour {
  [Header("Source")]
  [SerializeField] private GuardVisionCone vision;

  [Header("Visual fields")]
  [SerializeField] private PalaceConeLightSource nearField;
  [SerializeField] private PalaceConeLightSource farField;

  private float _lastShortRange;
  private float _lastShortAngle;
  private float _lastLongRange;
  private float _lastLongAngle;

  public void Configure(
    GuardVisionCone source,
    PalaceConeLightSource nearConeField,
    PalaceConeLightSource farConeField) {
    vision = source;
    nearField = nearConeField;
    farField = farConeField;
    Synchronize();
  }

  private void OnEnable() {
    Synchronize();
  }

  private void OnValidate() {
    Synchronize();
  }

  private void LateUpdate() {
    if (SourceShapeChanged()) Synchronize();
  }

  [ContextMenu("Synchronize With Vision Cone")]
  public void Synchronize() {
    if (vision == null) return;

    if (nearField != null)
      nearField.SynchronizeGameplayShape(vision.shortRange, vision.shortAngle, vision.obstacleMask);
    if (farField != null)
      farField.SynchronizeGameplayShape(vision.longRange, vision.longAngle, vision.obstacleMask);

    _lastShortRange = vision.shortRange;
    _lastShortAngle = vision.shortAngle;
    _lastLongRange = vision.longRange;
    _lastLongAngle = vision.longAngle;
  }

  private bool SourceShapeChanged() {
    if (vision == null) return false;
    return !Mathf.Approximately(_lastShortRange, vision.shortRange)
           || !Mathf.Approximately(_lastShortAngle, vision.shortAngle)
           || !Mathf.Approximately(_lastLongRange, vision.longRange)
           || !Mathf.Approximately(_lastLongAngle, vision.longAngle);
  }
}
