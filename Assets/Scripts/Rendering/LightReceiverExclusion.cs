using UnityEngine;

/// <summary>
/// Prevents renderers on this object and its children from receiving projected fixed or cone
/// light color. This keeps reusable fixture geometry independent from scene-level receivers.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class LightReceiverExclusion : MonoBehaviour {
  public const int RenderingLayerIndex = 27;
  public const uint RenderingLayerMask = 1u << RenderingLayerIndex;

  private Renderer[] renderers;

  private void OnEnable() => RefreshRenderers();

  private void OnDisable() => SetExclusionLayer(false);

  private void OnDestroy() => SetExclusionLayer(false);

  private void OnTransformChildrenChanged() => RefreshRenderers();

#if UNITY_EDITOR
  private void OnValidate() => RefreshRenderers();
#endif

  [ContextMenu("Refresh Projected-Light Exclusion")]
  public void RefreshRenderers() {
    SetExclusionLayer(false);
    renderers = GetComponentsInChildren<Renderer>(true);
    SetExclusionLayer(isActiveAndEnabled);
  }

  private void SetExclusionLayer(bool enabled) {
    if (renderers == null) return;
    foreach (Renderer targetRenderer in renderers) {
      if (targetRenderer == null) continue;
      uint layers = targetRenderer.renderingLayerMask & ~LightReceiver.RenderingLayerMask;
      targetRenderer.renderingLayerMask = enabled
        ? layers | RenderingLayerMask
        : layers & ~RenderingLayerMask;
    }
  }
}
