using UnityEngine;

/// <summary>
/// Optional key payload owned by a guard. The key uses one shared world prefab; its runtime id and
/// colour are supplied here, with colour derived from this guard's authored garment palette.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuardKeyCarrier : MonoBehaviour {
  [Header("Key")]
  [Tooltip("When disabled this guard drops no key.")]
  [SerializeField] private bool carriesKey;
  [Tooltip("Stable id required by the matching locked door. Required when Carries Key is enabled.")]
  [SerializeField] private string keyId;
  [Tooltip("Shared key prefab containing a WorldItem. Its sprite is tinted at runtime.")]
  [SerializeField] private GameObject keyWorldPrefab;

  [Header("Drop")]
  [Tooltip("Optional authored origin. Leave empty to drop from the guard transform.")]
  [SerializeField] private Transform dropAnchor;
  [SerializeField] private Vector3 dropOffset = new(0f, 0.08f, 0f);

  private bool dropped;

  public bool CarriesKey => carriesKey;
  public string KeyId => keyId;

  public bool DropKey() {
    if (dropped || !carriesKey || string.IsNullOrWhiteSpace(keyId) || keyWorldPrefab == null) {
      return false;
    }

    Transform origin = dropAnchor != null ? dropAnchor : transform;
    GameObject instance = Instantiate(keyWorldPrefab, origin.position + dropOffset, Quaternion.identity);
    WorldItem worldItem = instance.GetComponent<WorldItem>();
    if (worldItem == null) {
      Debug.LogError($"[GuardKeyCarrier] '{keyWorldPrefab.name}' has no WorldItem component.", this);
      Destroy(instance);
      return false;
    }

    GuardPaletteTint palette = GetComponent<GuardPaletteTint>();
    Color keyColor = palette != null ? palette.GarmentColor : Color.white;
    worldItem.ConfigureRuntimeIdentity(keyId, true, keyColor);
    dropped = true;
    return true;
  }

#if UNITY_EDITOR
  private void OnValidate() {
    if (!carriesKey) return;
    if (string.IsNullOrWhiteSpace(keyId))
      Debug.LogWarning($"[GuardKeyCarrier] '{name}' carries a key but Key Id is empty.", this);
    if (keyWorldPrefab == null)
      Debug.LogWarning($"[GuardKeyCarrier] '{name}' carries a key but Key World Prefab is missing.", this);
  }
#endif
}
