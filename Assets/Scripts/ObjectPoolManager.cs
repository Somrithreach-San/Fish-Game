using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance;

    private Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();
    private Dictionary<GameObject, string> activeObjects = new Dictionary<GameObject, string>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        string key = prefab.name;

        // Create pool if it doesn't exist
        if (!poolDictionary.ContainsKey(key))
        {
            poolDictionary.Add(key, new Queue<GameObject>());
        }

        GameObject objToSpawn;

        // Check if there's an inactive object in the pool
        if (poolDictionary[key].Count > 0)
        {
            objToSpawn = poolDictionary[key].Dequeue();
            
            // Safety check: The object might have been destroyed (e.g. scene change)
            if (objToSpawn == null)
            {
                // Recursive call to try again (or just instantiate new one)
                return Spawn(prefab, position, rotation);
            }
        }
        else
        {
            // Create a new one
            objToSpawn = Instantiate(prefab);
            objToSpawn.name = prefab.name; // Keep name consistent for keying
        }

        // Set position and rotation
        objToSpawn.transform.position = position;
        objToSpawn.transform.rotation = rotation;

        // Track it so we know which pool it belongs to when despawning (registered before SetActive for safety)
        if (!activeObjects.ContainsKey(objToSpawn))
        {
            activeObjects.Add(objToSpawn, key);
        }
        else
        {
            activeObjects[objToSpawn] = key;
        }
        
        // Activate
        objToSpawn.SetActive(true);

        return objToSpawn;
    }

    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        // If the object is already inactive, it has already been despawned / returned to pool
        if (!obj.activeSelf) return;

        string key = null;
        if (activeObjects.ContainsKey(obj))
        {
            key = activeObjects[obj];
            activeObjects.Remove(obj);
        }
        else
        {
            // Fallback: check if the object's name matches an existing pool (e.g. spawned via fallback Instantiate)
            string cleanKey = obj.name.Replace("(Clone)", "").Trim();
            if (poolDictionary.ContainsKey(cleanKey))
            {
                key = cleanKey;
            }
        }

        if (key != null)
        {
            // Deactivate and return to pool
            obj.SetActive(false);

            if (!poolDictionary.ContainsKey(key))
            {
                poolDictionary.Add(key, new Queue<GameObject>());
            }
            
            poolDictionary[key].Enqueue(obj);
        }
        else
        {
            // If it cannot be pooled, deactivate and destroy normally
            obj.SetActive(false);
            Destroy(obj);
        }
    }
    
    // Helper to clear pools (e.g. on scene change)
    public void ClearPools()
    {
        poolDictionary.Clear();
        activeObjects.Clear();
    }
    
    public void PreWarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;
        string key = prefab.name;
        if (!poolDictionary.ContainsKey(key))
        {
            poolDictionary.Add(key, new Queue<GameObject>());
        }
        var queue = poolDictionary[key];
        for (int i = 0; i < count; i++)
        {
            GameObject obj = Instantiate(prefab);
            obj.name = prefab.name;
            obj.SetActive(false);
            queue.Enqueue(obj);
        }
    }
}
