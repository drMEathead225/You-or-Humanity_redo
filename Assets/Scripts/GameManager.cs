using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState { Playing, Paused, GameOver }

/// <summary>
/// Owns game flow: pause, player death, game over, restart, score, cursor and optional audio.
/// Everything else (HUD, spawner, weapon) only talks to it through events and read-only properties.
/// Controls: Esc = pause/resume, R = restart (only while paused or dead; in play R reloads).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References (auto-found if left empty)")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private Weapon weapon;
    [SerializeField] private WeaponSwitcher switcher;
    [SerializeField] private WaveSpawner spawner;

    [Header("Score")]
    [SerializeField] private int pointsPerKill = 10;
    [Tooltip("Bonus for clearing a wave = this x the wave number.")]
    [SerializeField] private int waveClearBonus = 50;

    [Header("Death")]
    [Tooltip("Real seconds of slow motion before the game over screen appears.")]
    [SerializeField] private float deathSlowMoTime = 1.2f;

    [Header("Audio (all optional)")]
    [SerializeField] private AudioClip ambientLoop;
    [SerializeField] private AudioClip waveStartClip;
    [SerializeField] private AudioClip waveClearClip;
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.4f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.8f;

    public Health PlayerHealth => playerHealth;
    public WeaponSwitcher Switcher => switcher;
    /// <summary>The weapon currently in hand (or the single weapon in a one-gun setup).</summary>
    public Weapon Weapon => switcher != null && switcher.Current != null ? switcher.Current : weapon;
    public WaveSpawner Spawner => spawner;

    public GameState State { get; private set; } = GameState.Playing;
    public int Score { get; private set; }

    public event Action<int> OnScoreChanged;
    public event Action<GameState> OnStateChanged;

    private AudioSource sfxSource;
    private bool isDying;

    private void Awake()
    {
        Instance = this;

        // A restart reloads the scene, so make sure nothing stays frozen or paused.
        Time.timeScale = 1f;
        AudioListener.pause = false;

        FindReferences();
        SetupAudio();
    }

    private void Start()
    {
        if (spawner != null)
        {
            spawner.OnZombieKilled += HandleZombieKilled;
            spawner.OnWaveStarted += HandleWaveStarted;
            spawner.OnWaveCleared += HandleWaveCleared;
        }

        if (playerHealth != null)
            playerHealth.OnDeath += HandlePlayerDeath;
        else
            Debug.LogWarning("GameManager: no player Health found. Tag your player 'Player' and add a Health component.", this);

        SetState(GameState.Playing);
    }

    private void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnZombieKilled -= HandleZombieKilled;
            spawner.OnWaveStarted -= HandleWaveStarted;
            spawner.OnWaveCleared -= HandleWaveCleared;
        }

        if (playerHealth != null)
            playerHealth.OnDeath -= HandlePlayerDeath;

        if (Instance == this) Instance = null;

        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void Update()
    {
        if (isDying) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (State == GameState.Playing) SetState(GameState.Paused);
            else if (State == GameState.Paused) SetState(GameState.Playing);
        }

        if (State != GameState.Playing && Input.GetKeyDown(KeyCode.R))
            Restart();
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SetState(GameState newState)
    {
        State = newState;

        bool playing = newState == GameState.Playing;
        Time.timeScale = playing ? 1f : 0f;
        AudioListener.pause = newState == GameState.Paused;

        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !playing;

        OnStateChanged?.Invoke(newState);
    }

    // ---------------------------------------------------------------- events

    private void HandleZombieKilled(ZombieAI zombie)
    {
        AddScore(pointsPerKill);
    }

    private void HandleWaveStarted(int wave)
    {
        PlaySfx(waveStartClip);
    }

    private void HandleWaveCleared(int wave)
    {
        AddScore(waveClearBonus * wave);
        PlaySfx(waveClearClip);
    }

    private void HandlePlayerDeath()
    {
        if (isDying) return;
        StartCoroutine(GameOverRoutine());
    }

    private IEnumerator GameOverRoutine()
    {
        isDying = true;

        // Stop the player from moving or shooting while dead.
        if (switcher != null)
        {
            switcher.enabled = false;
            if (switcher.Current != null) switcher.Current.enabled = false;
        }
        else if (weapon != null)
        {
            weapon.enabled = false;
        }
        if (playerHealth != null)
        {
            foreach (FirstPersonController fpc in playerHealth.GetComponentsInChildren<FirstPersonController>())
                fpc.enabled = false;
        }

        PlaySfx(gameOverClip);

        Time.timeScale = 0.25f; // brief slow motion sells the moment
        yield return new WaitForSecondsRealtime(deathSlowMoTime);

        isDying = false;
        SetState(GameState.GameOver);
    }

    private void AddScore(int amount)
    {
        Score += amount;
        OnScoreChanged?.Invoke(Score);
    }

    // ---------------------------------------------------------------- setup

    private void FindReferences()
    {
        if (playerHealth == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerHealth = player.GetComponentInParent<Health>();
        }

        if (switcher == null) switcher = FindFirst<WeaponSwitcher>();
        if (weapon == null) weapon = FindFirst<Weapon>();
        if (spawner == null) spawner = FindFirst<WaveSpawner>();
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<T>();
#else
        return FindObjectOfType<T>();
#endif
    }

    private void SetupAudio()
    {
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f; // 2D

        if (ambientLoop != null)
        {
            AudioSource music = gameObject.AddComponent<AudioSource>();
            music.clip = ambientLoop;
            music.loop = true;
            music.volume = musicVolume;
            music.spatialBlend = 0f;
            music.Play();
        }
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, sfxVolume);
    }
}
