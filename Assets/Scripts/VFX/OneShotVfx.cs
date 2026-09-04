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

  // Legge .constant o .constantMax in base alla modalità della curva, leggere quello sbagliato restituisce silenziosamente 0 e uccide l'istanza troppo presto.
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
