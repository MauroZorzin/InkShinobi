using UnityEngine;

/// <summary>
/// Measures how much of a character's body overlaps the fixed Palace light fields.
/// The component only reads editor-authored light and sampling settings.
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PalaceLightExposure : MonoBehaviour, ILightExposureProvider {
  [Header("Body sampling")]
  [Tooltip("Character volume used to position the exposure samples. If empty, the local CharacterController is used.")]
  [SerializeField] private CharacterController body;

  [Tooltip("Number of evenly spaced samples from the character's feet to head.")]
  [SerializeField, Range(2, 9)] private int verticalSamples = 5;

  [Tooltip("Inset from the bottom and top of the CharacterController, in world units.")]
  [SerializeField, Min(0f)] private float endInset = 0.03f;

  [Tooltip("Horizontal distance of the left/right samples from the body center. Set to zero to sample only the center line.")]
  [SerializeField, Min(0f)] private float halfWidth = 0.12f;

  [Header("Gameplay")]
  [Tooltip("Minimum averaged body exposure considered illuminated by binary systems. Continuous consumers should use Exposure directly.")]
  [SerializeField, Range(0f, 1f)] private float exposedThreshold = 0.15f;

  [Header("Debug")]
  [Tooltip("Draw the sampled body points using their current exposure in the Scene view.")]
  [SerializeField] private bool showGizmos;

  private float[] sampleExposure = System.Array.Empty<float>();

  public float Exposure { get; private set; }
  public bool IsExposed => Exposure >= exposedThreshold;

  private void Reset() {
    body = GetComponent<CharacterController>();
  }

  private void Awake() {
    ResolveBody();
    RefreshExposure();
  }

  private void Update() {
    RefreshExposure();
  }

  private void OnValidate() {
    verticalSamples = Mathf.Clamp(verticalSamples, 2, 9);
    endInset = Mathf.Max(0f, endInset);
    halfWidth = Mathf.Max(0f, halfWidth);
    exposedThreshold = Mathf.Clamp01(exposedThreshold);
    ResolveBody();
  }

  /// <summary>Recomputes exposure immediately, which is useful after teleports or scripted movement.</summary>
  public void RefreshExposure() {
    ResolveBody();
    if (body == null) {
      Exposure = 0f;
      return;
    }

    int horizontalSamples = halfWidth > 0f ? 3 : 1;
    int count = verticalSamples * horizontalSamples;
    EnsureSampleBuffer(count);

    Vector3 center = transform.TransformPoint(body.center);
    Vector3 up = transform.up;
    Vector3 right = transform.right;
    float scaledHeight = body.height * Mathf.Abs(transform.lossyScale.y);
    float halfHeight = scaledHeight * 0.5f;
    float inset = Mathf.Min(endInset, halfHeight);
    float bottom = -halfHeight + inset;
    float top = halfHeight - inset;
    float scaledHalfWidth = halfWidth * Mathf.Abs(transform.lossyScale.x);

    float total = 0f;
    int sampleIndex = 0;
    for (int verticalIndex = 0; verticalIndex < verticalSamples; verticalIndex++) {
      float verticalT = verticalSamples == 1 ? 0.5f : verticalIndex / (verticalSamples - 1f);
      Vector3 rowCenter = center + up * Mathf.Lerp(bottom, top, verticalT);

      if (horizontalSamples == 1) {
        total += StoreSample(sampleIndex++, rowCenter);
        continue;
      }

      total += StoreSample(sampleIndex++, rowCenter - right * scaledHalfWidth);
      total += StoreSample(sampleIndex++, rowCenter);
      total += StoreSample(sampleIndex++, rowCenter + right * scaledHalfWidth);
    }

    Exposure = count > 0 ? Mathf.Clamp01(total / count) : 0f;
  }

  private float StoreSample(int index, Vector3 worldPosition) {
    float value = PalaceFixedLightSource.EvaluateCombinedExposure(worldPosition);
    sampleExposure[index] = value;
    return value;
  }

  private void EnsureSampleBuffer(int count) {
    if (sampleExposure.Length != count) sampleExposure = new float[count];
  }

  private void ResolveBody() {
    if (body == null) body = GetComponent<CharacterController>();
  }

  private void OnDrawGizmosSelected() {
    if (!showGizmos) return;
    ResolveBody();
    if (body == null) return;

    int horizontalCount = halfWidth > 0f ? 3 : 1;
    int count = verticalSamples * horizontalCount;
    if (sampleExposure.Length != count) RefreshExposure();

    Vector3 center = transform.TransformPoint(body.center);
    Vector3 up = transform.up;
    Vector3 right = transform.right;
    float scaledHeight = body.height * Mathf.Abs(transform.lossyScale.y);
    float halfHeight = scaledHeight * 0.5f;
    float inset = Mathf.Min(endInset, halfHeight);
    float scaledHalfWidth = halfWidth * Mathf.Abs(transform.lossyScale.x);
    int sampleIndex = 0;

    for (int verticalIndex = 0; verticalIndex < verticalSamples; verticalIndex++) {
      float verticalT = verticalIndex / (verticalSamples - 1f);
      Vector3 rowCenter = center + up * Mathf.Lerp(-halfHeight + inset, halfHeight - inset, verticalT);
      for (int horizontalIndex = 0; horizontalIndex < horizontalCount; horizontalIndex++) {
        float horizontalT = horizontalCount == 1 ? 0f : horizontalIndex - 1f;
        Vector3 point = rowCenter + right * (horizontalT * scaledHalfWidth);
        float value = sampleIndex < sampleExposure.Length
          ? sampleExposure[sampleIndex]
          : PalaceFixedLightSource.EvaluateCombinedExposure(point);
        Gizmos.color = Color.Lerp(new Color(0.1f, 0.1f, 0.1f, 0.8f), new Color(1f, 0.92f, 0.08f, 1f), value);
        Gizmos.DrawSphere(point, 0.025f);
        sampleIndex++;
      }
    }
  }
}
