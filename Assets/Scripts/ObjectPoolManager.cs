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
        
        // Activate
        objToSpawn.SetActive(true);

        // Track it so we know which pool it belongs to when despawning
        if (!activeObjects.ContainsKey(objToSpawn))
        {
            activeObjects.Add(objToSpawn, key);
        }
        else
        {
            activeObjects[objToSpawn] = key;
        }

        return objToSpawn;
    }

    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        // If we know which pool it belongs to
        if (activeObjects.ContainsKey(obj))
        {
            string key = activeObjects[obj];
            
            // Deactivate
            obj.SetActive(false);

            // Add back to pool
            if (!poolDictionary.ContainsKey(key))
            {
                poolDictionary.Add(key, new Queue<GameObject>());
            }
            
            poolDictionary[key].Enqueue(obj);
            activeObjects.Remove(obj);
        }
        else
        {
            // If it wasn't spawned via pool, just destroy it
            Debug.LogWarning($"Object {obj.name} was not spawned via ObjectPoolManager. Destroying normally.");
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
