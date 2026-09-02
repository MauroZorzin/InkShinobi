using UnityEngine;

/// <summary>Expands one floor-projected ring to the exact gameplay radius of a sound emission.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FloorCircleIndicator))]
public sealed class DistractionEchoPulse : MonoBehaviour {
  [SerializeField, Min(0.05f)] private float duration = 0.85f;
  [SerializeField] private AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
  [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

  private FloorCircleIndicator indicator;
  private Color baseFill;
  private Color baseRing;
  private float targetRadius;
  private float elapsed;

  private void Awake() {
    indicator = GetComponent<FloorCircleIndicator>();
    baseFill = indicator.fillColor;
    baseRing = indicator.ringColor;
  }

  public void Play(float radius) {
    targetRadius = Mathf.Max(0.1f, radius);
    elapsed = 0f;
    indicator.radius = 0.1f;
    enabled = true;
  }

  private void Update() {
    if (SceneTransitionManager.IsGamePaused) return;
    elapsed += Time.deltaTime;
    float normalized = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    indicator.radius = Mathf.Lerp(0.1f, targetRadius, radiusCurve.Evaluate(normalized));
    float alpha = Mathf.Clamp01(alphaCurve.Evaluate(normalized));
    indicator.fillColor = WithAlpha(baseFill, baseFill.a * alpha);
    indicator.ringColor = WithAlpha(baseRing, baseRing.a * alpha);
    if (normalized >= 1f) Destroy(gameObject);
  }

  private static Color WithAlpha(Color color, float alpha) =>
    new(color.r, color.g, color.b, alpha);

}
