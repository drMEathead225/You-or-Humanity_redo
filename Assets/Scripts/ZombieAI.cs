using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Zombie brain: Idle -> Chase -> Attack -> Dead, driven by a NavMeshAgent and the shared Health component.
/// Variety (fast, tanky, hard-hitting zombies) comes from changing the inspector numbers, not new code.
/// Pool-friendly: all reset logic runs in OnEnable, and despawning goes through the OnDespawn event.
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class ZombieAI : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Dead }

    [Header("Target")]
    [Tooltip("Leave empty to auto-find the object with the tag below.")]
    [SerializeField] private Transform target;
    [SerializeField] private string playerTag = "Player";

    [Header("Perception")]
    [SerializeField] private float detectionRange = 20f;
    [Tooltip("ON: hunts the player from the moment it spawns (best for wave spawners). OFF: waits until the player gets close or shoots it.")]
    [SerializeField] private bool startAggro = true;

    [Header("Movement")]
    [Tooltip("Each zombie picks a random speed in this range. Free variety.")]
    [SerializeField] private float minSpeed = 1.6f;
    [SerializeField] private float maxSpeed = 3.4f;
    [Tooltip("How often the path to the player is recalculated. Not every frame - that's costly.")]
    [SerializeField] private float repathInterval = 0.25f;
    [SerializeField] private float turnSpeed = 8f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.7f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackCooldown = 1.3f;
    [Tooltip("Delay between starting the swing and the damage landing. Match it to your attack animation.")]
    [SerializeField] private float hitDelay = 0.45f;

    [Header("Death")]
    [SerializeField] private float despawnDelay = 5f;

    [Header("Optional: animation and audio")]
    [Tooltip("Needs: float 'Speed', trigger 'Attack', trigger 'Die'. Leave empty to run without animation.")]
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] groanClips;
    [SerializeField] private float groanIntervalMin = 4f;
    [SerializeField] private float groanIntervalMax = 9f;

    public State CurrentState => state;

    /// <summary>
    /// Raised when the corpse should disappear. A pool can subscribe and recycle the object;
    /// with no subscriber the zombie is simply destroyed.
    /// </summary>
    public event Action<ZombieAI> OnDespawn;

    /// <summary>Raised the moment this zombie dies (before the corpse despawns).</summary>
    public event Action<ZombieAI> OnDied;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private NavMeshAgent agent;
    private Health health;
    private Health targetHealth;
    private Collider[] colliders;
    private float baseMaxHealth;

    private State state;
    private float nextRepathTime;
    private float nextAttackTime;
    private float hitTime;
    private float nextGroanTime;
    private float nextFindTime;
    private bool pendingHit;

    // Used only by the placeholder "tip over" death when there is no Animator.
    private bool tippedOver;
    private Quaternion uprightRotation;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        colliders = GetComponentsInChildren<Collider>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        agent.stoppingDistance = attackRange * 0.8f; // must be smaller than attackRange so it can reach it
        baseMaxHealth = health.Max; // remembered so difficulty scaling never compounds
    }

    private void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
        health.OnDeath += HandleDeath;
        ResetState();
    }

    private void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
        health.OnDeath -= HandleDeath;
        CancelInvoke();
    }

    private void ResetState()
    {
        CancelInvoke();

        if (tippedOver)
        {
            transform.rotation = uprightRotation;
            transform.position += Vector3.up * 0.5f;
            tippedOver = false;
        }

        if (health.IsDead) health.ResetHealth();

        foreach (Collider col in colliders)
            if (col != null) col.enabled = true;

        agent.enabled = true;
        agent.speed = UnityEngine.Random.Range(minSpeed, maxSpeed);
        agent.avoidancePriority = UnityEngine.Random.Range(30, 70); // stops zombies jamming each other

        pendingHit = false;
        nextAttackTime = 0f;
        nextGroanTime = Time.time + UnityEngine.Random.Range(groanIntervalMin, groanIntervalMax);

        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        if (target != null && targetHealth == null)
            targetHealth = target.GetComponentInParent<Health>();

        state = State.Idle;
        SetAgentStopped(true);

        if (startAggro)
        {
            if (!HasTarget()) TryFindTarget();
            if (HasTarget()) EnterChase();
        }
    }

    // ---------------------------------------------------------------- public API

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetHealth = newTarget != null ? newTarget.GetComponentInParent<Health>() : null;
    }

    /// <summary>Teleport onto the NavMesh. Use this when spawning.</summary>
    public void Warp(Vector3 position)
    {
        if (agent.enabled) agent.Warp(position);
        else transform.position = position;
    }

    /// <summary>Scales this zombie from its prefab's base stats. Called by the wave spawner.</summary>
    public void ApplyDifficulty(float healthMultiplier, float speedMultiplier)
    {
        health.SetMaxHealth(baseMaxHealth * Mathf.Max(0.1f, healthMultiplier));

        if (agent.enabled)
            agent.speed = UnityEngine.Random.Range(minSpeed, maxSpeed) * Mathf.Max(0.1f, speedMultiplier);
    }

    /// <summary>Wakes an idle zombie and sends it after its target.</summary>
    public void Aggro()
    {
        if (state == State.Idle && HasTarget())
            EnterChase();
    }

    // ---------------------------------------------------------------- update loop

    private void Update()
    {
        if (state == State.Dead) return;

        switch (state)
        {
            case State.Idle: UpdateIdle(); break;
            case State.Chase: UpdateChase(); break;
            case State.Attack: UpdateAttack(); break;
        }

        UpdateGroan();
        UpdateAnimator();
    }

    private void UpdateIdle()
    {
        if (!HasTarget())
        {
            if (Time.time >= nextFindTime)
            {
                nextFindTime = Time.time + 1f; // searching is cheap but pointless every frame
                TryFindTarget();
            }
            return;
        }

        if (FlatSqrDistance() <= detectionRange * detectionRange)
            EnterChase();
    }

    private void UpdateChase()
    {
        if (!HasTarget()) { EnterIdle(); return; }

        if (FlatSqrDistance() <= attackRange * attackRange)
        {
            EnterAttack();
            return;
        }

        if (Time.time >= nextRepathTime && agent.isOnNavMesh)
        {
            agent.SetDestination(target.position);
            nextRepathTime = Time.time + repathInterval;
        }
    }

    private void UpdateAttack()
    {
        if (!HasTarget()) { EnterIdle(); return; }

        FaceTarget();
        float sqr = FlatSqrDistance();

        // Slightly larger exit range than entry range so it doesn't flicker between states.
        float exitRange = attackRange * 1.25f;
        if (sqr > exitRange * exitRange)
        {
            EnterChase();
            return;
        }

        if (pendingHit)
        {
            if (Time.time >= hitTime)
            {
                pendingHit = false;
                float landRange = attackRange * 1.5f;
                if (sqr <= landRange * landRange)
                    targetHealth.TakeDamage(attackDamage);
            }
        }
        else if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            pendingHit = true;
            hitTime = Time.time + hitDelay;
            if (animator != null) animator.SetTrigger(AttackHash);
        }
    }

    // ---------------------------------------------------------------- state changes

    private void EnterIdle()
    {
        state = State.Idle;
        pendingHit = false;
        SetAgentStopped(true);
    }

    private void EnterChase()
    {
        state = State.Chase;
        pendingHit = false;
        nextRepathTime = 0f;
        SetAgentStopped(false);
    }

    private void EnterAttack()
    {
        state = State.Attack;
        SetAgentStopped(true);
        if (agent.isOnNavMesh) agent.velocity = Vector3.zero;
    }

    private void HandleDamaged(float amount)
    {
        // Getting shot wakes up an idle zombie.
        if (state != State.Idle) return;

        if (!HasTarget()) TryFindTarget();
        if (HasTarget()) EnterChase();
    }

    private void HandleDeath()
    {
        state = State.Dead;
        pendingHit = false;

        SetAgentStopped(true);
        agent.enabled = false;

        // Corpses shouldn't block bullets or other zombies.
        foreach (Collider col in colliders)
            if (col != null) col.enabled = false;

        if (animator != null)
        {
            animator.SetTrigger(DieHash);
        }
        else
        {
            // Placeholder death for an un-animated capsule: tip over onto its side.
            uprightRotation = transform.rotation;
            transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            transform.position += Vector3.down * 0.5f;
            tippedOver = true;
        }

        OnDied?.Invoke(this);
        Invoke(nameof(Despawn), despawnDelay);
    }

    private void Despawn()
    {
        if (OnDespawn != null) OnDespawn.Invoke(this);
        else Destroy(gameObject);
    }

    // ---------------------------------------------------------------- helpers

    private bool HasTarget()
    {
        return target != null && targetHealth != null && !targetHealth.IsDead;
    }

    private void TryFindTarget()
    {
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player != null) SetTarget(player.transform);
    }

    private float FlatSqrDistance()
    {
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        return d.sqrMagnitude;
    }

    private void FaceTarget()
    {
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    private void SetAgentStopped(bool stopped)
    {
        // Calling isStopped on an agent that isn't on the NavMesh throws errors, so guard it.
        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = stopped;
    }

    private void UpdateGroan()
    {
        if (audioSource == null || groanClips == null || groanClips.Length == 0) return;
        if (Time.time < nextGroanTime) return;

        nextGroanTime = Time.time + UnityEngine.Random.Range(groanIntervalMin, groanIntervalMax);
        audioSource.pitch = UnityEngine.Random.Range(0.85f, 1.1f);
        audioSource.PlayOneShot(groanClips[UnityEngine.Random.Range(0, groanClips.Length)]);
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(SpeedHash, agent.velocity.magnitude, 0.1f, Time.deltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
