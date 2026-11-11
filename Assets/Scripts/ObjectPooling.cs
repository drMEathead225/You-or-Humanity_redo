using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reuses GameObjects instead of Instantiate/Destroy, which is the main cause of stutter in horde games.
/// Pooled objects wait under an inactive holder, so they don't run Awake/OnEnable until they are
/// actually placed in the world (this also avoids NavMeshAgent warnings from spawning at the origin).
/// </summary>
public class GameObjectPool
{
    private readonly GameObject prefab;
    private readonly Transform activeRoot;
    private readonly Transform holder;
    private readonly Queue<GameObject> available = new Queue<GameObject>();

    public GameObjectPool(GameObject prefab, Transform activeRoot, int prewarm)
    {
        this.prefab = prefab;
        this.activeRoot = activeRoot;

        holder = new GameObject(prefab.name + "_Pool").transform;
        holder.SetParent(activeRoot, false);
        holder.gameObject.SetActive(false);

        for (int i = 0; i < prewarm; i++)
            available.Enqueue(CreateNew());
    }

    private GameObject CreateNew()
    {
        GameObject go = Object.Instantiate(prefab, holder);
        go.name = prefab.name;
        return go;
    }

    /// <summary>Takes an object from the pool (or creates one), places it, and activates it.</summary>
    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        GameObject go = null;

        // Skip anything destroyed behind our back (e.g. by a scene change).
        while (go == null && available.Count > 0)
            go = available.Dequeue();

        if (go == null) go = CreateNew();

        // Position first, then re-parent: moving out of the inactive holder is what wakes the object up.
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.SetParent(activeRoot, true);
        go.SetActive(true);
        return go;
    }

    public void Release(GameObject go)
    {
        if (go == null) return;

        go.SetActive(false);
        go.transform.SetParent(holder, false);
        available.Enqueue(go);
    }
}

/// <summary>
/// One-line pooled effects: EffectPool.Spawn(prefab, position, rotation, lifetime).
/// Replaces Instantiate + Destroy for hit sparks, blood, muzzle effects, etc.
/// </summary>
public static class EffectPool
{
    private static readonly Dictionary<GameObject, GameObjectPool> pools =
        new Dictionary<GameObject, GameObjectPool>();
    private static Transform root;

    // Keeps things correct when "Enter Play Mode without domain reload" is enabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pools.Clear();
        root = null;
    }

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = 2f)
    {
        if (prefab == null) return null;

        // The root is destroyed on scene load; if so, start fresh.
        if (root == null)
        {
            pools.Clear();
            root = new GameObject("EffectPool").transform;
        }

        if (!pools.TryGetValue(prefab, out GameObjectPool pool))
        {
            pool = new GameObjectPool(prefab, root, 4);
            pools[prefab] = pool;
        }

        GameObject go = pool.Get(position, rotation);

        PooledEffect timer = go.GetComponent<PooledEffect>();
        if (timer == null) timer = go.AddComponent<PooledEffect>();
        timer.Begin(pool, lifetime);

        return go;
    }
}
