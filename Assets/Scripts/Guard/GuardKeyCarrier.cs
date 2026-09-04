using UnityEngine;

/// <summary>
/// Optional key payload owned by a guard. Door, guard garment, and dropped key all use one shared
/// DoorKeyDefinition so their identity and color cannot drift apart.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuardKeyCarrier : MonoBehaviour {
  [Header("Key")]
  [Tooltip("When disabled this guard drops no key.")]
  [SerializeField] private bool carriesKey;
  [Tooltip("Shared identity and color for this guard's key and matching door.")]
  [SerializeField] private DoorKeyDefinition keyDefinition;
  [SerializeField, HideInInspector] private string keyId;
  [Tooltip("Shared key prefab containing a WorldItem. Its sprite is tinted at runtime.")]
  [SerializeField] private GameObject keyWorldPrefab;

  [Header("Drop")]
  [Tooltip("Optional authored origin. Leave empty to drop from the guard transform.")]
  [SerializeField] private Transform dropAnchor;
  [SerializeField] private Vector3 dropOffset = new(0f, 0.08f, 0f);

  private bool dropped;

  public bool CarriesKey => carriesKey;
  public DoorKeyDefinition KeyDefinition => ResolveKeyDefinition();
  public string KeyId => KeyDefinition != null ? KeyDefinition.KeyId : keyId;

  private void OnEnable() => RefreshGuardPalette();

  public bool HasKey(string requestedKeyId) =>
    carriesKey && !dropped && !string.IsNullOrWhiteSpace(requestedKeyId) &&
    string.Equals(KeyId, requestedKeyId.Trim(), System.StringComparison.OrdinalIgnoreCase);

  public bool DropKey() {
    if (dropped || !carriesKey || string.IsNullOrWhiteSpace(KeyId) || keyWorldPrefab == null) {
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

    DoorKeyDefinition definition = KeyDefinition;
    if (definition != null) worldItem.ConfigureRuntimeIdentity(definition);
    else worldItem.ConfigureRuntimeIdentity(KeyId, true, Color.white);
    dropped = true;
    return true;
  }

#if UNITY_EDITOR
  private void OnValidate() {
    if (carriesKey) {
      if (keyDefinition == null) keyDefinition = DoorKeyDefinition.FindById(keyId);
      if (string.IsNullOrWhiteSpace(KeyId))
        Debug.LogWarning($"[GuardKeyCarrier] '{name}' carries a key but Key Definition is missing.", this);
      if (keyWorldPrefab == null)
        Debug.LogWarning($"[GuardKeyCarrier] '{name}' carries a key but Key World Prefab is missing.", this);
    }
    RefreshGuardPalette();
  }
#endif

  private DoorKeyDefinition ResolveKeyDefinition() =>
    keyDefinition != null ? keyDefinition : DoorKeyDefinition.FindById(keyId);

  private void RefreshGuardPalette() {
    GuardPaletteTint palette = GetComponent<GuardPaletteTint>();
    if (palette != null) palette.Apply();
  }
}
