using UnityEngine;

public static class OneShotVfx {
  public static void PlayAtPoint(ParticleSystem prefab, Vector3 position) {
    if (prefab == null) {
      Debug.LogWarning("[OneShotVfx] PlayAtPoint called with a null prefab — nothing to spawn.");
      return;
    }

    ParticleSystem instance = Object.Instantiate(prefab, position, Quaternion.identity);
    instance.gameObject.SetActive(true);
    instance.Clear(true);
    instance.Play(true);

    ParticleSystem.MainModule main = instance.main;
    float lifetime = Mathf.Max(GetMaxLifetime(main.startLifetime), 0.1f);
    float destroyDelay = main.duration + lifetime + 0.5f;

    Object.Destroy(instance.gameObject, destroyDelay);
  }

  /// <summary>
  /// Reads the worst-case value out of a MinMaxCurve regardless of which curve mode it's set to —
  /// .constant/.constantMax are only meaningful for Constant/TwoConstants mode, and silently read
  /// as 0 otherwise, which was collapsing destroyDelay to ~0 and killing the instance before it
  /// ever got a visible frame.
  /// </summary>
  private static float GetMaxLifetime(ParticleSystem.MinMaxCurve curve) {
    switch (curve.mode) {
      case ParticleSystemCurveMode.TwoConstants:
        return curve.constantMax;
      case ParticleSystemCurveMode.Curve:
      case ParticleSystemCurveMode.TwoCurves:
        return curve.curveMultiplier;
      default:
        return curve.constant;
    }
  }
}
