using UnityEngine;
using System.Linq;

/// <summary>
/// Explicitly marks geometry that may receive guard-light projections. The dedicated
/// rendering-layer bit is consumed only by the fullscreen lighting composite.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class LightReceiver : MonoBehaviour {
  public const int RenderingLayerIndex = 30;
  public const uint RenderingLayerMask = 1u << RenderingLayerIndex;

  [SerializeField] private bool includeChildren = true;
  private Renderer[] renderers;

  private void OnEnable() => RefreshRenderers();

  private void OnDisable() {
    EnsureRendererCache();
    SetLayerBit(false);
  }

  private void OnDestroy() {
    EnsureRendererCache();
    SetLayerBit(false);
  }

  private void OnTransformChildrenChanged() {
    if (includeChildren) RefreshRenderers();
  }

#if UNITY_EDITOR
  private void OnValidate() => RefreshRenderers();
#endif

  [ContextMenu("Refresh Renderers")]
  public void RefreshRenderers() {
    SetLayerBit(false);
    Renderer[] candidates = includeChildren
      ? GetComponentsInChildren<Renderer>(true)
      : GetComponents<Renderer>();
    renderers = candidates.Where(renderer => renderer != null &&
      renderer.GetComponentInParent<LightReceiverExclusion>() == null).ToArray();
    SetLayerBit(isActiveAndEnabled);
  }

  private void EnsureRendererCache() {
    if (renderers == null)
      RefreshRenderers();
  }

  private void SetLayerBit(bool enabled) {
    if (renderers == null) return;
    foreach (Renderer targetRenderer in renderers) {
      if (targetRenderer == null) continue;
      uint layers = targetRenderer.renderingLayerMask;
      targetRenderer.renderingLayerMask = enabled
        ? layers | RenderingLayerMask
        : layers & ~RenderingLayerMask;
    }
  }
}
