using UnityEngine;
using UnityEngine.Audio;

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
  [SerializeField] private AudioClip throwSound;
  [SerializeField] private AudioClip landingSound;
  [Tooltip("Layers treated as water. A water landing plays splashSound instead of landingSound, spawns splashParticlePrefab in place of the rock, and never alerts guards or shows the echo pulse.")]
  [SerializeField] private LayerMask waterLayers;
  [SerializeField] private AudioClip splashSound;
  [SerializeField] private GameObject splashParticlePrefab;
  [Tooltip("Upward offset applied when spawning splashParticlePrefab, so its transparent geometry clears the opaque water surface's depth instead of being clipped by it.")]
  [SerializeField, Min(0f)] private float splashSpawnOffset = 0.05f;
  [Range(0f, 1f)] [SerializeField] private float soundVolume = 1f;
  [Range(0f, 0.5f)] [SerializeField] private float pitchVariance = 0.08f;
  [SerializeField] private AudioMixerGroup mixerGroup;

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
    PlaySound(throwSound, transform.position);
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

    bool isWater = (waterLayers.value & (1 << collision.gameObject.layer)) != 0;
    PlaySound(isWater ? splashSound : landingSound, contact.point);

    if (isWater) {
      // The rock is replaced outright by the splash — no guard alert, no echo pulse, no lingering rock.
      SpawnSplashParticle(contact.point + contact.normal * splashSpawnOffset, contact.normal);
      Destroy(gameObject);
      return;
    }

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

  private void PlaySound(AudioClip clip, Vector3 position) {
    OneShotAudio.PlayClipAtPoint(clip, position, soundVolume, mixerGroup, pitchVariance);
  }

  private void SpawnSplashParticle(Vector3 point, Vector3 normal) {
    if (splashParticlePrefab == null) return;
    GameObject instance = Instantiate(splashParticlePrefab, point, Quaternion.FromToRotation(Vector3.up, normal));
    PauseAwareUnscaledParticles.Configure(instance);
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    float lifetime = 1f;
    for (int i = 0; i < particles.Length; i++) {
      ParticleSystem.MainModule main = particles[i].main;
      lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax);
      particles[i].Play(true);
    }
    Destroy(instance, lifetime);
  }

}
