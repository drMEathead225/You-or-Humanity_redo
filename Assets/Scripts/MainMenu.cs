// IEnumerator is needed for the music fade coroutine.
using System.Collections;
// List is used to remember which gameplay scripts we paused.
using System.Collections.Generic;
// Core Unity types: MonoBehaviour, AudioSource, GUI, Input, Time, etc.
using UnityEngine;

/// <summary>
/// Title screen for "YOU OR HUMANITY".
///
/// Sits in the game scene and freezes the game until you press PLAY (or Enter):
///   - the arena stays visible behind a dark, red-tinted overlay with drifting embers
///   - flickering blood-red title, PLAY / CONTROLS / QUIT
///   - looping background music that fades out when the game starts
/// Restarting after a game over skips the menu (turn on Show On Restart to change that).
/// Drawn with IMGUI like ZombiesHUD, so it needs no Canvas or EventSystem.
/// </summary>
// Run BEFORE every other script (-1000), so the menu can pause gameplay before zombies spawn or the camera moves.
[DefaultExecutionOrder(-1000)]
public class MainMenu : MonoBehaviour
{
    // =====================================================================
    // INSPECTOR SETTINGS
    // =====================================================================

    [Header("Title")]
    // Big red title text at the top of the screen.
    [SerializeField] private string title = "YOU OR HUMANITY";
    // Smaller grey text under the title (letters get spaced out automatically).
    [SerializeField] private string subtitle = "SURVIVE THE NIGHT";

    [Header("Music")]
    // The song that plays on the title screen. Drag an audio file in here.
    [Tooltip("Background music for the title screen. Loops.")]
    [SerializeField] private AudioClip music;
    // How loud the music is on the menu (0 = silent, 1 = full).
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
    // How many seconds the music takes to fade after pressing PLAY.
    [SerializeField] private float musicFadeTime = 1.5f;
    // If ticked, the music keeps playing quietly during gameplay instead of stopping.
    [Tooltip("Keep the music playing quietly during the game instead of fading it out.")]
    [SerializeField] private bool keepMusicInGame = false;
    // The volume used during gameplay when keepMusicInGame is ticked.
    [SerializeField, Range(0f, 1f)] private float inGameMusicVolume = 0.15f;

    [Header("Behaviour")]
    // If ticked, restarting after game over shows the title screen again. If not, you go straight back into the game.
    [Tooltip("Show the title screen again after Restart (R) on game over.")]
    [SerializeField] private bool showOnRestart = false;
    // Makes the whole menu bigger or smaller.
    [SerializeField, Range(0.5f, 2f)] private float uiScale = 1f;

    // =====================================================================
    // SHARED STATE (static = shared across scene reloads and readable by other scripts)
    // =====================================================================

    /// <summary>True while the title screen is showing. Other scripts (HUD, input) can check this.</summary>
    // ZombiesHUD checks this and hides itself while the menu is open.
    public static bool IsOpen { get; private set; }
    // Becomes true after the first time PLAY is pressed. Static values survive a scene reload,
    // which is how the menu knows a restart is happening and can skip itself.
    private static bool startedOnce;

    // Unity calls this when you press Play in the editor, resetting the static values.
    // Without it, "startedOnce" could stay true from a previous play session and skip the menu.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        startedOnce = false;
    }

    // =====================================================================
    // COLOURS
    // =====================================================================

    // Dark blood red for the title, divider line, and button highlights.
    private static readonly Color BloodRed = new Color(0.80f, 0.05f, 0.04f, 1f);
    // Off-white for button labels and control key names.
    private static readonly Color TextWhite = new Color(0.95f, 0.95f, 0.92f, 1f);
    // Grey for the subtitle and control descriptions.
    private static readonly Color TextGrey = new Color(0.65f, 0.65f, 0.65f, 1f);

    // =====================================================================
    // PRIVATE RUNTIME STATE
    // =====================================================================

    // The speaker that plays the menu music.
    private AudioSource source;
    // Every gameplay script we switched off, so we can switch them back on when PLAY is pressed.
    private readonly List<Behaviour> frozen = new List<Behaviour>();
    // true = showing the Controls page, false = showing the main title page.
    private bool showControls;
    // The time the menu opened, used to fade the title in.
    private float openTime;
    // Black fade-in after pressing PLAY (1 = fully black, 0 = clear).
    private float fadeAlpha;
    // s = UI scale factor. refW/refH = screen size in "1080p units", so the layout looks the same at any resolution.
    private float s, refW, refH;
    // The text style used for all menu text (created once, then reused).
    private GUIStyle style;
    // Dark-edges image drawn over the screen, built in code.
    private Texture2D texVignette;

    // One floating ember particle: x = horizontal position (0-1), speed = rise speed,
    // size = pixel size, phase = random offset so they don't all move together.
    private struct Ember { public float x, speed, size, phase; }
    // All the embers on screen.
    private Ember[] embers;

    // ------------------------------------------------------------------ lifecycle

    // Awake runs as soon as the scene loads - before other scripts' Start, thanks to the execution order above.
    private void Awake()
    {
        // Use the AudioSource on this object, or add one if it is missing.
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        // Don't play automatically - we start it ourselves.
        source.playOnAwake = false;
        // Loop the song forever.
        source.loop = true;
        // 0 = 2D sound: same volume everywhere, not affected by where the camera is.
        source.spatialBlend = 0f;
        // Keep playing even if the game uses AudioListener.pause.
        source.ignoreListenerPause = true;
        source.clip = music;

        // Build the dark-edges texture.
        texVignette = MakeVignette(256);
        // Create 45 embers with random positions, speeds and sizes.
        embers = new Ember[45];
        // Fixed seed (23) means the embers look the same every time the game starts.
        var rng = new System.Random(23);
        for (int i = 0; i < embers.Length; i++)
            embers[i] = new Ember
            {
                x = (float)rng.NextDouble(),                       // anywhere across the screen
                speed = 0.02f + (float)rng.NextDouble() * 0.05f,   // slow upward drift
                size = 2f + (float)rng.NextDouble() * 4f,          // 2-6 pixels
                phase = (float)rng.NextDouble() * 10f,             // random starting point
            };

        // This is a restart (PLAY was already pressed before) and we don't want the menu again:
        // skip the menu and let the game run straight away.
        if (startedOnce && !showOnRestart)
        {
            IsOpen = false;
            // Optionally keep the quiet in-game music going.
            if (keepMusicInGame) PlayMusic(inGameMusicVolume);
            return;
        }

        // First launch (or showOnRestart is ticked): show the title screen.
        Open();
    }

    // Runs when this object is destroyed (scene unload, leaving play mode).
    private void OnDestroy()
    {
        // Free the texture we created so it doesn't leak memory.
        if (texVignette != null) Destroy(texVignette);
        // If destroyed while the menu was open, unfreeze time so the game is never left stuck.
        if (IsOpen)
        {
            IsOpen = false;
            Time.timeScale = 1f;
        }
    }

    // Shows the title screen and pauses the game behind it.
    private void Open()
    {
        IsOpen = true;
        // Remember when we opened, for the title fade-in.
        openTime = Time.unscaledTime;

        // Pause gameplay scripts before their Start runs, so no zombies spawn and the camera does not move.
        Freeze(FindObjectsByType<FirstPersonController>()); // movement + mouse look
        Freeze(FindObjectsByType<WaveSpawner>());           // zombie spawning
        Freeze(FindObjectsByType<WeaponSwitcher>());        // weapon switching (1-3 / scroll)
        Freeze(FindObjectsByType<WeaponZoom>());            // aiming
        Freeze(FindObjectsByType<GameManager>());           // pause/game-over handling (stops it locking the cursor)

        // Stop all physics/animation time (zombies, bullets, etc. freeze).
        Time.timeScale = 0f;
        // Show the mouse so the menu buttons can be clicked.
        FreeCursor();

        // Remind you in the Console if no song has been assigned.
        if (music == null)
            Debug.Log("[MainMenu] No music assigned. Drag an AudioClip into MainMenu > Music " +
                      "(or put one in Assets/ZombieFPS/Audio/ and rebuild the scene).");
        // Start the menu music.
        PlayMusic(musicVolume);
    }

    // Switches off every script in the list and remembers it so StartGame can switch it back on.
    // <T> means it works for any script type (FirstPersonController, WaveSpawner, ...).
    private void Freeze<T>(T[] behaviours) where T : Behaviour
    {
        foreach (T b in behaviours)
        {
            // Skip missing scripts and ones that were already off (we don't want to turn those on later).
            if (b == null || !b.enabled) continue;
            b.enabled = false;
            frozen.Add(b);
        }
    }

    // Runs every frame: handles the fade and keyboard shortcuts.
    private void Update()
    {
        // Fade out the black screen after PLAY. unscaledDeltaTime keeps working even when time is frozen.
        if (fadeAlpha > 0f) fadeAlpha = Mathf.Max(0f, fadeAlpha - Time.unscaledDeltaTime * 1.2f);
        // Nothing else to do once the menu is closed.
        if (!IsOpen) return;

        // Enter / keypad Enter / Space: start the game (or leave the Controls page if it is open).
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            if (showControls) showControls = false;
            else StartGame();
        }
        // Escape on the Controls page goes back to the main page.
        if (Input.GetKeyDown(KeyCode.Escape) && showControls) showControls = false;
    }

    // Runs every frame after all Updates.
    private void LateUpdate()
    {
        if (!IsOpen) return;
        // Other scripts may try to unpause or lock the mouse; the menu wins while it is open.
        Time.timeScale = 0f;
        FreeCursor();
    }

    // Unlocks and shows the mouse pointer.
    private static void FreeCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Closes the menu and starts gameplay (called by PLAY or Enter).
    private void StartGame()
    {
        // Guard so it can't run twice.
        if (!IsOpen) return;
        IsOpen = false;
        // Remember that we've played, so a restart skips the menu.
        startedOnce = true;
        // Start the fade-in from black.
        fadeAlpha = 1f;

        // Unfreeze time.
        Time.timeScale = 1f;
        // Switch every paused gameplay script back on (their Start runs now, so zombies begin spawning).
        foreach (Behaviour b in frozen)
            if (b != null) b.enabled = true;
        frozen.Clear();

        // Lock and hide the mouse for FPS controls.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Fade the music down to silent (or to the quiet in-game volume).
        StartCoroutine(FadeMusic(keepMusicInGame ? inGameMusicVolume : 0f));
    }

    // Quits the game. In the Unity editor it stops Play mode instead (Application.Quit does nothing in the editor).
    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ music

    // Starts the music at the given volume (does nothing if no song is assigned).
    private void PlayMusic(float volume)
    {
        if (music == null) return;
        source.clip = music;
        source.volume = volume;
        // Only call Play if it isn't already playing, so the song doesn't restart from the beginning.
        if (!source.isPlaying) source.Play();
    }

    // Coroutine: gradually changes the music volume to "target" over musicFadeTime seconds.
    private IEnumerator FadeMusic(float target)
    {
        if (music == null) yield break;
        float start = source.volume;
        // Each frame, move the volume a bit closer to the target.
        for (float t = 0f; t < musicFadeTime; t += Time.unscaledDeltaTime)
        {
            source.volume = Mathf.Lerp(start, target, t / musicFadeTime);
            // Wait one frame.
            yield return null;
        }
        // Land exactly on the target, and stop the song completely if it faded to silent.
        source.volume = target;
        if (target <= 0f) source.Stop();
    }

    // ------------------------------------------------------------------ drawing

    // OnGUI is Unity's immediate-mode UI. It runs several times per frame
    // (Layout, Repaint, mouse/keyboard events), so drawing only happens on Repaint.
    private void OnGUI()
    {
        // Create the text style the first time (GUI.skin is only available inside OnGUI).
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };

        // Lower depth = drawn on top. -200 puts the menu above the HUD.
        GUI.depth = -200;
        // Everything is laid out as if the screen were 1080 pixels tall, then scaled to the real size.
        s = Screen.height / 1080f * uiScale;
        refW = Screen.width / s;
        refH = Screen.height / s;

        if (IsOpen)
        {
            // Dark overlay, embers and glow first, then the page on top.
            DrawBackground();
            if (showControls) DrawControls();
            else DrawMain();
        }

        // Fade in from black after pressing PLAY.
        if (fadeAlpha > 0f && Event.current.type == EventType.Repaint)
            Box(0, 0, refW, refH, new Color(0f, 0f, 0f, fadeAlpha));

        // Reset the tint so other OnGUI scripts are not affected.
        GUI.color = Color.white;
    }

    // Draws the dark red overlay, vignette, glow, and floating embers.
    private void DrawBackground()
    {
        // Only draw during the Repaint pass.
        if (Event.current.type != EventType.Repaint) return;

        float t = Time.unscaledTime;
        // Very dark red layer over the arena (78% opaque, so you can still faintly see the map).
        Box(0, 0, refW, refH, new Color(0.02f, 0.0f, 0.0f, 0.78f));
        // Darken the screen edges with the vignette texture.
        GUI.color = new Color(0f, 0f, 0f, 0.95f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texVignette, ScaleMode.StretchToFill, true);

        // Slow red "breathing" glow from the bottom.
        // Brightness rises and falls with a sine wave.
        float breathe = 0.10f + 0.05f * Mathf.Sin(t * 0.8f);
        // 6 stacked strips, strongest at the bottom and fading out higher up.
        for (int i = 0; i < 6; i++)
            Box(0, refH - (i + 1) * 60f, refW, 60f, new Color(0.5f, 0.02f, 0.0f, breathe * (6 - i) / 6f));

        // Embers drifting up.
        foreach (Ember e in embers)
        {
            // Vertical position: rises over time and wraps from the top back to the bottom (Repeat).
            float y = 1f - Mathf.Repeat(e.phase * 0.1f + t * e.speed, 1f);
            // Slight side-to-side sway.
            float x = e.x + Mathf.Sin(t * 0.6f + e.phase) * 0.01f;
            // Twinkle brightness.
            float flicker = 0.5f + 0.5f * Mathf.Sin(t * 5f + e.phase * 3f);
            // Orange square; brighter near the bottom, fading as it rises.
            Box(x * refW, y * refH, e.size, e.size, new Color(1f, 0.45f, 0.1f, 0.25f + 0.45f * flicker * y));
        }
    }

    // Draws the main page: title, subtitle, PLAY/CONTROLS/QUIT, and "PRESS ENTER TO START".
    private void DrawMain()
    {
        float t = Time.unscaledTime;
        // Fade-in amount: goes 0 -> 1 over the first 1.5 seconds after opening.
        float appear = Mathf.Clamp01((t - openTime) / 1.5f);
        // Horizontal centre of the screen.
        float cx = refW / 2f;
        // Title sits 22% of the way down the screen.
        float titleY = refH * 0.22f;

        if (Event.current.type == EventType.Repaint)
        {
            // Title: fit to the screen width, flicker like a failing light, with a dark red glow.
            // Font size up to 140, shrunk if needed to fit 88% of the screen width.
            int size = FitSize(title, 140, refW * 0.88f);
            // Every so often (when the noise is high) the title dims to 55% - like a flickering light.
            float flicker = Mathf.PerlinNoise(t * 6f, 0f) > 0.85f ? 0.55f : 1f;
            // Occasionally nudge the title 4 pixels sideways for a glitchy feel.
            float jitter = Mathf.PerlinNoise(0f, t * 9f) > 0.92f ? 4f : 0f;

            // Glow: draw faint dark-red copies of the title offset in 4 steps around it.
            Color glow = new Color(0.45f, 0f, 0f, 0.18f * appear);
            for (int i = 1; i <= 4; i++)
            {
                float o = i * 3f;
                Text(-o, titleY - o, refW, 170, title, size, glow, TextAnchor.MiddleCenter, false);
                Text(o, titleY + o, refW, 170, title, size, glow, TextAnchor.MiddleCenter, false);
            }
            // The main blood-red title on top, with fade-in and flicker applied.
            Color tc = BloodRed; tc.a = appear * flicker;
            Text(jitter, titleY, refW, 170, title, size, tc, TextAnchor.MiddleCenter, true);

            // Red line under the title that grows outward from the centre as the menu fades in.
            Box(cx - 260f * appear, titleY + 170f, 520f * appear, 3f, new Color(0.8f, 0.05f, 0.04f, 0.8f));
            // Subtitle with spaced-out letters ("S U R V I V E ...").
            Color sc = TextGrey; sc.a = appear;
            Text(0, titleY + 182f, refW, 40, Spaced(subtitle), 22, sc, TextAnchor.MiddleCenter, true);
        }

        // Buttons start 56% down the screen, 70 units apart. MenuButton returns true when clicked.
        float by = refH * 0.56f;
        if (MenuButton(by, "PLAY")) StartGame();
        if (MenuButton(by + 70f, "CONTROLS")) showControls = true;
        if (MenuButton(by + 140f, "QUIT")) Quit();

        if (Event.current.type == EventType.Repaint)
        {
            // Pulsing hint text near the bottom of the screen.
            float pulse = 0.45f + 0.35f * Mathf.Sin(t * 3f);
            Text(0, refH - 80f, refW, 30, "PRESS ENTER TO START", 18, new Color(1f, 1f, 1f, pulse), TextAnchor.MiddleCenter, true);
        }
    }

    // Draws the Controls page: a two-column list of keys and what they do, plus a BACK button.
    private void DrawControls()
    {
        float top = refH * 0.22f;
        if (Event.current.type == EventType.Repaint)
        {
            // Page heading.
            Text(0, top, refW, 70, "CONTROLS", 54, BloodRed, TextAnchor.MiddleCenter, true);
            // Each row: { key, action }. Edit this list if you change your controls.
            string[,] rows =
            {
                { "WASD", "Move" }, { "SHIFT", "Sprint" }, { "SPACE", "Jump" }, { "MOUSE", "Look" },
                { "LEFT MOUSE", "Fire" }, { "RIGHT MOUSE", "Aim / zoom" }, { "R", "Reload" },
                { "1 - 3 / SCROLL", "Switch weapon" }, { "F", "Flashlight" }, { "ESC", "Pause" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                // Each row is 38 units below the last.
                float y = top + 95f + i * 38f;
                // Key name, right-aligned just left of the centre (white).
                Text(0, y, refW / 2f - 20f, 34, rows[i, 0], 22, TextWhite, TextAnchor.MiddleRight, true);
                // Action, left-aligned just right of the centre (grey).
                Text(refW / 2f + 20f, y, refW / 2f, 34, rows[i, 1], 22, TextGrey, TextAnchor.MiddleLeft, true);
            }
        }
        // BACK button placed under the last row (10 rows x 38 + a gap).
        if (MenuButton(top + 95f + 10 * 38f + 30f, "BACK")) showControls = false;
    }

    // Draws one centred menu button and returns true on the frame it is clicked.
    // On hover it gets a dark red background, red bars on each side, and bigger white text.
    private bool MenuButton(float y, string label)
    {
        // Button area: 400 wide x 56 tall, centred horizontally.
        Rect r = R(refW / 2f - 200f, y, 400f, 56f);
        // Is the mouse over the button?
        bool hover = r.Contains(Event.current.mousePosition);

        if (Event.current.type == EventType.Repaint)
        {
            if (hover)
            {
                // Highlight background plus left and right red bars.
                Box(refW / 2f - 200f, y, 400f, 56f, new Color(0.5f, 0.02f, 0.02f, 0.35f));
                Box(refW / 2f - 200f, y, 5f, 56f, BloodRed);
                Box(refW / 2f + 195f, y, 5f, 56f, BloodRed);
            }
            // Label: size 34 and white when hovered, size 30 and slightly dimmer otherwise.
            Text(refW / 2f - 200f, y, 400f, 56f, label, hover ? 34 : 30, hover ? Color.white : TextWhite * 0.85f, TextAnchor.MiddleCenter, true);
        }

        // An invisible GUI button over the same area handles the actual click.
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // ------------------------------------------------------------------ helpers

    // Converts a rectangle in 1080p layout units into real screen pixels.
    private Rect R(float x, float y, float w, float h) { return new Rect(x * s, y * s, w * s, h * s); }

    // Draws a solid coloured rectangle (a white texture tinted with the colour).
    private void Box(float x, float y, float w, float h, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(R(x, y, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
    }

    // Draws text at a position, size, colour and alignment. If shadow is true, a dark copy is drawn
    // 3 units down-right first so the text stands out from the background.
    private void Text(float x, float y, float w, float h, string t, int size, Color c, TextAnchor anchor, bool shadow)
    {
        GUI.color = Color.white;
        // Scale the font with the screen (never below 1).
        style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
        style.alignment = anchor;
        Rect r = R(x, y, w, h);
        if (shadow)
        {
            // Shadow fades with the text (uses the text's alpha).
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
            GUI.Label(new Rect(r.x + 3f * s, r.y + 3f * s, r.width, r.height), t, style);
        }
        style.normal.textColor = c;
        GUI.Label(r, t, style);
    }

    /// <summary>Largest font size (up to max) that fits the text in the given width.</summary>
    // Measures the text at the max size; if too wide, shrinks the size in proportion (minimum 10).
    private int FitSize(string text, int max, float width)
    {
        style.fontSize = Mathf.RoundToInt(max * s);
        float w = style.CalcSize(new GUIContent(text)).x / s;
        return w <= width ? max : Mathf.Max(10, Mathf.FloorToInt(max * width / w));
    }

    // Puts a space between every letter: "NIGHT" -> "N I G H T". Used for the subtitle look.
    private static string Spaced(string text)
    {
        var chars = new System.Text.StringBuilder();
        foreach (char ch in text) chars.Append(ch).Append(' ');
        // Remove the extra space at the end.
        return chars.ToString().TrimEnd();
    }

    // Builds a square texture that is see-through in the middle and gets more solid toward the edges.
    // Tinted black, it darkens the corners of the screen (a "vignette").
    private static Texture2D MakeVignette(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,       // don't repeat at the edges
            filterMode = FilterMode.Bilinear,       // smooth when stretched to full screen
            hideFlags = HideFlags.HideAndDontSave,  // don't save it into the scene
        };
        var px = new Color[size * size];
        // Centre point of the texture.
        Vector2 c = new Vector2((size - 1) / 2f, (size - 1) / 2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance from the centre (0 = middle, about 1 = edge, more in the corners).
                float d = Vector2.Distance(new Vector2(x, y), c) / (size / 2f);
                // Clear inside 35% of the radius, smoothly getting darker out to the corners.
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.25f, d)));
            }
        // Upload the pixels to the graphics card.
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
