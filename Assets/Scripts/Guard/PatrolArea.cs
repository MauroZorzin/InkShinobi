using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BoxCollider))]
public class PatrolArea : MonoBehaviour {
  [SerializeField] private BoxCollider boxCollider;

  private void Reset() {
    boxCollider = GetComponent<BoxCollider>();
    boxCollider.isTrigger = true;
  }

  private void Awake() {
    if (boxCollider == null) {
      boxCollider = GetComponent<BoxCollider>();
    }
  }

  public bool ContainsPoint(Vector3 worldPoint) {
    Vector3 localPoint = boxCollider.transform.InverseTransformPoint(worldPoint);

    Vector3 center = boxCollider.center;
    Vector3 halfSize = boxCollider.size * 0.5f;

    var insideX = localPoint.x >= center.x - halfSize.x && localPoint.x <= center.x + halfSize.x;
    var insideZ = localPoint.z >= center.z - halfSize.z && localPoint.z <= center.z + halfSize.z;

    return insideX && insideZ;
  }

  public bool TryGetRandomPointOnNavMesh(out Vector3 result, float navMeshSearchRadius, int navMeshAreaMask, int maxAttempts = 30) {
    Vector3 center = boxCollider.center;
    Vector3 halfSize = boxCollider.size * 0.5f;

    for (var i = 0; i < maxAttempts; i++) {
      var randomX = Random.Range(center.x - halfSize.x, center.x + halfSize.x);
      var randomZ = Random.Range(center.z - halfSize.z, center.z + halfSize.z);

      var localPoint = new Vector3(randomX, center.y, randomZ);
      Vector3 worldPoint = boxCollider.transform.TransformPoint(localPoint);

      if (NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, navMeshSearchRadius, navMeshAreaMask)) {
        if (ContainsPoint(hit.position)) {
          result = hit.position;
          return true;
        }
      }
    }

    result = transform.position;
    return false;
  }

  private void OnDrawGizmos() {
    BoxCollider box = boxCollider;

    if (box == null) {
      box = GetComponent<BoxCollider>();
    }

    if (box == null) {
      return;
    }

    Gizmos.color = Color.yellow;
    Gizmos.matrix = box.transform.localToWorldMatrix;
    Gizmos.DrawWireCube(box.center, box.size);
  }
}
