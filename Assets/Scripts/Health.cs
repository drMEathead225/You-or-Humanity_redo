using System;
using UnityEngine;

/// <summary>
/// Shared health for the player and every zombie.
/// Other systems (UI, AI, effects) subscribe to the events instead of polling.
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    public float Current { get; private set; }
    public float Max => maxHealth;
    public bool IsDead => Current <= 0f;

    /// <summary>(current, max) - fires on any change, including heal and reset.</summary>
    public event Action<float, float> OnHealthChanged;
    /// <summary>Amount of damage taken - good for hit flashes and sounds.</summary>
    public event Action<float> OnDamaged;
    public event Action OnDeath;

    private void Awake()
    {
        Current = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(Current, maxHealth);

        if (Current <= 0f)
            OnDeath?.Invoke();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Min(maxHealth, Current + amount);
        OnHealthChanged?.Invoke(Current, maxHealth);
    }

    /// <summary>Call when reusing a pooled object.</summary>
    public void ResetHealth()
    {
        Current = maxHealth;
        OnHealthChanged?.Invoke(Current, maxHealth);
    }

    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(1f, value);
        if (refill) Current = maxHealth;
        OnHealthChanged?.Invoke(Current, maxHealth);
    }
}
