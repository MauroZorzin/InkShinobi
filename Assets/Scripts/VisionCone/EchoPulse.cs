using UnityEngine;

[RequireComponent(typeof(FloorCircleIndicator))]
public class EchoPulse : MonoBehaviour {
  public float maxRadius = 6f;
  public float duration = 1f;
  public AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
  public AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

  private FloorCircleIndicator _indicator;
  private Color _baseFillColor;
  private Color _baseRingColor;
  private float _elapsed;

  private void Awake() {
    _indicator = GetComponent<FloorCircleIndicator>();
    _baseFillColor = _indicator.fillColor;
    _baseRingColor = _indicator.ringColor;
  }

  private void Update() {
    _elapsed += Time.deltaTime;
    float t = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;

    _indicator.radius = Mathf.Lerp(0.01f, maxRadius, radiusCurve.Evaluate(t));

    float alpha = alphaCurve.Evaluate(t);
    _indicator.fillColor = new Color(_baseFillColor.r, _baseFillColor.g, _baseFillColor.b, _baseFillColor.a * alpha);
    _indicator.ringColor = new Color(_baseRingColor.r, _baseRingColor.g, _baseRingColor.b, _baseRingColor.a * alpha);

    if (_elapsed >= duration) {
      Destroy(gameObject);
    }
  }
}
