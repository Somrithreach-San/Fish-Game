using UnityEngine;

public struct LevelConfig
{
    public int levelNumber;        // 1, 2, 3, 4
    public int startPlayerLevel;   // 1
    public int targetPlayerLevel;  // Level 1 -> 2, Level 2 -> 3, Level 3 -> 4, Level 4 -> 6
    public int maxEnemyLevel;      // Level 1 -> 2, Level 2 -> 3, Level 3 -> 4, Level 4 -> 6
    public bool enableFishingRod;  // Lv 1: false, Lv 2: true, Lv 3: false, Lv 4: true
    public bool enableShark;       // Lv 1: false, Lv 2: false, Lv 3: true, Lv 4: true
}

public static class LevelManager
{
    private const string UNLOCKED_KEY = "HighestUnlockedLevel";
    public const int TOTAL_LEVELS = 4;

    private static int _currentLevel = 1;
    public static int CurrentLevel
    {
        get => Mathf.Clamp(_currentLevel, 1, TOTAL_LEVELS);
        set => _currentLevel = Mathf.Clamp(value, 1, TOTAL_LEVELS);
    }

    public static int HighestUnlockedLevel
    {
        get => PlayerPrefs.GetInt(UNLOCKED_KEY, 1);
        set
        {
            PlayerPrefs.SetInt(UNLOCKED_KEY, Mathf.Clamp(value, 1, TOTAL_LEVELS));
            PlayerPrefs.Save();
        }
    }

    public static bool IsLevelUnlocked(int level)
    {
        return level <= HighestUnlockedLevel;
    }

    public static void CompleteCurrentLevel()
    {
        int nextLevel = CurrentLevel + 1;
        if (nextLevel > HighestUnlockedLevel && nextLevel <= TOTAL_LEVELS)
        {
            HighestUnlockedLevel = nextLevel;
        }
    }

    // Level Stats Tracking
    public static float LevelTimer { get; set; } = 0f;
    public static int[] FishEatenCounts { get; } = new int[7]; // 1-indexed for fish levels 1..6
    public static int GoldenFishEatenCount { get; set; } = 0;

    public static void ResetLevelStats()
    {
        LevelTimer = 0f;
        System.Array.Clear(FishEatenCounts, 0, FishEatenCounts.Length);
        GoldenFishEatenCount = 0;
    }

    public static void RecordFishEaten(int fishLevel, bool isGolden = false)
    {
        if (isGolden)
        {
            GoldenFishEatenCount++;
        }
        if (fishLevel >= 1 && fishLevel <= 6)
        {
            FishEatenCounts[fishLevel]++;
        }
    }

    public static LevelConfig GetConfig(int level)
    {
        switch (level)
        {
            case 1:
                return new LevelConfig
                {
                    levelNumber = 1,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableFishingRod = false,
                    enableShark = false
                };
            case 2:
                return new LevelConfig
                {
                    levelNumber = 2,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableFishingRod = true,
                    enableShark = false
                };
            case 3:
                return new LevelConfig
                {
                    levelNumber = 3,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableFishingRod = false,
                    enableShark = true
                };
            case 4:
            default:
                return new LevelConfig
                {
                    levelNumber = 4,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 6,
                    maxEnemyLevel = 6,
                    enableFishingRod = true,
                    enableShark = true
                };
        }
    }

    public static LevelConfig GetCurrentConfig()
    {
        return GetConfig(CurrentLevel);
    }
}
