using UnityEngine;

/// <summary>Executes a confirmed ballistic throw and emits one distraction when it lands.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class ThrownDistraction : MonoBehaviour {
  [Header("Landing")]
  [Tooltip("Only collisions on these layers count as a landing. Nothing accepts every layer.")]
  [SerializeField] private LayerMask landingLayers;
  [SerializeField, Range(0f, 1f)] private float minimumLandingNormalY = 0.35f;
  [SerializeField, Min(0f)] private float armDelay = 0.08f;
  [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;

  [Header("Sound and visual")]
  [SerializeField] private GuardSoundSignal soundSignal;
  [SerializeField] private DistractionEchoPulse echoPulsePrefab;

  [Header("Lifetime")]
  [Tooltip("When enabled, the physical rock is removed after its landing effects finish.")]
  [SerializeField] private bool destroyAfterLanding = true;
  [SerializeField, Min(0f)] private float destroyDelay = 1.1f;

  private Rigidbody body;
  private float armedAt;
  private bool launched;
  private bool landed;

  public float CollisionRadius => collisionRadius;

  private void Awake() {
    body = GetComponent<Rigidbody>();
    if (soundSignal == null) soundSignal = GetComponentInChildren<GuardSoundSignal>(true);
  }

  public bool Launch(DistractionThrowEvaluation evaluation, Collider[] ignoredThrowerColliders = null) {
    if (!evaluation.IsValid || body == null) return false;
    transform.SetPositionAndRotation(evaluation.Origin, Quaternion.identity);
    IgnoreThrowerCollisions(ignoredThrowerColliders);
    body.isKinematic = false;
    body.linearVelocity = evaluation.InitialVelocity;
    body.angularVelocity = Random.onUnitSphere * 7f;
    launched = true;
    landed = false;
    armedAt = Time.time + armDelay;
    return true;
  }

  private void IgnoreThrowerCollisions(Collider[] throwerColliders) {
    if (throwerColliders == null || throwerColliders.Length == 0) return;
    Collider[] projectileColliders = GetComponentsInChildren<Collider>(true);
    for (int projectileIndex = 0; projectileIndex < projectileColliders.Length; projectileIndex++) {
      Collider projectileCollider = projectileColliders[projectileIndex];
      if (projectileCollider == null) continue;
      for (int throwerIndex = 0; throwerIndex < throwerColliders.Length; throwerIndex++) {
        Collider throwerCollider = throwerColliders[throwerIndex];
        if (throwerCollider != null) Physics.IgnoreCollision(projectileCollider, throwerCollider, true);
      }
    }
  }

  private void OnCollisionEnter(Collision collision) {
    if (!launched || landed || Time.time < armedAt || collision.contactCount == 0) return;
    ContactPoint contact = collision.GetContact(0);
    if (landingLayers.value != 0 && (landingLayers.value & (1 << collision.gameObject.layer)) == 0) return;
    if (contact.normal.y < minimumLandingNormalY) return;

    landed = true;
    body.linearVelocity = Vector3.zero;
    body.angularVelocity = Vector3.zero;
    body.isKinematic = true;
    transform.position = contact.point + contact.normal * collisionRadius;
    soundSignal?.EmitOnce();

    if (echoPulsePrefab != null) {
      DistractionEchoPulse pulse = Instantiate(
        echoPulsePrefab,
        contact.point + contact.normal * 0.02f,
        Quaternion.FromToRotation(Vector3.up, contact.normal));
      pulse.Play(soundSignal != null ? soundSignal.AudibleRadius : 5f);
    }

    if (destroyAfterLanding) Destroy(gameObject, destroyDelay);
  }

}
