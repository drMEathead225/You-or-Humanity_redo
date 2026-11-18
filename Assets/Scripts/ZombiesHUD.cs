using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Black Ops style "Zombies" HUD for the Zombie FPS test scene.
///
/// Drawn with IMGUI (OnGUI), so it needs no Canvas, EventSystem, fonts or sprites and always renders
/// on top of the camera in both the Built-in pipeline and URP.
///
/// Layout (like BO3 Zombies):
///   top-left      zone name + red chalk round tally (numbers after round 5)
///   bottom-left   portrait, points panel with "+10" popups, health segments
///   bottom-centre weapon slots as perk-style diamonds (locked ones show the unlock round)
///   bottom-right  weapon name panel + ammo circle (magazine / reserve), reload bar
///   centre        dynamic crosshair, hit markers, reload / no-ammo prompts
///   overlays      damage vignette, PAUSED, GAME OVER "You Survived N Rounds"
///
/// It reads values from your existing scripts by name (see the *Names arrays below), so it works without
/// changing them. If something shows "--", check the Console: the HUD logs which field it could not find
/// and lists the fields that exist, so you can add the right name to the array.
/// </summary>
[DefaultExecutionOrder(1000)]
public class ZombiesHUD : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private string zoneName = "Test Arena: Holding Pen";
    [SerializeField] private string playerInitial = "S";

    [Header("Points (Black Ops style)")]
    [Tooltip("Always use the HUD's own points (per hit + per kill), even if GameManager has a score.")]
    [SerializeField] private bool useOwnPoints = false;
    [SerializeField] private int startingPoints = 500;
    [SerializeField] private int pointsPerHit = 10;
    [SerializeField] private int pointsPerKill = 60;

    [Header("Layout")]
    [SerializeField, Range(0.5f, 2f)] private float uiScale = 1f;
    [Tooltip("Disables the old HUD component so the two do not draw on top of each other.")]
    [SerializeField] private bool hideLegacyHud = true;
    [SerializeField] private bool showZombieCounter = true;

    // ------------------------------------------------------------------ colours

    private static readonly Color ChalkRed = new Color(0.78f, 0.05f, 0.04f, 1f);
    private static readonly Color PanelDark = new Color(0.06f, 0.06f, 0.07f, 0.85f);
    private static readonly Color PanelEdge = new Color(0.60f, 0.60f, 0.63f, 0.55f);
    private static readonly Color PointsYellow = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color TextWhite = new Color(0.96f, 0.96f, 0.93f, 1f);
    private static readonly Color TextGrey = new Color(0.72f, 0.72f, 0.72f, 1f);
    private static readonly Color HealthGreen = new Color(0.35f, 0.95f, 0.45f, 1f);

    private static readonly Color[] SlotColors =
    {
        new Color(0.20f, 0.45f, 0.85f, 0.95f), // pistol - blue
        new Color(0.80f, 0.18f, 0.15f, 0.95f), // rifle - red
        new Color(0.90f, 0.60f, 0.15f, 0.95f), // shotgun - orange
        new Color(0.45f, 0.75f, 0.30f, 0.95f),
        new Color(0.60f, 0.35f, 0.80f, 0.95f),
    };

    // ------------------------------------------------------------------ names looked up on your scripts (case-insensitive)

    private static readonly string[] HealthNames = { "CurrentHealth", "Current", "CurrentHP", "HP", "Health", "HealthValue", "Value" };
    private static readonly string[] MaxHealthNames = { "MaxHealth", "Max", "MaxHP" };
    private static readonly string[] DeadNames = { "IsDead", "Dead" };
    private static readonly string[] ScoreNames = { "Score", "Points", "CurrentScore", "PlayerScore", "PlayerPoints" };
    private static readonly string[] KillNames = { "Kills", "KillCount", "TotalKills", "ZombiesKilled" };
    private static readonly string[] WaveNames = { "CurrentWave", "Wave", "WaveNumber", "Round", "CurrentRound", "RoundNumber" };
    private static readonly string[] AliveNames = { "AliveCount", "ZombiesAlive", "Alive", "AliveZombies", "ZombiesRemaining", "Remaining", "RemainingCount", "EnemiesRemaining" };
    private static readonly string[] GameOverNames = { "IsGameOver", "GameOver", "GameIsOver", "IsOver" };
    private static readonly string[] MagNames = { "AmmoInMagazine", "AmmoInMag", "CurrentAmmo", "MagazineAmmo", "MagAmmo", "AmmoInClip", "ClipAmmo", "CurrentMagazine", "Ammo", "Magazine" };
    private static readonly string[] ReserveNames = { "ReserveAmmo", "Reserve", "AmmoReserve", "TotalAmmo", "ReserveCount", "SpareAmmo" };
    private static readonly string[] ReloadNames = { "IsReloading", "Reloading" };
    private static readonly string[] DataNames = { "Data", "WeaponData" };
    private static readonly string[] CurrentWeaponNames = { "CurrentWeapon", "ActiveWeapon", "Current", "Equipped" };
    private static readonly string[] SlotsNames = { "slots" };
    private static readonly string[] SlotWeaponNames = { "weapon" };
    private static readonly string[] SlotUnlockNames = { "unlockWave" };

    // ------------------------------------------------------------------ state

    private float s, refW, refH;
    private GUIStyle style;
    private Texture2D texCircle, texPanelRight, texPanelLeft, texChalk, texVignette;

    private GameManager gm;
    private WaveSpawner spawner;
    private WeaponSwitcher switcher;
    private GameObject player;
    private Health playerHealth;
    private CharacterController playerCC;
    private Weapon[] weapons = new Weapon[0];
    private float nextRefresh;

    private struct ZombieState { public float start, last; public bool dead; }
    private sealed class RefEq : IEqualityComparer<ZombieAI>
    {
        public bool Equals(ZombieAI a, ZombieAI b) { return ReferenceEquals(a, b); }
        public int GetHashCode(ZombieAI o) { return RuntimeHelpers.GetHashCode(o); }
    }
    private readonly Dictionary<ZombieAI, ZombieState> zombies = new Dictionary<ZombieAI, ZombieState>(new RefEq());
    private readonly List<ZombieAI> tmpKeys = new List<ZombieAI>();

    private struct Popup { public int amount; public float time; public float x; }
    private readonly List<Popup> popups = new List<Popup>();

    private int ownPoints, ownKills;
    private int displayedPoints = int.MinValue;
    private float hitMarkerTime = -10f;
    private bool hitMarkerKill;
    private int wave = -1;
    private float roundFlashTime = -10f;
    private float playerHp = -1f, playerMaxHp = 100f, lastPlayerHp = -1f, hurtTime = -10f;
    private bool hasPlayerHp;
    private Weapon activeWeapon;
    private int lastMag = -1;
    private float recoilKick;
    private bool reloading;
    private float reloadStart;
    private bool loggedDiagnostics;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        ownPoints = startingPoints;
        texCircle = MakeCircle(128);
        texPanelRight = MakePanel(256, 64, 34, true);
        texPanelLeft = MakePanel(256, 64, 34, false);
        texChalk = MakeChalk(128, 16);
        texVignette = MakeVignette(256);
    }

    private void Start()
    {
        Refresh();
        if (hideLegacyHud)
        {
            HUD legacy = GetComponent<HUD>();
            if (legacy == null) legacy = FindFirstObjectByType<HUD>();
            if (legacy != null) legacy.enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (Texture2D t in new[] { texCircle, texPanelRight, texPanelLeft, texChalk, texVignette })
            if (t != null) Destroy(t);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefresh) Refresh();

        TrackZombies();
        TrackPlayer();
        TrackWeapon();
        TrackScoreAndWave();

        recoilKick = Mathf.MoveTowards(recoilKick, 0f, Time.deltaTime * 60f);

        for (int i = popups.Count - 1; i >= 0; i--)
            if (Time.unscaledTime - popups[i].time > 1.1f) popups.RemoveAt(i);

        if (!loggedDiagnostics && Time.timeSinceLevelLoad > 2f) LogDiagnostics();
    }

    private void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.25f;

        if (gm == null) gm = FindFirstObjectByType<GameManager>();
        if (spawner == null) spawner = FindFirstObjectByType<WaveSpawner>();
        if (switcher == null) switcher = FindFirstObjectByType<WeaponSwitcher>();
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerHealth = player.GetComponent<Health>();
                playerCC = player.GetComponent<CharacterController>();
                weapons = player.GetComponentsInChildren<Weapon>(true);
            }
        }

        // Pick up newly spawned zombies; count destroyed ones that were damaged as kills.
        foreach (ZombieAI z in FindObjectsByType<ZombieAI>())
        {
            if (zombies.ContainsKey(z)) continue;
            Health h = z.GetComponent<Health>();
            if (h != null && Rx.TryFloat(h, out float hp, HealthNames))
                zombies[z] = new ZombieState { start = hp, last = hp, dead = hp <= 0f };
        }

        tmpKeys.Clear();
        foreach (var kv in zombies)
            if (kv.Key == null) tmpKeys.Add(kv.Key);
        foreach (ZombieAI k in tmpKeys)
        {
            ZombieState st = zombies[k];
            if (!st.dead && st.last < st.start - 0.01f) RegisterHit(true);
            zombies.Remove(k);
        }
    }

    // ------------------------------------------------------------------ tracking

    private void TrackZombies()
    {
        tmpKeys.Clear();
        tmpKeys.AddRange(zombies.Keys);
        foreach (ZombieAI z in tmpKeys)
        {
            if (z == null) continue;
            ZombieState st = zombies[z];
            if (st.dead) continue;

            Health h = z.GetComponent<Health>();
            if (h == null || !Rx.TryFloat(h, out float hp, HealthNames)) continue;

            bool deadFlag = Rx.TryBool(h, out bool d, DeadNames) && d;
            if (hp < st.last - 0.01f || deadFlag)
            {
                bool killed = hp <= 0f || deadFlag;
                RegisterHit(killed);
                st.dead = killed;
            }
            st.last = hp;
            zombies[z] = st;
        }
    }

    private void RegisterHit(bool killed)
    {
        ownPoints += pointsPerHit + (killed ? pointsPerKill : 0);
        if (killed) ownKills++;
        hitMarkerTime = Time.unscaledTime;
        hitMarkerKill = killed;
    }

    private void TrackPlayer()
    {
        hasPlayerHp = playerHealth != null && Rx.TryFloat(playerHealth, out playerHp, HealthNames);
        if (playerHealth != null && Rx.TryFloat(playerHealth, out float max, MaxHealthNames) && max > 0f) playerMaxHp = max;

        if (hasPlayerHp)
        {
            if (lastPlayerHp >= 0f && playerHp < lastPlayerHp - 0.01f) hurtTime = Time.unscaledTime;
            lastPlayerHp = playerHp;
        }
    }

    private void TrackWeapon()
    {
        Weapon w = FindActiveWeapon();
        if (w != activeWeapon)
        {
            activeWeapon = w;
            lastMag = -1;
            reloading = false;
        }
        if (w == null) return;

        if (Rx.TryInt(w, out int mag, MagNames))
        {
            if (lastMag >= 0 && mag < lastMag) recoilKick = Mathf.Min(recoilKick + 10f, 28f);
            lastMag = mag;
        }

        bool nowReloading = Rx.TryBool(w, out bool r, ReloadNames) && r;
        if (nowReloading && !reloading) reloadStart = Time.time;
        reloading = nowReloading;
    }

    private Weapon FindActiveWeapon()
    {
        if (switcher != null && Rx.TryGet(switcher, out object cur, CurrentWeaponNames) && cur is Weapon cw && cw != null)
            return cw;
        foreach (Weapon w in weapons)
            if (w != null && w.isActiveAndEnabled) return w;
        return null;
    }

    private void TrackScoreAndWave()
    {
        int points = CurrentPoints();
        if (displayedPoints != int.MinValue && points > displayedPoints)
        {
            popups.Add(new Popup { amount = points - displayedPoints, time = Time.unscaledTime, x = UnityEngine.Random.Range(0f, 40f) });
            if (popups.Count > 8) popups.RemoveAt(0);
        }
        displayedPoints = points;

        int w = 0;
        if (!Rx.TryInt(spawner, out w, WaveNames)) Rx.TryInt(gm, out w, WaveNames);
        if (w != wave)
        {
            if (wave >= 0 && w > wave) roundFlashTime = Time.unscaledTime;
            wave = w;
        }
    }

    private int CurrentPoints()
    {
        if (!useOwnPoints)
        {
            if (Rx.TryInt(gm, out int score, ScoreNames)) return score;
            if (Rx.TryInt(spawner, out score, ScoreNames)) return score;
        }
        return ownPoints;
    }

    private int CurrentKills()
    {
        if (Rx.TryInt(gm, out int k, KillNames)) return k;
        if (Rx.TryInt(spawner, out k, KillNames)) return k;
        return ownKills;
    }

    private int ZombiesAlive()
    {
        if (Rx.TryInt(spawner, out int a, AliveNames)) return a;
        if (Rx.TryInt(gm, out a, AliveNames)) return a;
        int n = 0;
        foreach (var kv in zombies) if (kv.Key != null && !kv.Value.dead) n++;
        return n;
    }

    private bool IsGameOver()
    {
        if (Rx.TryBool(gm, out bool over, GameOverNames) && over) return true;
        if (playerHealth != null && Rx.TryBool(playerHealth, out bool dead, DeadNames) && dead) return true;
        return hasPlayerHp && playerHp <= 0f;
    }

    private static WeaponData GetData(Weapon w)
    {
        return Rx.TryGet(w, out object v, DataNames) ? v as WeaponData : null;
    }

    private void LogDiagnostics()
    {
        loggedDiagnostics = true;
        var missing = new List<string>();

        if (playerHealth != null && !Rx.TryFloat(playerHealth, out _, HealthNames))
            missing.Add("current health on Health (HealthNames). Fields on Health: " + Rx.Describe(playerHealth));
        if (spawner != null && !Rx.TryInt(spawner, out _, WaveNames) && !Rx.TryInt(gm, out _, WaveNames))
            missing.Add("wave number (WaveNames). Fields on WaveSpawner: " + Rx.Describe(spawner));
        if (activeWeapon != null && !Rx.TryInt(activeWeapon, out _, MagNames))
            missing.Add("magazine ammo on Weapon (MagNames). Fields on Weapon: " + Rx.Describe(activeWeapon));
        if (activeWeapon != null && !Rx.TryInt(activeWeapon, out _, ReserveNames))
            missing.Add("reserve ammo on Weapon (ReserveNames).");

        if (missing.Count > 0)
            Debug.LogWarning("[ZombiesHUD] Could not find:\n- " + string.Join("\n- ", missing) +
                             "\nAdd the matching field/property name to the arrays at the top of ZombiesHUD.cs.");

        if (!useOwnPoints && !Rx.TryInt(gm, out _, ScoreNames) && !Rx.TryInt(spawner, out _, ScoreNames))
            Debug.Log("[ZombiesHUD] No score found on GameManager - using Black Ops style points (" +
                      pointsPerHit + " per hit, +" + pointsPerKill + " per kill).");
    }

    // ------------------------------------------------------------------ drawing

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (MainMenu.IsOpen) return; // the title screen covers everything

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                richText = false,
            };
        }

        GUI.depth = -100;
        s = Screen.height / 1080f * uiScale;
        refW = Screen.width / s;
        refH = Screen.height / s;

        bool gameOver = IsGameOver();

        DrawVignette();

        if (!gameOver)
        {
            DrawZoneAndRound();
            DrawPoints();
            DrawSlots();
            DrawWeapon();
            DrawCrosshair();
            DrawHitMarker();
            DrawPrompts();
            DrawZombieCounter();
        }

        if (gameOver) DrawGameOver();
        else if (Time.timeScale == 0f) DrawPaused();

        GUI.color = Color.white;
    }

    private void DrawZoneAndRound()
    {
        Text(40, 22, 800, 40, zoneName, 26, TextGrey, TextAnchor.UpperLeft);
        if (wave <= 0) return;

        // When a new round starts the tally flashes white like in Black Ops.
        float flash = 1f - Mathf.Clamp01((Time.unscaledTime - roundFlashTime) / 3f);
        float pulse = flash > 0f ? (Mathf.Sin(Time.unscaledTime * 10f) * 0.5f + 0.5f) * flash : 0f;
        Color c = Color.Lerp(ChalkRed, Color.white, pulse);

        if (wave <= 5) DrawTally(wave, new Vector2(56f, 74f), c);
        else Text(40, 56, 400, 120, wave.ToString(), 104, c, TextAnchor.UpperLeft);
    }

    private void DrawTally(int n, Vector2 start, Color c)
    {
        const float h = 86f, gap = 26f, width = 10f;
        for (int i = 0; i < Mathf.Min(n, 4); i++)
        {
            float x = start.x + i * gap;
            float j = Jitter(i);
            Line(new Vector2(x + j * 4f, start.y), new Vector2(x - j * 3f, start.y + h), width, c, texChalk);
        }
        if (n >= 5)
            Line(new Vector2(start.x - 16f, start.y + h * 0.22f), new Vector2(start.x + 3f * gap + 16f, start.y + h * 0.72f), width, c, texChalk);
    }

    private void DrawPoints()
    {
        float top = refH - 142f;

        // Points panel (drawn first so the portrait overlaps its left end).
        Tex(R(146, top - 2, 314, 84), texPanelRight, PanelEdge);
        Tex(R(148, top, 310, 80), texPanelRight, PanelDark);
        Text(170, top - 4, 250, 56, Mathf.Max(0, displayedPoints).ToString(), 46, TextWhite, TextAnchor.MiddleCenter);

        // Health segments under the points, like the bar in the reference.
        float frac = hasPlayerHp ? Mathf.Clamp01(playerHp / Mathf.Max(1f, playerMaxHp)) : 1f;
        Color hc = frac > 0.5f ? HealthGreen : frac > 0.25f ? PointsYellow : ChalkRed;
        Text(174, top + 46, 60, 26, hasPlayerHp ? Mathf.CeilToInt(Mathf.Max(0f, playerHp)).ToString() : "--", 18, TextWhite, TextAnchor.MiddleLeft);
        for (int i = 0; i < 5; i++)
        {
            float x = 222f + i * 38f;
            float fill = Mathf.Clamp01(frac * 5f - i);
            Box(x, top + 56, 32, 7, new Color(1f, 1f, 1f, 0.15f));
            if (fill > 0f) Box(x, top + 56, 32f * fill, 7, hc);
        }

        // Portrait.
        Vector2 pc = new Vector2(100f, refH - 100f);
        Tex(R(pc.x - 60, pc.y - 60, 120, 120), texCircle, PanelEdge);
        Tex(R(pc.x - 55, pc.y - 55, 110, 110), texCircle, new Color(0.11f, 0.11f, 0.13f, 0.97f));
        Text(pc.x - 55, pc.y - 55, 110, 110, playerInitial, 56, TextWhite, TextAnchor.MiddleCenter);

        // "+10" popups floating up from the panel.
        foreach (Popup p in popups)
        {
            float age = Time.unscaledTime - p.time;
            float a = 1f - Mathf.Clamp01(age / 1.1f);
            Color c = PointsYellow; c.a = a;
            Text(470 + p.x, top + 10 - age * 70f, 140, 30, "+" + p.amount, 26, c, TextAnchor.MiddleLeft);
        }
    }

    private void DrawSlots()
    {
        if (!Rx.TryGet(switcher, out object v, SlotsNames) || !(v is IList slots) || slots.Count == 0) return;

        int n = slots.Count;
        float spacing = 78f;
        float cy = refH - 88f;
        float startX = refW / 2f - spacing * (n - 1) / 2f;
        int currentRound = Mathf.Max(1, wave);

        Box(startX - 60f, cy + 40f, spacing * (n - 1) + 120f, 2f, PanelEdge);

        for (int i = 0; i < n; i++)
        {
            object slot = slots[i];
            Weapon w = Rx.TryGet(slot, out object wo, SlotWeaponNames) ? wo as Weapon : null;
            int unlock = Rx.TryInt(slot, out int u, SlotUnlockNames) ? u : 1;
            bool locked = currentRound < unlock;
            bool active = w != null && w == activeWeapon;

            Vector2 c = new Vector2(startX + i * spacing, cy);
            float size = active ? 50f : 42f;
            Color col = locked ? new Color(0.25f, 0.25f, 0.27f, 0.9f) : SlotColors[i % SlotColors.Length];
            if (!active && !locked) col *= new Color(0.75f, 0.75f, 0.75f, 1f);

            Diamond(c, size + 7f, new Color(1f, 1f, 1f, active ? 0.9f : 0.35f));
            Diamond(c, size, col);

            WeaponData d = GetData(w);
            string label = d != null && !string.IsNullOrEmpty(d.weaponName) ? d.weaponName : (w != null ? w.name : "");
            Text(c.x - 60, c.y - 62, 120, 20, label.ToUpperInvariant(), 13, locked ? TextGrey * 0.8f : TextGrey, TextAnchor.MiddleCenter);
            Text(c.x - 30, c.y - 15, 60, 30, locked ? "R" + unlock : (i + 1).ToString(), locked ? 15 : 20, TextWhite, TextAnchor.MiddleCenter);
        }
    }

    private void DrawWeapon()
    {
        Weapon w = activeWeapon;
        if (w == null) return;

        WeaponData d = GetData(w);
        string weaponName = d != null && !string.IsNullOrEmpty(d.weaponName) ? d.weaponName : w.name;
        Vector2 cc = new Vector2(refW - 100f, refH - 100f);
        float top = refH - 142f;
        float pw = 330f;
        float px = cc.x - 40f - pw;

        Tex(R(px - 2, top - 2, pw + 4, 84), texPanelLeft, PanelEdge);
        Tex(R(px, top, pw, 80), texPanelLeft, PanelDark);
        Text(px + 40, top + 6, pw - 100, 34, weaponName.ToUpperInvariant(), 26, TextWhite, TextAnchor.MiddleRight);

        string mode = d == null ? "" : d.pellets > 1 ? "PUMP ACTION" : d.automatic ? "FULL AUTO" : "SEMI AUTO";
        Text(px + 40, top + 38, pw - 100, 22, mode, 14, TextGrey, TextAnchor.MiddleRight);

        if (reloading && d != null && d.reloadTime > 0f)
        {
            float t = Mathf.Clamp01((Time.time - reloadStart) / d.reloadTime);
            Box(px + 70, top + 64, pw - 130, 5, new Color(1f, 1f, 1f, 0.15f));
            Box(px + 70, top + 64, (pw - 130) * t, 5, PointsYellow);
        }

        // Ammo circle.
        Tex(R(cc.x - 62, cc.y - 62, 124, 124), texCircle, PanelEdge);
        Tex(R(cc.x - 57, cc.y - 57, 114, 114), texCircle, new Color(0.08f, 0.08f, 0.09f, 0.97f));

        bool hasMag = Rx.TryInt(w, out int mag, MagNames);
        bool hasRes = Rx.TryInt(w, out int reserve, ReserveNames);
        int magSize = d != null ? Mathf.Max(1, d.magazineSize) : 1;
        bool low = hasMag && mag <= Mathf.CeilToInt(magSize * 0.25f);

        Text(cc.x - 57, cc.y - 52, 114, 66, hasMag ? mag.ToString() : "--", 52, low ? ChalkRed : TextWhite, TextAnchor.MiddleCenter);
        Box(cc.x - 26, cc.y + 14, 52, 2, new Color(1f, 1f, 1f, 0.3f));
        Text(cc.x - 57, cc.y + 16, 114, 30, hasRes ? reserve.ToString() : "--", 20, TextGrey, TextAnchor.MiddleCenter);
    }

    private void DrawCrosshair()
    {
        if (activeWeapon == null || Time.timeScale == 0f) return;

        WeaponData d = GetData(activeWeapon);
        float speed = playerCC != null ? new Vector2(playerCC.velocity.x, playerCC.velocity.z).magnitude : 0f;
        // Aiming down sights tightens the crosshair and fades it out, leaving a small centre dot.
        float aim = WeaponZoom.AimAmount;
        float gap = (9f + speed * 2.2f + recoilKick + (d != null ? d.spread * 3f * WeaponZoom.SpreadMultiplier : 0f)) * Mathf.Lerp(1f, 0.35f, aim);
        Vector2 c = new Vector2(refW / 2f, refH / 2f);
        Color col = new Color(1f, 1f, 1f, (reloading ? 0.35f : 0.85f) * (1f - aim));
        const float len = 12f, th = 2.5f;

        if (aim > 0.05f) Box(c.x - 1.5f, c.y - 1.5f, 3f, 3f, new Color(1f, 1f, 1f, 0.9f * aim));
        if (col.a <= 0.01f) return;

        Box(c.x - th / 2f, c.y - gap - len, th, len, col);
        Box(c.x - th / 2f, c.y + gap, th, len, col);
        Box(c.x - gap - len, c.y - th / 2f, len, th, col);
        Box(c.x + gap, c.y - th / 2f, len, th, col);
    }

    private void DrawHitMarker()
    {
        float age = Time.unscaledTime - hitMarkerTime;
        if (age > 0.22f) return;

        Color col = hitMarkerKill ? ChalkRed : Color.white;
        col.a = 1f - age / 0.22f;
        Vector2 c = new Vector2(refW / 2f, refH / 2f);
        foreach (Vector2 dir in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            Line(c + dir * 7f, c + dir * 17f, 3f, col, Texture2D.whiteTexture);
    }

    private void DrawPrompts()
    {
        if (activeWeapon == null || Time.timeScale == 0f) return;
        if (!Rx.TryInt(activeWeapon, out int mag, MagNames) || mag > 0 || reloading) return;

        bool hasRes = Rx.TryInt(activeWeapon, out int reserve, ReserveNames);
        string msg = !hasRes || reserve > 0 ? "Press R to Reload" : "No Ammo";
        Text(0, refH / 2f + 60f, refW, 36, msg, 24, msg == "No Ammo" ? ChalkRed : TextWhite, TextAnchor.MiddleCenter);
    }

    private void DrawZombieCounter()
    {
        if (!showZombieCounter || wave <= 0) return;
        Text(refW - 360, 22, 320, 30, "ZOMBIES  " + ZombiesAlive(), 20, TextGrey, TextAnchor.UpperRight);
    }

    private void DrawVignette()
    {
        if (!hasPlayerHp) return;
        float frac = Mathf.Clamp01(playerHp / Mathf.Max(1f, playerMaxHp));
        float low = Mathf.Clamp01((0.5f - frac) / 0.5f) * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f));
        float hurt = 1f - Mathf.Clamp01((Time.unscaledTime - hurtTime) / 0.6f);
        float a = Mathf.Max(low * 0.8f, hurt * 0.6f);
        if (a <= 0.01f) return;
        Tex(new Rect(0, 0, Screen.width, Screen.height), texVignette, new Color(0.55f, 0f, 0f, a));
    }

    private void DrawPaused()
    {
        Box(0, 0, refW, refH, new Color(0f, 0f, 0f, 0.5f));
        Text(0, refH * 0.38f, refW, 90, "PAUSED", 72, TextWhite, TextAnchor.MiddleCenter);
        Text(0, refH * 0.38f + 90f, refW, 40, "Press Esc to resume", 22, TextGrey, TextAnchor.MiddleCenter);
    }

    private void DrawGameOver()
    {
        Box(0, 0, refW, refH, new Color(0f, 0f, 0f, 0.65f));
        float y = refH * 0.30f;
        int rounds = Mathf.Max(1, wave);
        Text(0, y, refW, 130, "GAME OVER", 104, ChalkRed, TextAnchor.MiddleCenter);
        Text(0, y + 125f, refW, 50, "You Survived " + rounds + (rounds == 1 ? " Round" : " Rounds"), 34, TextWhite, TextAnchor.MiddleCenter);
        Text(0, y + 180f, refW, 40, "Points " + Mathf.Max(0, displayedPoints) + "     Kills " + CurrentKills(), 24, TextGrey, TextAnchor.MiddleCenter);
        Text(0, y + 240f, refW, 40, "Press R to restart", 22, TextGrey, TextAnchor.MiddleCenter);
    }

    // ------------------------------------------------------------------ draw helpers (reference units = 1080p)

    private Rect R(float x, float y, float w, float h)
    {
        return new Rect(x * s, y * s, w * s, h * s);
    }

    private static void Tex(Rect r, Texture t, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
    }

    private void Box(float x, float y, float w, float h, Color c)
    {
        Tex(R(x, y, w, h), Texture2D.whiteTexture, c);
    }

    private void Text(float x, float y, float w, float h, string t, int size, Color c, TextAnchor anchor)
    {
        GUI.color = Color.white;
        style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
        style.alignment = anchor;
        Rect r = R(x, y, w, h);

        style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * c.a);
        GUI.Label(new Rect(r.x + 2f * s, r.y + 2f * s, r.width, r.height), t, style);
        style.normal.textColor = c;
        GUI.Label(r, t, style);
    }

    private void Line(Vector2 a, Vector2 b, float width, Color c, Texture tex)
    {
        Vector2 pa = a * s, pb = b * s;
        Vector2 d = pb - pa;
        float len = d.magnitude;
        if (len < 0.01f) return;

        Matrix4x4 old = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, pa);
        Tex(new Rect(pa.x, pa.y - width * s * 0.5f, len, width * s), tex, c);
        GUI.matrix = old;
    }

    private void Diamond(Vector2 c, float size, Color col)
    {
        Matrix4x4 old = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, c * s);
        Tex(R(c.x - size / 2f, c.y - size / 2f, size, size), Texture2D.whiteTexture, col);
        GUI.matrix = old;
    }

    private static float Jitter(int i)
    {
        return Mathf.Sin(i * 12.9898f + 1.3f);
    }

    // ------------------------------------------------------------------ procedural textures

    private static Texture2D NewTex(int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    private static Texture2D MakeCircle(int size)
    {
        Texture2D t = NewTex(size, size);
        float r = size / 2f - 1f;
        Vector2 c = new Vector2((size - 1) / 2f, (size - 1) / 2f);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x, y), c) + 0.5f));
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    /// <summary>Dark plate with a pointed end (pointRight = tip on the right), like BO points / weapon panels.</summary>
    private static Texture2D MakePanel(int w, int h, int tip, bool pointRight)
    {
        Texture2D t = NewTex(w, h);
        var px = new Color[w * h];
        float cy = (h - 1) / 2f;
        for (int y = 0; y < h; y++)
        {
            float mid = 1f - Mathf.Abs(y - cy) / cy;
            float edge = (w - tip) + tip * mid;
            float shade = Mathf.Lerp(0.75f, 1f, y / (float)(h - 1));
            for (int x = 0; x < w; x++)
            {
                float xx = pointRight ? x : (w - 1 - x);
                float a = Mathf.Clamp01(edge - xx + 0.5f);
                px[y * w + x] = new Color(shade, shade, shade, a);
            }
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    /// <summary>Rough chalk / blood stroke used for the round tally marks.</summary>
    private static Texture2D MakeChalk(int w, int h)
    {
        Texture2D t = NewTex(w, h);
        var px = new Color[w * h];
        float cy = (h - 1) / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float across = 1f - Mathf.Abs(y - cy) / cy;
                float ends = Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / 8f);
                float noise = Mathf.PerlinNoise(x * 0.35f, y * 0.9f);
                float a = Mathf.Clamp01(across * 2.4f) * ends * Mathf.Lerp(0.55f, 1f, noise);
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    private static Texture2D MakeVignette(int size)
    {
        Texture2D t = NewTex(size, size);
        var px = new Color[size * size];
        Vector2 c = new Vector2((size - 1) / 2f, (size - 1) / 2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (size / 2f);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.2f, d)));
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // ------------------------------------------------------------------ reflection helper

    /// <summary>Reads fields/properties by name (case-insensitive, public or private) so the HUD works with the existing scripts.</summary>
    private static class Rx
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> Cache = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        private static bool IsNull(object target)
        {
            if (target == null) return true;
            return target is UnityEngine.Object uo && uo == null;
        }

        private static MemberInfo Find(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                try
                {
                    PropertyInfo p = t.GetProperty(name, Flags);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p;
                }
                catch (AmbiguousMatchException) { }

                try
                {
                    FieldInfo f = t.GetField(name, Flags);
                    if (f != null) return f;
                }
                catch (AmbiguousMatchException) { }
            }
            return null;
        }

        private static IEnumerable<object> Values(object target, string[] names)
        {
            if (IsNull(target)) yield break;

            Type type = target.GetType();
            if (!Cache.TryGetValue(type, out Dictionary<string, MemberInfo> map))
                Cache[type] = map = new Dictionary<string, MemberInfo>();

            foreach (string n in names)
            {
                if (!map.TryGetValue(n, out MemberInfo m)) map[n] = m = Find(type, n);
                if (m == null) continue;

                object v = null;
                bool ok = true;
                try { v = m is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)m).GetValue(target); }
                catch { ok = false; }
                if (ok) yield return v;
            }
        }

        public static bool TryGet(object target, out object value, string[] names)
        {
            foreach (object v in Values(target, names))
            {
                if (!IsNull(v)) { value = v; return true; }
            }
            value = null;
            return false;
        }

        public static bool TryFloat(object target, out float value, string[] names)
        {
            foreach (object v in Values(target, names))
            {
                if (v == null || v is bool || v is Enum || v is string || !(v is IConvertible c)) continue;
                try { value = Convert.ToSingle(c); return true; } catch { }
            }
            value = 0f;
            return false;
        }

        public static bool TryInt(object target, out int value, string[] names)
        {
            foreach (object v in Values(target, names))
            {
                if (v == null || v is bool || v is Enum || v is string) continue;
                if (v is ICollection col) { value = col.Count; return true; }
                if (!(v is IConvertible c)) continue;
                try { value = Mathf.RoundToInt(Convert.ToSingle(c)); return true; } catch { }
            }
            value = 0;
            return false;
        }

        public static bool TryBool(object target, out bool value, string[] names)
        {
            foreach (object v in Values(target, names))
            {
                if (v is bool b) { value = b; return true; }
            }
            value = false;
            return false;
        }

        /// <summary>Lists number/bool members of an object, used in the Console hint when a value is not found.</summary>
        public static string Describe(object target)
        {
            if (IsNull(target)) return "(none)";
            var names = new List<string>();
            for (Type t = target.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Flags & ~BindingFlags.IgnoreCase))
                    if (f.FieldType.IsPrimitive) names.Add(f.Name);
                foreach (PropertyInfo p in t.GetProperties(Flags & ~BindingFlags.IgnoreCase))
                    if (p.PropertyType.IsPrimitive && p.GetIndexParameters().Length == 0) names.Add(p.Name);
            }
            return names.Count > 0 ? string.Join(", ", names) : "(no number/bool fields)";
        }
    }
}
