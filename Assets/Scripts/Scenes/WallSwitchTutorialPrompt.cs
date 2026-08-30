using UnityEngine;

/// <summary>
/// Drives the first wall-switch lesson from the real wall-switch state machine. The prompt changes
/// only after aim mode has actually started, survives an invalid click, returns to the first step
/// if aiming is cancelled, and completes only after a valid switch.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WallSwitchTutorialPrompt : MonoBehaviour {
  [Header("Wall switch")]
  [SerializeField] private WallSwitchController wallSwitch;
  [SerializeField] private bool enableWallSwitchOnEnter = true;

  [Header("Messages")]
  [SerializeField] private string readyMessage = "Press [Space] to switch walls.";
  [SerializeField] private string aimingMessage = "Aim at the opposite wall and click.";

  [Header("Suggested target")]
  [Tooltip("Optional visual hint. It suggests one valid point but does not constrain selection to it.")]
  [SerializeField] private Transform targetMarker;
  [SerializeField, Min(0f)] private float pulseAmplitude = 0.14f;
  [SerializeField, Min(0f)] private float pulseSpeed = 2.2f;
  [Tooltip("Keeps this scene's suggested point horizontally aligned with the player when aim starts.")]
  [SerializeField] private bool alignMarkerWorldZWithPlayer;
  [SerializeField] private Vector2 markerWorldZLimits = new(float.NegativeInfinity, float.PositiveInfinity);

  private bool introduced;
  private bool tutorialSwitchInProgress;
  private bool completed;
  private Vector3 markerBaseScale = Vector3.one;
  private Camera markerCamera;

  private void Awake() {
    Collider trigger = GetComponent<Collider>();
    trigger.isTrigger = true;
    if (targetMarker != null) {
      markerBaseScale = targetMarker.localScale;
      targetMarker.gameObject.SetActive(false);
    }
  }

  private void Start() {
    ResolveWallSwitch();
    Subscribe();
  }

  private void OnDestroy() {
    Unsubscribe();
    ClearTutorialInformation();
  }

  private void Update() {
    if (targetMarker == null || !targetMarker.gameObject.activeSelf) return;

    float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmplitude;
    targetMarker.localScale = markerBaseScale * pulse;

    FaceMarkerToCamera();
  }

  private void OnTriggerEnter(Collider other) {
    if (completed || !other.CompareTag("Player")) return;

    introduced = true;
    if (wallSwitch == null) {
      wallSwitch = other.GetComponent<WallSwitchController>();
      Subscribe();
    }

    if (wallSwitch != null && enableWallSwitchOnEnter) wallSwitch.enabled = true;
    ShowReadyMessage();
  }

  private void ResolveWallSwitch() {
    if (wallSwitch != null) return;
    GameObject player = GameObject.FindWithTag("Player");
    if (player != null) wallSwitch = player.GetComponent<WallSwitchController>();
  }

  private void Subscribe() {
    if (wallSwitch == null) return;
    Unsubscribe();
    wallSwitch.AimStarted += HandleAimStarted;
    wallSwitch.AimCancelled += HandleAimCancelled;
    wallSwitch.SwitchStarted += HandleSwitchStarted;
    wallSwitch.SwitchCompleted += HandleSwitchCompleted;
  }

  private void Unsubscribe() {
    if (wallSwitch == null) return;
    wallSwitch.AimStarted -= HandleAimStarted;
    wallSwitch.AimCancelled -= HandleAimCancelled;
    wallSwitch.SwitchStarted -= HandleSwitchStarted;
    wallSwitch.SwitchCompleted -= HandleSwitchCompleted;
  }

  private void HandleAimStarted() {
    if (completed || !introduced) return;
    DialogueHUD.Instance?.ShowInformation(aimingMessage);
    if (targetMarker != null) {
      if (alignMarkerWorldZWithPlayer && wallSwitch != null) {
        Vector3 position = targetMarker.position;
        position.z = Mathf.Clamp(wallSwitch.transform.position.z, markerWorldZLimits.x, markerWorldZLimits.y);
        targetMarker.position = position;
      }
      FaceMarkerToCamera();
      targetMarker.gameObject.SetActive(true);
    }
  }

  private void HandleAimCancelled() {
    if (completed || !introduced) return;
    HideMarker();
    ShowReadyMessage();
  }

  private void HandleSwitchStarted() {
    if (completed || !introduced) return;
    tutorialSwitchInProgress = true;
    HideMarker();
    ClearTutorialInformation();
  }

  private void HandleSwitchCompleted() {
    if (!tutorialSwitchInProgress) return;
    tutorialSwitchInProgress = false;
    completed = true;
    HideMarker();
    ClearTutorialInformation();
  }

  private void ShowReadyMessage() {
    DialogueHUD.Instance?.ShowInformation(readyMessage);
  }

  private void HideMarker() {
    if (targetMarker == null) return;
    targetMarker.localScale = markerBaseScale;
    targetMarker.gameObject.SetActive(false);
  }

  private void FaceMarkerToCamera() {
    if (targetMarker == null) return;
    if (markerCamera == null) markerCamera = Camera.main;
    if (markerCamera == null) return;
    Vector3 towardCamera = markerCamera.transform.position - targetMarker.position;
    if (towardCamera.sqrMagnitude > 0.0001f)
      targetMarker.rotation = Quaternion.LookRotation(towardCamera.normalized, Vector3.up);
  }

  private void ClearTutorialInformation() {
    if (DialogueHUD.Instance == null) return;
    DialogueHUD.Instance.ClearInformationIfMatches(readyMessage);
    DialogueHUD.Instance.ClearInformationIfMatches(aimingMessage);
  }
}
