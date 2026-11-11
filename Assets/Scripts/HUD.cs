using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the whole HUD in code: health bar, ammo, wave info, score, crosshair, hit marker,
/// damage flash, low-health pulse, wave announcements, and pause / game over screens.
/// Add this to any empty GameObject. No Canvas or UI setup needed.
/// Everything is driven by events, so nothing is polled except two cheap counters.
/// </summary>
public class HUD : MonoBehaviour
{
    private GameManager game;
    private Health playerHealth;
    private Weapon weapon;
    private WaveSpawner spawner;
    private Font font;

    private RectTransform canvasRect;
    private RectTransform healthFill;
    private Image damageOverlay;
    private Text healthText;
    private Text ammoText;
    private Text reloadText;
    private Text waveText;
    private Text remainingText;
    private Text scoreText;
    private Text announceText;
    private Text panelTitle;
    private Text panelBody;
    private GameObject panel;
    private RectTransform hitMarker;
    private Image[] hitLines;

    private float healthPct = 1f;
    private float flashAlpha;
    private float hitAlpha;
    private bool lastHitWasKill;
    private bool lastHitWasHeadshot;
    private bool headshotPending;
    private float announceTimer;
    private int lastRemaining = -1;
    private int lastCountdown = -1;

    private void Start()
    {
        game = GameManager.Instance;
        if (game == null)
        {
            Debug.LogError("HUD: add a GameManager to the scene first.", this);
            enabled = false;
            return;
        }

        playerHealth = game.PlayerHealth;
        weapon = game.Weapon;
        spawner = game.Spawner;

        font = LoadFont();
        BuildUI();

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += HandleHealthChanged;
            playerHealth.OnDamaged += HandleDamaged;
            HandleHealthChanged(playerHealth.Current, playerHealth.Max);
        }

        if (weapon != null)
        {
            weapon.OnAmmoChanged += HandleAmmoChanged;
            weapon.OnReloadStateChanged += HandleReloadChanged;
            weapon.OnHit += HandleHit;
            weapon.OnHeadshot += HandleHeadshot;
            HandleAmmoChanged(weapon.MagazineAmmo, weapon.ReserveAmmo);
        }

        if (spawner != null)
        {
            spawner.OnWaveStarted += HandleWaveStarted;
            spawner.OnWaveCleared += HandleWaveCleared;
        }

        game.OnScoreChanged += HandleScoreChanged;
        game.OnStateChanged += HandleStateChanged;
        HandleScoreChanged(game.Score);
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= HandleHealthChanged;
            playerHealth.OnDamaged -= HandleDamaged;
        }

        if (weapon != null)
        {
            weapon.OnAmmoChanged -= HandleAmmoChanged;
            weapon.OnReloadStateChanged -= HandleReloadChanged;
            weapon.OnHit -= HandleHit;
            weapon.OnHeadshot -= HandleHeadshot;
        }

        if (spawner != null)
        {
            spawner.OnWaveStarted -= HandleWaveStarted;
            spawner.OnWaveCleared -= HandleWaveCleared;
        }

        if (game != null)
        {
            game.OnScoreChanged -= HandleScoreChanged;
            game.OnStateChanged -= HandleStateChanged;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime; // keeps UI animating during slow motion and pause

        // Damage flash plus a pulsing red edge when health is low.
        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 1.8f);
        float lowAlpha = 0f;
        if (healthPct < 0.35f)
            lowAlpha = (0.35f - healthPct) / 0.35f * (0.18f + 0.07f * Mathf.Sin(Time.unscaledTime * 4f));
        damageOverlay.color = new Color(0.6f, 0f, 0f, Mathf.Max(flashAlpha, lowAlpha));

        // Hit marker fade.
        hitAlpha = Mathf.MoveTowards(hitAlpha, 0f, dt * 5f);
        Color hitColor;
        if (lastHitWasHeadshot) hitColor = new Color(1f, 0.8f, 0.1f, hitAlpha);      // gold = headshot
        else if (lastHitWasKill) hitColor = new Color(1f, 0.2f, 0.2f, hitAlpha);     // red = kill
        else hitColor = new Color(1f, 1f, 1f, hitAlpha);
        foreach (Image line in hitLines) line.color = hitColor;

        // Announcement holds, then fades over its last second.
        if (announceTimer > 0f)
        {
            announceTimer -= dt;
            announceText.color = new Color(1f, 1f, 1f, Mathf.Clamp01(announceTimer));
        }

        UpdateWaveInfo();
    }

    private void UpdateWaveInfo()
    {
        if (spawner == null) return;

        if (spawner.Wave == 0)
        {
            remainingText.text = "Get ready...";
            return;
        }

        if (spawner.TimeUntilNextWave > 0f)
        {
            int secs = Mathf.CeilToInt(spawner.TimeUntilNextWave);
            if (secs != lastCountdown)
            {
                lastCountdown = secs;
                lastRemaining = -1;
                remainingText.text = "Next wave in " + secs;
            }
            return;
        }

        lastCountdown = -1;
        int remaining = spawner.ZombiesRemaining;
        if (remaining != lastRemaining)
        {
            lastRemaining = remaining;
            remainingText.text = "Zombies: " + remaining;
        }
    }

    // ---------------------------------------------------------------- event handlers

    private void HandleHealthChanged(float current, float max)
    {
        healthPct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        healthFill.anchorMax = new Vector2(healthPct, 1f);
        healthText.text = Mathf.CeilToInt(current).ToString();
    }

    private void HandleDamaged(float amount)
    {
        flashAlpha = Mathf.Clamp(0.25f + amount * 0.01f, 0.25f, 0.6f);
    }

    private void HandleAmmoChanged(int magazine, int reserve)
    {
        ammoText.text = magazine + " / " + reserve;
        ammoText.color = magazine == 0 ? new Color(1f, 0.3f, 0.3f) : Color.white;
    }

    private void HandleReloadChanged(bool reloading)
    {
        reloadText.gameObject.SetActive(reloading);
    }

    private void HandleHit(bool killed)
    {
        hitAlpha = 1f;
        lastHitWasKill = killed;
        lastHitWasHeadshot = headshotPending;
        headshotPending = false;
        hitMarker.localScale = Vector3.one * (killed || lastHitWasHeadshot ? 1.5f : 1f);
    }

    private void HandleHeadshot()
    {
        // Weapon raises this just before OnHit, so HandleHit picks the flag up.
        headshotPending = true;
    }

    private void HandleWaveStarted(int wave)
    {
        waveText.text = "WAVE " + wave;
        Announce("WAVE " + wave);
    }

    private void HandleWaveCleared(int wave)
    {
        Announce("WAVE CLEARED");
    }

    private void HandleScoreChanged(int score)
    {
        scoreText.text = "SCORE " + score;
    }

    private void HandleStateChanged(GameState state)
    {
        panel.SetActive(state != GameState.Playing);

        if (state == GameState.Paused)
        {
            panelTitle.text = "PAUSED";
            panelTitle.color = Color.white;
            panelBody.text = "Esc  -  Resume\nR  -  Restart";
        }
        else if (state == GameState.GameOver)
        {
            int wave = spawner != null ? spawner.Wave : 0;
            int kills = spawner != null ? spawner.Kills : 0;
            panelTitle.text = "YOU DIED";
            panelTitle.color = new Color(0.85f, 0.1f, 0.1f);
            panelBody.text = "Wave " + wave + "     Kills " + kills + "     Score " + game.Score + "\n\nPress R to restart";
        }
    }

    private void Announce(string message)
    {
        announceText.text = message;
        announceText.color = Color.white;
        announceTimer = 2.5f;
    }

    // ---------------------------------------------------------------- UI construction

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasRect = canvasObject.GetComponent<RectTransform>();

        // Damage flash covers the whole screen, so it goes first (behind everything else).
        damageOverlay = MakeImage("DamageOverlay", canvasRect, new Color(0.6f, 0f, 0f, 0f),
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Health bar (bottom left).
        Image barBack = MakeImage("HealthBack", canvasRect, new Color(0f, 0f, 0f, 0.55f),
            Vector2.zero, Vector2.zero, new Vector2(40f, 40f), new Vector2(440f, 76f));
        Image fill = MakeImage("HealthFill", barBack.rectTransform, new Color(0.75f, 0.12f, 0.12f),
            Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        healthFill = fill.rectTransform;
        healthText = MakeText("HealthText", barBack.rectTransform, 24, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Ammo (bottom right).
        ammoText = MakeText("Ammo", canvasRect, 60, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-440f, 36f), new Vector2(-40f, 116f));
        reloadText = MakeText("Reloading", canvasRect, 28, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-440f, 120f), new Vector2(-40f, 160f));
        reloadText.text = "RELOADING";
        reloadText.color = new Color(1f, 0.85f, 0.2f);
        reloadText.gameObject.SetActive(false);

        // Wave info (top centre).
        waveText = MakeText("Wave", canvasRect, 40, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -80f), new Vector2(300f, -30f));
        waveText.text = "WAVE 0";
        remainingText = MakeText("Remaining", canvasRect, 26, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -118f), new Vector2(300f, -80f));
        remainingText.color = new Color(0.85f, 0.85f, 0.85f);

        // Score (top right).
        scoreText = MakeText("Score", canvasRect, 36, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-440f, -80f), new Vector2(-40f, -30f));

        // Crosshair dot.
        MakeImage("Crosshair", canvasRect, new Color(1f, 1f, 1f, 0.85f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-2.5f, -2.5f), new Vector2(2.5f, 2.5f));

        // Hit marker: two thin bars rotated into an X.
        hitMarker = MakeRect("HitMarker", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-16f, -16f), new Vector2(16f, 16f));
        hitLines = new Image[2];
        for (int i = 0; i < 2; i++)
        {
            hitLines[i] = MakeImage("Line" + i, hitMarker, new Color(1f, 1f, 1f, 0f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-15f, -1.5f), new Vector2(15f, 1.5f));
            hitLines[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
        }

        // Centre announcement.
        announceText = MakeText("Announce", canvasRect, 80, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-600f, 140f), new Vector2(600f, 250f));
        announceText.color = new Color(1f, 1f, 1f, 0f);

        // Pause / game over panel.
        Image dim = MakeImage("Panel", canvasRect, new Color(0f, 0f, 0f, 0.65f),
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        panel = dim.gameObject;
        panelTitle = MakeText("PanelTitle", dim.rectTransform, 110, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-700f, 20f), new Vector2(700f, 160f));
        panelBody = MakeText("PanelBody", dim.rectTransform, 38, TextAnchor.UpperCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-700f, -200f), new Vector2(700f, 10f));
        panel.SetActive(false);
    }

    private RectTransform MakeRect(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                   Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    private Image MakeImage(string objectName, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
                            Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rt = MakeRect(objectName, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text MakeText(string objectName, Transform parent, int size, TextAnchor alignment, Vector2 anchorMin,
                          Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rt = MakeRect(objectName, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        Text text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        rt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static Font LoadFont()
    {
#if UNITY_2022_2_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }
}
