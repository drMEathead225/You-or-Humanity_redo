// List/Dictionary collections used to store weapon hip positions and sensitivity values.
using System.Collections.Generic;
// Reflection lets this script read/write fields on other scripts (e.g. mouse sensitivity) without editing them.
using System.Reflection;
// Core Unity types: MonoBehaviour, Camera, Input, Mathf, Vector3, etc.
using UnityEngine;

/// <summary>
/// Aim down sights / zoom for the Zombie FPS player.
/// Put it on the Main Camera (next to the WeaponSwitcher). Hold right mouse button to aim:
///   - the camera field of view zooms in (per weapon, the rifle zooms the most)
///   - the active weapon (and its 3D model) slides from the hip to the centre of the screen
///   - mouse sensitivity drops so long-range aiming is easier
///   - WeaponZoom.SpreadMultiplier gets smaller - use it in Weapon.cs to tighten the spread
/// Aiming is blocked while reloading or paused.
/// </summary>
// This script must sit on an object that has a Camera (Unity adds one if missing), because it changes the FOV.
[RequireComponent(typeof(Camera))]
// Run AFTER most other scripts (default order is 0), so our weapon position/FOV changes are the final ones each frame.
[DefaultExecutionOrder(500)]
public class WeaponZoom : MonoBehaviour
{
    // =====================================================================
    // PER-WEAPON SETTINGS
    // =====================================================================

    // One block of zoom settings for a single weapon. [Serializable] makes it editable in the Inspector.
    [System.Serializable]
    public class ZoomSettings
    {
        // Which weapon these settings belong to. Matched against the weapon object's name or its WeaponData name.
        [Tooltip("Must match the weapon's GameObject name or its WeaponData weaponName.")]
        public string weaponName = "Rifle";
        // Camera field of view when fully aimed. Smaller number = more zoom (normal view is about 75).
        [Range(10f, 90f)] public float zoomFov = 45f;
        // How far the gun slides when aiming. X = left/right, Y = up/down, Z = toward/away from the camera.
        [Tooltip("How far the weapon moves from its hip position when fully aimed (local camera space).")]
        public Vector3 aimOffset = new Vector3(-0.25f, 0.08f, -0.05f);
        // Mouse speed while aimed. 0.6 = 60% of normal speed, so small adjustments are easier at range.
        [Range(0.05f, 1f)] public float sensitivityMultiplier = 0.6f;
        // Bullet spread while aimed. 0.35 = 35% of normal spread (more accurate). Read by Weapon.cs.
        [Range(0f, 1f)] public float spreadMultiplier = 0.35f;
    }

    // =====================================================================
    // INSPECTOR SETTINGS
    // =====================================================================

    [Header("Input")]
    // true = hold the button to aim. false = click once to aim, click again to stop (toggle).
    [SerializeField] private bool holdToAim = true;
    // Which mouse button aims. 1 = right mouse button (the standard for FPS games).
    [SerializeField, Tooltip("0 = left, 1 = right, 2 = middle")] private int mouseButton = 1;

    [Header("Feel")]
    // How quickly the zoom goes in and out. Bigger = faster/snappier, smaller = slower/smoother.
    [SerializeField, Tooltip("Higher = snappier zoom")] private float zoomSpeed = 14f;

    [Header("Per weapon")]
    // Settings used for any weapon that is not listed in the "weapons" array below.
    [SerializeField] private ZoomSettings fallback = new ZoomSettings { weaponName = "(any other)", zoomFov = 55f };
    // Default zoom settings for the three starting weapons. You can change these in the Inspector.
    [SerializeField]
    private ZoomSettings[] weapons =
    {
        // Pistol: light zoom, moderate accuracy boost.
        new ZoomSettings { weaponName = "Pistol",  zoomFov = 55f, aimOffset = new Vector3(-0.25f, 0.10f, -0.05f), sensitivityMultiplier = 0.70f, spreadMultiplier = 0.40f },
        // Rifle: strongest zoom and tightest spread - the long-range weapon.
        new ZoomSettings { weaponName = "Rifle",   zoomFov = 32f, aimOffset = new Vector3(-0.25f, 0.09f, -0.10f), sensitivityMultiplier = 0.45f, spreadMultiplier = 0.15f },
        // Shotgun: barely zooms and keeps most of its spread, since it is a close-range weapon.
        new ZoomSettings { weaponName = "Shotgun", zoomFov = 62f, aimOffset = new Vector3(-0.25f, 0.08f, -0.05f), sensitivityMultiplier = 0.80f, spreadMultiplier = 0.70f },
    };

    // =====================================================================
    // PUBLIC VALUES OTHER SCRIPTS CAN READ
    // (static = read them anywhere with WeaponZoom.IsAiming etc., no reference needed)
    // =====================================================================

    /// <summary>True while the player is holding aim.</summary>
    public static bool IsAiming { get; private set; }
    /// <summary>0 = hip, 1 = fully aimed (smoothed).</summary>
    // Used by ZombiesHUD (crosshair shrinks/fades) and Weapon.cs (less recoil when aimed).
    public static float AimAmount { get; private set; }
    /// <summary>Multiply your weapon spread by this (1 at the hip, smaller when aimed).</summary>
    // Used by Weapon.GetSpreadDirection and the HUD crosshair gap.
    public static float SpreadMultiplier { get; private set; } = 1f;

    // =====================================================================
    // PRIVATE RUNTIME STATE
    // =====================================================================

    // The camera on this object - the one we zoom.
    private Camera cam;
    // The camera's normal (un-zoomed) field of view, saved at startup so we can return to it.
    private float baseFov;
    // Every Weapon found under the player (active or not).
    private Weapon[] allWeapons = new Weapon[0];
    // The weapon currently held (the one that is switched on).
    private Weapon active;
    // Remembers whether aim is "on" when using toggle mode (holdToAim = false).
    private bool toggled;
    // Each weapon's normal hip position, so we can slide it to aim and back again without drifting.
    private readonly Dictionary<Weapon, Vector3> hipPositions = new Dictionary<Weapon, Vector3>();

    // The FirstPersonController (the mouse-look script) whose sensitivity we lower while aiming.
    private Component look;
    // The sensitivity fields found on that script (e.g. "mouseSensitivity").
    private readonly List<FieldInfo> sensitivityFields = new List<FieldInfo>();
    // The original value of each sensitivity field, so we always scale from the real value.
    private readonly List<float> sensitivityBase = new List<float>();

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    // Awake runs first, as soon as the object loads.
    private void Awake()
    {
        // Grab the camera and remember its normal field of view.
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;
    }

    // Start runs once, just before the first frame (after every object's Awake).
    private void Start()
    {
        // The top object of the hierarchy (the Player), so we can search everything under it.
        Transform root = transform.root;
        // Find all weapons, including hidden ones (the 'true' means "include inactive").
        allWeapons = root.GetComponentsInChildren<Weapon>(true);
        // Save where each weapon normally sits at the hip.
        foreach (Weapon w in allWeapons) hipPositions[w] = w.transform.localPosition;

        // Find the mouse sensitivity on FirstPersonController (any float field with "sensitivity" in its name).
        look = root.GetComponentInChildren<FirstPersonController>(true);
        if (look != null)
        {
            // Search public AND private instance fields.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo f in look.GetType().GetFields(flags))
            {
                // Skip anything that is not a float or does not have "sensitivity" in its name.
                if (f.FieldType != typeof(float) || !f.Name.ToLowerInvariant().Contains("sensitivity")) continue;
                // Remember the field and its starting value.
                sensitivityFields.Add(f);
                sensitivityBase.Add((float)f.GetValue(look));
            }
            // Warn in the Console if nothing was found (zoom still works, mouse just won't slow down).
            if (sensitivityFields.Count == 0)
                Debug.Log("[WeaponZoom] No 'sensitivity' field found on FirstPersonController - zoom will not slow the mouse.");
        }
    }

    // Runs when this script is switched off (e.g. paused by the main menu, or the player dies).
    // Puts everything back to normal so the game is never stuck zoomed in.
    private void OnDisable()
    {
        // Reset the shared values to "not aiming".
        IsAiming = false;
        AimAmount = 0f;
        SpreadMultiplier = 1f;
        // Restore the normal field of view.
        if (cam != null) cam.fieldOfView = baseFov;
        // Move every weapon back to its hip position.
        foreach (var kv in hipPositions)
            if (kv.Key != null) kv.Key.transform.localPosition = kv.Value;
        // Restore full mouse sensitivity.
        ApplySensitivity(1f);
    }

    // Runs every frame: reads input, smooths the aim amount, and applies the zoom.
    private void Update()
    {
        // Work out which weapon is in the player's hands right now.
        Weapon w = FindActive();
        // If the player switched weapons, snap the old one back to its hip position.
        if (w != active)
        {
            if (active != null && hipPositions.TryGetValue(active, out Vector3 hip)) active.transform.localPosition = hip;
            active = w;
        }

        // Toggle mode: flip aim on/off each time the button is clicked.
        if (!holdToAim && Input.GetMouseButtonDown(mouseButton)) toggled = !toggled;
        // Does the player WANT to aim? (button held, or toggle is on)
        bool wants = holdToAim ? Input.GetMouseButton(mouseButton) : toggled;
        // Are they ALLOWED to aim? Needs a weapon, game not paused (timeScale 0), and not reloading.
        bool canAim = w != null && Time.timeScale > 0f && !IsReloading(w);

        // Only aim if both are true.
        IsAiming = wants && canAim;
        // Cancel toggle aim if it got blocked (e.g. started reloading).
        if (!canAim) toggled = false;

        // Target: 1 = fully aimed, 0 = at the hip.
        float target = IsAiming ? 1f : 0f;
        // Smoothly move AimAmount toward the target. The Exp formula gives the same smooth speed at any frame rate.
        AimAmount = Mathf.Lerp(AimAmount, target, 1f - Mathf.Exp(-zoomSpeed * Time.unscaledDeltaTime));
        // Snap the last tiny bit so it settles exactly on 0 or 1.
        if (Mathf.Abs(AimAmount - target) < 0.001f) AimAmount = target;

        // Get the zoom settings for the held weapon.
        ZoomSettings z = SettingsFor(w);
        // Blend the camera FOV between normal and zoomed based on how far we are into aiming.
        cam.fieldOfView = Mathf.Lerp(baseFov, z.zoomFov, AimAmount);
        // Blend the spread multiplier (1 at hip, smaller when aimed) - Weapon.cs reads this.
        SpreadMultiplier = Mathf.Lerp(1f, z.spreadMultiplier, AimAmount);
        // Blend the mouse sensitivity down while aiming.
        ApplySensitivity(Mathf.Lerp(1f, z.sensitivityMultiplier, AimAmount));

        // Slide the weapon from its hip position toward the centre of the screen.
        if (w != null && hipPositions.TryGetValue(w, out Vector3 hipPos))
            w.transform.localPosition = hipPos + z.aimOffset * AimAmount;
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    // Returns the weapon that is currently switched on (WeaponSwitcher only keeps one active), or null if none.
    private Weapon FindActive()
    {
        foreach (Weapon w in allWeapons)
            if (w != null && w.isActiveAndEnabled) return w;
        return null;
    }

    // Picks the ZoomSettings for a weapon by matching its object name or its WeaponData name.
    private ZoomSettings SettingsFor(Weapon w)
    {
        // No weapon held: use the fallback settings.
        if (w == null) return fallback;
        // Read the weapon's private "data" field (its WeaponData asset) and get the weaponName from it, if present.
        string dataName = w.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(w) is WeaponData d ? d.weaponName : null;
        // Look for a matching entry (ignoring upper/lower case).
        foreach (ZoomSettings z in weapons)
            if (string.Equals(z.weaponName, w.name, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(z.weaponName, dataName, System.StringComparison.OrdinalIgnoreCase))
                return z;
        // Nothing matched: use the fallback settings.
        return fallback;
    }

    // Checks whether the weapon is reloading by looking for an "IsReloading" or "Reloading" bool on it.
    // Uses reflection so it works whether Weapon.cs exposes it as a property or a field.
    private static bool IsReloading(Weapon w)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        foreach (string n in new[] { "IsReloading", "Reloading" })
        {
            // Try it as a property first (e.g. public bool IsReloading { get; })...
            PropertyInfo p = w.GetType().GetProperty(n, flags);
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(w);
            // ...then as a plain field (e.g. private bool reloading;).
            FieldInfo f = w.GetType().GetField(n, flags);
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(w);
        }
        // Not found: assume it is not reloading.
        return false;
    }

    // Sets every sensitivity field to (original value x multiplier). 1 = normal speed, 0.5 = half speed.
    private void ApplySensitivity(float multiplier)
    {
        // No mouse-look script found, nothing to change.
        if (look == null) return;
        for (int i = 0; i < sensitivityFields.Count; i++)
            sensitivityFields[i].SetValue(look, sensitivityBase[i] * multiplier);
    }
}
