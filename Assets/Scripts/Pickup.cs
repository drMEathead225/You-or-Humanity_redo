using UnityEngine;

public enum PickupType { Health, Ammo }

/// <summary>
/// A collectible. Walk into it to use it. Requires a trigger Collider.
/// Works two ways: dropped by PickupDropper (pooled, expires after a while)
/// or hand-placed in the level (set Lifetime to 0 and it stays forever).
/// A pickup that would be wasted (full health, full ammo) is left on the ground for later.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Pickup : MonoBehaviour
{
    [Header("What it does")]
    [SerializeField] private PickupType type = PickupType.Health;
    [Tooltip("Health restored, or for ammo: percent of a weapon's max reserve (25 = a quarter of the max).")]
    [SerializeField] private float amount = 25f;
    [Tooltip("Seconds before it disappears. 0 = never (use for hand-placed pickups).")]
    [SerializeField] private float lifetime = 25f;
    [Tooltip("It blinks during its last few seconds so players can see it is about to vanish.")]
    [SerializeField] private float blinkTime = 5f;

    [Header("Look")]
    [Tooltip("Child object to spin and bob. Leave empty to animate this object itself.")]
    [SerializeField] private Transform visual;
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 2.5f;

    [Header("Feedback (optional)")]
    [SerializeField] private AudioClip collectClip;
    [SerializeField, Range(0f, 1f)] private float collectVolume = 0.8f;
    [SerializeField] private GameObject collectEffectPrefab;

    public PickupType Type => type;

    private Renderer[] renderers;
    private Vector3 baseLocalPosition;
    private Vector3 childBasePosition;
    private float despawnTime;
    private float phase;
    private bool collected;
    private bool visible = true;

    private void Reset()
    {
        // Picking up needs a trigger; set it up automatically when the component is added.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        if (visual == null) visual = transform;
        else childBasePosition = visual.localPosition;

        renderers = GetComponentsInChildren<Renderer>();
    }

    private void OnEnable()
    {
        collected = false;
        despawnTime = Time.time + lifetime;
        phase = Random.value * Mathf.PI * 2f; // so a group of pickups doesn't bob in sync

        // A visual child always bobs around its original spot. If we bob the root itself, the
        // spot it was placed at this time is the base (pooled objects get re-placed on every use).
        baseLocalPosition = visual == transform ? transform.localPosition : childBasePosition;

        SetVisible(true);
    }

    private void Update()
    {
        visual.localPosition = baseLocalPosition + Vector3.up * (Mathf.Sin((Time.time + phase) * bobSpeed) * bobHeight);
        visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);

        if (lifetime <= 0f) return;

        float remaining = despawnTime - Time.time;
        if (remaining <= 0f)
        {
            Despawn();
            return;
        }

        if (remaining < blinkTime)
            SetVisible(Mathf.Repeat(Time.time * 8f, 1f) > 0.5f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;

        Health health = other.GetComponentInParent<Health>();
        if (health == null || health.IsDead) return;

        // Zombies have Health too, so make sure it is the player.
        if (!health.gameObject.CompareTag("Player")) return;

        if (!TryApply(health)) return;

        collected = true;

        if (collectClip != null)
            AudioSource.PlayClipAtPoint(collectClip, transform.position, collectVolume);

        EffectPool.Spawn(collectEffectPrefab, transform.position, Quaternion.identity, 1.5f);

        Despawn();
    }

    private bool TryApply(Health player)
    {
        switch (type)
        {
            case PickupType.Health:
                if (player.Current >= player.Max) return false; // already full: don't waste it
                player.Heal(amount);
                return true;

            case PickupType.Ammo:
                // With a weapon switcher, the ammo goes to whichever weapon is lowest.
                WeaponSwitcher switcher = player.GetComponentInChildren<WeaponSwitcher>();
                if (switcher != null)
                    return switcher.AddAmmoPercent(amount) > 0;

                Weapon weapon = player.GetComponentInChildren<Weapon>();
                if (weapon == null) return false;
                return weapon.AddReserveAmmoPercent(amount) > 0; // 0 added = already full
        }

        return false;
    }

    private void Despawn()
    {
        PooledEffect pooled = GetComponent<PooledEffect>();
        if (pooled != null) pooled.ReturnNow();
        else Destroy(gameObject); // hand-placed pickup
    }

    private void SetVisible(bool show)
    {
        if (visible == show) return;
        visible = show;

        foreach (Renderer r in renderers)
            if (r != null) r.enabled = show;
    }
}
