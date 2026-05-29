using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Manages detection UI feedback including vignette, screen flash, and directional indicator.
/// Communicates the player's stealth state with visual effects.
///
/// Responsibilities
/// ─────────────────
///  - Display black vignette when player is hidden (smooth fade in/out)
///  - Show screen flash effect when player is detected (fades over 0.5s)
///  - Maintain a directional indicator pointing to the closest detecting guard
///  - Hide all UI elements when player is not hidden and not detected
/// </summary>
public class DetectionIndicator : MonoBehaviour {
  [Header("References")]
  [Tooltip("Player stealth controller whose state is displayed by this indicator.")]
  public PlayerStealthController player;

  [Header("Vignette (Hidden State)")]
  [Tooltip("Image component for the black vignette effect. Leave empty to skip vignette.")]
  public Image vignetteImage;

  [Tooltip("Alpha value when vignette is fully visible (player hidden).")]
  [Range(0f, 1f)] public float vignetteAlpha = 0.6f;

  [Tooltip("Speed at which vignette fades in/out.")]
  public float vignetteFadeSpeed = 3f;

  [Header("Screen Flash (Detection Alert)")]
  [Tooltip("Image component for the screen flash effect. Leave empty to skip flash.")]
  public Image flashImage;

  [Tooltip("Color of the flash effect when detected.")]
  public Color flashColor = new Color(1f, 0f, 0f, 0.7f);

  [Tooltip("Duration over which the flash fades out.")]
  public float flashFadeDuration = 0.5f;

  [Header("Direction Indicator (Guard Position)")]
  [Tooltip("RectTransform of the indicator arrow pointing to detecting guard. Leave empty to skip.")]
  public RectTransform indicatorArrow;

  [Tooltip("Canvas RectTransform for screen-space calculations. Auto-fetched if left blank.")]
  public RectTransform canvasRect;

  [Tooltip("Offset from screen edge where the indicator appears (in screen pixels).")]
  public float edgeOffset = 50f;

  [Tooltip("Speed at which the indicator rotates toward the guard direction.")]
  public float rotationSmoothSpeed = 10f;

  [Tooltip("Outer radius of the circular indicator zone (0-1 means 0% to 100% of screen diagonal).")]
  public float indicatorRadius = 0.3f;

  // ─────────────────────────────────────────────────────────────────────────
  // Private state
  // ─────────────────────────────────────────────────────────────────────────

  private float _flashTimer = 0f;
  private float _currentVignetteAlpha = 0f;
  private float _targetVignetteAlpha = 0f;
  private GuardController _closestDetectingGuard = null;

  // ─────────────────────────────────────────────────────────────────────────
  // Unity lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  private void Awake() {
    ValidateReferences();
  }

  private void Update() {
    if (player == null) return;

    UpdateVignette();
    UpdateFlash();
    UpdateIndicator();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Reference validation
  // ─────────────────────────────────────────────────────────────────────────

  private void ValidateReferences() {
    if (canvasRect == null) {
      Canvas canvas = GetComponentInParent<Canvas>();
      if (canvas != null) {
        canvasRect = canvas.GetComponent<RectTransform>();
      } else {
        Debug.LogWarning($"[{nameof(DetectionIndicator)}] Canvas not found. Assign canvasRect manually.", this);
      }
    }

    if (vignetteImage != null) {
      var color = vignetteImage.color;
      color.a = 0f;
      vignetteImage.color = color;
      _currentVignetteAlpha = 0f;
    }

    if (flashImage != null) {
      var color = flashImage.color;
      color.a = 0f;
      flashImage.color = color;
    }

    if (indicatorArrow != null) {
      indicatorArrow.gameObject.SetActive(false);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Vignette (Hidden state)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Updates vignette visibility based on stealth state.
  /// Shows full vignette when hidden, fades out when exposed/detected.
  /// </summary>
  private void UpdateVignette() {
    if (vignetteImage == null) return;

    // Determine target alpha based on state
    _targetVignetteAlpha = player.CurrentState == PlayerStealthController.StealthState.Hidden
      ? vignetteAlpha
      : 0f;

    // Smoothly fade toward target alpha
    _currentVignetteAlpha = Mathf.Lerp(
      _currentVignetteAlpha,
      _targetVignetteAlpha,
      vignetteFadeSpeed * Time.deltaTime
    );

    // Apply to image
    var color = vignetteImage.color;
    color.a = _currentVignetteAlpha;
    vignetteImage.color = color;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Allert
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Updates effect when player is detected. Fades over time.
  /// Triggers full opacity when detection begins.
  /// </summary>
  private void UpdateFlash() {
    if (flashImage == null) return;

    // Trigger new flash when transitioning to Detected state
    if (player.CurrentState == PlayerStealthController.StealthState.Detected && _flashTimer <= 0f) {
      _flashTimer = flashFadeDuration;
    }

    // Countdown flash timer
    if (_flashTimer > 0f) {
      _flashTimer -= Time.deltaTime;
    }

    // Calculate flash alpha (linear fade)
    float flashAlpha = Mathf.Max(0f, _flashTimer / flashFadeDuration);
    var color = flashImage.color;
    color = flashColor;
    color.a *= flashAlpha;
    flashImage.color = color;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Direction indicator (Guard position pointer)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Updates the direction indicator to point toward the closest detecting guard.
  /// Only visible when the player is detected.
  /// </summary>
  private void UpdateIndicator() {
    if (indicatorArrow == null) return;

    // Find the closest detecting guard
    _closestDetectingGuard = FindClosestDetectingGuard();

    // Hide indicator if not detected or no guard found
    if (player.CurrentState != PlayerStealthController.StealthState.Detected || _closestDetectingGuard == null) {
      indicatorArrow.gameObject.SetActive(false);
      return;
    }

    // Show and position indicator
    indicatorArrow.gameObject.SetActive(true);
    PositionAndRotateIndicator();
  }

  /// <summary>
  /// Finds the closest guard currently detecting the player.
  /// Searches all GuardController instances in the scene.
  /// </summary>
  private GuardController FindClosestDetectingGuard() {
    GuardController closest = null;
    float closestDistance = float.MaxValue;

    GuardController[] guards = FindObjectsOfType<GuardController>();
    foreach (GuardController guard in guards) {
      // Check if this guard is detecting the player
      if (guard.visionCone != null && guard.visionCone.PlayerDetected && guard.visionCone.DetectedPlayer == player) {
        float distance = Vector3.Distance(player.transform.position, guard.transform.position);
        if (distance < closestDistance) {
          closest = guard;
          closestDistance = distance;
        }
      }
    }

    return closest;
  }

  /// <summary>
  /// Positions the indicator at the screen edge and rotates it to point toward the guard.
  /// </summary>
  private void PositionAndRotateIndicator() {
    if (canvasRect == null || _closestDetectingGuard == null) return;

    // Get world positions
    Vector3 playerPos = player.transform.position;
    Vector3 guardPos = _closestDetectingGuard.transform.position;

    // Calculate direction in world space
    Vector3 directionToGuard = (guardPos - playerPos).normalized;

    // Calculate rotation angle (Y rotation only, for 2D arrow)
    float angle = Mathf.Atan2(directionToGuard.x, directionToGuard.z) * Mathf.Rad2Deg;

    // Smoothly rotate toward the guard
    float currentRotation = indicatorArrow.eulerAngles.z;
    float targetRotation = -angle;

    // Normalize angles to avoid 360-degree spinning
    while (targetRotation - currentRotation > 180f) targetRotation -= 360f;
    while (targetRotation - currentRotation < -180f) targetRotation += 360f;

    float newRotation = Mathf.Lerp(currentRotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);
    indicatorArrow.eulerAngles = new Vector3(0f, 0f, newRotation);

    // Position at screen edge in the direction of the guard
    PositionIndicatorAtScreenEdge(directionToGuard);
  }

  /// <summary>
  /// Places the indicator at the screen edge, pointing in the given world direction.
  /// </summary>
  private void PositionIndicatorAtScreenEdge(Vector3 worldDirection) {
    // Convert world direction to screen direction
    Vector2 screenDirection = new Vector2(worldDirection.x, worldDirection.z).normalized;

    // Calculate position on screen edge
    Vector2 screenCenter = canvasRect.rect.size * 0.5f;
    float screenDiagonal = new Vector2(canvasRect.rect.width, canvasRect.rect.height).magnitude;
    float radius = screenDiagonal * indicatorRadius;

    Vector2 indicatorScreenPos = screenCenter + screenDirection * radius;

    // Convert to local position on canvas
    indicatorArrow.localPosition = new Vector3(indicatorScreenPos.x, indicatorScreenPos.y, 0f);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Debug
  // ─────────────────────────────────────────────────────────────────────────

  private void OnDrawGizmosSelected() {
#if UNITY_EDITOR
    if (player == null || _closestDetectingGuard == null) return;

    Gizmos.color = Color.red;
    Gizmos.DrawLine(player.transform.position, _closestDetectingGuard.transform.position);

    Gizmos.color = Color.yellow;
    Gizmos.DrawSphere(_closestDetectingGuard.transform.position, 0.2f);
#endif
  }
}
