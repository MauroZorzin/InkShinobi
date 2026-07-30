using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Selects the nearest usable passageway door and toggles it from player interaction input.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerDoorInteractor : MonoBehaviour {
  [Header("Interaction")]
  [Tooltip("Maximum distance from the player to a door interaction surface.")]
  [SerializeField] private float interactionRange = 2f;

  [Tooltip("Inventory checked against door item requirements.")]
  [SerializeField] private PlayerInventory inventory;

  [Tooltip("Doors this interactor can consider. If empty, all PassagewayDoor instances are discovered on Awake.")]
  [SerializeField] private PassagewayDoor[] doors;

  [Header("Corner Lock")]
  [Tooltip("Camera used for the camera-relative back-ray probes below. Defaults to Camera.main if left empty.")]
  [SerializeField] private Transform cameraPivot;

  [Tooltip("Side offset (world units) the back-ray probes start from, either side of the player.")]
  [SerializeField] private float lateralRayLength = 0.5f;

  [Tooltip("Length (world units) of each backward probe ray used to detect a blocked door front.")]
  [SerializeField] private float backRayLength = 1f;

  [Tooltip("Layer mask used when checking whether a closed passageway door blocks the player's back rays.")]
  [SerializeField] private LayerMask passagewayLayer;

  [Header("Debug")]
  [Tooltip("Draws the door interaction range in the Scene view when this object is selected.")]
  [SerializeField] private bool showInteractionRangeGizmo = true;
  private PassagewayDoor currentDoor;
  private readonly RaycastHit[] backRayHitBuffer = new RaycastHit[8];

  private void Awake() {
    if (inventory == null) {
      inventory = GetComponent<PlayerInventory>();
    }

    if (cameraPivot == null && Camera.main != null) {
      cameraPivot = Camera.main.transform;
    }

    if (doors == null || doors.Length == 0) {
      doors = FindObjectsByType<PassagewayDoor>(FindObjectsSortMode.None);
    }
  }

  private void Update() {
    PassagewayDoor nearestDoor = FindNearestDoorInRange();

    UpdateHighlight(nearestDoor);
  }

  public void OnInteract(InputValue value) {
    if (!value.isPressed) {
      return;
    }

    PassagewayDoor nearestDoor = FindNearestDoorInRange();

    if (nearestDoor != null && CanUseDoor(nearestDoor)) {
      nearestDoor.TryToggle(inventory);
    }
  }

  /// <summary>
  /// Finds the nearest configured door inside interaction range.
  /// </summary>
  /// <returns>The nearest door in range, or null when none are reachable.</returns>
  private PassagewayDoor FindNearestDoorInRange() {
    PassagewayDoor nearestDoor = null;
    var nearestDistance = interactionRange;

    foreach (PassagewayDoor door in doors) {
      if (door == null) {
        continue;
      }

      var distance = door.GetInteractionDistance(transform.position);

      if (distance <= nearestDistance) {
        nearestDistance = distance;
        nearestDoor = door;
      }
    }

    return nearestDoor;
  }

  /// <summary>
  /// Updates visual feedback when the nearest door changes or its usability changes.
  /// </summary>
  /// <param name="nearestDoor">The door currently closest to the player.</param>
  private void UpdateHighlight(PassagewayDoor nearestDoor) {
    if (currentDoor != null && currentDoor != nearestDoor) {
      currentDoor.SetHighlighted(false, false);
    }

    currentDoor = nearestDoor;

    if (currentDoor != null) {
      var canUse = CanUseDoor(currentDoor);
      currentDoor.SetHighlighted(true, canUse);
    }
  }

  /// <summary>
  /// Checks item requirements and corner-locking rules for a door.
  /// </summary>
  /// <param name="door">The door being evaluated.</param>
  /// <returns>True when the player is allowed to toggle the door.</returns>
  private bool CanUseDoor(PassagewayDoor door) {
    if (door == null) {
      return false;
    }

    if (IsDoorFrontLocked(door)) {
      return false;
    }

    return door.CanToggle(inventory);
  }

  /// <summary>
  /// Detects closed passageway doors directly behind the player's side probes so corner turning stays locked.
  /// </summary>
  /// <param name="door">The door being checked.</param>
  /// <returns>True when the door blocks the turner's back rays.</returns>
  private bool IsDoorFrontLocked(PassagewayDoor door) {
    if (door == null || door.IsOpen || passagewayLayer.value == 0) {
      return false;
    }

    if (!TryGetBackRayConfig(out Vector3 cameraForward, out Vector3 logicalLeftDir, out Vector3 logicalRightDir, out float lateralLength, out float backLength)) {
      return false;
    }

    Vector3 probeOrigin = transform.position;
    Vector3 leftTip = probeOrigin + logicalLeftDir * lateralLength;
    Vector3 rightTip = probeOrigin + logicalRightDir * lateralLength;

    return BackRayHitsDoor(leftTip, cameraForward, backLength, door) || BackRayHitsDoor(rightTip, cameraForward, backLength, door);
  }

  /// <summary>
  /// Reads the wall turner's camera-relative ray settings for door-front lock checks.
  /// </summary>
  /// <param name="cameraForward">The flattened camera forward direction.</param>
  /// <param name="logicalLeftDir">The logical left probe direction.</param>
  /// <param name="logicalRightDir">The logical right probe direction.</param>
  /// <param name="lateralLength">The side offset used before casting backward.</param>
  /// <param name="backLength">The backward ray length.</param>
  /// <returns>True when a valid ray configuration is available.</returns>
  private bool TryGetBackRayConfig(out Vector3 cameraForward, out Vector3 logicalLeftDir, out Vector3 logicalRightDir, out float lateralLength, out float backLength) {
    if (cameraPivot == null) {
      cameraForward = Vector3.zero;
      logicalLeftDir = Vector3.zero;
      logicalRightDir = Vector3.zero;
      lateralLength = 0f;
      backLength = 0f;
      return false;
    }

    Vector3 forward = cameraPivot.forward;
    forward.y = 0f;
    if (forward.sqrMagnitude < 0.0001f) {
      cameraForward = Vector3.zero;
      logicalLeftDir = Vector3.zero;
      logicalRightDir = Vector3.zero;
      lateralLength = 0f;
      backLength = 0f;
      return false;
    }

    cameraForward = forward.normalized;

    Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);
    if (cameraRight.sqrMagnitude < 0.0001f) {
      logicalLeftDir = Vector3.zero;
      logicalRightDir = Vector3.zero;
      lateralLength = 0f;
      backLength = 0f;
      return false;
    }

    cameraRight.Normalize();
    logicalLeftDir = cameraRight;
    logicalRightDir = -cameraRight;

    lateralLength = Mathf.Max(0.05f, lateralRayLength);
    backLength = Mathf.Max(0.05f, backRayLength);

    return true;
  }

  /// <summary>
  /// Casts one back ray and checks whether it hits the expected passageway door.
  /// </summary>
  /// <param name="origin">The ray origin.</param>
  /// <param name="direction">The ray direction.</param>
  /// <param name="distance">The ray distance.</param>
  /// <param name="targetDoor">The door that should be detected.</param>
  /// <returns>True when the ray hits the target door.</returns>
  private bool BackRayHitsDoor(Vector3 origin, Vector3 direction, float distance, PassagewayDoor targetDoor) {
    int hitCount = Physics.RaycastNonAlloc(origin, direction, backRayHitBuffer, distance, passagewayLayer, QueryTriggerInteraction.Ignore);

    for (int i = 0; i < hitCount; i++) {
      Collider hitCollider = backRayHitBuffer[i].collider;

      if (hitCollider == null) {
        continue;
      }

      PassagewayDoor hitDoor = hitCollider.GetComponentInParent<PassagewayDoor>();

      if (hitDoor == targetDoor) {
        return true;
      }
    }

    return false;
  }

  private void OnDrawGizmosSelected() {
    if (!showInteractionRangeGizmo) {
      return;
    }

    Gizmos.color = Color.cyan;
    Gizmos.DrawWireSphere(transform.position, interactionRange);
  }
}
