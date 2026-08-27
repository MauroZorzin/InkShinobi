using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Asset-free aiming flourish that draws a tapered ink sleeve between the player's body and the
/// moving distraction release anchor. This is presentation only and never affects throw solving.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProceduralInkArm : MonoBehaviour {
  private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
  private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
  private static readonly int BreakupThresholdId = Shader.PropertyToID("_BreakupThreshold");
  private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");

  [Header("References")]
  [Tooltip("Transform used as the body end of the ink arm. Defaults to this Player transform.")]
  [SerializeField] private Transform bodyOrigin;
  [Tooltip("The moving release point selected by DistractionController.")]
  [SerializeField] private Transform throwAnchor;
  [Tooltip("Camera used to interpret the body offset and keep the bend facing the player.")]
  [SerializeField] private Camera viewCamera;
  [Tooltip("Ink material used by the generated ribbon. WallSwitchInk is suitable.")]
  [SerializeField] private Material inkMaterial;
  [Tooltip("Optional player renderer whose sorting layer is copied by the ink arm.")]
  [SerializeField] private Renderer bodyRenderer;

  [Header("Shape")]
  [Tooltip("Body attachment offset in the sprite plane. X is the horizontal distance from the player's vertical symmetry axis and is automatically mirrored toward the throw anchor; Y is vertical.")]
  [SerializeField] private Vector2 bodyOffset = new(0.08f, 0.14f);
  [Tooltip("Width where the ink arm leaves the player's body.")]
  [SerializeField, Min(0.001f)] private float bodyWidth = 0.105f;
  [Tooltip("Width at the moving throw anchor.")]
  [SerializeField, Min(0.001f)] private float handWidth = 0.032f;
  [Tooltip("Number of points used to form the procedural ribbon.")]
  [SerializeField, Range(3, 24)] private int segmentCount = 9;
  [Tooltip("Maximum sideways bow at the middle of the stretched arm.")]
  [SerializeField, Min(0f)] private float bend = 0.025f;

  [Header("Ink Motion")]
  [Tooltip("Small silhouette movement applied between the fixed body and hand endpoints.")]
  [SerializeField, Min(0f)] private float wobbleAmplitude = 0.008f;
  [Tooltip("Speed of the procedural ink movement in cycles per unscaled second.")]
  [SerializeField, Min(0f)] private float wobbleSpeed = 1.8f;
  [SerializeField] private Color inkColor = new(0.015f, 0.012f, 0.02f, 0.98f);
  [SerializeField, Range(0f, 1f)] private float edgeAlpha = 0.08f;
  [SerializeField, Range(0f, 1f)] private float breakup = 0.06f;

  [Header("Rendering")]
  [Tooltip("Offset relative to the player's sprite sorting order.")]
  [SerializeField] private int sortingOrderOffset = 1;

  private LineRenderer line;
  private MaterialPropertyBlock properties;
  private bool visible;

  private void Awake() {
    ResolveReferences();
    BuildRenderer();
    Hide();
  }

  private void LateUpdate() {
    if (!visible) return;
    if (bodyOrigin == null || throwAnchor == null) {
      Hide();
      return;
    }
    DrawArm();
  }

  private void OnDisable() => Hide();

#if UNITY_EDITOR
  private void OnValidate() {
    bodyWidth = Mathf.Max(0.001f, bodyWidth);
    handWidth = Mathf.Max(0.001f, handWidth);
    segmentCount = Mathf.Clamp(segmentCount, 3, 24);
    bend = Mathf.Max(0f, bend);
    wobbleAmplitude = Mathf.Max(0f, wobbleAmplitude);
    wobbleSpeed = Mathf.Max(0f, wobbleSpeed);
  }
#endif

  public void Show() {
    ResolveReferences();
    BuildRenderer();
    visible = bodyOrigin != null && throwAnchor != null && line != null;
    if (line != null) line.enabled = visible;
    if (visible) DrawArm();
  }

  public void Hide() {
    visible = false;
    if (line != null) line.enabled = false;
  }

  private void DrawArm() {
    Vector3 end = throwAnchor.position;
    Vector3 start = GetBodyPosition(end);
    Vector3 arm = end - start;
    if (arm.sqrMagnitude < 0.000001f) {
      line.enabled = false;
      return;
    }

    line.enabled = true;
    line.positionCount = segmentCount;
    Vector3 armDirection = arm.normalized;
    Vector3 cameraUp = viewCamera != null ? viewCamera.transform.up : Vector3.up;
    Vector3 bendDirection = Vector3.ProjectOnPlane(cameraUp, armDirection);
    if (bendDirection.sqrMagnitude < 0.0001f) {
      Vector3 cameraRight = viewCamera != null ? viewCamera.transform.right : Vector3.right;
      bendDirection = Vector3.ProjectOnPlane(cameraRight, armDirection);
    }
    bendDirection = bendDirection.sqrMagnitude > 0.0001f ? bendDirection.normalized : Vector3.up;

    float phase = Time.unscaledTime * wobbleSpeed * Mathf.PI * 2f;
    for (int i = 0; i < segmentCount; i++) {
      float normalized = i / (float)(segmentCount - 1);
      float endpointMask = Mathf.Sin(normalized * Mathf.PI);
      float bow = bend * endpointMask;
      float wobble = Mathf.Sin(phase + normalized * Mathf.PI * 2.7f) * wobbleAmplitude * endpointMask;
      line.SetPosition(i, Vector3.Lerp(start, end, normalized) + bendDirection * (bow + wobble));
    }
  }

  private Vector3 GetBodyPosition(Vector3 anchorPosition) {
    Vector3 right = viewCamera != null ? viewCamera.transform.right : bodyOrigin.right;
    Vector3 up = viewCamera != null ? viewCamera.transform.up : Vector3.up;
    float anchorSide = Vector3.Dot(anchorPosition - bodyOrigin.position, right);
    float horizontalOffset = Mathf.Abs(bodyOffset.x) * (anchorSide < 0f ? -1f : 1f);
    return bodyOrigin.position + right * horizontalOffset + up * bodyOffset.y;
  }

  private void BuildRenderer() {
    if (line == null) {
      Transform existing = transform.Find("ProceduralInkArmRibbon");
      GameObject child = existing != null ? existing.gameObject : new GameObject("ProceduralInkArmRibbon");
      child.transform.SetParent(transform, false);
      child.layer = gameObject.layer;
      line = child.GetComponent<LineRenderer>();
      if (line == null) line = child.AddComponent<LineRenderer>();
    }

    line.useWorldSpace = true;
    line.loop = false;
    line.alignment = LineAlignment.View;
    line.textureMode = LineTextureMode.Tile;
    line.numCapVertices = 4;
    line.numCornerVertices = 3;
    line.widthMultiplier = 1f;
    line.widthCurve = new AnimationCurve(
      new Keyframe(0f, bodyWidth),
      new Keyframe(0.72f, Mathf.Lerp(bodyWidth, handWidth, 0.7f)),
      new Keyframe(1f, handWidth));
    line.sharedMaterial = inkMaterial;
    line.shadowCastingMode = ShadowCastingMode.Off;
    line.receiveShadows = false;
    line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    line.renderingLayerMask |= SelectiveColor.RenderingLayerMask;

    if (bodyRenderer != null) {
      line.sortingLayerID = bodyRenderer.sortingLayerID;
      line.sortingOrder = bodyRenderer.sortingOrder + sortingOrderOffset;
    }

    properties ??= new MaterialPropertyBlock();
    line.GetPropertyBlock(properties);
    properties.SetColor(CoreColorId, inkColor);
    properties.SetColor(EdgeColorId, new Color(inkColor.r, inkColor.g, inkColor.b, inkColor.a * edgeAlpha));
    properties.SetFloat(BreakupThresholdId, breakup);
    properties.SetFloat(AlphaMultiplierId, inkColor.a);
    line.SetPropertyBlock(properties);
  }

  private void ResolveReferences() {
    if (bodyOrigin == null) bodyOrigin = transform;
    if (throwAnchor == null) throwAnchor = transform.Find("DistractionThrowAnchor");
    if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
    if (bodyRenderer == null) bodyRenderer = GetComponent<Renderer>();
  }
}
