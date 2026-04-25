using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional HUD that displays the player's stealth state.
/// Attach to a Canvas UI GameObject.
/// </summary>
public class StealthHUD : MonoBehaviour {
  [Header("References")]
  public PlayerStealthController player;

  [Header("UI Elements")]
  public Text statusText;      // UnityEngine.UI.Text
  public Image statusIcon;     // optional
  public Image lightIndicator; // optional

  [Header("Colors")]
  public Color hiddenColor = new Color(0.2f, 0.8f, 0.2f);
  public Color spottedColor = new Color(0.9f, 0.15f, 0.15f);
  public Color inLightColor = new Color(1f, 0.85f, 0.2f);
  public Color inShadowColor = new Color(0.3f, 0.3f, 0.5f);

  // ── Unity Messages ────────────────────────────────────────────────────────
  private void Update() {
    if (player == null) return;

    // ── Status text ───────────────────────────────────────────────────────
    if (statusText != null) {
      if (player.DetectingGuardCount > 0) {
        statusText.text = "! SPOTTED !";
        statusText.color = spottedColor;
      } else if (player.IsHidden) {
        statusText.text = "Hidden";
        statusText.color = hiddenColor;
      } else {
        statusText.text = "Hiding...";
        statusText.color = Color.Lerp(spottedColor, hiddenColor, 0.5f);
      }
    }

    // ── Icon color ────────────────────────────────────────────────────────
    if (statusIcon != null)
      statusIcon.color = player.IsHidden ? hiddenColor : spottedColor;

    // ── Light indicator ───────────────────────────────────────────────────
    if (lightIndicator != null)
      lightIndicator.color = player.IsInLight ? inLightColor : inShadowColor;
  }
}
