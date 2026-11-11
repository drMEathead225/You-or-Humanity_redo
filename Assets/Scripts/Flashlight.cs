using UnityEngine;

/// <summary>
/// Toggleable flashlight with a battery. Add to the Main Camera.
/// It creates its own spot light if you don't assign one. The battery drains while on and recharges
/// while off. As it dies the beam dims and flickers, and at zero it cuts out until it has recovered a little.
/// </summary>
public class Flashlight : MonoBehaviour
{
    [Header("Light (applied to the spot light)")]
    [Tooltip("Leave empty and a spot light is created for you.")]
    [SerializeField] private Light spotLight;
    [SerializeField] private float intensity = 3f;
    [SerializeField] private float range = 28f;
    [SerializeField, Range(10f, 120f)] private float spotAngle = 60f;
    [SerializeField] private Color lightColor = new Color(1f, 0.96f, 0.85f);
    [Tooltip("Shadows look great but cost performance. Try them on a good PC.")]
    [SerializeField] private bool castShadows = false;

    [Header("Controls")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;
    [SerializeField] private bool startOn = true;

    [Header("Battery")]
    [SerializeField] private bool useBattery = true;
    [Tooltip("Seconds of light from a full battery.")]
    [SerializeField] private float batteryDuration = 90f;
    [Tooltip("Seconds to recharge from empty to full while switched off.")]
    [SerializeField] private float rechargeDuration = 45f;
    [Tooltip("Below this charge (0-1) the beam dims and flickers.")]
    [SerializeField, Range(0.05f, 0.5f)] private float lowChargeThreshold = 0.25f;
    [Tooltip("After running dry, charge must recover to this before it can be switched on again.")]
    [SerializeField, Range(0f, 0.5f)] private float relightThreshold = 0.2f;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip toggleClip;

    public bool IsOn { get; private set; }
    /// <summary>Battery charge from 0 to 1.</summary>
    public float Charge => charge;

    private float charge = 1f;
    private Health playerHealth;

    private void Awake()
    {
        if (spotLight == null)
        {
            GameObject go = new GameObject("Flashlight");
            go.transform.SetParent(transform, false);
            spotLight = go.AddComponent<Light>();
        }

        spotLight.type = LightType.Spot;
        spotLight.range = range;
        spotLight.spotAngle = spotAngle;
        spotLight.innerSpotAngle = spotAngle * 0.5f;
        spotLight.color = lightColor;
        spotLight.intensity = intensity;
        spotLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;

        SetOn(startOn);
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            playerHealth = GameManager.Instance.PlayerHealth;
            if (playerHealth != null) playerHealth.OnDeath += HandlePlayerDeath;
        }
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnDeath -= HandlePlayerDeath;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return; // paused

        if (Input.GetKeyDown(toggleKey)) Toggle();

        if (useBattery) UpdateBattery(Time.deltaTime);
        if (IsOn) UpdateBeam();
    }

    private void Toggle()
    {
        PlayToggleSound();

        if (IsOn)
        {
            SetOn(false);
            return;
        }

        // A dead battery just clicks until it has recovered a little.
        if (useBattery && charge < relightThreshold) return;

        SetOn(true);
    }

    private void UpdateBattery(float dt)
    {
        if (IsOn)
        {
            charge = Mathf.Max(0f, charge - dt / Mathf.Max(1f, batteryDuration));
            if (charge <= 0f) SetOn(false);
        }
        else
        {
            charge = Mathf.Min(1f, charge + dt / Mathf.Max(1f, rechargeDuration));
        }
    }

    private void UpdateBeam()
    {
        float dim = 1f;
        float flicker = 1f;

        if (useBattery && charge < lowChargeThreshold)
        {
            float low = charge / lowChargeThreshold; // 1 = just started dying, 0 = empty
            dim = Mathf.Lerp(0.4f, 1f, low);

            // Noise-driven dropouts that get more frequent as the battery empties.
            float noise = Mathf.PerlinNoise(Time.time * 22f, 0.5f);
            if (noise < (1f - low) * 0.6f) flicker = 0.1f;
        }

        spotLight.intensity = intensity * dim * flicker;
    }

    private void SetOn(bool on)
    {
        IsOn = on;
        spotLight.enabled = on;
        if (on) spotLight.intensity = intensity;
    }

    private void HandlePlayerDeath()
    {
        SetOn(false);
        enabled = false;
    }

    private void PlayToggleSound()
    {
        if (audioSource != null && toggleClip != null)
            audioSource.PlayOneShot(toggleClip);
    }
}
