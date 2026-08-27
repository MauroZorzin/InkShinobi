using UnityEngine;

/// <summary>
/// Marks this object's renderers as exceptions to the monochrome pass.
/// The marker uses one dedicated Rendering Layer bit, so it does not duplicate or replace
/// materials and can coexist with ordinary GameObject layers.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SelectiveColor : MonoBehaviour {
  public const int RenderingLayerIndex = 31;
  public const uint RenderingLayerMask = 1u << RenderingLayerIndex;

  [Tooltip("Also preserve color on renderers below this GameObject.")]
  [SerializeField] private bool includeChildren = true;

  [Tooltip("When disabled, the renderers remain part of the normal monochrome world.")]
  [SerializeField] private bool preserveColor = true;

  private Renderer[] _renderers;

  public bool PreserveColor {
    get => preserveColor;
    set {
      if (preserveColor == value) return;
      preserveColor = value;
      Apply();
    }
  }

  private void OnEnable() {
    RefreshRenderers();
  }

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
  private void OnValidate() {
    RefreshRenderers();
  }
#endif

  [ContextMenu("Refresh Renderers")]
  public void RefreshRenderers() {
    // Remove the marker from the previous cached set before rebuilding it. This matters when
    // Include Children changes or a renderer is reparented in Edit Mode.
    SetLayerBit(false);
    CacheRenderers();
    SetLayerBit(isActiveAndEnabled && preserveColor);
  }

  private void EnsureRendererCache() {
    if (_renderers == null) CacheRenderers();
  }

  private void CacheRenderers() {
    _renderers = includeChildren
      ? GetComponentsInChildren<Renderer>(true)
      : GetComponents<Renderer>();
  }

  private void Apply() {
    if (_renderers == null) RefreshRenderers();
    else SetLayerBit(isActiveAndEnabled && preserveColor);
  }

  private void SetLayerBit(bool enabled) {
    if (_renderers == null) return;

    foreach (Renderer targetRenderer in _renderers) {
      if (targetRenderer == null) continue;

      uint layers = targetRenderer.renderingLayerMask;
      targetRenderer.renderingLayerMask = enabled
        ? layers | RenderingLayerMask
        : layers & ~RenderingLayerMask;
    }
  }
}
