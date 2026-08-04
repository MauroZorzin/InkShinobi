using UnityEngine;

/// <summary>
/// Tags what a surface sounds like underfoot. Put this on a ground Collider a guard's
/// NavMeshAgent walks on (GuardFootsteps finds it via a downward raycast), or on a LinePath the
/// player rides along (LineFollowController reads it directly, no raycast needed since the line
/// IS the surface). Leave it off anything that should just use SurfaceAudioLibrary's fallback.
/// </summary>
public class SurfaceMarker : MonoBehaviour {
  public SurfaceType surfaceType = SurfaceType.Default;
}
