using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Decides what zombies drop. Drop chances adapt to the player's situation:
/// low health makes health drops likelier, low ammo makes ammo drops likelier, and an empty gun
/// always drops ammo so the player can never get stuck. Clearing a wave also drops a small supply
/// crate near the player. Add to any empty GameObject; it finds everything else itself.
/// </summary>
public class PickupDropper : MonoBehaviour
{
    [Header("Pickup Prefabs (pickups with a Pickup component)")]
    [SerializeField] private GameObject healthPickupPrefab;
    [SerializeField] private GameObject ammoPickupPrefab;

    [Header("Zombie Drops")]
    [Tooltip("Chance per kill when the player is at full health.")]
    [SerializeField, Range(0f, 1f)] private float baseHealthChance = 0.10f;
    [Tooltip("Chance per kill when the player has plenty of ammo.")]
    [SerializeField, Range(0f, 1f)] private float baseAmmoChance = 0.20f;
    [Tooltip("How much a drop's chance multiplies when the player badly needs it. 1 = no adapting.")]
    [SerializeField, Range(1f, 5f)] private float maxNeedBoost = 2.5f;
    [SerializeField] private float dropHeight = 0.6f;

    [Header("Wave Clear Supply Drop")]
    [SerializeField] private bool dropOnWaveClear = true;
    [SerializeField] private float supplyMinDistance = 3f;
    [SerializeField] private float supplyMaxDistance = 6f;

    private WaveSpawner spawner;
    private Health playerHealth;
    private Weapon weapon;
    private WeaponSwitcher switcher;

    private void Start()
    {
        GameManager game = GameManager.Instance;
        if (game == null)
        {
            Debug.LogError("PickupDropper: add a GameManager to the scene first.", this);
            enabled = false;
            return;
        }

        spawner = game.Spawner;
        playerHealth = game.PlayerHealth;
        weapon = game.Weapon;
        switcher = game.Switcher;

        if (spawner == null)
        {
            Debug.LogError("PickupDropper: no WaveSpawner found.", this);
            enabled = false;
            return;
        }

        spawner.OnZombieKilled += HandleZombieKilled;
        spawner.OnWaveCleared += HandleWaveCleared;
    }

    private void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnZombieKilled -= HandleZombieKilled;
            spawner.OnWaveCleared -= HandleWaveCleared;
        }
    }

    private void HandleZombieKilled(ZombieAI zombie)
    {
        GameObject prefab = RollDrop();
        if (prefab == null) return;

        // Snap to the NavMesh so drops always land on walkable ground, even if the corpse tipped over.
        Vector3 position;
        if (!TryGroundPoint(zombie.transform.position, out position))
            position = zombie.transform.position + Vector3.up * dropHeight;

        Spawn(prefab, position);
    }

    private void HandleWaveCleared(int wave)
    {
        if (!dropOnWaveClear || playerHealth == null) return;

        SpawnSupply(healthPickupPrefab);
        SpawnSupply(ammoPickupPrefab);
    }

    private void SpawnSupply(GameObject prefab)
    {
        if (prefab == null) return;

        // A few attempts to find a walkable spot at a comfortable distance from the player.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(supplyMinDistance, supplyMaxDistance);
            Vector3 candidate = playerHealth.transform.position + new Vector3(dir.x, 0f, dir.y);

            if (TryGroundPoint(candidate, out Vector3 position))
            {
                Spawn(prefab, position);
                return;
            }
        }
    }

    private GameObject RollDrop()
    {
        float ammoChance = baseAmmoChance;
        float healthChance = baseHealthChance;

        Weapon current = switcher != null ? switcher.Current : weapon;
        if (current != null && current.Data != null)
        {
            // Every weapon completely dry: always drop some, so the player can never be stuck.
            bool allEmpty = switcher != null
                ? switcher.AllWeaponsEmpty
                : current.MagazineAmmo + current.ReserveAmmo == 0;
            if (allEmpty) return ammoPickupPrefab;

            // Judge need by the weapon lowest on ammo, not only the one in hand.
            float reserveFraction = switcher != null
                ? switcher.LowestReserveFraction
                : current.ReserveAmmo / Mathf.Max(1f, current.Data.maxReserve);
            float ammoNeed = 1f - Mathf.Clamp01(reserveFraction / 0.5f);
            ammoChance *= Mathf.Lerp(1f, maxNeedBoost, ammoNeed);
        }

        if (playerHealth != null && playerHealth.Max > 0f)
        {
            float healthNeed = 1f - Mathf.Clamp01(playerHealth.Current / playerHealth.Max);
            healthChance *= Mathf.Lerp(1f, maxNeedBoost, healthNeed);
        }

        // One roll, at most one drop per kill.
        float roll = UnityEngine.Random.value;
        if (roll < ammoChance) return ammoPickupPrefab;
        if (roll < ammoChance + healthChance) return healthPickupPrefab;
        return null;
    }

    private bool TryGroundPoint(Vector3 around, out Vector3 point)
    {
        if (NavMesh.SamplePosition(around, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            point = hit.position + Vector3.up * dropHeight;
            return true;
        }

        point = around;
        return false;
    }

    private static void Spawn(GameObject prefab, Vector3 position)
    {
        // Lifetime 0: the pickup manages its own expiry (and blinking).
        EffectPool.Spawn(prefab, position, Quaternion.identity, 0f);
    }
}
