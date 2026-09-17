using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[CreateAssetMenu(menuName ="EnemyLibrary/new library")]
public class EnemyLibrary : ScriptableObject
{
    [Header("Spawn Pool for each level")]
    [SerializeField]
    private SpawnPool[] pools;


    private int maxLevel = 0;
    private Dictionary<int, SpawnPool> levelToPool = new Dictionary<int, SpawnPool>();
    private List<int> sortedLevels = new List<int>();

    private void OnEnable()
    {
        if (pools == null) return;
        
        levelToPool.Clear();
        sortedLevels.Clear();
        
        for (int i = 0; i < pools.Length; i++)
        {
            var lvl = pools[i].Level;
            if (!levelToPool.ContainsKey(lvl))
            {
                levelToPool[lvl] = pools[i];
                sortedLevels.Add(lvl);
            }
        }
        
        sortedLevels.Sort();
        maxLevel = (sortedLevels.Count > 0) ? sortedLevels[sortedLevels.Count - 1] : 0;
        Debug.Log("Max available spawn pool level: " + maxLevel.ToString());
    }


    /// <summary>
    /// Helper to find the best matching pool.
    /// If exact level is missing, finds the closest lower level.
    /// </summary>
    private SpawnPool GetPoolForLevel(int level)
    {
        if (pools == null || pools.Length == 0) return null;

        if (levelToPool.TryGetValue(level, out var exact))
        {
            return exact;
        }
        
        if (sortedLevels.Count == 0) return null;
        
        // Find highest available level <= requested
        for (int i = sortedLevels.Count - 1; i >= 0; i--)
        {
            int candidate = sortedLevels[i];
            if (candidate <= level)
            {
                return levelToPool[candidate];
            }
        }
        
        return null;
    }

    /// <summary>
    /// Spawn a fish and return it
    /// </summary>
    /// <param name="level">Current player level</param>
    /// <param name="position">Position to spawn</param>
    /// <returns></returns>
    public Fish Spawn(int level, Vector2 position, float RandomizeX = 3f, float RandomizeY = 3f)
    {
        SpawnPool result = GetPoolForLevel(level);

        if( result == null)
        {
            Debug.Log("Cant find spawnpool for level " + level);
            return null;
        }

        Fish prefabToSpawn = result.Get();
        return SpawnSpecific(prefabToSpawn, position, RandomizeX, RandomizeY);
    }

    /// <summary>
    /// Get a random prefab for a specific level (useful for schooling to get same type)
    /// </summary>
    public Fish GetRandomPrefab(int level)
    {
        return GetRandomPrefab(level, LevelManager.IsCurrentLakeLevel);
    }

    /// <summary>
    /// Environment-aware random prefab retrieval ensuring river and ocean fish never mix.
    /// </summary>
    public Fish GetRandomPrefab(int level, bool isLake)
    {
        if (isLake)
        {
            // For River levels: strictly look for river fish
            string riverName = $"river level {Mathf.Clamp(level, 1, 5)} fish";
            Fish riverFish = GetPrefabByName(level, riverName, true);
            if (riverFish != null) return riverFish;

            // Fallback: try any river fish from level 5 down to 1
            for (int lvl = 5; lvl >= 1; lvl--)
            {
                riverFish = GetPrefabByName(lvl, $"river level {lvl} fish", true);
                if (riverFish != null) return riverFish;
            }
        }
        else
        {
            // For Ocean levels: strictly look for ocean fish
            string oceanName = $"level {Mathf.Clamp(level, 1, 5)} fish";
            Fish oceanFish = GetPrefabByName(level, oceanName, false);
            if (oceanFish != null) return oceanFish;

            for (int lvl = 5; lvl >= 1; lvl--)
            {
                oceanFish = GetPrefabByName(lvl, $"level {lvl} fish", false);
                if (oceanFish != null) return oceanFish;
            }
        }

        SpawnPool result = GetPoolForLevel(level);
        Fish chosen = result != null ? result.Get(isLake) : null;
        return chosen;
    }

    /// <summary>
    /// Get a specific prefab by checking if its name contains the search string
    /// </summary>
    public Fish GetPrefabByName(int level, string namePart)
    {
        return GetPrefabByName(level, namePart, LevelManager.IsCurrentLakeLevel);
    }

    /// <summary>
    /// Environment-aware specific prefab retrieval ensuring river and ocean fish never mix.
    /// </summary>
    public Fish GetPrefabByName(int level, string namePart, bool isLake)
    {
#if UNITY_EDITOR
        // 1. In Editor, prioritize loading directly from Prefabs folder
        string editorPath = $"Assets/Prefabs/Fishes Prefabs/{namePart}.prefab";
        var editorGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(editorPath);
        if (editorGo != null)
        {
            Fish f = editorGo.GetComponent<Fish>();
            if (f != null && SpawnPool.IsMatchingEnvironment(f.name, isLake)) return f;
        }
#endif

        // 2. Primary pool search
        SpawnPool result = GetPoolForLevel(level);
        if (result != null)
        {
            Fish found = result.FindPrefab(namePart, isLake);
            if (found != null) return found;
        }

        // 3. Search across all pools
        if (pools != null)
        {
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null)
                {
                    Fish found = pools[i].FindPrefab(namePart, isLake);
                    if (found != null) return found;
                }
            }
        }

        // 4. Resources folder fallback
        if (SpawnPool.IsMatchingEnvironment(namePart, isLake))
        {
            Fish resFish = Resources.Load<Fish>(namePart);
            if (resFish != null && SpawnPool.IsMatchingEnvironment(resFish.name, isLake)) return resFish;
            GameObject resGo = Resources.Load<GameObject>(namePart);
            if (resGo != null)
            {
                resFish = resGo.GetComponent<Fish>();
                if (resFish != null && SpawnPool.IsMatchingEnvironment(resFish.name, isLake)) return resFish;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Spawn a specific fish prefab
    /// </summary>
    public Fish SpawnSpecific(Fish prefab, Vector2 position, float RandomizeX = 3f, float RandomizeY = 3f, int overrideLevel = -1)
    {
        if (prefab == null) return null;

        if( RandomizeX > 0f)
        {
            RandomizeX = Random.Range(RandomizeX * -10f, RandomizeX * 10f) / 10f;
            position.x += RandomizeX;
        }
        if (RandomizeY > 0f)
        {
            RandomizeY = Random.Range(RandomizeY * -10f, RandomizeY * 10f) / 10f;
            position.y += RandomizeY;
        }

        GameObject newFish;
        
        if (ObjectPoolManager.Instance != null)
        {
            newFish = ObjectPoolManager.Instance.Spawn(prefab.gameObject, position, Quaternion.identity);
        }
        else
        {
            newFish = GameObject.Instantiate(prefab.gameObject, position, Quaternion.identity);
        }

        Fish fishComp = newFish.GetComponent<Fish>();
        
        if (fishComp != null && overrideLevel > 0)
        {
            fishComp.SetLevel(overrideLevel);
        }
        
        return fishComp;
    }
    
    
}

[System.Serializable]
public class SpawnPool
{
    [Header("Spawnable Enemies Of Player Level")]
    [SerializeField]
    private int playerLevel;

    public int Level => playerLevel;

    [Header("Add same prefab multiple times to increase spawn chance")]
    [SerializeField]
    private Fish[] fishPrefabs;

    public Fish Get()
    {
        return Get(LevelManager.IsCurrentLakeLevel);
    }

    public Fish Get(bool isLake)
    {
        if (fishPrefabs == null || fishPrefabs.Length == 0 || playerLevel <= 0) return null;

        var matching = System.Array.FindAll(fishPrefabs, x => x != null && IsMatchingEnvironment(x.name, isLake));
        if (matching.Length == 0)
        {
            // Safety fallback: if no matching prefabs found for this environment, return null
            return null;
        }

        int index = Random.Range(0, matching.Length);
        return matching[index];
    }

    public Fish FindPrefab(string namePart)
    {
        return FindPrefab(namePart, LevelManager.IsCurrentLakeLevel);
    }

    public Fish FindPrefab(string namePart, bool isLake)
    {
        if (fishPrefabs == null) return null;

        // 1. Exact name match within the requested environment
        Fish exact = System.Array.Find(fishPrefabs, x => x != null && IsMatchingEnvironment(x.name, isLake) && string.Equals(x.name, namePart, System.StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Substring match within the requested environment
        return System.Array.Find(fishPrefabs, x => x != null && IsMatchingEnvironment(x.name, isLake) && x.name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static bool IsMatchingEnvironment(string prefabName, bool isLake)
    {
        if (string.IsNullOrEmpty(prefabName)) return false;
        bool isRiverPrefab = prefabName.IndexOf("river", System.StringComparison.OrdinalIgnoreCase) >= 0;
        return isLake ? isRiverPrefab : !isRiverPrefab;
    }
}
