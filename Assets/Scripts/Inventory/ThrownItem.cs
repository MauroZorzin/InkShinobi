using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ThrownItem : MonoBehaviour {
  [Header("Throw")]
  [Tooltip("Degrees above the flat direction to the target the throw arcs at. Speed is solved so the arc actually lands on the target.")]
  public float throwUpAngle = 35f;

  [Tooltip("Speed is solved to hit the target exactly, but never exceeds this — keeps very long throws from moving absurdly fast.")]
  public float maxThrowSpeed = 20f;

  [Header("Distraction")]
  [Tooltip("Trigger collider (separate from this item's physical collider) that notifies nearby guards on landing.")]
  public GuardSoundSignal distractionSignal;
  public float distractionLifetime = 3f;

  [Header("Echo Visual")]
  public EchoPulse echoPrefab;

  public LayerMask echoLayer;

  private const float ArmDelay = 0.15f;

  private Rigidbody _rigidbody;
  private float _armTimer;
  private bool _hasLanded;
  private bool _wasThrown;

  private void Awake() {
    _rigidbody = GetComponent<Rigidbody>();
    //_rigidbody.isKinematic = true;
    echoPrefab.gameObject.SetActive(false);
  }

  private void Update() {
    if (_armTimer > 0f) {
      _armTimer -= Time.deltaTime;
    }
  }

  public void Launch(Vector3 targetPoint) {

    Vector3 toTarget = targetPoint - transform.position;
    Vector3 flatDelta = new(toTarget.x, 0f, toTarget.z);
    float range = flatDelta.magnitude;
    Vector3 direction = range > 0.0001f ? flatDelta / range : transform.forward;

    float rad = throwUpAngle * Mathf.Deg2Rad;
    float gravity = Mathf.Abs(Physics.gravity.y);
    float sin2Theta = Mathf.Sin(2f * rad);

    float speed = maxThrowSpeed;
    if (range > 0.0001f && sin2Theta > 0.0001f && gravity > 0.0001f) {
      speed = Mathf.Min(Mathf.Sqrt(range * gravity / sin2Theta), maxThrowSpeed);
    }

    _rigidbody.isKinematic = false;
    _rigidbody.linearDamping = 0f;
    _rigidbody.linearVelocity = (Mathf.Cos(rad) * direction + Mathf.Sin(rad) * Vector3.up) * speed;

    _wasThrown = true;
    _armTimer = ArmDelay;

  }

  private void OnCollisionEnter(Collision collision) {
    if (!_wasThrown || _hasLanded) {
      return;
    }
    if (echoPrefab != null) {
      if ((echoLayer.value & (1 << collision.collider.gameObject.layer)) == 0) {
        return;
      }
    }

    _hasLanded = true;

    if (distractionSignal != null) {
      distractionSignal.Activate(distractionLifetime);
    }

    if (echoPrefab != null) {
      echoPrefab.gameObject.SetActive(true);
    }
  }
}
