using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class FloorCircleTriggerEvent : UnityEvent<Transform> { }

[RequireComponent(typeof(SphereCollider))]
public class FloorCircleTrigger : MonoBehaviour {
  [Tooltip("Reads radius, obstacleMask and lightSourceHeight from this indicator. Leave empty to use one on the same GameObject.")]
  public FloorCircleIndicator indicator;

  [Tooltip("Layers allowed to trigger this (e.g. Player).")]
  public LayerMask targetMask;

  public FloorCircleTriggerEvent onEnteredLight;
  public FloorCircleTriggerEvent onExitedLight;

  [Header("Debug")]
  public bool showGizmos = true;

  private SphereCollider _collider;
  private readonly HashSet<Collider> _candidates = new HashSet<Collider>();
  private readonly HashSet<Collider> _litColliders = new HashSet<Collider>();

  private void Awake() {
    _collider = GetComponent<SphereCollider>();
    _collider.isTrigger = true;
    _collider.center = Vector3.zero;


    if (indicator == null) {
      indicator = GetComponent<FloorCircleIndicator>();
    }
  }

  private void Update() {
    if (indicator != null) {
      Vector3 scale = transform.lossyScale;
      float scaleFactor = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
      _collider.radius = scaleFactor > 0.0001f ? indicator.radius / scaleFactor : indicator.radius;
    }

    UpdateLitState();
  }

  private void OnTriggerEnter(Collider other) {
    if (((1 << other.gameObject.layer) & targetMask.value) == 0) {
      return;
    }

    _candidates.Add(other);
  }

  private void OnTriggerExit(Collider other) {
    _candidates.Remove(other);

    if (_litColliders.Remove(other)) {
      onExitedLight?.Invoke(other.transform);
    }
  }

  private void UpdateLitState() {
    if (indicator == null) {
      return;
    }

    Vector3 lightOrigin = transform.position + transform.up * indicator.lightSourceHeight;

    foreach (Collider candidate in _candidates) {
      if (candidate == null) {
        continue;
      }

      bool blocked = indicator.obstacleMask.value != 0 &&
        Physics.Linecast(lightOrigin, candidate.bounds.center, indicator.obstacleMask);
      bool isLit = !blocked;
      bool wasLit = _litColliders.Contains(candidate);

      if (isLit && !wasLit) {
        _litColliders.Add(candidate);
        onEnteredLight?.Invoke(candidate.transform);
      } else if (!isLit && wasLit) {
        _litColliders.Remove(candidate);
        onExitedLight?.Invoke(candidate.transform);
      }
    }
  }

  public bool IsLit(Transform target) {
    foreach (Collider litCollider in _litColliders) {
      if (litCollider != null && litCollider.transform == target) {
        return true;
      }
    }

    return false;
  }

  private void OnDrawGizmosSelected() {
    if (!showGizmos) {
      return;
    }

    float radius = indicator != null ? indicator.radius : (_collider != null ? _collider.radius : 0f);
    Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
    Gizmos.DrawSphere(transform.position, radius);
    Gizmos.color = Color.cyan;
    Gizmos.DrawWireSphere(transform.position, radius);

    Gizmos.color = Color.yellow;
    foreach (Collider candidate in _candidates) {
      if (candidate == null) continue;
      Gizmos.color = _litColliders.Contains(candidate) ? Color.green : Color.red;
      Gizmos.DrawWireSphere(candidate.bounds.center, 0.2f);
    }
  }
}
