using UnityEngine;

/// <summary>
/// Added automatically by EffectPool. Returns the effect to its pool after a set time.
/// </summary>
public class PooledEffect : MonoBehaviour
{
    private GameObjectPool pool;

    public void Begin(GameObjectPool owner, float lifetime)
    {
        pool = owner;
        CancelInvoke();

        // A lifetime of 0 or less means the object manages its own despawn (pickups do this).
        if (lifetime > 0f)
            Invoke(nameof(ReturnToPool), lifetime);
    }

    /// <summary>Returns this object to its pool right now (or destroys it if it isn't pooled).</summary>
    public void ReturnNow()
    {
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        if (pool != null) pool.Release(gameObject);
        else Destroy(gameObject);
    }

    private void OnDisable()
    {
        CancelInvoke();
    }
}
