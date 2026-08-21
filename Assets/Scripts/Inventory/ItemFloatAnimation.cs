using UnityEngine;

/// <summary>Idle animation for items lying in the world: spins in place and bobs up/down.</summary>
public class ItemFloatAnimation : MonoBehaviour {
  [Tooltip("Degrees per second the item spins around its local up axis.")]
  public float spinSpeed = 90f;

  [Tooltip("How far up/down the item bobs from its starting position.")]
  public float bobHeight = 0.15f;

  [Tooltip("Bob cycles per second.")]
  public float bobSpeed = 1f;

  private Vector3 _basePosition;

  private void OnEnable() {
    _basePosition = transform.localPosition;
  }

  private void Update() {
    transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

    float offset = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobHeight;
    transform.localPosition = _basePosition + Vector3.up * offset;
  }
}
