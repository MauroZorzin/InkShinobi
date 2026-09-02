using UnityEngine;

namespace IdyllicFantasyNature {
  /// <summary>Disables the shared player-driven vegetation bend while the player is inside.</summary>
  [DefaultExecutionOrder(1000)]
  [DisallowMultipleComponent]
  [RequireComponent(typeof(BoxCollider))]
  public sealed class VegetationBendSuppressionVolume : MonoBehaviour {
    private static readonly int PlayerPositionId = Shader.PropertyToID("_Player_Position");
    private static readonly Vector3 SuppressedPosition = new(100000f, 100000f, 100000f);

    [SerializeField] private Material[] materials = System.Array.Empty<Material>();
    private BoxCollider volume;

    private void Awake() => volume = GetComponent<BoxCollider>();

    private void LateUpdate() {
      LineFollowController player = LineFollowController.ActivePlayer;
      if (player == null || !ContainsPoint(player.FeetPosition)) return;

      for (int i = 0; i < materials.Length; i++) {
        if (materials[i] != null) materials[i].SetVector(PlayerPositionId, SuppressedPosition);
      }
    }

    private bool ContainsPoint(Vector3 worldPosition) {
      if (volume == null) volume = GetComponent<BoxCollider>();
      if (volume == null || !volume.enabled) return false;

      Vector3 localPosition = volume.transform.InverseTransformPoint(worldPosition) - volume.center;
      Vector3 halfSize = volume.size * 0.5f;
      return Mathf.Abs(localPosition.x) <= halfSize.x &&
             Mathf.Abs(localPosition.y) <= halfSize.y &&
             Mathf.Abs(localPosition.z) <= halfSize.z;
    }

#if UNITY_EDITOR
    private void OnValidate() {
      BoxCollider box = GetComponent<BoxCollider>();
      if (box != null) box.isTrigger = true;
    }
#endif
  }
}
