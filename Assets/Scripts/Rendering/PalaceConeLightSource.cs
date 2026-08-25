using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Registers a world-space cone with the Palace fullscreen composite. Unlike a transparent
/// volume mesh, the cone is evaluated directly on visible scene surfaces reconstructed from depth.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PalaceConeLightSource : MonoBehaviour {
  public const int MaximumVisibleCones = 8;
  public const int VisibilitySampleCount = 48;

  private static readonly List<PalaceConeLightSource> ActiveSources = new();

  [SerializeField] private Transform origin;
  [SerializeField] private Color color = new(1f, 0.92f, 0.08f, 1f);
  [SerializeField, Min(0.01f)] private float range = 5f;
  [SerializeField, Range(1f, 179f)] private float angle = 50f;

  [Tooltip("World-space width of the fade before the far end of the cone. Set to zero for a hard edge.")]
  [SerializeField, Min(0f)] private float rangeFeather = 0.12f;

  [Tooltip("Width in degrees of the fade along both angular edges. Smaller values make the cone crisper.")]
  [SerializeField, Range(0.01f, 30f)] private float angleFeather = 1.5f;

  [SerializeField, Range(0f, 1f)] private float colorIntensity = 0.9f;

  [FormerlySerializedAs("brightnessBoost")]
  [Tooltip("Target luminance of the projected color. This controls apparent brightness independently of the physical Light intensity.")]
  [SerializeField, Range(0f, 1f)] private float projectedBrightness = 0.35f;

  [Tooltip("Higher-priority cones win where vision fields overlap.")]
  [SerializeField] private float visualPriority;

  [Tooltip("While a surface is inside this cone's gameplay boundary, do not show lower-priority cone colors through its feathered edge.")]
  [SerializeField] private bool maskLowerPriorityCones;

  [Header("Visibility")]
  [Tooltip("Layers that stop the floor visibility field and provide end-wall impacts.")]
  [SerializeField] private LayerMask obstacleMask;

  [Tooltip("Small forward offset that prevents a visibility ray from immediately touching the guard's own collider.")]
  [SerializeField, Min(0f)] private float rayOriginOffset = 0.05f;

  [Header("Flicker")]
  [Tooltip("Maximum fractional brightness variation. Shape, range, and detection boundaries remain stable.")]
  [SerializeField, Range(0f, 0.25f)] private float flickerAmount = 0.02f;

  [SerializeField, Min(0.01f)] private float flickerSpeed = 2.4f;

  [SerializeField, Range(0f, 1f)] private float flickerIrregularity = 0.75f;

  /// <summary>
  /// Mirrors only the gameplay-authoritative cone geometry. Presentation values on this
  /// component remain editor-authored and are never replaced when Play Mode starts.
  /// </summary>
  public void SynchronizeGameplayShape(float coneRange, float coneAngle, LayerMask blockers) {
    range = Mathf.Max(0.01f, coneRange);
    angle = Mathf.Clamp(coneAngle, 1f, 179f);
    obstacleMask = blockers;
  }

  private void OnEnable() {
    if (!ActiveSources.Contains(this)) ActiveSources.Add(this);
  }

  private void OnValidate() {
    colorIntensity = Mathf.Clamp01(colorIntensity);
    projectedBrightness = Mathf.Clamp01(projectedBrightness);
    flickerAmount = Mathf.Clamp(flickerAmount, 0f, 0.25f);
    flickerSpeed = Mathf.Max(0.01f, flickerSpeed);
    flickerIrregularity = Mathf.Clamp01(flickerIrregularity);
  }

  private void OnDisable() {
    ActiveSources.Remove(this);
  }

  private void OnDestroy() {
    ActiveSources.Remove(this);
  }

  public static int FillShaderData(
    Vector4[] positionsAndRanges,
    Vector4[] directionsAndOuterCosines,
    Vector4[] colorsAndIntensities,
    Vector4[] featherParameters,
    Vector4[] lookParameters,
    float[] visibilityRanges,
    Vector4[] endWallPositionsAndRadii,
    Vector4[] endWallNormalsAndValidity) {
    int count = 0;
    for (int i = ActiveSources.Count - 1; i >= 0 && count < MaximumVisibleCones; i--) {
      PalaceConeLightSource source = ActiveSources[i];
      if (source == null) {
        ActiveSources.RemoveAt(i);
        continue;
      }
      if (!source.isActiveAndEnabled) continue;

      Transform sourceTransform = source.origin != null ? source.origin : source.transform;
      Vector3 position = sourceTransform.position;
      Vector3 direction = sourceTransform.forward.normalized;
      float outerHalfAngle = source.angle * 0.5f;
      float innerHalfAngle = Mathf.Max(0f, outerHalfAngle - source.angleFeather);

      positionsAndRanges[count] = new Vector4(position.x, position.y, position.z, source.range);
      directionsAndOuterCosines[count] = new Vector4(
        direction.x, direction.y, direction.z, Mathf.Cos(outerHalfAngle * Mathf.Deg2Rad));
      colorsAndIntensities[count] = new Vector4(
        source.color.r, source.color.g, source.color.b, source.colorIntensity);
      featherParameters[count] = new Vector4(
        Mathf.Min(source.rangeFeather, source.range),
        Mathf.Cos(innerHalfAngle * Mathf.Deg2Rad),
        source.visualPriority,
        source.maskLowerPriorityCones ? 1f : 0f);
      lookParameters[count] = new Vector4(source.projectedBrightness, source.flickerAmount, source.flickerSpeed, source.flickerIrregularity);
      source.FillVisibilityData(
        sourceTransform, direction, count, visibilityRanges,
        endWallPositionsAndRadii, endWallNormalsAndValidity);
      count++;
    }
    return count;
  }

  private void FillVisibilityData(
    Transform sourceTransform,
    Vector3 forward,
    int coneIndex,
    float[] visibilityRanges,
    Vector4[] endWallPositionsAndRadii,
    Vector4[] endWallNormalsAndValidity) {
    Vector3 position = sourceTransform.position;
    Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
    if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;

    float halfAngle = angle * 0.5f;
    int sampleOffset = coneIndex * VisibilitySampleCount;
    for (int sample = 0; sample < VisibilitySampleCount; sample++) {
      float t = sample / (VisibilitySampleCount - 1f);
      Vector3 rayDirection = Quaternion.AngleAxis(Mathf.Lerp(-halfAngle, halfAngle, t), Vector3.up) * flatForward;
      visibilityRanges[sampleOffset + sample] = CastVisibilityRay(position, rayDirection, out _);
    }

    float centerDistance = CastVisibilityRay(position, flatForward, out RaycastHit centerHit);
    bool hitVerticalSurface = centerDistance < range && Mathf.Abs(centerHit.normal.y) < 0.55f;
    if (!hitVerticalSurface) {
      endWallPositionsAndRadii[coneIndex] = Vector4.zero;
      endWallNormalsAndValidity[coneIndex] = Vector4.zero;
      return;
    }

    // Cap very broad near fields so their wall mark stays legible rather than covering
    // the entire wall. It still scales naturally with guard-to-wall distance.
    float visualHalfAngle = Mathf.Min(halfAngle, 35f);
    float radiusAtWall = Mathf.Clamp(
      centerDistance * Mathf.Tan(visualHalfAngle * Mathf.Deg2Rad), 0.12f, 1.5f);
    endWallPositionsAndRadii[coneIndex] = new Vector4(
      centerHit.point.x, centerHit.point.y, centerHit.point.z, radiusAtWall);
    endWallNormalsAndValidity[coneIndex] = new Vector4(
      centerHit.normal.x, centerHit.normal.y, centerHit.normal.z, 1f);
  }

  private float CastVisibilityRay(Vector3 position, Vector3 direction, out RaycastHit hit) {
    float offset = Mathf.Min(rayOriginOffset, range);
    Vector3 rayOrigin = position + direction * offset;
    float rayLength = Mathf.Max(0f, range - offset);
    if (obstacleMask.value != 0
        && Physics.Raycast(rayOrigin, direction, out hit, rayLength, obstacleMask, QueryTriggerInteraction.Ignore))
      return hit.distance + offset;

    hit = default;
    return range;
  }
}
