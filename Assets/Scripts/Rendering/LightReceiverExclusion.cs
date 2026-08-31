using UnityEngine;

/// <summary>
/// Prevents renderers on this object and its children from receiving projected fixed or cone
/// light color. This keeps reusable fixture geometry independent from scene-level receivers.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class LightReceiverExclusion : MonoBehaviour {
  private void OnEnable() => ClearReceiverLayer();

  private void OnTransformChildrenChanged() => ClearReceiverLayer();

#if UNITY_EDITOR
  private void OnValidate() => ClearReceiverLayer();
#endif

  [ContextMenu("Clear Projected-Light Receiver Layer")]
  private void ClearReceiverLayer() {
    foreach (Renderer targetRenderer in GetComponentsInChildren<Renderer>(true)) {
      if (targetRenderer == null) continue;
      targetRenderer.renderingLayerMask &= ~LightReceiver.RenderingLayerMask;
    }
  }
}
