using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Object pool for FishBloodCloud effects.
/// Ensures all active blood particles are completely cleared whenever a level restarts or reloads.
/// </summary>
public class FishBloodCloudPool : MonoBehaviour
{
    private static FishBloodCloudPool s_Instance;

    private const int INITIAL_POOL_SIZE = 16;
    private readonly Queue<FishBloodCloud> pool = new Queue<FishBloodCloud>();
    private readonly List<FishBloodCloud> allInstances = new List<FishBloodCloud>();

    public static FishBloodCloudPool GetOrCreate()
    {
        if (s_Instance != null) return s_Instance;

        GameObject poolRoot = new GameObject("FishBloodCloudPool");
        if (Application.isPlaying)
        {
            Object.DontDestroyOnLoad(poolRoot);
        }

        s_Instance = poolRoot.AddComponent<FishBloodCloudPool>();
        s_Instance.Prewarm(INITIAL_POOL_SIZE);
        return s_Instance;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearAllActive();
    }

    private void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            pool.Enqueue(CreatePooledInstance());
        }
    }

    private FishBloodCloud CreatePooledInstance()
    {
        GameObject obj = new GameObject("FishBloodCloud_Pooled");
        obj.transform.SetParent(transform, worldPositionStays: false);

        FishBloodCloud cloud = obj.AddComponent<FishBloodCloud>();
        cloud.PrewarmParticleSystem();
        obj.SetActive(false);

        allInstances.Add(cloud);
        return cloud;
    }

    public FishBloodCloud GetFromPool()
    {
        if (pool.Count > 0)
        {
            return pool.Dequeue();
        }

        return CreatePooledInstance();
    }

    public void ReturnToPool(FishBloodCloud cloud)
    {
        if (cloud == null) return;
        cloud.gameObject.SetActive(false);
        if (!pool.Contains(cloud))
        {
            pool.Enqueue(cloud);
        }
    }

    /// <summary>
    /// Instantly clears and resets all active blood clouds across the entire pool
    /// (called on level restart, scene load, or game reset).
    /// </summary>
    public void ClearAllActive()
    {
        StopAllCoroutines();
        pool.Clear();

        for (int i = 0; i < allInstances.Count; i++)
        {
            FishBloodCloud cloud = allInstances[i];
            if (cloud != null)
            {
                cloud.StopAllCoroutines();
                var ps = cloud.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Clear(true);
                }
                cloud.gameObject.SetActive(false);
                pool.Enqueue(cloud);
            }
        }
    }
}
