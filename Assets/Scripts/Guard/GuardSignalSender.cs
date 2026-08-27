using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Emits a sound stimulus and guarantees that one emission notifies each guard at most once.
/// The one-shot path is used by thrown distractions; Activate remains for legacy trigger-based scenes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class GuardSoundSignal : MonoBehaviour {
  [Header("Signal")]
  [Tooltip("Whether the legacy trigger signal is currently active.")]
  public bool IsActive;
  [Tooltip("Seconds until a legacy active signal deactivates. Zero means it remains active.")]
  [Min(0f)] public float lifetime;
  [Tooltip("Gameplay radius used by EmitOnce. When zero, the attached collider bounds are used.")]
  [SerializeField, Min(0f)] private float audibleRadius = 5f;
  [Tooltip("Layers containing guard colliders. Nothing means all layers for backwards compatibility.")]
  [SerializeField] private LayerMask guardLayers;
  [SerializeField, Range(8, 256)] private int overlapCapacity = 64;

  [Header("Debug")]
  public bool verboseLogging;

  private readonly HashSet<GuardController> notifiedGuards = new();
  private Collider[] overlapBuffer;
  private Collider signalCollider;
  private float activeTimer;
  private bool wasActive;

  public float AudibleRadius => audibleRadius > 0f ? audibleRadius : GetColliderRadius();

  private void Awake() {
    signalCollider = GetComponent<Collider>();
    if (!signalCollider.isTrigger) signalCollider.isTrigger = true;
    EnsureBuffer();
  }

  private void Update() {
    if (IsActive && !wasActive) BeginEmission();
    wasActive = IsActive;
    if (!IsActive || lifetime <= 0f) return;
    activeTimer += Time.deltaTime;
    if (activeTimer >= lifetime) Deactivate();
  }

  /// <summary>Immediately emits one sound event, then leaves no persistent trigger active.</summary>
  public void EmitOnce() {
    BeginEmission();
    IsActive = false;
    wasActive = false;
  }

  /// <summary>Starts the legacy active trigger and immediately catches guards already inside.</summary>
  public void Activate(float newLifetime = -1f) {
    if (newLifetime >= 0f) lifetime = newLifetime;
    activeTimer = 0f;
    IsActive = true;
    BeginEmission();
    wasActive = true;
  }

  public void Deactivate() {
    IsActive = false;
    wasActive = false;
    activeTimer = 0f;
    notifiedGuards.Clear();
  }

  private void OnTriggerEnter(Collider other) {
    if (IsActive) TryNotifyGuard(other);
  }

  private void BeginEmission() {
    EnsureBuffer();
    notifiedGuards.Clear();
    activeTimer = 0f;
    int mask = guardLayers.value == 0 ? Physics.AllLayers : guardLayers.value;
    int hitCount = Physics.OverlapSphereNonAlloc(
      transform.position,
      AudibleRadius,
      overlapBuffer,
      mask,
      QueryTriggerInteraction.Collide);
    for (int i = 0; i < hitCount; i++) TryNotifyGuard(overlapBuffer[i]);
    if (hitCount == overlapBuffer.Length)
      Debug.LogWarning($"[SoundSignal] '{name}' filled its overlap buffer; increase Overlap Capacity.", this);
    if (verboseLogging)
      Debug.Log($"[SoundSignal] '{name}' notified {notifiedGuards.Count} guard(s) within {AudibleRadius:F2} units.", this);
  }

  private void TryNotifyGuard(Collider candidate) {
    if (candidate == null) return;
    GuardController guard = candidate.GetComponentInParent<GuardController>();
    if (guard == null || !notifiedGuards.Add(guard)) return;
    guard.InvestigateSound(transform.position);
  }

  private void EnsureBuffer() {
    int size = Mathf.Clamp(overlapCapacity, 8, 256);
    if (overlapBuffer == null || overlapBuffer.Length != size) overlapBuffer = new Collider[size];
  }

  private float GetColliderRadius() {
    if (signalCollider == null) signalCollider = GetComponent<Collider>();
    if (signalCollider is SphereCollider sphere) {
      Vector3 scale = signalCollider.transform.lossyScale;
      return sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
    }
    Vector3 extents = signalCollider.bounds.extents;
    return Mathf.Max(extents.x, extents.y, extents.z);
  }

#if UNITY_EDITOR
  private void OnValidate() {
    audibleRadius = Mathf.Max(0f, audibleRadius);
    overlapCapacity = Mathf.Clamp(overlapCapacity, 8, 256);
  }

  private void OnDrawGizmosSelected() {
    Gizmos.color = IsActive ? new Color(1f, 0.65f, 0.1f, 0.8f) : new Color(1f, 0.75f, 0.25f, 0.45f);
    Gizmos.DrawWireSphere(transform.position, AudibleRadius);
  }

  public void Configure(float radius, LayerMask layers) {
    audibleRadius = Mathf.Max(0.01f, radius);
    guardLayers = layers;
    lifetime = 0f;
    IsActive = false;
  }
#endif
}
