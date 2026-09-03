using UnityEngine;

/// <summary>
/// Drives the first distraction lesson from the real aiming state. The second instruction appears
/// only after aim mode starts, and the lesson completes only after a valid throw.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DistractionTutorialPrompt : MonoBehaviour {
  [Header("Distraction")]
  [SerializeField] private DistractionController distraction;
  [SerializeField] private bool enableDistractionOnEnter = true;

  [Header("Messages")]
  [SerializeField] private string readyMessage = "Aim a distraction with [Right Mouse].";
  [SerializeField] private string aimingMessage = "Choose a landing point, then [Left Mouse] to throw.";

  private bool introduced;
  private bool completed;

  private void Awake() {
    GetComponent<Collider>().isTrigger = true;
  }

  private void Start() {
    ResolveDistraction();
    Subscribe();
  }

  private void OnDestroy() {
    Unsubscribe();
    ClearTutorialInformation();
  }

  private void OnTriggerEnter(Collider other) {
    if (completed || !other.CompareTag("Player")) return;

    introduced = true;
    if (distraction == null) {
      distraction = other.GetComponent<DistractionController>();
      Subscribe();
    }

    if (distraction != null && enableDistractionOnEnter) distraction.enabled = true;
    ShowReadyMessage();
  }

  private void ResolveDistraction() {
    if (distraction != null) return;
    GameObject player = GameObject.FindWithTag("Player");
    if (player != null) distraction = player.GetComponent<DistractionController>();
  }

  private void Subscribe() {
    if (distraction == null) return;
    Unsubscribe();
    distraction.AimStarted += HandleAimStarted;
    distraction.AimCancelled += HandleAimCancelled;
    distraction.DistractionThrown += HandleDistractionThrown;
  }

  private void Unsubscribe() {
    if (distraction == null) return;
    distraction.AimStarted -= HandleAimStarted;
    distraction.AimCancelled -= HandleAimCancelled;
    distraction.DistractionThrown -= HandleDistractionThrown;
  }

  private void HandleAimStarted() {
    if (completed || !introduced) return;
    DialogueHUD.Instance?.ShowInformation(aimingMessage);
  }

  private void HandleAimCancelled() {
    if (completed || !introduced) return;
    ShowReadyMessage();
  }

  private void HandleDistractionThrown() {
    if (completed || !introduced) return;
    completed = true;
    ClearTutorialInformation();
  }

  private void ShowReadyMessage() {
    DialogueHUD.Instance?.ShowInformation(readyMessage);
  }

  private void ClearTutorialInformation() {
    if (DialogueHUD.Instance == null) return;
    DialogueHUD.Instance.ClearInformationIfMatches(readyMessage);
    DialogueHUD.Instance.ClearInformationIfMatches(aimingMessage);
  }
}
