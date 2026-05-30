using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages vignette effect to communicate player stealth state.
///
/// Responsibilities
/// ─────────────────
///  - Display black vignette when player is hidden (smooth fade in/out)
///  - Hide vignette when player is exposed or detected
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

  // ─────────────────────────────────────────────────────────────────────────
  // Private state
  // ─────────────────────────────────────────────────────────────────────────

  private float _currentVignetteAlpha = 0f;
  private float _targetVignetteAlpha = 0f;

  // ─────────────────────────────────────────────────────────────────────────
  // Unity lifecycle
  // ─────────────────────────────────────────────────────────────────────────

  private void Awake() {
    ValidateReferences();
  }

  private void Update() {
    if (player == null) return;
    UpdateVignette();
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Reference validation
  // ─────────────────────────────────────────────────────────────────────────

  private void ValidateReferences() {
    if (vignetteImage != null) {
      var color = vignetteImage.color;
      color.a = 0f;
      vignetteImage.color = color;
      _currentVignetteAlpha = 0f;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Vignette
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
}
