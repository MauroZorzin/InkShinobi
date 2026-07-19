using UnityEngine;

/// <summary>
/// Simple, self-contained screen-shake utility using Perlin noise for smooth,
/// non-jittery shake. Attach automatically by Camera2_5DController, or add manually
/// and call TriggerShake() from anywhere (e.g. on hit, on landing, on explosion).
/// </summary>
public class CameraShake : MonoBehaviour
{
    public Vector3 CurrentOffset { get; private set; }

    private float amplitude;
    private float duration;
    private float frequency;
    private float timer;
    private float seedX, seedY, seedZ;

    private void Awake()
    {
        seedX = Random.Range(0f, 100f);
        seedY = Random.Range(0f, 100f);
        seedZ = Random.Range(0f, 100f);
    }

    /// <summary>Start (or restart, if stronger) a shake impulse.</summary>
    public void TriggerShake(float shakeAmplitude, float shakeDuration, float shakeFrequency = 25f)
    {
        // Only override an in-progress shake if the new one is stronger, to avoid weak shakes cutting off strong ones.
        if (timer <= 0f || shakeAmplitude >= amplitude)
        {
            amplitude = shakeAmplitude;
            duration = shakeDuration;
            frequency = shakeFrequency;
            timer = shakeDuration;
        }
    }

    private void Update()
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            float falloff = Mathf.Clamp01(timer / duration);
            float t = Time.time * frequency;

            float x = (Mathf.PerlinNoise(seedX, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(seedY, t) - 0.5f) * 2f;
            float z = (Mathf.PerlinNoise(seedZ, t) - 0.5f) * 2f;

            CurrentOffset = new Vector3(x, y, z) * amplitude * falloff;
        }
        else
        {
            CurrentOffset = Vector3.zero;
        }
    }
}
