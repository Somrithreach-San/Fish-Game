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
        SpawnPool result = GetPoolForLevel(level);
        if (result == null) return null;
        return result.Get();
    }

    /// <summary>
    /// Get a specific prefab by checking if its name contains the search string
    /// </summary>
    public Fish GetPrefabByName(int level, string namePart)
    {
        SpawnPool result = GetPoolForLevel(level);
        if (result == null) return null;
        
        return result.FindPrefab(namePart);
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
        if (fishPrefabs.Length == 0 || playerLevel <= 0) return null;

        int index = Random.Range(0, fishPrefabs.Length);

        return fishPrefabs[index];
    }

    public Fish FindPrefab(string namePart)
    {
        if (fishPrefabs == null) return null;
        return System.Array.Find(fishPrefabs, x => x.name.Contains(namePart));
    }
}
