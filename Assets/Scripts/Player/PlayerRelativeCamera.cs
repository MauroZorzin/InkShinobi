using UnityEngine;

/// <summary>
/// Shared convention for player camera endpoints.
/// X is lateral framing, Y is height relative to the player, and Z is radial distance from the
/// player on the camera's current side. This makes framing independent of player orientation and
/// of whether the camera currently occupies positive or negative local Z.
/// </summary>
public static class PlayerRelativeCamera {
  private const float DirectionEpsilon = 0.0001f;

  public static Vector3 ResolveLocalEndpoint(
    Transform player,
    Transform cameraTransform,
    Vector3 playerRelativeEndpoint) {
    if (player == null || cameraTransform == null) return Vector3.zero;

    Vector3 awayFromPlayer = cameraTransform.position - player.position;
    awayFromPlayer.y = 0f;
    if (awayFromPlayer.sqrMagnitude <= DirectionEpsilon) {
      awayFromPlayer = -player.forward;
      awayFromPlayer.y = 0f;
    }
    if (awayFromPlayer.sqrMagnitude <= DirectionEpsilon) awayFromPlayer = Vector3.back;
    else awayFromPlayer.Normalize();

    Vector3 lateral = Vector3.Cross(awayFromPlayer, Vector3.up).normalized;
    Vector3 worldEndpoint = player.position
      + awayFromPlayer * Mathf.Max(0f, playerRelativeEndpoint.z)
      + lateral * playerRelativeEndpoint.x
      + Vector3.up * playerRelativeEndpoint.y;
    return cameraTransform.parent != null
      ? cameraTransform.parent.InverseTransformPoint(worldEndpoint)
      : worldEndpoint;
  }

  public static void ClampDistance(ref Vector3 playerRelativeEndpoint) {
    playerRelativeEndpoint.z = Mathf.Max(0f, playerRelativeEndpoint.z);
  }
}
