using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One-click builder for the Zombie FPS test scene (Unity 6, Built-in render pipeline).
/// Put this file in Assets/Editor/ and use the menu:  Zombie FPS > Build Test Scene.
///
/// It creates (or overwrites) everything under Assets/ZombieFPS/ and a new scene:
/// materials, 3 WeaponData assets, effect + pickup + 3 zombie prefabs, a walled arena with cover,
/// the player with camera and weapons, all managers, a baked NavMesh, and it adds the scene to Build Settings.
/// Finishes by running the same checks as  Zombie FPS > Validate Current Scene.
/// </summary>
public static class ZombieFpsSceneBuilder
{
    private const string Root = "Assets/ZombieFPS";
    private const string ScenePath = Root + "/Scenes/ZombieFPS_Test.unity";
    private const string NavMeshAssetPath = Root + "/Scenes/ZombieFPS_Test_NavMesh.asset";
    private const string LogTag = "[ZombieFPS Builder] ";

    private const string NavSurfaceType = "Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation";

    // ------------------------------------------------------------------ menu

    [MenuItem("Zombie FPS/Build Test Scene", false, 0)]
    public static void BuildTestScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("Zombie FPS", "Stop Play mode and wait for compiling to finish, then try again.", "OK");
            return;
        }

        if (!EnsureNavigationPackage()) return;

        if (!EditorUtility.DisplayDialog("Zombie FPS - Build Test Scene",
                "This creates a new scene and (re)creates assets in Assets/ZombieFPS/.\n\n" +
                "Existing materials, weapon data and prefabs in that folder will be overwritten with default values.\n\n" +
                "Continue?", "Build", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        try
        {
            BuildInternal();
        }
        catch (System.Exception e)
        {
            Debug.LogError(LogTag + "Build failed: " + e);
            EditorUtility.DisplayDialog("Zombie FPS", "Build failed. See the Console for details.\n\n" + e.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Zombie FPS/Validate Current Scene", false, 1)]
    public static void ValidateMenu()
    {
        Validate(true);
    }

    // ------------------------------------------------------------------ main build

    private static void BuildInternal()
    {
        Progress("Preparing folders and layer", 0.02f);
        foreach (string folder in new[] { "Scenes", "Materials", "Data", "Prefabs" })
            EnsureFolder(Root + "/" + folder);

        Debug.Log(LogTag + "Render pipeline detected: " +
                  (UsingScriptableRenderPipeline() ? "Scriptable (URP) - using Universal Render Pipeline/Lit" : "Built-in - using Standard"));

        int playerLayer = EnsureLayer("Player");
        if (playerLayer < 0)
            Debug.LogWarning(LogTag + "Could not create a 'Player' layer (all 24 user layers are used). Shots may hit the player.");

        Progress("Creating materials", 0.08f);
        Material matFloor = MakeMat("Floor", new Color(0.18f, 0.19f, 0.18f), 0f, 0.05f);
        Material matWall = MakeMat("Wall", new Color(0.12f, 0.12f, 0.13f), 0f, 0.05f);
        Material matCover = MakeMat("Cover", new Color(0.30f, 0.26f, 0.20f), 0f, 0.1f);
        Material matWalker = MakeMat("Zombie_Walker", new Color(0.30f, 0.50f, 0.28f), 0f, 0.1f);
        Material matRunner = MakeMat("Zombie_Runner", new Color(0.62f, 0.60f, 0.20f), 0f, 0.1f);
        Material matTank = MakeMat("Zombie_Tank", new Color(0.45f, 0.18f, 0.18f), 0f, 0.1f);
        Material matFace = MakeMat("Zombie_Face", new Color(0.35f, 0.04f, 0.04f), 0f, 0.1f);
        Material matHealth = MakeMat("Pickup_Health", new Color(0.15f, 0.95f, 0.30f), 1.6f, 0.4f);
        Material matAmmo = MakeMat("Pickup_Ammo", new Color(1.00f, 0.80f, 0.15f), 1.6f, 0.4f);

        Progress("Creating weapon data", 0.14f);
        WeaponData pistol = MakeWeaponData("Pistol", "Pistol", 25f, 100f, 1, 0.5f, 4f, false, 12, 48, 120, 1.4f, 1.5f, 0.4f);
        WeaponData rifle = MakeWeaponData("Rifle", "Rifle", 18f, 150f, 1, 1.2f, 11f, true, 30, 90, 240, 2.0f, 0.9f, 0.5f);
        WeaponData shotgun = MakeWeaponData("Shotgun", "Shotgun", 14f, 40f, 8, 5f, 1.1f, false, 6, 18, 40, 2.6f, 5f, 1.0f);

        Progress("Creating prefabs", 0.22f);
        GameObject bloodFx = MakeBurstEffect("FX_Blood", new Color(0.45f, 0.02f, 0.02f, 0.95f), 18, 2f, 5f, 0.06f, 0.14f, 0.8f, 1.2f);
        GameObject sparkFx = MakeBurstEffect("FX_HitSpark", new Color(1f, 0.85f, 0.4f, 1f), 8, 2f, 6f, 0.03f, 0.07f, 0.35f, 1f);

        GameObject healthPickup = MakePickupPrefab("Pickup_Health", PickupType.Health, 25f, matHealth);
        GameObject ammoPickup = MakePickupPrefab("Pickup_Ammo", PickupType.Ammo, 25f, matAmmo);

        GameObject walker = MakeZombiePrefab("Zombie_Walker", matWalker, matFace, 100f, 1.6f, 3.4f, 10f, 1.3f);
        GameObject runner = MakeZombiePrefab("Zombie_Runner", matRunner, matFace, 50f, 3.8f, 5.2f, 8f, 1.0f);
        GameObject tank = MakeZombiePrefab("Zombie_Tank", matTank, matFace, 400f, 1.0f, 1.5f, 25f, 2.0f);

        AssetDatabase.SaveAssets();

        Progress("Creating the scene", 0.36f);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Replace the default camera with our player camera; keep the directional light as the moon.
        foreach (Camera defaultCamera in Object.FindObjectsByType<Camera>())
            Object.DestroyImmediate(defaultCamera.gameObject);
        Light moon = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (moon != null)
        {
            moon.name = "Moon";
            moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            moon.shadows = LightShadows.Soft;
        }

        Progress("Building the arena", 0.46f);
        Transform level = new GameObject("Level").transform;
        MakeBox("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(62f, 1f, 62f), matFloor, level);
        MakeBox("Wall_N", new Vector3(0f, 2.5f, 30.5f), new Vector3(62f, 5f, 1f), matWall, level);
        MakeBox("Wall_S", new Vector3(0f, 2.5f, -30.5f), new Vector3(62f, 5f, 1f), matWall, level);
        MakeBox("Wall_E", new Vector3(30.5f, 2.5f, 0f), new Vector3(1f, 5f, 62f), matWall, level);
        MakeBox("Wall_W", new Vector3(-30.5f, 2.5f, 0f), new Vector3(1f, 5f, 62f), matWall, level);

        var cover = new (Vector3 pos, Vector3 size)[]
        {
            (new Vector3(8f, 1f, 6f),     new Vector3(4f, 2f, 1f)),
            (new Vector3(-9f, 1f, 7f),    new Vector3(1f, 2f, 5f)),
            (new Vector3(12f, 1.5f, -5f), new Vector3(2f, 3f, 2f)),
            (new Vector3(-12f, 1f, -8f),  new Vector3(5f, 2f, 1f)),
            (new Vector3(4f, 0.75f, -12f),new Vector3(3f, 1.5f, 3f)),
            (new Vector3(-5f, 1f, 12f),   new Vector3(3f, 2f, 1f)),
            (new Vector3(16f, 1f, 12f),   new Vector3(1f, 2f, 6f)),
            (new Vector3(-17f, 1.5f, 2f), new Vector3(2f, 3f, 2f)),
            (new Vector3(0f, 1f, 18f),    new Vector3(8f, 2f, 1f)),
            (new Vector3(-18f, 1f, -16f), new Vector3(4f, 2f, 4f)),
            (new Vector3(20f, 1f, -14f),  new Vector3(1f, 2f, 5f)),
            (new Vector3(-3f, 0.5f, -6f), new Vector3(2f, 1f, 2f)),
        };
        for (int i = 0; i < cover.Length; i++)
            MakeBox("Cover_" + (i + 1), cover[i].pos, cover[i].size, matCover, level);

        Progress("Placing spawn points", 0.54f);
        Transform spawnRoot = new GameObject("SpawnPoints").transform;
        Vector3[] spawnPositions =
        {
            new Vector3(0f, 0f, 26f),  new Vector3(0f, 0f, -26f),
            new Vector3(26f, 0f, 0f),  new Vector3(-26f, 0f, 0f),
            new Vector3(21f, 0f, 21f), new Vector3(-21f, 0f, 21f),
            new Vector3(21f, 0f, -21f), new Vector3(-21f, 0f, -21f),
        };
        var spawnPoints = new List<Transform>();
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var sp = new GameObject("Spawn_" + (i + 1)).transform;
            sp.SetParent(spawnRoot, false);
            sp.position = spawnPositions[i];
            spawnPoints.Add(sp);
        }

        Progress("Baking the NavMesh", 0.62f);
        BakeNavMesh();

        Progress("Building the player", 0.74f);
        BuildPlayer(playerLayer, new[] { pistol, rifle, shotgun }, new[] { 1, 3, 5 }, bloodFx, sparkFx);

        Progress("Creating managers", 0.86f);
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        new GameObject("GameManager").AddComponent<GameManager>();
        new GameObject("HUD").AddComponent<HUD>();
        new GameObject("Atmosphere").AddComponent<AtmosphereController>();

        var dropper = new GameObject("PickupDropper").AddComponent<PickupDropper>();
        SetRef(dropper, "healthPickupPrefab", healthPickup);
        SetRef(dropper, "ammoPickupPrefab", ammoPickup);

        var spawner = new GameObject("WaveSpawner").AddComponent<WaveSpawner>();
        ConfigureSpawner(spawner, playerObject != null ? playerObject.transform : null, spawnPoints,
            new[] { walker, runner, tank },
            new[] { "Walker", "Runner", "Tank" },
            new[] { 1f, 0.5f, 0.25f },
            new[] { 1, 3, 6 });

        Progress("Saving", 0.94f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (playerObject != null)
        {
            Selection.activeGameObject = playerObject;
            EditorGUIUtility.PingObject(playerObject);
        }

        Progress("Validating", 0.98f);
        EditorUtility.ClearProgressBar();
        Validate(true);
    }

    // ------------------------------------------------------------------ player

    private static void BuildPlayer(int playerLayer, WeaponData[] weaponData, int[] unlockWaves,
                                    GameObject bloodFx, GameObject sparkFx)
    {
        var player = new GameObject("Player");
        player.tag = "Player";
        if (playerLayer >= 0) player.layer = playerLayer;
        player.transform.position = new Vector3(0f, 0.1f, 0f);

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        player.AddComponent<Health>();
        var fpc = player.AddComponent<FirstPersonController>();

        var camObject = new GameObject("Main Camera");
        camObject.tag = "MainCamera";
        camObject.transform.SetParent(player.transform, false);
        camObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var cam = camObject.AddComponent<Camera>();
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        camObject.AddComponent<AudioListener>();
        SetRef(fpc, "cameraPivot", camObject.transform);

        camObject.AddComponent<CameraShake>();
        camObject.AddComponent<DeathCamera>();
        camObject.AddComponent<Flashlight>();

        // Muzzle light shared by all weapons (the Weapon script switches it on for a moment per shot).
        var muzzleObject = new GameObject("MuzzleLight");
        muzzleObject.transform.SetParent(camObject.transform, false);
        muzzleObject.transform.localPosition = new Vector3(0.15f, -0.1f, 0.6f);
        var muzzle = muzzleObject.AddComponent<Light>();
        muzzle.type = LightType.Point;
        muzzle.color = new Color(1f, 0.7f, 0.3f);
        muzzle.intensity = 2.5f;
        muzzle.range = 6f;
        muzzle.shadows = LightShadows.None;
        muzzle.enabled = false;

        int hitMask = playerLayer >= 0 ? ~(1 << playerLayer) : ~0;
        string[] names = { "Pistol", "Rifle", "Shotgun" };
        var weapons = new Weapon[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            var weaponObject = new GameObject(names[i]);
            weaponObject.transform.SetParent(camObject.transform, false);

            var weapon = weaponObject.AddComponent<Weapon>();
            SetRef(weapon, "data", weaponData[i]);
            SetRef(weapon, "aimOrigin", camObject.transform);
            SetMask(weapon, "hitMask", hitMask);
            SetRef(weapon, "player", fpc);
            SetRef(weapon, "muzzleLight", muzzle);
            SetRef(weapon, "hitEffectPrefab", sparkFx);
            SetRef(weapon, "bloodEffectPrefab", bloodFx);
            weapons[i] = weapon;
        }

        var switcher = camObject.AddComponent<WeaponSwitcher>();
        var so = new SerializedObject(switcher);
        SerializedProperty slots = Prop(so, "slots");
        if (slots != null)
        {
            slots.arraySize = weapons.Length;
            for (int i = 0; i < weapons.Length; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("weapon").objectReferenceValue = weapons[i];
                slot.FindPropertyRelative("unlockWave").intValue = unlockWaves[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ------------------------------------------------------------------ spawner

    private static void ConfigureSpawner(WaveSpawner spawner, Transform player, List<Transform> points,
                                         GameObject[] prefabs, string[] names, float[] weights, int[] firstWaves)
    {
        var so = new SerializedObject(spawner);

        SerializedProperty types = Prop(so, "zombieTypes");
        if (types != null)
        {
            types.arraySize = prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++)
            {
                SerializedProperty t = types.GetArrayElementAtIndex(i);
                t.FindPropertyRelative("name").stringValue = names[i];
                t.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
                t.FindPropertyRelative("weight").floatValue = weights[i];
                t.FindPropertyRelative("firstWave").intValue = firstWaves[i];
            }
        }

        SerializedProperty pts = Prop(so, "spawnPoints");
        if (pts != null)
        {
            pts.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++)
                pts.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        }

        SerializedProperty playerProp = Prop(so, "player");
        if (playerProp != null) playerProp.objectReferenceValue = player;

        SerializedProperty overlay = Prop(so, "showDebugOverlay");
        if (overlay != null) overlay.boolValue = false; // the HUD replaces it

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ prefabs

    private static GameObject MakeZombiePrefab(string name, Material body, Material face, float hp,
                                               float minSpeed, float maxSpeed, float damage, float cooldown)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.GetComponent<MeshRenderer>().sharedMaterial = body;

        // The head hit zone only works if the head collider is hit BEFORE the body collider.
        // So the body gets one capsule covering the lower part, and the head a separate sphere on top (no overlap).
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());

        var bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(go.transform, false);
        var bodyCollider = bodyObject.AddComponent<CapsuleCollider>();
        bodyCollider.radius = 0.5f;
        bodyCollider.height = 1.4f;
        bodyCollider.center = new Vector3(0f, -0.3f, 0f); // spans y -1.0 to 0.4

        var headObject = new GameObject("Head");
        headObject.transform.SetParent(go.transform, false);
        headObject.transform.localPosition = new Vector3(0f, 0.7f, 0f); // sphere spans y 0.4 to 1.0
        var headCollider = headObject.AddComponent<SphereCollider>();
        headCollider.radius = 0.3f;
        headObject.AddComponent<HitZone>();

        // A dark "face" block so you can see which way the zombie is looking.
        var faceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        faceObject.name = "Face";
        Object.DestroyImmediate(faceObject.GetComponent<BoxCollider>());
        faceObject.transform.SetParent(go.transform, false);
        faceObject.transform.localPosition = new Vector3(0f, 0.55f, 0.42f);
        faceObject.transform.localScale = new Vector3(0.45f, 0.18f, 0.2f);
        faceObject.GetComponent<MeshRenderer>().sharedMaterial = face;

        var agent = go.AddComponent<NavMeshAgent>();
        agent.height = 2f;
        agent.radius = 0.5f;
        agent.baseOffset = 1f; // the capsule's pivot is its centre, 1m above the feet
        agent.acceleration = 14f;
        agent.angularSpeed = 300f;

        var health = go.AddComponent<Health>();
        SetFloat(health, "maxHealth", hp);

        var ai = go.AddComponent<ZombieAI>();
        SetFloat(ai, "minSpeed", minSpeed);
        SetFloat(ai, "maxSpeed", maxSpeed);
        SetFloat(ai, "attackDamage", damage);
        SetFloat(ai, "attackCooldown", cooldown);

        go.AddComponent<HitFlash>();

        string path = $"{Root}/Prefabs/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject MakePickupPrefab(string name, PickupType type, float amount, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.localScale = Vector3.one * 0.4f;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;

        var col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 2f; // x scale 0.4 = 0.8m pickup range

        var pickup = go.AddComponent<Pickup>();
        SetEnum(pickup, "type", (int)type);
        SetFloat(pickup, "amount", amount);
        SetFloat(pickup, "lifetime", 25f);

        string path = $"{Root}/Prefabs/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject MakeBurstEffect(string name, Color color, int count, float speedMin, float speedMax,
                                              float sizeMin, float sizeMax, float life, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone; // emits along +Z, which Weapon aims along the surface normal
        shape.angle = 30f;
        shape.radius = 0.02f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetParticleMaterial();

        string path = $"{Root}/Prefabs/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------------ assets

    private static WeaponData MakeWeaponData(string fileName, string weaponName, float damage, float range, int pellets,
        float spread, float fireRate, bool automatic, int magazine, int startReserve, int maxReserve,
        float reload, float recoilPitch, float recoilYaw)
    {
        string path = $"{Root}/Data/{fileName}.asset";
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<WeaponData>();
            AssetDatabase.CreateAsset(data, path);
        }

        data.weaponName = weaponName;
        data.damage = damage;
        data.range = range;
        data.pellets = pellets;
        data.spread = spread;
        data.fireRate = fireRate;
        data.automatic = automatic;
        data.magazineSize = magazine;
        data.startingReserve = startReserve;
        data.maxReserve = maxReserve;
        data.reloadTime = reload;
        data.recoilPitch = recoilPitch;
        data.recoilYaw = recoilYaw;

        EditorUtility.SetDirty(data);
        return data;
    }

    private static Material MakeMat(string name, Color color, float emission, float smoothness)
    {
        string path = $"{Root}/Materials/{name}.mat";

        Shader shader = FindLitShader();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader; // re-running after a pipeline mismatch repairs old pink materials
        }

        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static bool UsingScriptableRenderPipeline()
    {
        return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
    }

    private static Shader FindLitShader()
    {
        // URP (and any other SRP) cannot render the old Standard shader: it shows up magenta.
        string[] order = UsingScriptableRenderPipeline()
            ? new[] { "Universal Render Pipeline/Lit", "Standard" }
            : new[] { "Standard", "Universal Render Pipeline/Lit" };

        foreach (string shaderName in order)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null) return shader;
        }
        throw new System.Exception("Could not find a Lit shader (Standard or Universal Render Pipeline/Lit).");
    }

    private static Material GetParticleMaterial()
    {
        bool srp = UsingScriptableRenderPipeline();

        if (!srp)
        {
            Material builtin = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            if (builtin != null) return builtin;
        }

        Shader shader = srp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : null;
        if (shader == null) shader = Shader.Find("Sprites/Default");

        string path = $"{Root}/Materials/FX_Particle.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static GameObject MakeBox(string name, Vector3 position, Vector3 size, Material material, Transform parent)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    // ------------------------------------------------------------------ navmesh

    private static bool EnsureNavigationPackage()
    {
        if (System.Type.GetType(NavSurfaceType) != null) return true;

        bool install = EditorUtility.DisplayDialog("Zombie FPS - AI Navigation package needed",
            "Zombies need a baked NavMesh, which uses the 'AI Navigation' package (com.unity.ai.navigation).\n\n" +
            "It is not installed in this project. Install it now? Afterwards wait for Unity to finish compiling, then run Zombie FPS > Build Test Scene again.",
            "Install", "Cancel");

        if (install)
        {
            UnityEditor.PackageManager.Client.Add("com.unity.ai.navigation");
            Debug.Log(LogTag + "Installing com.unity.ai.navigation. When Unity finishes, run Zombie FPS > Build Test Scene again.");
        }
        return false;
    }

    private static void BakeNavMesh()
    {
        System.Type surfaceType = System.Type.GetType(NavSurfaceType);
        if (surfaceType == null)
            throw new System.Exception("AI Navigation package not found.");

        var host = new GameObject("NavMesh");
        Component surface = host.AddComponent(surfaceType);

        surfaceType.GetMethod("BuildNavMesh", System.Type.EmptyTypes).Invoke(surface, null);

        // Save the baked data as an asset so it survives closing and reopening the scene.
        var navData = surfaceType.GetProperty("navMeshData").GetValue(surface) as NavMeshData;
        if (navData == null)
        {
            Debug.LogWarning(LogTag + "NavMesh bake produced no data. Check that the floor has a MeshRenderer.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(NavMeshAssetPath) != null)
            AssetDatabase.DeleteAsset(NavMeshAssetPath);
        AssetDatabase.CreateAsset(navData, NavMeshAssetPath);
        EditorUtility.SetDirty(surface);
    }

    // ------------------------------------------------------------------ project settings helpers

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        return -1;
    }

    private static void AddToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        int index = scenes.FindIndex(s => s.path == scenePath);

        if (index >= 0)
        {
            scenes[index] = new EditorBuildSettingsScene(scenePath, true);
        }
        else
        {
            // First in the list so it is build index 0 (GameManager.Restart reloads by build index).
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void Progress(string message, float t)
    {
        EditorUtility.DisplayProgressBar("Zombie FPS - Build Test Scene", message, t);
    }

    // ------------------------------------------------------------------ serialized field helpers

    private static SerializedProperty Prop(SerializedObject so, string field)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
            Debug.LogWarning(LogTag + $"Field '{field}' was not found on {so.targetObject.GetType().Name}. " +
                             "That script may be an older or different version than the builder expects.");
        return p;
    }

    private static void SetFloat(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = Prop(so, field);
        if (p == null) return;
        p.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEnum(Object target, string field, int index)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = Prop(so, field);
        if (p == null) return;
        p.enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = Prop(so, field);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetMask(Object target, string field, int mask)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = Prop(so, field);
        if (p == null) return;
        p.intValue = mask;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ validation

    private static void Validate(bool showDialog)
    {
        var problems = new List<string>();
        var notes = new List<string>();

        // Project settings.
        var projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (projectSettings != null && projectSettings.Length > 0)
        {
            SerializedProperty handler = new SerializedObject(projectSettings[0]).FindProperty("activeInputHandler");
            if (handler != null && handler.intValue == 1)
                problems.Add("Active Input Handling is 'Input System Package (New)'. Set it to 'Both' (Project Settings > Player > Other Settings).");
        }

        // Player.
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) problems.Add("No object is tagged 'Player'.");
        else
        {
            if (player.GetComponent<CharacterController>() == null) problems.Add("Player has no CharacterController.");
            if (player.GetComponent<Health>() == null) problems.Add("Player has no Health component.");
            if (player.GetComponent<FirstPersonController>() == null) problems.Add("Player has no FirstPersonController.");
            if (player.layer == 0) notes.Add("Player is on the Default layer, so weapon Hit Masks cannot exclude it. (The builder uses a 'Player' layer.)");
        }

        // Camera and audio.
        int listeners = Object.FindObjectsByType<AudioListener>().Length;
        if (listeners == 0) problems.Add("No AudioListener in the scene.");
        else if (listeners > 1) problems.Add($"{listeners} AudioListeners in the scene (should be exactly 1).");
        if (Camera.main == null) problems.Add("No camera is tagged 'MainCamera'.");

        // Managers.
        RequireOne<GameManager>(problems, "GameManager");
        RequireOne<HUD>(problems, "HUD");
        RequireOne<WaveSpawner>(problems, "WaveSpawner");
        RequireOne<PickupDropper>(problems, "PickupDropper");
        RequireOne<AtmosphereController>(problems, "AtmosphereController");
        RequireOne<WeaponSwitcher>(problems, "WeaponSwitcher");

        // Weapons.
        Weapon[] weapons = Object.FindObjectsByType<Weapon>();
        if (weapons.Length == 0) problems.Add("No Weapon components found.");
        foreach (Weapon w in weapons)
        {
            var so = new SerializedObject(w);
            if (so.FindProperty("data") != null && so.FindProperty("data").objectReferenceValue == null)
                problems.Add($"Weapon '{w.name}' has no WeaponData assigned.");

            SerializedProperty mask = so.FindProperty("hitMask");
            int playerLayer = player != null ? player.layer : 0;
            if (mask != null && playerLayer != 0 && (mask.intValue & (1 << playerLayer)) != 0)
                problems.Add($"Weapon '{w.name}' Hit Mask includes the Player layer (you could shoot yourself).");
        }

        var switcher = Object.FindFirstObjectByType<WeaponSwitcher>();
        if (switcher != null)
        {
            SerializedProperty slots = new SerializedObject(switcher).FindProperty("slots");
            if (slots == null || slots.arraySize == 0 || slots.GetArrayElementAtIndex(0).FindPropertyRelative("weapon").objectReferenceValue == null)
                problems.Add("WeaponSwitcher slot 1 is empty.");
        }
        if (Object.FindFirstObjectByType<WeaponSwitcher>() != null && weapons.Any(w => w.transform.parent != null && w.GetComponent<Camera>() != null))
            notes.Add("A Weapon sits directly on the camera next to the WeaponSwitcher - make sure it is not a leftover from the old setup.");

        // Spawner and zombie prefabs.
        var spawner = Object.FindFirstObjectByType<WaveSpawner>();
        if (spawner != null)
        {
            var so = new SerializedObject(spawner);
            SerializedProperty points = so.FindProperty("spawnPoints");
            SerializedProperty types = so.FindProperty("zombieTypes");

            if (points == null || points.arraySize == 0) problems.Add("WaveSpawner has no spawn points.");
            else
            {
                for (int i = 0; i < points.arraySize; i++)
                {
                    var t = points.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (t == null) { problems.Add($"WaveSpawner spawn point {i + 1} is empty."); continue; }
                    if (!NavMesh.SamplePosition(t.position, out _, 4f, NavMesh.AllAreas))
                        problems.Add($"Spawn point '{t.name}' is more than 4m from the NavMesh.");
                }
            }

            if (types == null || types.arraySize == 0) problems.Add("WaveSpawner has no zombie types.");
            else
            {
                for (int i = 0; i < types.arraySize; i++)
                {
                    var prefab = types.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue as GameObject;
                    if (prefab == null) { problems.Add($"Zombie type {i + 1} has no prefab."); continue; }
                    if (prefab.GetComponent<ZombieAI>() == null) problems.Add($"Zombie prefab '{prefab.name}' has no ZombieAI.");
                    if (prefab.GetComponent<NavMeshAgent>() == null) problems.Add($"Zombie prefab '{prefab.name}' has no NavMeshAgent.");
                    if (prefab.GetComponentInChildren<HitZone>() == null)
                        notes.Add($"Zombie prefab '{prefab.name}' has no HitZone, so headshots will not register.");
                    if (prefab.GetComponent<Collider>() != null)
                        notes.Add($"Zombie prefab '{prefab.name}' has a collider on its root. A body collider that surrounds the head sphere will block headshots - use separate, non-overlapping colliders.");
                }
            }
        }

        // NavMesh.
        if (NavMesh.CalculateTriangulation().vertices.Length == 0)
            problems.Add("No baked NavMesh found. Zombies cannot move. (Add a NavMeshSurface and click Bake.)");

        // Build settings.
        string activeScene = EditorSceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(activeScene)) problems.Add("The scene is not saved yet.");
        else if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == activeScene))
            problems.Add("This scene is not in Build Settings. Restart (R) will fail. Use File > Build Profiles > Add Open Scenes.");

        // Report.
        string summary;
        if (problems.Count == 0)
        {
            summary = "All checks passed.";
            Debug.Log(LogTag + summary + (notes.Count > 0 ? "\nNotes:\n- " + string.Join("\n- ", notes) : ""));
        }
        else
        {
            summary = $"{problems.Count} problem(s) found:\n- " + string.Join("\n- ", problems);
            Debug.LogWarning(LogTag + summary + (notes.Count > 0 ? "\nNotes:\n- " + string.Join("\n- ", notes) : ""));
        }

        if (showDialog)
        {
            string body = problems.Count == 0
                ? "Scene built and all checks passed.\n\nPress Play to test. Controls: WASD, Shift sprint, Space jump, mouse look, LMB fire, R reload, F flashlight, 1-3 / scroll to switch weapons, Esc pause."
                : "Scene built, but " + summary + "\n\n(Full details are in the Console.)";
            EditorUtility.DisplayDialog("Zombie FPS", body, "OK");
        }
    }

    private static void RequireOne<T>(List<string> problems, string label) where T : Object
    {
        int count = Object.FindObjectsByType<T>().Length;
        if (count == 0) problems.Add($"No {label} in the scene.");
        else if (count > 1) problems.Add($"{count} {label} objects in the scene (should be exactly 1).");
    }
}
