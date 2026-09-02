using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tracks the closest nearby IInteractable every frame, dispatches interaction input to it, and drives
/// the interaction-prompt slot on the shared DialogueHUD. Reach is defined by player-oriented boxes
/// that begin at the player's feet. DialogueHUD decides whether the prompt is actually visible; an
/// active dialogue message takes priority over it.
/// </summary>
public class PlayerInteractor : MonoBehaviour {
  private const int InitialHitBufferSize = 16;
  private const int MaximumHitBufferSize = 256;

  private enum InteractionCategory { Default, Pickup, Door, HidingSpot }

  [System.Serializable]
  private sealed class InteractionReach {
    [Min(0f)] public float left = 0.5f;
    [Min(0f)] public float right = 0.5f;
    [Min(0f)] public float forward = 0.5f;
    [Min(0f)] public float backward = 0.5f;
    [Min(0f)] public float height = 1.5f;

    public void Clamp() {
      left = Mathf.Max(0f, left);
      right = Mathf.Max(0f, right);
      forward = Mathf.Max(0f, forward);
      backward = Mathf.Max(0f, backward);
      height = Mathf.Max(0f, height);
    }
  }

  [System.Serializable]
  private sealed class CategoryReachOverride {
    public InteractionCategory category;
    [Tooltip("Interactable layers belonging to this category.")]
    public LayerMask layers;
    public InteractionReach reach = new();
  }

  [System.Serializable]
  private sealed class LayerPrompt {
    public LayerMask layer;
    public string text = "Interagisci";
  }

  private readonly struct CandidateHit {
    public readonly Collider Collider;
    public readonly float Distance;

    public CandidateHit(Collider collider, float distance) {
      Collider = collider;
      Distance = distance;
    }
  }

  [Header("Inventory")]
  [Tooltip("Inventory used when interacting with nearby pickable objects.")]
  [SerializeField] private PlayerInventory inventory;

  [Tooltip("Shared camera feedback used when interaction is pressed without a usable target.")]
  [SerializeField] private RejectedAimCameraFeedback rejectionFeedback;

  [Tooltip("Camera whose player-facing horizontal axis defines forward reach. Leave empty to use the player's child camera or Camera.main.")]
  [SerializeField] private Camera interactionCamera;

  [Header("Interaction Reach")]
  [Tooltip("Fallback reach for interactable layers not assigned to a category override.")]
  [SerializeField] private InteractionReach defaultReach = new();

  [Tooltip("Optional reach overrides and their associated interaction-category layers.")]
  [SerializeField] private CategoryReachOverride[] categoryOverrides = System.Array.Empty<CategoryReachOverride>();

  [Tooltip("Only these solid layers can obstruct an otherwise valid interaction. Wall is enabled by default.")]
  [SerializeField] private LayerMask interactionObstructionLayers = 1 << 8;

  [Header("Prompts")]
  [Tooltip("Which layers count as interactable, and what fallback prompt text to show for each.")]
  [SerializeField] private LayerPrompt[] layerPrompts = System.Array.Empty<LayerPrompt>();

  public bool interactionSuppressed;

  /// <summary>The midpoint of the default interaction volume.</summary>
  public Vector3 InteractionOrigin => FeetPosition + Vector3.up * (ReachOrDefault(defaultReach).height * 0.5f);

  private Collider[] _hitBuffer = new Collider[InitialHitBufferSize];
  private readonly Dictionary<IInteractable, CandidateHit> _candidateHits = new();
  private IInteractable _currentTarget;
  private IInteractionFocus _currentFocus;
  private LineFollowController _movement;

  private Vector3 FeetPosition => _movement != null ? _movement.FeetPosition : transform.position;

  private void Awake() {
    EnsureValidSettings();
    _movement = GetComponent<LineFollowController>();
    if (rejectionFeedback == null)
      rejectionFeedback = GetComponentInChildren<RejectedAimCameraFeedback>(true);
    ResolveInteractionCamera();
  }

  private void Update() {
    Collider hit = FindNearest(out IInteractable interactable);

    IInteractionFocus nextFocus = interactable as IInteractionFocus;
    if (!ReferenceEquals(_currentFocus, nextFocus)) {
      SetFocusStateIfAlive(_currentFocus, false);
      _currentFocus = nextFocus;
    }

    _currentTarget = interactable;
    SetFocusStateIfAlive(_currentFocus, true);
    UpdatePrompt(hit, interactable);
  }

  public void OnInteract(InputValue value) {
    if (!value.isPressed || interactionSuppressed) return;

    // Input callbacks may run before Update, and nearby items may have spawned or moved since the
    // cached search. The input-time query is authoritative for what can be interacted with now.
    FindNearest(out _currentTarget);
    if (!IsUnityInterfaceAlive(_currentTarget)) {
      _currentTarget = null;
      if (!SceneTransitionManager.IsGamePaused && !SceneTransitionManager.IsDeathSequenceActive)
        rejectionFeedback?.PlayRejectedAction();
      return;
    }
    _currentTarget.Interact(inventory);
  }

  /// <summary>Searches the configured interaction boxes for the highest-priority nearest target.</summary>
  private Collider FindNearest(out IInteractable interactable) {
    interactable = null;
    _candidateHits.Clear();

    int remainingLayers = InteractableMask();
    for (int i = 0; i < categoryOverrides.Length; i++) {
      CategoryReachOverride category = categoryOverrides[i];
      if (category == null) continue;

      // First entry wins when category masks accidentally overlap, keeping every collider in one
      // well-defined reach profile and preventing duplicate physics work.
      int categoryLayers = category.layers.value & remainingLayers;
      if (categoryLayers == 0) continue;

      CollectCategoryHits(categoryLayers, ReachOrDefault(category.reach));
      remainingLayers &= ~categoryLayers;
    }

    if (remainingLayers != 0)
      CollectCategoryHits(remainingLayers, ReachOrDefault(defaultReach));

    Collider closestCollider = null;
    float closestDistance = float.MaxValue;
    int closestPriority = int.MinValue;

    foreach (KeyValuePair<IInteractable, CandidateHit> entry in _candidateHits) {
      IInteractable candidate = entry.Key;
      CandidateHit hit = entry.Value;
      int priority = candidate is IInteractionPriority customPriority ? customPriority.Priority : 0;
      if (priority < closestPriority || (priority == closestPriority && hit.Distance >= closestDistance))
        continue;

      closestDistance = hit.Distance;
      closestPriority = priority;
      closestCollider = hit.Collider;
      interactable = candidate;
    }

    return closestCollider;
  }

  private void CollectCategoryHits(int layers, InteractionReach reach) {
    GetBox(reach, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation);
    if (halfExtents.x <= 0f || halfExtents.y <= 0f || halfExtents.z <= 0f) return;

    int hitCount;
    do {
      hitCount = Physics.OverlapBoxNonAlloc(
        center,
        halfExtents,
        _hitBuffer,
        orientation,
        layers,
        QueryTriggerInteraction.Collide);

      if (hitCount < _hitBuffer.Length || _hitBuffer.Length >= MaximumHitBufferSize) break;
      int expandedSize = Mathf.Min(_hitBuffer.Length * 2, MaximumHitBufferSize);
      System.Array.Resize(ref _hitBuffer, expandedSize);
    } while (true);

    Vector3 obstructionOrigin = FeetPosition + Vector3.up * (reach.height * 0.5f);
    for (int i = 0; i < hitCount; i++) {
      Collider hit = _hitBuffer[i];
      if (hit == null) continue;

      IInteractable candidate = hit.GetComponentInParent<IInteractable>();
      if (!IsUnityInterfaceAlive(candidate)) continue;

      Vector3 closestPoint = hit.ClosestPoint(obstructionOrigin);
      if (IsWallObstructed(obstructionOrigin, closestPoint)) continue;

      float distance = Vector3.Distance(obstructionOrigin, closestPoint);
      if (_candidateHits.TryGetValue(candidate, out CandidateHit previous) && previous.Distance <= distance)
        continue;

      _candidateHits[candidate] = new CandidateHit(hit, distance);
    }
  }

  private bool IsWallObstructed(Vector3 origin, Vector3 target) {
    if (interactionObstructionLayers.value == 0 || (target - origin).sqrMagnitude <= 0.000001f)
      return false;

    return Physics.Linecast(
      origin,
      target,
      interactionObstructionLayers,
      QueryTriggerInteraction.Ignore);
  }

  private void GetBox(
    InteractionReach reach,
    out Vector3 center,
    out Vector3 halfExtents,
    out Quaternion orientation) {
    float width = reach.left + reach.right;
    float depth = reach.forward + reach.backward;
    halfExtents = new Vector3(width * 0.5f, reach.height * 0.5f, depth * 0.5f);

    ResolveInteractionCamera();
    Vector3 planarForward = interactionCamera != null
      ? Vector3.ProjectOnPlane(interactionCamera.transform.position - FeetPosition, Vector3.up)
      : Vector3.ProjectOnPlane(transform.forward, Vector3.up);
    if (planarForward.sqrMagnitude <= 0.000001f) planarForward = Vector3.forward;
    orientation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
    Vector3 planarRight = orientation * Vector3.right;
    Vector3 forward = orientation * Vector3.forward;
    center = FeetPosition
      + Vector3.up * halfExtents.y
      + planarRight * ((reach.right - reach.left) * 0.5f)
      + forward * ((reach.forward - reach.backward) * 0.5f);
  }

  private int InteractableMask() {
    int mask = 0;
    foreach (LayerPrompt entry in layerPrompts) {
      if (entry != null) mask |= entry.layer.value;
    }
    return mask;
  }

  private static InteractionReach ReachOrDefault(InteractionReach reach) {
    return reach ?? new InteractionReach();
  }

  private void UpdatePrompt(Collider target, IInteractable interactable) {
    if (DialogueHUD.Instance == null) return;

    string text = null;
    if (interactable != null) {
      text = (interactable as IInteractionPrompt)?.GetPromptText(inventory);
      if (string.IsNullOrEmpty(text) && target != null)
        text = TextForLayer(target.gameObject.layer);
    }

    if (string.IsNullOrEmpty(text)) DialogueHUD.Instance.ClearInteractionPrompt();
    else DialogueHUD.Instance.ShowInteractionPrompt(text);
  }

  private void OnDisable() {
    SetFocusStateIfAlive(_currentFocus, false);
    _currentFocus = null;
    _currentTarget = null;
    _candidateHits.Clear();
    DialogueHUD.Instance?.ClearInteractionPrompt();
  }

  private void SetFocusStateIfAlive(IInteractionFocus focus, bool focused) {
    if (IsUnityInterfaceAlive(focus)) focus.SetInteractionFocused(focused, inventory);
  }

  private static bool IsUnityInterfaceAlive(object value) {
    if (value == null) return false;
    return value is not Object unityObject || unityObject != null;
  }

  private string TextForLayer(int layer) {
    foreach (LayerPrompt entry in layerPrompts) {
      if (entry != null && (entry.layer.value & (1 << layer)) != 0)
        return entry.text;
    }
    return null;
  }

  private void OnValidate() {
    EnsureValidSettings();
  }

  private void EnsureValidSettings() {
    defaultReach ??= new InteractionReach();
    defaultReach.Clamp();
    categoryOverrides ??= System.Array.Empty<CategoryReachOverride>();
    layerPrompts ??= System.Array.Empty<LayerPrompt>();
    for (int i = 0; i < categoryOverrides.Length; i++) {
      CategoryReachOverride category = categoryOverrides[i];
      if (category?.reach != null) category.reach.Clamp();
    }
  }

  private void ResolveInteractionCamera() {
    if (interactionCamera != null) return;
    interactionCamera = GetComponentInChildren<Camera>(true);
    if (interactionCamera == null) interactionCamera = Camera.main;
  }

  private void OnDrawGizmosSelected() {
    EnsureValidSettings();
    LineFollowController previousMovement = _movement;
    if (_movement == null) _movement = GetComponent<LineFollowController>();

    DrawReachGizmo(ReachOrDefault(defaultReach), new Color(1f, 1f, 1f, 0.9f));
    for (int i = 0; i < categoryOverrides.Length; i++) {
      CategoryReachOverride category = categoryOverrides[i];
      if (category == null) continue;
      DrawReachGizmo(ReachOrDefault(category.reach), CategoryColor(category.category));
    }

    _movement = previousMovement;
  }

  private void DrawReachGizmo(InteractionReach reach, Color color) {
    GetBox(reach, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation);
    Matrix4x4 previousMatrix = Gizmos.matrix;
    Color previousColor = Gizmos.color;
    Gizmos.matrix = Matrix4x4.TRS(center, orientation, Vector3.one);
    Gizmos.color = color;
    Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
    Gizmos.matrix = previousMatrix;
    Gizmos.color = previousColor;
  }

  private static Color CategoryColor(InteractionCategory category) {
    return category switch {
      InteractionCategory.Pickup => new Color(0.2f, 0.85f, 1f, 0.9f),
      InteractionCategory.Door => new Color(1f, 0.75f, 0.15f, 0.9f),
      InteractionCategory.HidingSpot => new Color(0.35f, 1f, 0.35f, 0.9f),
      _ => new Color(1f, 1f, 1f, 0.9f)
    };
  }
}
