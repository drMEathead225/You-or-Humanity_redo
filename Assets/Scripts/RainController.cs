using UnityEngine;

/// <summary>
/// Rain that follows the player. Builds its own particle system, so there is nothing to set up:
/// add it to an empty GameObject (the scene builder does this for you).
/// The optional Rain Material should use a transparent unlit shader such as Sprites/Default.
/// </summary>
public class RainController : MonoBehaviour
{
    [SerializeField] private Material rainMaterial;
    [SerializeField, Range(0f, 1f)] private float intensity = 0.7f;
    [SerializeField] private float height = 16f;
    [SerializeField] private float areaSize = 36f;
    [SerializeField] private float fallSpeed = 24f;
    [SerializeField] private float maxParticlesPerSecond = 1400f;
    [SerializeField] private Color rainColor = new Color(0.75f, 0.82f, 0.95f, 0.45f);
    [Tooltip("Tag of the object the rain follows.")]
    [SerializeField] private string followTag = "Player";

    private ParticleSystem rain;
    private Transform target;

    public float Intensity
    {
        get { return intensity; }
        set { intensity = Mathf.Clamp01(value); ApplyIntensity(); }
    }

    private void Awake()
    {
        GameObject go = new GameObject("RainParticles");
        go.transform.SetParent(transform, false);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rain.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = (height + 4f) / fallSpeed;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
        main.startColor = rainColor;
        main.gravityModifier = 0f;
        main.maxParticles = 4000;

        var shape = rain.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(areaSize, 0.1f, areaSize);

        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(1.2f);   // slight wind
        velocity.y = new ParticleSystem.MinMaxCurve(-fallSpeed);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 6f;
        renderer.velocityScale = 0f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        if (rainMaterial != null)
        {
            renderer.sharedMaterial = rainMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) renderer.sharedMaterial = new Material(shader);
            else Debug.LogWarning("RainController: no Rain Material assigned and Sprites/Default was not found.", this);
        }

        ApplyIntensity();
        rain.Play();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag(followTag);
            if (found != null) target = found.transform;
        }

        if (target != null)
        {
            transform.position = target.position + Vector3.up * height;
            transform.rotation = Quaternion.identity;
        }
    }

    private void ApplyIntensity()
    {
        if (rain == null) return;

        var emission = rain.emission;
        emission.rateOverTime = maxParticlesPerSecond * intensity;
    }

    private void OnValidate()
    {
        ApplyIntensity();
    }
}