using UnityEngine;

/// <summary>Impedisce ai renderer di questo oggetto e dei suoi figli di ricevere il colore proiettato da luci fisse o coniche.</summary>
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
