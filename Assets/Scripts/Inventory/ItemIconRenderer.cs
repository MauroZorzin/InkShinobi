using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Renders a 3D item preview into a RenderTexture for inventory UI slots.
/// </summary>
public class ItemIconRenderer : MonoBehaviour {
  [Header("Preview Scene")]
  [Tooltip("Camera used to render the preview prefab into the render texture.")]
  [SerializeField] private Camera previewCamera;

  [Tooltip("Parent transform where preview prefab instances are created.")]
  [SerializeField] private Transform previewRoot;

  [Tooltip("Render texture assigned to UI elements that display the preview.")]
  [SerializeField] private RenderTexture renderTexture;

  [Tooltip("Layer assigned to preview instances so the preview camera can isolate them.")]
  [SerializeField] private LayerMask previewLayer;

  [Header("Optional Style")]
  [Tooltip("Optional material applied to all renderers in the preview instance.")]
  [SerializeField] private Material previewOverrideMaterial;

  private GameObject currentPreviewInstance;

  public RenderTexture RenderTexture => renderTexture;

  /// <summary>
  /// Replaces the current preview with the configured preview model for an item.
  /// </summary>
  /// <param name="item">The item to preview, or null to clear the preview.</param>
  public void ShowItem(ItemDefinition item) {
    ClearPreview();

    if (item == null || item.iconPreviewPrefab == null) {
      if (previewCamera != null) {
        previewCamera.enabled = false;
      }
      return;
    }

    currentPreviewInstance = Instantiate(item.iconPreviewPrefab, previewRoot.position + item.iconOffset, Quaternion.Euler(item.iconRotation), previewRoot);

    SetLayerRecursively(currentPreviewInstance, GetLayerFromMask(previewLayer));

    if (previewOverrideMaterial != null) {
      ApplyMaterialOverride(currentPreviewInstance, previewOverrideMaterial);
    }

    previewCamera.orthographicSize = item.iconOrthographicSize;
    previewCamera.targetTexture = renderTexture;
    previewCamera.enabled = true;

    if (CanRenderPreviewImmediately()) {
      previewCamera.Render();
    }
  }

  /// <summary>
  /// Destroys the active preview instance, if one exists.
  /// </summary>
  public void ClearPreview() {
    if (currentPreviewInstance != null) {
      Destroy(currentPreviewInstance);
      currentPreviewInstance = null;
    }
  }

  /// <summary>
  /// Applies a single material to every renderer in the preview model.
  /// </summary>
  /// <param name="target">The root of the preview model.</param>
  /// <param name="material">The material to assign to all renderer slots.</param>
  private void ApplyMaterialOverride(GameObject target, Material material) {
    Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
    foreach (Renderer renderer in renderers) {
      Material[] materials = renderer.sharedMaterials;
      for (var i = 0; i < materials.Length; i++) {
        materials[i] = material;
      }
      renderer.sharedMaterials = materials;
    }
  }

  /// <summary>
  /// Assigns a layer to the preview object hierarchy so only the preview camera renders it.
  /// </summary>
  /// <param name="target">The root object to update.</param>
  /// <param name="layer">The layer index to assign.</param>
  private void SetLayerRecursively(GameObject target, int layer) {
    target.layer = layer;
    foreach (Transform child in target.transform) {
      SetLayerRecursively(child.gameObject, layer);
    }
  }

  /// <summary>
  /// Converts the first layer bit in a LayerMask into a layer index.
  /// </summary>
  /// <param name="mask">The mask containing the preview layer.</param>
  /// <returns>The selected layer index, or the default layer when the mask is empty.</returns>
  private int GetLayerFromMask(LayerMask mask) {
    var value = mask.value;
    if (value == 0) {
      Debug.LogWarning("Preview layer mask is empty.");
      return 0;
    }
    return Mathf.RoundToInt(Mathf.Log(value, 2));
  }

  /// <summary>
  /// Checks whether the current runtime can safely force a camera render for the preview texture.
  /// </summary>
  /// <returns>True when an immediate preview render should be attempted.</returns>
  private static bool CanRenderPreviewImmediately() {
    return !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
  }
}
