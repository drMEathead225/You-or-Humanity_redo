using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Zombie FPS > Add Zombies HUD To Current Scene  - adds the Black Ops style HUD without rebuilding.</summary>
public static class ZombiesHUDMenu
{
    [MenuItem("Zombie FPS/Add Zombies HUD To Current Scene", false, 2)]
    public static void AddHud()
    {
        if (Object.FindFirstObjectByType<ZombiesHUD>() != null)
        {
            EditorUtility.DisplayDialog("Zombie FPS", "The scene already has a ZombiesHUD.", "OK");
            return;
        }

        HUD legacy = Object.FindFirstObjectByType<HUD>();
        GameObject host;
        if (legacy != null) host = legacy.gameObject;
        else
        {
            host = new GameObject("HUD");
            Undo.RegisterCreatedObjectUndo(host, "Add Zombies HUD");
        }
        Undo.AddComponent<ZombiesHUD>(host);

        EditorSceneManager.MarkSceneDirty(host.scene);
        Selection.activeGameObject = host;
        Debug.Log("[ZombieFPS] Added ZombiesHUD to '" + host.name + "'. Save the scene and press Play.");
    }
}
