using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registers one fixed light with the fullscreen monochrome composite. Its spherical
/// world-space field produces one continuous colored projection across every visible surface.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class FixedLightSource : MonoBehaviour {
  public const int MaximumVisibleLights = 24;
  public const int VisibilitySampleCount = 128;
  public const int PackedVisibilityVectorCount = MaximumVisibleLights * VisibilitySampleCount / 4;

  private static readonly List<FixedLightSource> ActiveSources = new();
  private static readonly List<FixedLightSource> VisibleSources = new();
  private static readonly RaycastHit[] VisibilityHits = new RaycastHit[32];

  [Tooltip("Transform at the physical light origin. Usually the PointLight child inside the fixture.")]
  [SerializeField] private Transform origin;

  [SerializeField] private Color color = new(1f, 0.92f, 0.08f, 1f);

  [Tooltip("World-space radius of the connected floor, wall, and ceiling projection.")]
  [SerializeField, Min(0.01f)] private float radius = 3.2f;

  [Tooltip("Width in world units of the projection's edge feather. Set to zero for a hard edge.")]
  [SerializeField, Min(0f)] private float edgeFeather = 0.12f;

  [Tooltip("Strength of the tint applied after the monochrome pass.")]
  [SerializeField, Range(0f, 1f)] private float colorIntensity = 0.9f;

  [Tooltip("Target luminance of the projected color. This controls apparent brightness independently of the physical Point Light intensity.")]
  [SerializeField, Range(0f, 1f)] private float projectedBrightness = 0.15f;

  [Header("Visibility")]
  [Tooltip("Layers that prevent this light's tint and gameplay exposure from passing through geometry.")]
  [SerializeField] private LayerMask obstacleMask = 67955;

  [Tooltip("Small offset from the light origin used to avoid immediately hitting its own fixture.")]
  [SerializeField, Min(0f)] private float rayOriginOffset = 0.05f;

  [Tooltip("When disabled, the fixture remains visible and tinted but does not expose the player or activate light-dependent gameplay.")]
  [SerializeField] private bool affectsGameplay = true;

  [Header("Surface lighting")]
  [Tooltip("The real Point Light used for illumination and shadows. If empty, the light on Origin is used.")]
  [SerializeField] private Light surfaceLight;

  [Header("Visible core")]
  [Tooltip("When enabled, this light drives its visible core without modifying the shared core material.")]
  [SerializeField] private bool driveCoreVisual;

  [SerializeField] private Renderer coreRenderer;

  [Tooltip("Authored core hue before its independent brightness and flicker are applied.")]
  [SerializeField] private Color coreColor = new(1f, 0.84f, 0.45f, 1f);

  [SerializeField, Range(0f, 1f)] private float coreBrightness = 0.35f;

  [Tooltip("How strongly the shared light flicker affects the visible core. Zero is steady; one exactly follows the light.")]
  [SerializeField, Range(0f, 1f)] private float coreFlickerInfluence = 0.75f;

  [Header("Flicker")]
  [SerializeField] private bool flickerEnabled = true;

  [Tooltip("Maximum fractional brightness variation. The light radius and gameplay area never change.")]
  [SerializeField, Range(0f, 0.25f)] private float flickerAmount = 0.05f;

  [SerializeField, Min(0.01f)] private float flickerSpeed = 2.4f;

  [Tooltip("Blends from a simple slow flame pulse to layered, less repetitive variation.")]
  [SerializeField, Range(0f, 1f)] private float flickerIrregularity = 0.75f;

  private float authoredSurfaceIntensity;
  private bool surfaceIntensityCaptured;
  private MaterialPropertyBlock corePropertyBlock;
  private readonly float[] cachedVisibilityRanges = new float[VisibilitySampleCount];
  private Vector3 cachedVisibilityPosition;
  private float cachedVisibilityRadius;
  private int cachedVisibilityMask;
  private float nextVisibilityRefreshTime;
  private bool visibilityCacheValid;
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");

  public void Configure(Transform lightOrigin, Color lightColor, float lightRadius, float feather, float intensity) {
    origin = lightOrigin;
    color = lightColor;
    radius = Mathf.Max(0.01f, lightRadius);
    edgeFeather = Mathf.Clamp(feather, 0f, radius);
    colorIntensity = Mathf.Clamp01(intensity);
    ResolveSurfaceLight();
  }

  private void OnEnable() {
    if (!ActiveSources.Contains(this)) ActiveSources.Add(this);
    ResolveSurfaceLight();
    CaptureSurfaceIntensity();
    ResolveCoreRenderer();
    ApplyCoreVisual(1f);
  }

  private void OnValidate() {
    colorIntensity = Mathf.Clamp01(colorIntensity);
    projectedBrightness = Mathf.Clamp01(projectedBrightness);
    flickerAmount = Mathf.Clamp(flickerAmount, 0f, 0.25f);
    flickerSpeed = Mathf.Max(0.01f, flickerSpeed);
    flickerIrregularity = Mathf.Clamp01(flickerIrregularity);
    coreBrightness = Mathf.Clamp01(coreBrightness);
    coreFlickerInfluence = Mathf.Clamp01(coreFlickerInfluence);
    rayOriginOffset = Mathf.Max(0f, rayOriginOffset);
    visibilityCacheValid = false;
    ResolveSurfaceLight();
    ResolveCoreRenderer();
    ApplyCoreVisual(1f);
  }

  private void Update() {
    if (!Application.isPlaying) return;
    float flickerMultiplier = EvaluateFlickerMultiplier(Time.time);
    if (surfaceLight != null) {
      CaptureSurfaceIntensity();
      surfaceLight.intensity = authoredSurfaceIntensity * flickerMultiplier;
    }
    ApplyCoreVisual(flickerMultiplier);
  }

  private void OnDisable() {
    RestoreSurfaceIntensity();
    ClearCoreVisual();
    ActiveSources.Remove(this);
  }

  private void OnDestroy() {
    RestoreSurfaceIntensity();
    ClearCoreVisual();
    ActiveSources.Remove(this);
  }

  [ContextMenu("Resolve Surface Light Reference")]
  private void ResolveSurfaceLight() {
    if (surfaceLight == null && origin != null) surfaceLight = origin.GetComponent<Light>();
    if (surfaceLight == null) surfaceLight = GetComponentInChildren<Light>(true);
  }

  private void ResolveCoreRenderer() {
    if (!driveCoreVisual || coreRenderer != null) return;
    Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
    for (int i = 0; i < renderers.Length; i++) {
      if (renderers[i].name != "ColoredLightCore") continue;
      coreRenderer = renderers[i];
      break;
    }
  }

  private void ApplyCoreVisual(float flickerMultiplier) {
    if (!driveCoreVisual || coreRenderer == null) return;
    corePropertyBlock ??= new MaterialPropertyBlock();
    coreRenderer.GetPropertyBlock(corePropertyBlock);
    float visibleFlicker = Mathf.Lerp(1f, flickerMultiplier, coreFlickerInfluence);
    Color visibleColor = new(
      coreColor.r * coreBrightness * visibleFlicker,
      coreColor.g * coreBrightness * visibleFlicker,
      coreColor.b * coreBrightness * visibleFlicker,
      coreColor.a);
    corePropertyBlock.SetColor(BaseColorId, visibleColor);
    corePropertyBlock.SetColor(ColorId, visibleColor);
    coreRenderer.SetPropertyBlock(corePropertyBlock);
  }

  private void ClearCoreVisual() {
    if (!driveCoreVisual || coreRenderer == null) return;
    coreRenderer.SetPropertyBlock(null);
  }

  public static int FillShaderData(
    Camera camera,
    Vector4[] positionsAndRadii,
    Vector4[] colorsAndIntensities,
    float[] feathers,
    Vector4[] lookParameters,
    Vector4[] packedVisibilityRanges) {
    CollectVisibleSources(camera);
    int count = 0;
    for (int i = 0; i < VisibleSources.Count && count < MaximumVisibleLights; i++) {
      FixedLightSource source = VisibleSources[i];

      Vector3 position = source.origin != null ? source.origin.position : source.transform.position;
      positionsAndRadii[count] = new Vector4(position.x, position.y, position.z, source.radius);
      colorsAndIntensities[count] = new Vector4(source.color.r, source.color.g, source.color.b, source.colorIntensity);
      feathers[count] = Mathf.Min(source.edgeFeather, source.radius);
      lookParameters[count] = new Vector4(
        source.projectedBrightness,
        source.flickerEnabled ? source.flickerAmount : 0f,
        source.flickerSpeed,
        source.flickerIrregularity);
      source.FillVisibilityData(packedVisibilityRanges, count * VisibilitySampleCount);
      count++;
    }
    return count;
  }

  private static void CollectVisibleSources(Camera camera) {
    VisibleSources.Clear();
    for (int i = ActiveSources.Count - 1; i >= 0; i--) {
      FixedLightSource source = ActiveSources[i];
      if (source == null) {
        ActiveSources.RemoveAt(i);
        continue;
      }
      if (source.isActiveAndEnabled) VisibleSources.Add(source);
    }

    if (camera == null) return;
    Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
    for (int i = VisibleSources.Count - 1; i >= 0; i--) {
      FixedLightSource source = VisibleSources[i];
      Vector3 position = source.origin != null ? source.origin.position : source.transform.position;
      Bounds bounds = new(position, Vector3.one * source.radius * 2f);
      if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) VisibleSources.RemoveAt(i);
    }

    Vector3 cameraPosition = camera.transform.position;
    VisibleSources.Sort((left, right) => {
      Vector3 leftPosition = left.origin != null ? left.origin.position : left.transform.position;
      Vector3 rightPosition = right.origin != null ? right.origin.position : right.transform.position;
      float leftDistance = (leftPosition - cameraPosition).sqrMagnitude;
      float rightDistance = (rightPosition - cameraPosition).sqrMagnitude;
      return leftDistance.CompareTo(rightDistance);
    });
  }

  private void FillVisibilityData(Vector4[] destination, int destinationOffset) {
    Vector3 position = origin != null ? origin.position : transform.position;
    bool moved = !visibilityCacheValid
                 || (position - cachedVisibilityPosition).sqrMagnitude > 0.000001f
                 || !Mathf.Approximately(radius, cachedVisibilityRadius)
                 || obstacleMask.value != cachedVisibilityMask;
    if (moved || Time.realtimeSinceStartup >= nextVisibilityRefreshTime) {
      for (int sample = 0; sample < VisibilitySampleCount; sample++) {
        float angle = sample * (360f / VisibilitySampleCount);
        Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
        cachedVisibilityRanges[sample] = CastVisibilityRay(position, direction, radius);
      }
      cachedVisibilityPosition = position;
      cachedVisibilityRadius = radius;
      cachedVisibilityMask = obstacleMask.value;
      nextVisibilityRefreshTime = Time.realtimeSinceStartup + (Application.isPlaying ? 0.1f : 0.25f);
      visibilityCacheValid = true;
    }

    for (int sample = 0; sample < VisibilitySampleCount; sample++) {
      int packedIndex = (destinationOffset + sample) / 4;
      int componentIndex = (destinationOffset + sample) % 4;
      Vector4 packed = destination[packedIndex];
      packed[componentIndex] = cachedVisibilityRanges[sample];
      destination[packedIndex] = packed;
    }
  }

  private float CastVisibilityRay(Vector3 position, Vector3 direction, float maximumDistance) {
    if (obstacleMask.value == 0) return maximumDistance;
    float offset = Mathf.Min(rayOriginOffset, maximumDistance);
    Vector3 rayOrigin = position + direction * offset;
    float rayLength = Mathf.Max(0f, maximumDistance - offset);
    int hitCount = Physics.RaycastNonAlloc(
      rayOrigin, direction, VisibilityHits, rayLength, obstacleMask, QueryTriggerInteraction.Ignore);
    float closestDistance = rayLength;
    for (int i = 0; i < hitCount; i++) {
      RaycastHit hit = VisibilityHits[i];
      if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
      closestDistance = Mathf.Min(closestDistance, hit.distance);
    }
    return closestDistance + offset;
  }

  private void CaptureSurfaceIntensity() {
    if (!Application.isPlaying || surfaceIntensityCaptured || surfaceLight == null) return;
    authoredSurfaceIntensity = surfaceLight.intensity;
    surfaceIntensityCaptured = true;
  }

  private void RestoreSurfaceIntensity() {
    if (!surfaceIntensityCaptured || surfaceLight == null) return;
    surfaceLight.intensity = authoredSurfaceIntensity;
    surfaceIntensityCaptured = false;
  }

  private float EvaluateFlickerMultiplier(float time) {
    if (!flickerEnabled || flickerAmount <= 0f) return 1f;

    Vector3 position = origin != null ? origin.position : transform.position;
    float hash = Mathf.Sin(position.x * 12.9898f + position.z * 78.233f) * 43758.5453f;
    float phase = Mathf.Repeat(hash, 1f) * Mathf.PI * 2f;
    float t = time * flickerSpeed;
    float slow = Mathf.Sin(t + phase);
    float layered = Mathf.Sin(t * 0.73f + phase) * 0.55f
                    + Mathf.Sin(t * 1.91f + phase * 1.37f) * 0.3f
                    + Mathf.Sin(t * 4.17f + phase * 2.11f) * 0.15f;
    float signal = Mathf.Lerp(slow, layered, flickerIrregularity);
    return Mathf.Max(0f, 1f + signal * flickerAmount);
  }

  /// <summary>
  /// Uses the same spherical fields sent to the selective-color composite, allowing gameplay checks to
  /// agree with the fixed-light area shown to the player.
  /// </summary>
  public static bool Illuminates(Vector3 worldPosition) {
    return EvaluateCombinedExposure(worldPosition) > 0f;
  }

  /// <summary>
  /// Returns the strongest normalized fixed-light contribution at a world position. The radius
  /// and feather calculation intentionally matches SelectiveColorComposite.shader.
  /// </summary>
  public static float EvaluateCombinedExposure(Vector3 worldPosition) {
    float strongest = 0f;
    for (int i = ActiveSources.Count - 1; i >= 0; i--) {
      FixedLightSource source = ActiveSources[i];
      if (source == null) {
        ActiveSources.RemoveAt(i);
        continue;
      }
      if (!source.isActiveAndEnabled || !source.affectsGameplay) continue;

      strongest = Mathf.Max(strongest, source.EvaluateExposure(worldPosition));
    }
    return strongest;
  }

  /// <summary>Evaluates this light's editor-authored spherical field at one world position.</summary>
  public float EvaluateExposure(Vector3 worldPosition) {
    Vector3 lightPosition = origin != null ? origin.position : transform.position;
    float distance = Vector3.Distance(worldPosition, lightPosition);
    if (distance >= radius) return 0f;
    if (IsOccluded(lightPosition, worldPosition, distance)) return 0f;

    float feather = Mathf.Min(edgeFeather, radius);
    if (feather <= 0f) return 1f;

    float innerRadius = radius - feather;
    if (distance <= innerRadius) return 1f;

    float t = Mathf.InverseLerp(innerRadius, radius, distance);
    return 1f - t * t * (3f - 2f * t);
  }

  private bool IsOccluded(Vector3 lightPosition, Vector3 worldPosition, float distance) {
    if (obstacleMask.value == 0 || distance <= 0.0001f) return false;
    Vector3 direction = (worldPosition - lightPosition) / distance;
    float offset = Mathf.Min(rayOriginOffset, distance);
    // Stop just short of the sample so a feet-level query does not mistake the floor under the
    // character for an intervening blocker. The margin stays small enough for thin walls to win.
    float rayLength = Mathf.Max(0f, distance - offset - 0.01f);
    if (rayLength <= 0f) return false;
    Vector3 rayOrigin = lightPosition + direction * offset;
    int hitCount = Physics.RaycastNonAlloc(
      rayOrigin, direction, VisibilityHits, rayLength, obstacleMask, QueryTriggerInteraction.Ignore);
    for (int i = 0; i < hitCount; i++) {
      Collider blocker = VisibilityHits[i].collider;
      if (blocker != null && !blocker.transform.IsChildOf(transform)) return true;
    }
    return false;
  }
}
