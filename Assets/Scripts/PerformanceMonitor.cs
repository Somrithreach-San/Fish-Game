using System.Collections.Generic;
using UnityEngine;

public class PerformanceMonitor : MonoBehaviour
{
    public static PerformanceMonitor Instance;
    [SerializeField] private int sampleWindow = 240;
    [SerializeField] private float logInterval = 2f;
    private readonly List<float> samples = new List<float>();
    private float intervalTimer;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        samples.Add(dt);
        if (samples.Count > sampleWindow) samples.RemoveAt(0);
        intervalTimer += Time.unscaledDeltaTime;
        if (intervalTimer >= logInterval)
        {
            intervalTimer = 0f;
            LogStats();
        }
    }
    private void LogStats()
    {
        if (samples.Count == 0) return;
        float sum = 0f;
        float max = 0f;
        float min = float.MaxValue;
        for (int i = 0; i < samples.Count; i++)
        {
            float v = samples[i];
            sum += v;
            if (v > max) max = v;
            if (v < min) min = v;
        }
        float avg = sum / samples.Count;
        float avgFps = 1f / Mathf.Max(0.0001f, avg);
        float minFps = 1f / Mathf.Max(0.0001f, max);
        float maxFps = 1f / Mathf.Max(0.0001f, min);
        var arr = samples.ToArray();
        System.Array.Sort(arr);
        int idx = Mathf.Clamp(Mathf.FloorToInt(arr.Length * 0.99f), 0, arr.Length - 1);
        float p99 = arr[idx];
        float p99Fps = 1f / Mathf.Max(0.0001f, p99);
        long mem = System.GC.GetTotalMemory(false);
        float memMb = mem / (1024f * 1024f);
        Debug.Log($"Perf avgFPS={avgFps:F1} p99FPS={p99Fps:F1} minFPS={minFps:F1} maxFPS={maxFps:F1} mem={memMb:F1}MB");
    }
}
