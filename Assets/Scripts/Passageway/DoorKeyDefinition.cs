using System;
using UnityEngine;

/// <summary>Identità e presentazione condivise per una chiave, la porta corrispondente e chi la porta.</summary>
[CreateAssetMenu(menuName = "Ink Shinobi/Door Key", fileName = "DoorKey")]
public sealed class DoorKeyDefinition : ScriptableObject {
  private static DoorKeyDefinition[] cachedDefinitions;

  [SerializeField] private string keyId;
  [SerializeField] private string displayName;
  [SerializeField] private Color color = Color.white;

  public string KeyId => keyId?.Trim() ?? string.Empty;
  public string DisplayName => displayName?.Trim() ?? string.Empty;
  public Color Color => color;

  public static DoorKeyDefinition FindById(string requestedKeyId) {
    if (string.IsNullOrWhiteSpace(requestedKeyId)) return null;

    cachedDefinitions ??= Resources.LoadAll<DoorKeyDefinition>("DoorKeys");
    DoorKeyDefinition[] definitions = cachedDefinitions;
    for (int i = 0; i < definitions.Length; i++) {
      DoorKeyDefinition definition = definitions[i];
      if (definition != null && string.Equals(
            definition.KeyId,
            requestedKeyId.Trim(),
            StringComparison.OrdinalIgnoreCase)) {
        return definition;
      }
    }
    return null;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetCache() => cachedDefinitions = null;
}
