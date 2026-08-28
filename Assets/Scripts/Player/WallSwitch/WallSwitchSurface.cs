using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a solid collider as geometry that participates in wall-switch trajectory evaluation.
/// Its position relative to the selected destination LinePath determines its role automatically:
/// geometry behind the path receives the ink stain, while geometry in front blocks the switch.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WallSwitchSurface : MonoBehaviour {
  private static readonly HashSet<WallSwitchSurface> activeSurfaces = new();

  private Collider surfaceCollider;

  public static IReadOnlyCollection<WallSwitchSurface> ActiveSurfaces => activeSurfaces;
  public Collider SurfaceCollider => surfaceCollider;

  private void Awake() {
    surfaceCollider = GetComponent<Collider>();
  }

  private void OnEnable() {
    if (surfaceCollider == null) surfaceCollider = GetComponent<Collider>();
    activeSurfaces.Add(this);
  }

  private void OnDisable() {
    activeSurfaces.Remove(this);
  }
}
