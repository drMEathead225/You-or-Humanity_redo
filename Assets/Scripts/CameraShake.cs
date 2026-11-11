using UnityEngine;

/// <summary>
/// Trauma-based camera shake. Add to the Main Camera (the same object FirstPersonController uses as its pivot).
/// Hits on the player cause a strong shake and each shot a tiny one. Anything else can call AddTrauma().
/// The controller reads Rotation and applies it itself, so the two never fight over the camera.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Shake")]
    [Tooltip("Maximum rotation in degrees at full trauma.")]
    [SerializeField] private float maxAngle = 4f;
    [Tooltip("Maximum camera position wobble in metres at full trauma.")]
    [SerializeField] private float maxOffset = 0.05f;
    [Tooltip("How fast trauma fades, per second.")]
    [SerializeField] private float decay = 1.6f;
    [SerializeField] private float frequency = 22f;

    [Header("Triggers")]
    [SerializeField] private float damageTraumaPerPoint = 0.03f;
    [SerializeField] private float minDamageTrauma = 0.25f;
    [SerializeField] private float maxDamageTrauma = 0.7f;
    [Tooltip("Tiny shake on every shot. 0 to disable.")]
    [SerializeField] private float shotTrauma = 0.05f;

    /// <summary>Rotation offset for this frame. FirstPersonController multiplies it onto the view.</summary>
    public Quaternion Rotation { get; private set; } = Quaternion.identity;

    private float trauma;
    private float seed;
    private Vector3 basePosition;
    private Health playerHealth;
    private Weapon weapon;

    private void Awake()
    {
        basePosition = transform.localPosition;
        seed = Random.value * 100f;
    }

    private void Start()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        playerHealth = game.PlayerHealth;
        weapon = game.Weapon;

        if (playerHealth != null) playerHealth.OnDamaged += HandleDamaged;
        if (weapon != null) weapon.OnFired += HandleFired;
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnDamaged -= HandleDamaged;
        if (weapon != null) weapon.OnFired -= HandleFired;
    }

    private void Update()
    {
        if (trauma <= 0f)
        {
            Rotation = Quaternion.identity;
            transform.localPosition = basePosition;
            return;
        }

        trauma = Mathf.Max(0f, trauma - decay * Time.deltaTime);

        // Squaring makes small trauma barely visible and big trauma violent, which feels natural.
        float shake = trauma * trauma;
        float t = Time.time * frequency;

        float x = Mathf.PerlinNoise(seed, t) * 2f - 1f;
        float y = Mathf.PerlinNoise(seed + 10f, t) * 2f - 1f;
        float z = Mathf.PerlinNoise(seed + 20f, t) * 2f - 1f;

        Rotation = Quaternion.Euler(x * maxAngle * shake, y * maxAngle * shake, z * maxAngle * shake);
        transform.localPosition = basePosition + new Vector3(y, x, 0f) * (maxOffset * shake);
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    private void HandleDamaged(float damage)
    {
        AddTrauma(Mathf.Clamp(damage * damageTraumaPerPoint, minDamageTrauma, maxDamageTrauma));
    }

    private void HandleFired()
    {
        if (shotTrauma > 0f) AddTrauma(shotTrauma);
    }
}
