using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the player's stealth state: visibility, light exposure, and takedown capability.
/// Attach to the Player GameObject.
/// Requires a PlayerInput component with a "Takedown" action in your Input Action Asset.
/// </summary>
public class PlayerStealthController : MonoBehaviour
{
  [Header("Stealth Settings")]
  [Tooltip("How long the player must stay still/hidden before becoming fully hidden")]
  public float timeToHide = 1.0f;


  [Header("Debug")]
  public bool showDebugGizmos = true;
  [Tooltip("Logs detailed takedown check info every time you press the takedown button")]
  public bool verboseLogging = true;

  // ── Public read-only state ──────────────────────────────────────────────
  public bool IsHidden { get; private set; } = true;
  public bool IsInLight { get; private set; } = false;
  public int DetectingGuardCount { get; private set; } = 0;

  // ── Private ─────────────────────────────────────────────────────────────
  private float _hiddenTimer = 0f;
  private LightZone _currentLightZone;

  // ── Unity Messages ───────────────────────────────────────────────────────
  private void Update() => UpdateHiddenState();


  // ── Stealth Logic ────────────────────────────────────────────────────────
  private void UpdateHiddenState()
  {
    if (DetectingGuardCount > 0)
    {
      IsHidden = false;
      _hiddenTimer = 0f;
    }
    else
    {
      _hiddenTimer += Time.deltaTime;
      if (_hiddenTimer >= timeToHide)
        IsHidden = true;
    }
  }

  public void OnGuardStartsDetecting()
  {
    DetectingGuardCount++;
    IsHidden = false;
    _hiddenTimer = 0f;
  }

  public void OnGuardStopsDetecting()
  {
    DetectingGuardCount = Mathf.Max(0, DetectingGuardCount - 1);
  }

  // ── Light Zone ───────────────────────────────────────────────────────────
  public void EnterLight(LightZone zone)
  {
    _currentLightZone = zone;
    IsInLight = true;
  }

  public void ExitLight(LightZone zone)
  {
    if (_currentLightZone == zone)
    {
      _currentLightZone = null;
      IsInLight = false;
    }
  }

}