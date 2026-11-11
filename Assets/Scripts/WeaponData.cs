using UnityEngine;

/// <summary>
/// All the numbers that define a weapon. Create assets via
/// Right-click in Project > Create > Zombie FPS > Weapon Data.
/// Pistol, rifle and shotgun are the same Weapon script with different data.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Zombie FPS/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Damage")]
    public string weaponName = "Pistol";
    public float damage = 25f;
    public float range = 100f;
    [Tooltip("Projectiles per shot. 1 for pistol/rifle, 6-10 for a shotgun.")]
    [Min(1)] public int pellets = 1;
    [Tooltip("Random cone angle in degrees. 0 = perfectly accurate.")]
    public float spread = 0.5f;

    [Header("Fire Rate")]
    [Tooltip("Shots per second.")]
    public float fireRate = 4f;
    [Tooltip("Hold to keep firing.")]
    public bool automatic = false;

    [Header("Ammo")]
    [Min(1)] public int magazineSize = 12;
    public int startingReserve = 48;
    public int maxReserve = 120;
    public float reloadTime = 1.4f;

    [Header("Recoil (degrees)")]
    public float recoilPitch = 1.5f;
    public float recoilYaw = 0.4f;
}
