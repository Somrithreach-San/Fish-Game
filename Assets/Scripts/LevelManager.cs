using UnityEngine;

public struct LevelConfig
{
    public int levelNumber;        // 1 to 13
    public string levelName;       // e.g. "Tidepool Warmup"
    public int startPlayerLevel;   // 1
    public int targetPlayerLevel;  // 2, 3, 4, 5
    public int maxEnemyLevel;      // 2, 3, 4, 5
    public bool enableGoldenFish;  // Ocean Lv >= 2
    public bool enableSickFish;    // Ocean Lv >= 3
    public bool enableClam;        // Ocean Lv >= 2
    public bool enableCuttlefish;  // Ocean Lv >= 4
    public bool enableShark;       // Ocean Lv >= 6
    public bool enableFishingRod;  // Ocean Lv >= 7, Lake Lv 5 (global 13)
    public bool isLake;            // Lv 1-8: false (Ocean), Lv 9-13: true (Lake)
}

public static class LevelManager
{
    private const string UNLOCKED_KEY = "HighestUnlockedLevel";
    public const int TOTAL_LEVELS = 13;
    public const int OCEAN_LEVELS = 8;
    public const int LAKE_LEVELS = 5;

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
        return level >= 9 && level <= TOTAL_LEVELS;
    }

    public static bool IsCurrentLakeLevel => IsLakeLevel(CurrentLevel);

    public static int GetDisplayLevelNumber(int level)
    {
        return (level <= 8) ? level : (level - 8);
    }

    public static bool IsLevelCompleted { get; set; } = false;

    public static void CompleteCurrentLevel()
    {
        IsLevelCompleted = true;
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
    public static int SickFishEatenCount { get; set; } = 0;
    public static int NormalPearlEatenCount { get; set; } = 0;
    public static int BlackPearlEatenCount { get; set; } = 0;

    public static void ResetLevelStats()
    {
        IsLevelCompleted = false;
        LevelTimer = 0f;
        System.Array.Clear(FishEatenCounts, 0, FishEatenCounts.Length);
        GoldenFishEatenCount = 0;
        SickFishEatenCount = 0;
        NormalPearlEatenCount = 0;
        BlackPearlEatenCount = 0;
    }

    public static void RecordFishEaten(int fishLevel, bool isGolden = false, bool isSick = false)
    {
        if (isSick)
        {
            SickFishEatenCount++;
        }
        else if (isGolden)
        {
            GoldenFishEatenCount++;
        }
        if (fishLevel >= 1 && fishLevel <= 6)
        {
            FishEatenCounts[fishLevel]++;
        }
    }

    public static void RecordPearlEaten(bool isBlackPearl)
    {
        if (isBlackPearl)
        {
            BlackPearlEatenCount++;
        }
        else
        {
            NormalPearlEatenCount++;
        }
    }

    public static LevelConfig GetConfig(int level)
    {
        switch (level)
        {
            // === CORAL COAST (OCEAN) — LEVELS 1 TO 8 ===
            case 1:
                return new LevelConfig
                {
                    levelNumber = 1,
                    levelName = "Tidepool Warmup",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = false
                };
            case 2:
                return new LevelConfig
                {
                    levelNumber = 2,
                    levelName = "Shallow Reef",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableGoldenFish = true,
                    enableSickFish = false,
                    enableClam = true,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = false
                };
            case 3:
                return new LevelConfig
                {
                    levelNumber = 3,
                    levelName = "Coral Garden",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = false
                };
            case 4:
                return new LevelConfig
                {
                    levelNumber = 4,
                    levelName = "Ink & Tentacles",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = true,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = false
                };
            case 5:
                return new LevelConfig
                {
                    levelNumber = 5,
                    levelName = "Deep Blue Dropoff",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = true,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = false
                };
            case 6:
                return new LevelConfig
                {
                    levelNumber = 6,
                    levelName = "Shadow of the Apex",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = true,
                    enableShark = true,
                    enableFishingRod = false,
                    isLake = false
                };
            case 7:
                return new LevelConfig
                {
                    levelNumber = 7,
                    levelName = "The Angler's Reach",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 5,
                    maxEnemyLevel = 5,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = true,
                    enableShark = true,
                    enableFishingRod = true,
                    isLake = false
                };
            case 8:
                return new LevelConfig
                {
                    levelNumber = 8,
                    levelName = "Ocean Mastery",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 5,
                    maxEnemyLevel = 5,
                    enableGoldenFish = true,
                    enableSickFish = true,
                    enableClam = true,
                    enableCuttlefish = true,
                    enableShark = true,
                    enableFishingRod = true,
                    isLake = false
                };

            // === LOST LAKE (RIVER / LAKE) — LEVELS 9 TO 13 ===
            case 9:
                return new LevelConfig
                {
                    levelNumber = 9,
                    levelName = "Lily Pad Creek",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 2,
                    maxEnemyLevel = 2,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = true
                };
            case 10:
                return new LevelConfig
                {
                    levelNumber = 10,
                    levelName = "Sunken Timber",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 3,
                    maxEnemyLevel = 3,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = true
                };
            case 11:
                return new LevelConfig
                {
                    levelNumber = 11,
                    levelName = "Murky Shallows",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 4,
                    maxEnemyLevel = 4,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = true
                };
            case 12:
                return new LevelConfig
                {
                    levelNumber = 12,
                    levelName = "Laser in the Mist",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 5,
                    maxEnemyLevel = 5,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = false,
                    isLake = true
                };
            case 13:
            default:
                return new LevelConfig
                {
                    levelNumber = 13,
                    levelName = "Swamp Rapids",
                    startPlayerLevel = 1,
                    targetPlayerLevel = 5,
                    maxEnemyLevel = 5,
                    enableGoldenFish = false,
                    enableSickFish = false,
                    enableClam = false,
                    enableCuttlefish = false,
                    enableShark = false,
                    enableFishingRod = true, // Harpoon fisherman boat in Lost Lake Level 5
                    isLake = true
                };
        }
    }

    public static LevelConfig GetCurrentConfig()
    {
        return GetConfig(CurrentLevel);
    }
}
