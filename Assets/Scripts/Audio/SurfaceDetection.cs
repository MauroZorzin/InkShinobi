using UnityEngine;

/// <summary>Shared downward-raycast surface lookup for anything walking on real ground colliders (not a LinePath).</summary>
public static class SurfaceDetection {
  public static SurfaceType DetectBelow(Vector3 position, float raycastHeight, float raycastDistance, LayerMask groundMask) {
    Vector3 origin = position + Vector3.up * raycastHeight;
    if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDistance + raycastHeight, groundMask)) {
      SurfaceMarker marker = hit.collider.GetComponent<SurfaceMarker>();
      if (marker != null) return marker.surfaceType;
    }

    return SurfaceType.Default;
  }
}
