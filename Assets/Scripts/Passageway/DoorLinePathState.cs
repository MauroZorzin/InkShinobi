using UnityEngine;

/// <summary>
/// Selects which opposite edges of a door's four-corner path rectangle are traversable.
/// Long sides represent the two faces of a closed door; short sides cross an open doorway.
/// </summary>
[DisallowMultipleComponent]
public sealed class DoorLinePathState : MonoBehaviour {
  [SerializeField] private PassagewayDoor door;
  [SerializeField] private LinePath[] longSidePaths = System.Array.Empty<LinePath>();
  [SerializeField] private LinePath[] shortSidePaths = System.Array.Empty<LinePath>();

  public LinePath[] LongSidePaths => longSidePaths;
  public LinePath[] ShortSidePaths => shortSidePaths;

  public bool Contains(LinePath path) => Contains(longSidePaths, path) || Contains(shortSidePaths, path);

  private void OnEnable() {
    if (door == null) door = GetComponentInParent<PassagewayDoor>();
    if (door == null) return;
    door.PassageStateChanged += ApplyState;
    ApplyState(door.CurrentState);
  }

  private void Start() {
    if (door != null) ApplyState(door.CurrentState);
  }

  private void OnDisable() {
    if (door != null) door.PassageStateChanged -= ApplyState;
  }

  public void Configure(PassagewayDoor controlledDoor, LinePath[] longSides, LinePath[] shortSides) {
    if (door != null && isActiveAndEnabled) door.PassageStateChanged -= ApplyState;
    door = controlledDoor;
    longSidePaths = longSides ?? System.Array.Empty<LinePath>();
    shortSidePaths = shortSides ?? System.Array.Empty<LinePath>();
    if (door != null && isActiveAndEnabled) door.PassageStateChanged += ApplyState;
    if (door != null) ApplyState(door.CurrentState);
  }

  private void ApplyState(PassagewayDoor.PassageState state) {
    // The blocker remains present while opening and is restored as soon as closing begins.
    bool actsAsWall = state != PassagewayDoor.PassageState.Open;
    SetPathsActive(longSidePaths, actsAsWall);
    SetPathsActive(shortSidePaths, !actsAsWall);
  }

  private static void SetPathsActive(LinePath[] paths, bool active) {
    if (paths == null) return;
    foreach (LinePath path in paths)
      if (path != null && path.gameObject.activeSelf != active) path.gameObject.SetActive(active);
  }

  private static bool Contains(LinePath[] paths, LinePath candidate) {
    if (candidate == null || paths == null) return false;
    foreach (LinePath path in paths)
      if (path == candidate) return true;
    return false;
  }
}
