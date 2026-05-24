using UnityEngine;

/// <summary>
/// Data asset that identifies an inventory item and describes how it appears in the UI preview.
/// </summary>
[CreateAssetMenu(menuName = "Ink Shinobi/Item")]
public class ItemDefinition : ScriptableObject {
  [Tooltip("Stable id used by inventory checks and item requirements.")]
  public string itemId;

  [Tooltip("Human-readable item name for UI display.")]
  public string displayName;

  [Header("3D Icon Preview")]
  [Tooltip("Prefab instantiated by the item icon renderer for 3D UI previews.")]
  public GameObject iconPreviewPrefab;

  [Tooltip("Rotation applied to the preview prefab inside the icon render scene.")]
  public Vector3 iconRotation = new(20f, -35f, 0f);

  [Tooltip("Local position offset applied to the preview prefab inside the icon render scene.")]
  public Vector3 iconOffset = Vector3.zero;

  [Tooltip("Orthographic camera size used while rendering this item's icon preview.")]
  public float iconOrthographicSize = 1f;
}
