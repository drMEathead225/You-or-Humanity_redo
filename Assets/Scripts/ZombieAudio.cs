// Reflection lets this script read the zombie's health without changing Health.cs.
using System.Reflection;
// Core Unity types.
using UnityEngine;

/// <summary>
/// Zombie sounds. Put this on the ROOT of each zombie prefab (next to ZombieAI and Health).
///   - Groans: random moans every few seconds while alive
///   - Attack: a snarl/swipe when the zombie is close enough to hit the player
///   - Hurt:   a grunt when its health goes down
///   - Death:  a death cry when its health reaches 0 (still plays if the zombie is destroyed)
/// Sounds are 3D, so they get louder as zombies get closer and you can hear which side they are on.
/// Pitch is randomised a little so a horde doesn't sound like copies of one zombie.
/// </summary>
[DisallowMultipleComponent]
public class ZombieAudio : MonoBehaviour
{
    [Header("Clips (drag audio files in, several per slot = random pick)")]
    [SerializeField] private AudioClip[] groanClips;   // idle / chasing moans
    [SerializeField] private AudioClip[] attackClips;  // played when close enough to hit you
    [SerializeField] private AudioClip[] hurtClips;    // played when shot
    [SerializeField] private AudioClip[] deathClips;   // played when killed

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float groanVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float attackVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float hurtVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 1f;

    [Header("Timing")]
    // Seconds between groans (random value between X and Y).
    [SerializeField] private Vector2 groanInterval = new Vector2(3f, 8f);
    // How close (metres) the player must be for the attack sound.
    [SerializeField] private float attackRange = 2.2f;
    // Minimum seconds between attack sounds.
    [SerializeField] private float attackSoundCooldown = 1.2f;
    // Minimum seconds between hurt sounds (stops a shotgun blast playing 8 grunts at once).
    [SerializeField] private float hurtSoundCooldown = 0.25f;

    [Header("3D sound")]
    // Full volume inside this distance.
    [SerializeField] private float minDistance = 2f;
    // Silent beyond this distance.
    [SerializeField] private float maxDistance = 30f;
    // Random pitch range, e.g. 0.85-1.15. Bigger zombies (Tank) sound better with lower pitch.
    [SerializeField] private Vector2 pitchRange = new Vector2(0.85f, 1.15f);

    // The speaker on this zombie.
    private AudioSource source;
    // The zombie's Health component (found automatically).
    private Component health;
    // The health field/property we read on it.
    private MemberInfo hpMember;
    // Health last frame, to spot hits.
    private float lastHp;
    // Stops the death sound playing twice.
    private bool dead;
    // The player (camera) position we measure distance to.
    private Transform player;
    // When the next groan / attack / hurt sound is allowed.
    private float nextGroan, nextAttack, nextHurt;

    private void Awake()
    {
        // Use an AudioSource on the zombie, or add one.
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;                       // 1 = fully 3D
        source.rolloffMode = AudioRolloffMode.Linear;   // fades out evenly up to maxDistance
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;                       // no weird pitch bending while running

        // Find the Health component and its current-health value.
        foreach (Component c in GetComponents<Component>())
            if (c != null && c.GetType().Name == "Health") { health = c; break; }
        if (health != null) hpMember = FindHp(health);
        lastHp = ReadHp();
    }

    private void OnEnable()
    {
        // Zombies may be reused by a spawner, so reset each time they appear.
        dead = false;
        lastHp = ReadHp();
        // First groan comes at a random time so a new wave doesn't all groan together.
        nextGroan = Time.time + Random.Range(0.5f, groanInterval.y);
    }

    private void Update()
    {
        // Silent while the title screen is open or the game is paused.
        if (MainMenu.IsOpen || Time.timeScale <= 0f || dead) return;

        if (player == null && Camera.main != null) player = Camera.main.transform;

        // ---- Hurt / death: compare health with last frame.
        float hp = ReadHp();
        if (hp < lastHp)
        {
            if (hp <= 0f) PlayDeath();
            else if (Time.time >= nextHurt)
            {
                Play(hurtClips, hurtVolume);
                nextHurt = Time.time + hurtSoundCooldown;
            }
        }
        lastHp = hp;
        if (dead) return;

        // ---- Attack: close enough to hit the player.
        if (player != null && Time.time >= nextAttack &&
            Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            Play(attackClips, attackVolume);
            nextAttack = Time.time + attackSoundCooldown;
            nextGroan = Time.time + Random.Range(groanInterval.x, groanInterval.y); // don't groan right after
        }

        // ---- Groan every few seconds (skipped if another sound is still playing).
        if (Time.time >= nextGroan)
        {
            if (!source.isPlaying) Play(groanClips, groanVolume);
            nextGroan = Time.time + Random.Range(groanInterval.x, groanInterval.y);
        }
    }

    // If Health destroys/disables the zombie the same frame it dies, Update never sees it - so check here too.
    private void OnDisable()
    {
        if (!dead && health != null && ReadHp() <= 0f) PlayDeath();
    }

    // Death sound is played from a temporary object, so it keeps playing after the zombie is removed.
    private void PlayDeath()
    {
        dead = true;
        AudioClip clip = Pick(deathClips);
        if (clip == null) return;
        var go = new GameObject("ZombieDeathSound");
        go.transform.position = transform.position;
        var a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.volume = deathVolume;
        a.pitch = Random.Range(pitchRange.x, pitchRange.y);
        a.spatialBlend = 1f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = minDistance;
        a.maxDistance = maxDistance;
        a.Play();
        Destroy(go, clip.length / Mathf.Max(0.1f, a.pitch) + 0.1f); // clean up when finished
    }

    // Plays a random clip from the list with a random pitch.
    private void Play(AudioClip[] clips, float volume)
    {
        AudioClip clip = Pick(clips);
        if (clip == null) return;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(clip, volume);
    }

    private static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    // ---- Health reading (works with a field or a property, public or private).

    private static readonly string[] HpNames = { "CurrentHealth", "currentHealth", "Current", "CurrentHP", "HP", "hp", "health", "Health", "Value" };

    private static MemberInfo FindHp(Component c)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string n in HpNames)
        {
            PropertyInfo p = c.GetType().GetProperty(n, f);
            if (p != null && p.CanRead && IsNumber(p.PropertyType)) return p;
            FieldInfo fi = c.GetType().GetField(n, f);
            if (fi != null && IsNumber(fi.FieldType)) return fi;
        }
        Debug.LogWarning($"[ZombieAudio] Could not find current health on {c.GetType().Name}; hurt/death sounds disabled.");
        return null;
    }

    private static bool IsNumber(System.Type t) { return t == typeof(float) || t == typeof(int); }

    private float ReadHp()
    {
        if (health == null || hpMember == null) return 1f; // unknown = treat as alive
        object v = hpMember is PropertyInfo p ? p.GetValue(health) : ((FieldInfo)hpMember).GetValue(health);
        return System.Convert.ToSingle(v);
    }
}
