using TMPro;
using UnityEngine;

/// <summary>Small camera-facing state glyph above a guard; the global vignette remains authoritative.</summary>
[DisallowMultipleComponent]
public sealed class GuardStateIndicator : MonoBehaviour {
  [SerializeField] private GuardController guard;
  [SerializeField] private TMP_Text glyph;
  [SerializeField] private Camera gameCamera;
  [SerializeField] private Color noticingColor = new(1f, 0.75f, 0.08f, 1f);
  [SerializeField] private Color chasingColor = new(0.9f, 0.02f, 0.01f, 1f);
  [SerializeField] private Color searchingColor = new(1f, 0.88f, 0.35f, 1f);
  [SerializeField, Min(0f)] private float fadeSpeed = 6f;
  [SerializeField, Range(0f, 0.25f)] private float pulseScale = 0.08f;

  private float alpha;
  private Vector3 authoredScale;

  public void Configure(GuardController owner, TMP_Text text, Camera camera) {
    guard = owner;
    glyph = text;
    gameCamera = camera;
  }

  private void Awake() {
    if (guard == null) guard = GetComponentInParent<GuardController>();
    if (glyph == null) glyph = GetComponent<TMP_Text>();
    if (gameCamera == null) gameCamera = Camera.main;
    authoredScale = transform.localScale;
  }

  private void LateUpdate() {
    if (guard == null || glyph == null) return;
    string content = "";
    Color color = noticingColor;
    switch (guard.CurrentState) {
      case GuardController.GuardState.Noticing: content = "!"; color = noticingColor; break;
      case GuardController.GuardState.Chasing: content = "!"; color = chasingColor; break;
      case GuardController.GuardState.Searching: content = "?"; color = searchingColor; break;
    }
    alpha = Mathf.MoveTowards(alpha, content.Length > 0 ? 1f : 0f, fadeSpeed * Time.deltaTime);
    glyph.text = content;
    color.a *= alpha;
    glyph.color = color;
    float pulse = 1f + Mathf.Sin(Time.time * 6f) * pulseScale * alpha;
    transform.localScale = authoredScale * pulse;
    if (gameCamera != null) transform.rotation = gameCamera.transform.rotation;
  }
}
