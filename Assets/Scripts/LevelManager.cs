using UnityEngine;

public struct LevelConfig
{
    public int levelNumber;        // 1 to 8
    public int startPlayerLevel;   // 1
    public int targetPlayerLevel;  // 2, 3, 4, 6
    public int maxEnemyLevel;      // 2, 3, 4, 6
    public bool enableFishingRod;  // Lv 1/5: false, Lv 2/6: true, Lv 3/7: false, Lv 4/8: true
    public bool enableShark;       // Lv 1/5: false, Lv 2/6: false, Lv 3/7: true, Lv 4/8: true
    public bool isLake;            // Lv 1-4: Ocean (false), Lv 5-8: Lake (true)
}

public static class LevelManager
{
    private const string UNLOCKED_KEY = "HighestUnlockedLevel";
    public const int TOTAL_LEVELS = 8;

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

    public static bool IsLakeLevel(int level)
    {
        return level >= 5 && level <= TOTAL_LEVELS;
    }

    public static bool IsCurrentLakeLevel => IsLakeLevel(CurrentLevel);

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
            // === OCEAN SCENARIOS (Levels 1 - 4) ===
            case 1:
                return new LevelConfig
                {
                    levelNumber = 1,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableFishingRod = false,
                    enableShark = false,
                    isLake = false
                };
            case 2:
                return new LevelConfig
                {
                    levelNumber = 2,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableFishingRod = true,
                    enableShark = false,
                    isLake = false
                };
            case 3:
                return new LevelConfig
                {
                    levelNumber = 3,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableFishingRod = false,
                    enableShark = true,
                    isLake = false
                };
            case 4:
                return new LevelConfig
                {
                    levelNumber = 4,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 6,
                    maxEnemyLevel = 6,
                    enableFishingRod = true,
                    enableShark = true,
                    isLake = false
                };

            // === LAKE SCENARIOS (Levels 5 - 8: Duplicated Ocean Logic with Lake Background) ===
            case 5:
                return new LevelConfig
                {
                    levelNumber = 5,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableFishingRod = false,
                    enableShark = false,
                    isLake = true
                };
            case 6:
                return new LevelConfig
                {
                    levelNumber = 6,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableFishingRod = true,
                    enableShark = false,
                    isLake = true
                };
            case 7:
                return new LevelConfig
                {
                    levelNumber = 7,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableFishingRod = false,
                    enableShark = true,
                    isLake = true
                };
            case 8:
            default:
                return new LevelConfig
                {
                    levelNumber = 8,
                    startPlayerLevel = 1,
                    targetPlayerLevel = 6,
                    maxEnemyLevel = 6,
                    enableFishingRod = true,
                    enableShark = true,
                    isLake = true
                };
        }
    }

    public static LevelConfig GetCurrentConfig()
    {
        return GetConfig(CurrentLevel);
    }
}
