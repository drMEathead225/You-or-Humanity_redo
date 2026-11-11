using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One component that sets the whole mood: dark fog, night ambient light, moonlight, and optional lightning.
/// The fog gets thicker every wave, so the horde emerges from the dark and distant pop-in stays hidden.
/// Add to any empty GameObject. Everything is applied from code, so there is nothing else to configure.
/// </summary>
public class AtmosphereController : MonoBehaviour
{
    [Header("Fog")]
    [SerializeField] private Color fogColor = new Color(0.05f, 0.07f, 0.08f);
    [Tooltip("Fog thickness at wave 1. Higher = shorter view distance.")]
    [SerializeField] private float startDensity = 0.03f;
    [Tooltip("Fog thickness once the final fog wave is reached.")]
    [SerializeField] private float maxDensity = 0.07f;
    [Tooltip("Number of waves it takes to go from start to max density.")]
    [SerializeField] private int wavesToMaxFog = 10;

    [Header("Lighting")]
    [SerializeField] private Color ambientColor = new Color(0.035f, 0.045f, 0.065f);
    [Tooltip("The directional light (sun/moon). Auto-found if left empty.")]
    [SerializeField] private Light sun;
    [SerializeField] private float nightSunIntensity = 0.08f;
    [SerializeField] private Color moonColor = new Color(0.5f, 0.6f, 0.9f);
    [Tooltip("Replaces the daytime skybox with a flat night colour that matches the fog.")]
    [SerializeField] private bool replaceSkybox = true;

    [Header("Lightning (optional)")]
    [SerializeField] private bool enableLightning = true;
    [SerializeField] private float minInterval = 18f;
    [SerializeField] private float maxInterval = 45f;
    [SerializeField] private float flashIntensity = 1.2f;
    [SerializeField] private AudioClip[] thunderClips;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.7f;

    private WaveSpawner spawner;
    private AudioSource audioSource;
    private float targetDensity;

    private void Awake()
    {
        if (sun == null) sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectionalLight();

        ApplyMood();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            spawner = GameManager.Instance.Spawner;
            if (spawner != null) spawner.OnWaveStarted += HandleWaveStarted;
        }

        if (enableLightning && sun != null)
            StartCoroutine(LightningRoutine());
    }

    private void OnDestroy()
    {
        if (spawner != null) spawner.OnWaveStarted -= HandleWaveStarted;
    }

    private void OnDisable()
    {
        RestoreLighting();
    }

    private void Update()
    {
        // Ease toward the target so the fog creeps in rather than snapping between waves.
        RenderSettings.fogDensity = Mathf.Lerp(
            RenderSettings.fogDensity, targetDensity, 1f - Mathf.Exp(-0.4f * Time.deltaTime));
    }

    private void ApplyMood()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = startDensity;
        targetDensity = startDensity;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;

        if (replaceSkybox)
        {
            RenderSettings.skybox = null;

            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fogColor;
            }
        }

        if (sun != null)
        {
            sun.intensity = nightSunIntensity;
            sun.color = moonColor;
        }
    }

    private void RestoreLighting()
    {
        RenderSettings.ambientLight = ambientColor;
        if (sun != null) sun.intensity = nightSunIntensity;
    }

    private void HandleWaveStarted(int wave)
    {
        float t = Mathf.Clamp01((wave - 1f) / Mathf.Max(1, wavesToMaxFog));
        targetDensity = Mathf.Lerp(startDensity, maxDensity, t);
    }

    private IEnumerator LightningRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            // A few quick pulses look far more like real lightning than one steady flash.
            int pulses = Random.Range(2, 4);
            for (int i = 0; i < pulses; i++)
            {
                float strength = Random.Range(0.6f, 1f);
                sun.intensity = flashIntensity * strength;
                RenderSettings.ambientLight = Color.Lerp(ambientColor, moonColor * 0.5f, strength);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.1f));

                RestoreLighting();
                yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            }

            // Sound arrives after the light, like the real thing.
            if (thunderClips != null && thunderClips.Length > 0)
            {
                yield return new WaitForSeconds(Random.Range(0.4f, 2.5f));
                audioSource.PlayOneShot(thunderClips[Random.Range(0, thunderClips.Length)], thunderVolume);
            }
        }
    }

    private static Light FindDirectionalLight()
    {
#if UNITY_2023_1_OR_NEWER
        Light[] lights = FindObjectsByType<Light>();
#else
        Light[] lights = FindObjectsOfType<Light>();
#endif
        foreach (Light l in lights)
            if (l.type == LightType.Directional) return l;

        return null;
    }
}
