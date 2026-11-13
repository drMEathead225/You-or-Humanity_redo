using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hitscan weapon. Attach to the camera (or a weapon object under it).
/// Behaviour comes from a WeaponData asset, so new guns need no new code.
/// Uses the classic Input Manager: Fire1 (left mouse) to shoot, R to reload.
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private WeaponData data;
    [Tooltip("Where shots originate and aim from. Usually the player camera.")]
    [SerializeField] private Transform aimOrigin;
    [Tooltip("Layers a shot can hit. EXCLUDE the Player layer so you can't shoot yourself.")]
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("Optional - used to add recoil to the camera.")]
    [SerializeField] private FirstPersonController player;

    [Header("Effects (all optional)")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private GameObject hitEffectPrefab;
    [Tooltip("Used instead of the hit effect when a shot hits a zombie (blood).")]
    [SerializeField] private GameObject bloodEffectPrefab;
    [Tooltip("A point light at the muzzle that blinks on each shot.")]
    [SerializeField] private Light muzzleLight;
    [SerializeField] private float muzzleLightTime = 0.05f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireClip;
    [SerializeField] private AudioClip emptyClip;
    [SerializeField] private AudioClip reloadClip;

    public int MagazineAmmo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public WeaponData Data => data;

    /// <summary>(magazine, reserve) - hook your ammo UI here.</summary>
    public event Action<int, int> OnAmmoChanged;
    /// <summary>Fired every shot - for screen shake, weapon animation, etc.</summary>
    public event Action OnFired;
    /// <summary>True if the hit killed the target - for hit markers.</summary>
    public event Action<bool> OnHit;
    /// <summary>Fires just before OnHit when the shot landed on a headshot zone.</summary>
    public event Action OnHeadshot;
    public event Action<bool> OnReloadStateChanged;

    private float nextFireTime;
    private float muzzleLightOffTime;

    private void Awake()
    {
        if (aimOrigin == null && Camera.main != null)
            aimOrigin = Camera.main.transform;

        if (player == null)
            player = GetComponentInParent<FirstPersonController>();

        if (data == null)
        {
            Debug.LogError("Weapon: no WeaponData assigned.", this);
            enabled = false;
            return;
        }

        if (aimOrigin == null)
        {
            Debug.LogError("Weapon: no Aim Origin assigned and no MainCamera found.", this);
            enabled = false;
            return;
        }

        MagazineAmmo = data.magazineSize;
        ReserveAmmo = data.startingReserve;
    }

    private void Start()
    {
        // Start (not Awake) so UI subscribed in Awake gets the first update.
        OnAmmoChanged?.Invoke(MagazineAmmo, ReserveAmmo);
    }

    private void OnDisable()
    {
        // Prevents getting stuck in "reloading" if the weapon is swapped out mid-reload.
        StopAllCoroutines();
        if (IsReloading)
        {
            IsReloading = false;
            OnReloadStateChanged?.Invoke(false);
        }
    }

    private void Update()
    {
        // WeaponSwitcher can re-enable a weapon whose Awake failed (e.g. no WeaponData). Do nothing rather than crash.
        if (data == null || aimOrigin == null) return;

        // Turn the muzzle light back off (this also switches it off at start if it was left on).
        if (muzzleLight != null && muzzleLight.enabled && Time.time >= muzzleLightOffTime)
            muzzleLight.enabled = false;

        // Don't shoot while the cursor is free (pause menu, or the click that recaptures the mouse).
        if (Cursor.lockState != CursorLockMode.Locked) return;

        bool wantsFire = data.automatic ? Input.GetButton("Fire1") : Input.GetButtonDown("Fire1");

        if (wantsFire) TryFire();
        if (Input.GetKeyDown(KeyCode.R)) TryReload();
    }

    private void TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return;

        if (MagazineAmmo <= 0)
        {
            nextFireTime = Time.time + 0.25f; // stops the empty click from spamming
            Play(emptyClip);
            TryReload(); // auto-reload when empty
            return;
        }

        nextFireTime = Time.time + 1f / data.fireRate;
        MagazineAmmo--;

        Shoot();
        OnAmmoChanged?.Invoke(MagazineAmmo, ReserveAmmo);
    }

    private void Shoot()
    {
        if (muzzleFlash != null) muzzleFlash.Play();

        if (muzzleLight != null)
        {
            muzzleLight.enabled = true;
            muzzleLightOffTime = Time.time + muzzleLightTime;
        }
        Play(fireClip);

        for (int i = 0; i < data.pellets; i++)
        {
            Vector3 dir = GetSpreadDirection();

            if (Physics.Raycast(aimOrigin.position, dir, out RaycastHit hit, data.range,
                                hitMask, QueryTriggerInteraction.Ignore))
            {
                HandleHit(hit);
            }
        }

        if (player != null)
        {
            float yaw = UnityEngine.Random.Range(-data.recoilYaw, data.recoilYaw);
            player.AddRecoil(data.recoilPitch, yaw);
        }

        OnFired?.Invoke();
    }

    private Vector3 GetSpreadDirection()
    {
        if (data.spread <= 0f) return aimOrigin.forward;

        Quaternion offset = Quaternion.Euler(
            UnityEngine.Random.Range(-data.spread, data.spread),
            UnityEngine.Random.Range(-data.spread, data.spread),
            0f);

        return aimOrigin.rotation * (offset * Vector3.forward);
    }

    private void HandleHit(RaycastHit hit)
    {
        // Zombies may have colliders on child bones, so search upward for Health.
        Health health = hit.collider.GetComponentInParent<Health>();

        if (health != null && !health.IsDead)
        {
            // A HitZone on the collider we hit (e.g. the head) scales the damage.
            HitZone zone = hit.collider.GetComponent<HitZone>();
            float multiplier = zone != null ? zone.DamageMultiplier : 1f;

            health.TakeDamage(data.damage * multiplier);

            if (zone != null && zone.IsHeadshot) OnHeadshot?.Invoke();
            OnHit?.Invoke(health.IsDead);
        }

        // Flesh gets blood, everything else gets the generic impact effect (both pooled).
        GameObject effect = (health != null && bloodEffectPrefab != null) ? bloodEffectPrefab : hitEffectPrefab;
        EffectPool.Spawn(effect, hit.point, Quaternion.LookRotation(hit.normal), 2f);
    }

    public void TryReload()
    {
        if (IsReloading) return;
        if (MagazineAmmo >= data.magazineSize) return;
        if (ReserveAmmo <= 0) return;

        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        OnReloadStateChanged?.Invoke(true);
        Play(reloadClip);

        yield return new WaitForSeconds(data.reloadTime);

        int needed = data.magazineSize - MagazineAmmo;
        int taken = Mathf.Min(needed, ReserveAmmo);
        MagazineAmmo += taken;
        ReserveAmmo -= taken;

        IsReloading = false;
        OnReloadStateChanged?.Invoke(false);
        OnAmmoChanged?.Invoke(MagazineAmmo, ReserveAmmo);
    }

    /// <summary>For ammo pickups. Returns how much was actually added.</summary>
    public int AddReserveAmmo(int amount)
    {
        int before = ReserveAmmo;
        ReserveAmmo = Mathf.Min(data.maxReserve, ReserveAmmo + Mathf.Max(0, amount));
        OnAmmoChanged?.Invoke(MagazineAmmo, ReserveAmmo);
        return ReserveAmmo - before;
    }

    /// <summary>Reserve ammo as a fraction of the maximum, from 0 to 1.</summary>
    public float ReserveFraction => data != null && data.maxReserve > 0 ? (float)ReserveAmmo / data.maxReserve : 1f;

    /// <summary>Adds ammo as a percentage of this weapon's max reserve, so one pickup suits every gun.</summary>
    public int AddReserveAmmoPercent(float percent)
    {
        if (data == null) return 0;

        int amount = Mathf.Max(1, Mathf.RoundToInt(data.maxReserve * percent / 100f));
        return AddReserveAmmo(amount);
    }

    /// <summary>Blocks firing for a moment, used by the weapon switcher when equipping.</summary>
    public void SetEquipDelay(float seconds)
    {
        nextFireTime = Mathf.Max(nextFireTime, Time.time + seconds);
    }

    private void Play(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f); // tiny variation sounds far less repetitive
        audioSource.PlayOneShot(clip);
    }
}