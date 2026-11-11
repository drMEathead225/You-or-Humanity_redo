using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns zombies in waves from pooled prefabs. Each wave has more zombies that are tougher and faster.
/// Add zombie variants (runner, tank) as extra entries with their own weight and first wave.
/// </summary>
public class WaveSpawner : MonoBehaviour
{
    [Serializable]
    public class ZombieType
    {
        public string name = "Walker";
        [Tooltip("Prefab asset with ZombieAI, NavMeshAgent and Health.")]
        public GameObject prefab;
        [Tooltip("Higher = more common.")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("This type only appears from this wave onward.")]
        [Min(1)] public int firstWave = 1;
    }

    [Header("Zombie Types")]
    [SerializeField] private ZombieType[] zombieTypes;

    [Header("Spawning")]
    [Tooltip("Empty GameObjects placed on or near the NavMesh, around the edges of the map.")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("Spawn points closer to the player than this are skipped, so zombies never pop in next to you.")]
    [SerializeField] private float minSpawnDistance = 12f;
    [SerializeField] private float spawnJitter = 2f;
    [Tooltip("Leave empty to auto-find the object tagged Player.")]
    [SerializeField] private Transform player;

    [Header("Waves")]
    [SerializeField] private float startDelay = 3f;
    [SerializeField] private int baseCount = 6;
    [SerializeField] private int extraPerWave = 3;
    [Tooltip("Never more than this many zombies alive at once (keeps performance steady).")]
    [SerializeField] private int maxAlive = 20;
    [SerializeField] private float spawnInterval = 0.7f;
    [SerializeField] private float timeBetweenWaves = 8f;

    [Header("Difficulty Scaling (per wave after the first)")]
    [SerializeField] private float healthPerWave = 0.1f;
    [SerializeField] private float speedPerWave = 0.04f;
    [SerializeField] private float maxSpeedMultiplier = 1.5f;

    [Header("Pooling and Debug")]
    [SerializeField] private int prewarmPerType = 6;
    [Tooltip("Shows wave info in the top-left corner. Turn off once the real UI exists.")]
    [SerializeField] private bool showDebugOverlay = true;

    public int Wave { get; private set; }
    public int Alive { get; private set; }
    public int Kills { get; private set; }
    public float TimeUntilNextWave { get; private set; }
    /// <summary>Zombies still to spawn plus zombies still alive in the current wave.</summary>
    public int ZombiesRemaining => toSpawn + Alive;

    public event Action<int> OnWaveStarted;
    public event Action<int> OnWaveCleared;
    /// <summary>Fires on every kill - hook score and kill counters here.</summary>
    public event Action<ZombieAI> OnZombieKilled;

    private readonly Dictionary<ZombieType, GameObjectPool> pools = new Dictionary<ZombieType, GameObjectPool>();
    private readonly Dictionary<ZombieAI, GameObjectPool> owners = new Dictionary<ZombieAI, GameObjectPool>();
    private readonly List<Transform> validPoints = new List<Transform>();
    private readonly List<ZombieType> eligible = new List<ZombieType>();

    private Health playerHealth;
    private Transform zombieRoot;
    private int toSpawn;

    private bool PlayerDead => playerHealth != null && playerHealth.IsDead;

    private void Awake()
    {
        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }

        if (player == null)
            Debug.LogWarning("WaveSpawner: no player assigned or tagged 'Player'. Zombies won't know whom to chase.", this);
        else
            playerHealth = player.GetComponentInParent<Health>();

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("WaveSpawner: assign at least one spawn point.", this);
            enabled = false;
            return;
        }

        zombieRoot = new GameObject("Zombies").transform;

        foreach (ZombieType type in zombieTypes)
        {
            if (type.prefab == null || type.prefab.GetComponent<ZombieAI>() == null)
            {
                Debug.LogError($"WaveSpawner: zombie type '{type.name}' needs a prefab with a ZombieAI component. Skipping it.", this);
                continue;
            }

            pools[type] = new GameObjectPool(type.prefab, zombieRoot, prewarmPerType);
        }

        if (pools.Count == 0)
        {
            Debug.LogError("WaveSpawner: no valid zombie types.", this);
            enabled = false;
        }
    }

    private void Start()
    {
        StartCoroutine(WaveLoop());
    }

    private IEnumerator WaveLoop()
    {
        yield return new WaitForSeconds(startDelay);

        while (!PlayerDead)
        {
            Wave++;
            toSpawn = baseCount + (Wave - 1) * extraPerWave;
            OnWaveStarted?.Invoke(Wave);

            WaitForSeconds wait = new WaitForSeconds(spawnInterval);
            while (toSpawn > 0 && !PlayerDead)
            {
                if (Alive < maxAlive && TrySpawn())
                    toSpawn--;

                yield return wait;
            }

            while (Alive > 0 && !PlayerDead)
                yield return null;

            if (PlayerDead) break;

            OnWaveCleared?.Invoke(Wave);

            TimeUntilNextWave = timeBetweenWaves;
            while (TimeUntilNextWave > 0f && !PlayerDead)
            {
                TimeUntilNextWave -= Time.deltaTime;
                yield return null;
            }
            TimeUntilNextWave = 0f;
        }
    }

    private bool TrySpawn()
    {
        ZombieType type = PickType();
        if (type == null) return false;

        Transform point = PickSpawnPoint();
        if (point == null) return false;

        Vector2 jitter = UnityEngine.Random.insideUnitCircle * spawnJitter;
        Vector3 candidate = point.position + new Vector3(jitter.x, 0f, jitter.y);

        // Snap to the NavMesh. If there's none nearby, skip and try again next tick.
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            return false;

        Quaternion rotation = Quaternion.identity;
        if (player != null)
        {
            Vector3 toPlayer = player.position - hit.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f) rotation = Quaternion.LookRotation(toPlayer);
        }

        GameObjectPool pool = pools[type];
        GameObject go = pool.Get(hit.position, rotation);
        ZombieAI zombie = go.GetComponent<ZombieAI>();

        owners[zombie] = pool;

        // Unsubscribe first so a recycled zombie is never subscribed twice.
        zombie.OnDied -= HandleZombieDied;
        zombie.OnDied += HandleZombieDied;
        zombie.OnDespawn -= HandleZombieDespawn;
        zombie.OnDespawn += HandleZombieDespawn;

        float wavesPast = Wave - 1;
        float healthMult = 1f + wavesPast * healthPerWave;
        float speedMult = Mathf.Min(1f + wavesPast * speedPerWave, maxSpeedMultiplier);

        zombie.SetTarget(player);
        zombie.ApplyDifficulty(healthMult, speedMult);
        zombie.Warp(hit.position);
        zombie.Aggro();

        Alive++;
        return true;
    }

    private ZombieType PickType()
    {
        eligible.Clear();
        float totalWeight = 0f;

        foreach (var kv in pools)
        {
            ZombieType type = kv.Key;
            if (type.firstWave > Wave || type.weight <= 0f) continue;
            eligible.Add(type);
            totalWeight += type.weight;
        }

        if (eligible.Count == 0) return null;

        float roll = UnityEngine.Random.value * totalWeight;
        foreach (ZombieType type in eligible)
        {
            roll -= type.weight;
            if (roll <= 0f) return type;
        }
        return eligible[eligible.Count - 1];
    }

    private Transform PickSpawnPoint()
    {
        validPoints.Clear();

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            if (player != null)
            {
                Vector3 d = point.position - player.position;
                d.y = 0f;
                if (d.sqrMagnitude < minSpawnDistance * minSpawnDistance) continue;
            }
            validPoints.Add(point);
        }

        // If the player is near every point, fall back to any point rather than stalling the wave.
        if (validPoints.Count == 0)
        {
            foreach (Transform point in spawnPoints)
                if (point != null) validPoints.Add(point);
        }

        if (validPoints.Count == 0) return null;
        return validPoints[UnityEngine.Random.Range(0, validPoints.Count)];
    }

    private void HandleZombieDied(ZombieAI zombie)
    {
        Alive = Mathf.Max(0, Alive - 1);
        Kills++;
        OnZombieKilled?.Invoke(zombie);
    }

    private void HandleZombieDespawn(ZombieAI zombie)
    {
        if (owners.TryGetValue(zombie, out GameObjectPool pool))
            pool.Release(zombie.gameObject);
        else
            Destroy(zombie.gameObject);
    }

    private void OnGUI()
    {
        if (!showDebugOverlay) return;

        GUI.Label(new Rect(10, 10, 400, 24), $"Wave {Wave}   Alive {Alive}   Remaining {ZombiesRemaining}   Kills {Kills}");
        if (TimeUntilNextWave > 0f)
            GUI.Label(new Rect(10, 30, 400, 24), $"Next wave in {TimeUntilNextWave:0.0}s");
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;

        Gizmos.color = Color.red;
        foreach (Transform point in spawnPoints)
            if (point != null) Gizmos.DrawWireSphere(point.position, spawnJitter);
    }
}
