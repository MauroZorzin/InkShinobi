using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Play Mode tests — Detection Indicator (Vignette system)
// Run via: Window > General > Test Runner > Play Mode > Run All
public class DetectionIndicatorTests {
  const int PlayerLayer = 3;

  // ─────────────────────────────────────────────────────────────────────────
  // Helpers
  // ─────────────────────────────────────────────────────────────────────────

  static PlayerStealthController MakePlayer(Vector3 pos = default) {
    var go = new GameObject("TestPlayer");
    go.layer = PlayerLayer;
    go.transform.position = pos;
    go.AddComponent<CapsuleCollider>();

    var p = go.AddComponent<PlayerStealthController>();
    var takedown = go.AddComponent<TakedownController>();
    p.takedownController = takedown;

    return p;
  }

  static (DetectionIndicator indicator, Image vignetteImage, GameObject go) MakeDetectionIndicator(PlayerStealthController player) {
    var go = new GameObject("TestDetectionIndicator");
    var indicator = go.AddComponent<DetectionIndicator>();

    // Create vignette image
    var vignetteGO = new GameObject("VignetteImage");
    vignetteGO.transform.SetParent(go.transform);
    var vignetteImage = vignetteGO.AddComponent<Image>();
    vignetteImage.color = new Color(0.1f, 0.1f, 0.1f, 0f); // Start transparent

    indicator.player = player;
    indicator.vignetteImage = vignetteImage;
    indicator.vignetteAlpha = 0.6f;
    indicator.vignetteFadeSpeed = 10f; // Fast for testing

    return (indicator, vignetteImage, go);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Test 1: Vignette shows when player is hidden
  // ─────────────────────────────────────────────────────────────────────────

  [UnityTest]
  public IEnumerator DetectionIndicator_VignetteFadesIn_WhenPlayerHidden() {
    var player = MakePlayer();
    var (indicator, vignetteImage, indicatorGO) = MakeDetectionIndicator(player);

    // Player starts hidden
    Assert.IsTrue(player.IsHidden, "Player should start in Hidden state.");


    // Wait for vignette to fade in
    yield return new WaitForSeconds(0.2f);

    // Vignette should now be visible (close to vignetteAlpha value)
    float expectedAlpha = indicator.vignetteAlpha;
    Assert.Greater(vignetteImage.color.a, 0.5f,
        $"Vignette alpha should be near {expectedAlpha} when player is hidden.");

    Object.Destroy(player.gameObject);
    Object.Destroy(indicatorGO);
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Test 2: Vignette fades out when player is exposed
  // ─────────────────────────────────────────────────────────────────────────

  [UnityTest]
  public IEnumerator DetectionIndicator_VignetteFadesOut_WhenPlayerExposed() {
    var player = MakePlayer();
    var (indicator, vignetteImage, indicatorGO) = MakeDetectionIndicator(player);

    // Player starts hidden
    Assert.IsTrue(player.IsHidden, "Player should start in Hidden state.");

    // Wait for vignette to fade in
    yield return new WaitForSeconds(0.2f);
    float vignetteAlphaWhenHidden = vignetteImage.color.a;
    Assert.Greater(vignetteAlphaWhenHidden, 0.5f, "Vignette should be visible when hidden.");

    // Simulate player entering light (becoming exposed)
    var zone = new GameObject("TestLightZone");
    var lightZone = zone.AddComponent<LightZone>();
    player.EnterLight(lightZone);

    yield return null; // Let state update
    Assert.AreEqual(player.CurrentState, PlayerStealthController.StealthState.Exposed,
        "Player should be in Exposed state after entering light.");

    // Wait for vignette to fade out
    yield return new WaitForSeconds(0.2f);

    // Vignette should now be transparent (close to 0)
    Assert.Less(vignetteImage.color.a, 0.1f,
        "Vignette should be transparent when player is exposed.");

    Object.Destroy(player.gameObject);
    Object.Destroy(indicatorGO);
    Object.Destroy(zone);
  }
}
