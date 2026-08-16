using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerFlipController : MonoBehaviour {
  [Header("Flip")]
  [Tooltip("Degrees turned per Q/E press. 180 = classic \"turn around\" flip.")]
  public float flipAngle = 180f;

  [Tooltip("Seconds the turn takes to complete. 0 = instant snap.")]
  public float flipDuration = 0.2f;

  [Tooltip("Rotation axis, in the player's LOCAL space at the moment the flip starts. World up (0,1,0) turns the player left/right on the spot without any pitch/roll.")]
  public Vector3 rotationAxis = Vector3.up;

  [Header("Guards")]
  [Tooltip("If true, a flip already in progress ignores new Q/E presses until it finishes instead of restarting mid-turn.")]
  public bool blockInputDuringFlip = true;

  [Header("Debug")]
  public bool logFlips = true;

  private Coroutine _flipRoutine;
  private bool _isFlipping;

  public bool IsFlipping => _isFlipping;

  public bool IsFlipped { get; private set; }

#pragma warning disable IDE0051
  private void OnRotateRight(InputValue value) {
    if (value.isPressed) TryFlip(flipAngle);
  }

  private void OnRotateLeft(InputValue value) {
    if (value.isPressed) TryFlip(-flipAngle);
  }
#pragma warning restore IDE0051

  public bool TryFlip(float degrees) {
    if (!enabled) return false;
    if (blockInputDuringFlip && _isFlipping) return false;

    IsFlipped = !IsFlipped;

    if (_flipRoutine != null) StopCoroutine(_flipRoutine);
    _flipRoutine = StartCoroutine(FlipRoutine(degrees));
    return true;
  }

  private IEnumerator FlipRoutine(float degrees) {
    _isFlipping = true;
    if (logFlips) Debug.Log($"[PlayerFlipController] Flip started: {degrees}°.");

    Quaternion startRot = transform.rotation;
    Vector3 worldAxis = transform.TransformDirection(rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up);
    Quaternion endRot = Quaternion.AngleAxis(degrees, worldAxis) * startRot;

    float duration = Mathf.Max(0f, flipDuration);
    if (duration <= 0f) {
      transform.rotation = endRot;
    } else {
      float elapsed = 0f;
      while (elapsed < duration) {
        elapsed += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
        transform.rotation = Quaternion.Slerp(startRot, endRot, t);
        yield return null;
      }
      transform.rotation = endRot;
    }

    _isFlipping = false;
    _flipRoutine = null;
    if (logFlips) Debug.Log("[PlayerFlipController] Flip complete.");
  }
}
