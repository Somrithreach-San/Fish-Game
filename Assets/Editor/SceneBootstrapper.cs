#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ensures that clicking 'Play' in the Unity Editor always starts at the Main Menu scene (MainMenu.unity)
/// instead of directly jumping into gameplay in SampleScene.unity.
/// You can toggle this behavior at any time via 'Tools > Ocean Invader > Always Start from Main Menu'.
/// </summary>
[InitializeOnLoad]
public static class SceneBootstrapper
{
    private const string PREF_KEY = "OceanInvader_AlwaysStartFromMainMenu";
    private const string MENU_PATH = "Tools/Ocean Invader/Always Start from Main Menu";
    private const string MAIN_MENU_SCENE_PATH = "Assets/Scenes/MainMenu.unity";

    static SceneBootstrapper()
    {
        EditorApplication.delayCall += ApplyPlayModeStartScene;
    }

    private static bool IsEnabled
    {
        get => EditorPrefs.GetBool(PREF_KEY, true); // Enabled by default
        set
        {
            EditorPrefs.SetBool(PREF_KEY, value);
            ApplyPlayModeStartScene();
        }
    }

    [MenuItem(MENU_PATH, false, 1)]
    private static void ToggleStartFromMainMenu()
    {
        IsEnabled = !IsEnabled;
        Debug.Log($"[SceneBootstrapper] Always start from Main Menu: {(IsEnabled ? "ENABLED (Play button boots into MainMenu)" : "DISABLED (Play button boots into currently open scene)")}");
    }

    [MenuItem(MENU_PATH, true)]
    private static bool ToggleStartFromMainMenuValidate()
    {
        Menu.SetChecked(MENU_PATH, IsEnabled);
        return true;
    }

    public static void ApplyPlayModeStartScene()
    {
        if (IsEnabled)
        {
            SceneAsset mainMenuAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(MAIN_MENU_SCENE_PATH);
            if (mainMenuAsset != null)
            {
                EditorSceneManager.playModeStartScene = mainMenuAsset;
            }
            else
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find scene at '{MAIN_MENU_SCENE_PATH}'");
            }
        }
        else
        {
            EditorSceneManager.playModeStartScene = null;
        }
    }

    [MenuItem("Tools/Ocean Invader/Unlock All Levels (1-8)", false, 20)]
    public static void UnlockAllLevels()
    {
        LevelManager.HighestUnlockedLevel = LevelManager.TOTAL_LEVELS;
        PlayerPrefs.Save();
        Debug.Log($"[LevelManager] Unlocked all {LevelManager.TOTAL_LEVELS} levels for testing!");
    }

    [MenuItem("Tools/Ocean Invader/Reset Progress (Unlock Level 1 Only)", false, 21)]
    public static void ResetLevelProgress()
    {
        LevelManager.HighestUnlockedLevel = 1;
        LevelManager.CurrentLevel = 1;
        PlayerPrefs.Save();
        Debug.Log("[LevelManager] Reset progress: Only Level 1 is unlocked.");
    }
}
#endif
