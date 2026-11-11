using System;
using UnityEngine;

[Serializable]
public class WeaponSlot
{
    public Weapon weapon;
    [Tooltip("Optional: the visible weapon model. It is only shown while this weapon is equipped.")]
    public GameObject viewModel;
    [Tooltip("The weapon becomes available when this wave starts. 1 = from the beginning.")]
    [Min(1)] public int unlockWave = 1;
}

/// <summary>
/// Manages several Weapon components: number keys and scroll wheel switch between them,
/// new weapons unlock as waves progress, and ammo pickups feed the weapon that needs it most.
/// Add to the Main Camera. Each Weapon sits on its own child object; this script enables one at a time.
/// Other systems (HUD, camera shake) listen to this instead of to individual weapons.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    [Tooltip("Slot order = number keys: slot 1 is key 1, slot 2 is key 2, and so on.")]
    [SerializeField] private WeaponSlot[] slots;
    [Tooltip("Seconds after equipping before the weapon can fire.")]
    [SerializeField] private float equipDelay = 0.3f;
    [SerializeField] private bool scrollToSwitch = true;

    public Weapon Current { get; private set; }
    public int CurrentIndex { get; private set; } = -1;

    // Events forwarded from whichever weapon is currently equipped.
    public event Action OnFired;
    public event Action<bool> OnHit;
    public event Action OnHeadshot;
    public event Action<int, int> OnAmmoChanged;
    public event Action<bool> OnReloadStateChanged;

    public event Action<Weapon> OnWeaponChanged;
    public event Action<Weapon> OnWeaponUnlocked;

    private bool[] unlocked;
    private WaveSpawner spawner;

    /// <summary>True when every unlocked weapon has no ammo at all (magazine and reserve).</summary>
    public bool AllWeaponsEmpty
    {
        get
        {
            bool any = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!unlocked[i] || slots[i].weapon == null) continue;
                any = true;
                if (slots[i].weapon.MagazineAmmo + slots[i].weapon.ReserveAmmo > 0) return false;
            }
            return any;
        }
    }

    /// <summary>Reserve ammo of the emptiest unlocked weapon, from 0 to 1.</summary>
    public float LowestReserveFraction
    {
        get
        {
            float lowest = 1f;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!unlocked[i] || slots[i].weapon == null) continue;
                lowest = Mathf.Min(lowest, slots[i].weapon.ReserveFraction);
            }
            return lowest;
        }
    }

    private void Awake()
    {
        if (slots == null || slots.Length == 0 || slots[0].weapon == null)
        {
            Debug.LogError("WeaponSwitcher: assign at least one weapon in Slots (slot 1 must not be empty).", this);
            enabled = false;
            return;
        }

        unlocked = new bool[slots.Length];

        for (int i = 0; i < slots.Length; i++)
        {
            Weapon w = slots[i].weapon; // declared per loop pass so each handler below keeps its own weapon
            if (w == null) continue;

            unlocked[i] = slots[i].unlockWave <= 1;

            // Forward events, but only from the equipped weapon.
            w.OnFired += () => { if (w == Current) OnFired?.Invoke(); };
            w.OnHit += killed => { if (w == Current) OnHit?.Invoke(killed); };
            w.OnHeadshot += () => { if (w == Current) OnHeadshot?.Invoke(); };
            w.OnAmmoChanged += (mag, reserve) => { if (w == Current) OnAmmoChanged?.Invoke(mag, reserve); };
            w.OnReloadStateChanged += reloading => { if (w == Current) OnReloadStateChanged?.Invoke(reloading); };
        }
    }

    private void Start()
    {
        // Wait until Start so every Weapon has finished its own Awake before we switch it off.
        foreach (WeaponSlot slot in slots)
            SetEquipped(slot, false);

        for (int i = 0; i < slots.Length; i++)
        {
            if (unlocked[i])
            {
                Equip(i);
                break;
            }
        }

        if (GameManager.Instance != null)
        {
            spawner = GameManager.Instance.Spawner;
            if (spawner != null) spawner.OnWaveStarted += HandleWaveStarted;
        }
    }

    private void OnDestroy()
    {
        if (spawner != null) spawner.OnWaveStarted -= HandleWaveStarted;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return;

        for (int i = 0; i < slots.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                Equip(i);
        }

        if (!scrollToSwitch) return;

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) Cycle(-1);
        else if (scroll < 0f) Cycle(1);
    }

    /// <summary>
    /// Adds ammo as a percentage of a weapon's maximum reserve, to the unlocked weapon that is lowest on ammo.
    /// Returns the number of rounds added (0 if every weapon is full).
    /// </summary>
    public int AddAmmoPercent(float percent)
    {
        Weapon target = null;
        float lowest = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!unlocked[i]) continue;

            Weapon w = slots[i].weapon;
            if (w == null || w.Data == null) continue;
            if (w.ReserveAmmo >= w.Data.maxReserve) continue; // full

            float fraction = w.ReserveFraction;
            if (fraction < lowest)
            {
                lowest = fraction;
                target = w;
            }
        }

        return target != null ? target.AddReserveAmmoPercent(percent) : 0;
    }

    private void Equip(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        if (!unlocked[index] || slots[index].weapon == null || index == CurrentIndex) return;

        if (CurrentIndex >= 0)
            SetEquipped(slots[CurrentIndex], false); // switching mid-reload cancels the reload

        CurrentIndex = index;
        Current = slots[index].weapon;

        SetEquipped(slots[index], true);
        Current.SetEquipDelay(equipDelay);

        OnWeaponChanged?.Invoke(Current);
    }

    private void Cycle(int direction)
    {
        int count = slots.Length;

        for (int step = 1; step <= count; step++)
        {
            int index = ((CurrentIndex + direction * step) % count + count) % count;
            if (unlocked[index] && slots[index].weapon != null)
            {
                Equip(index);
                return;
            }
        }
    }

    private void HandleWaveStarted(int wave)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (unlocked[i] || slots[i].weapon == null || slots[i].unlockWave > wave) continue;

            unlocked[i] = true;
            OnWeaponUnlocked?.Invoke(slots[i].weapon);
            Equip(i); // a new weapon is a reward, so put it straight into the player's hands
        }
    }

    private static void SetEquipped(WeaponSlot slot, bool on)
    {
        if (slot.weapon != null) slot.weapon.enabled = on;
        if (slot.viewModel != null) slot.viewModel.SetActive(on);
    }
}
