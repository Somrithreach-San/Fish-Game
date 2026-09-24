using UnityEngine;

public class PerformanceMonitor : MonoBehaviour
{
    public static PerformanceMonitor Instance;
    [SerializeField] private int sampleWindow = 240;
    [SerializeField] private float logInterval = 2f;

    private float[] sampleRing;
    private float[] sortBuffer;
    private int sampleIndex = 0;
    private int sampleCount = 0;
    private float intervalTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        sampleRing = new float[sampleWindow];
        sortBuffer = new float[sampleWindow];
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (sampleRing == null || sampleRing.Length != sampleWindow)
        {
            sampleRing = new float[sampleWindow];
            sortBuffer = new float[sampleWindow];
            sampleIndex = 0;
            sampleCount = 0;
        }

        sampleRing[sampleIndex] = dt;
        sampleIndex = (sampleIndex + 1) % sampleWindow;
        if (sampleCount < sampleWindow) sampleCount++;

        intervalTimer += Time.unscaledDeltaTime;
        if (intervalTimer >= logInterval)
        {
            intervalTimer = 0f;
            LogStats();
        }
    }

    private void LogStats()
    {
        if (sampleCount == 0) return;
        float sum = 0f;
        float max = 0f;
        float min = float.MaxValue;

        for (int i = 0; i < sampleCount; i++)
        {
            float v = sampleRing[i];
            sortBuffer[i] = v;
            sum += v;
            if (v > max) max = v;
            if (v < min) min = v;
        }

        float avg = sum / sampleCount;
        float avgFps = 1f / Mathf.Max(0.0001f, avg);
        float minFps = 1f / Mathf.Max(0.0001f, max);
        float maxFps = 1f / Mathf.Max(0.0001f, min);

        System.Array.Sort(sortBuffer, 0, sampleCount);
        int idx = Mathf.Clamp(Mathf.FloorToInt(sampleCount * 0.99f), 0, sampleCount - 1);
        float p99 = sortBuffer[idx];
        float p99Fps = 1f / Mathf.Max(0.0001f, p99);
        long mem = System.GC.GetTotalMemory(false);
        float memMb = mem / (1024f * 1024f);
        Debug.Log($"Perf avgFPS={avgFps:F1} p99FPS={p99Fps:F1} minFPS={minFps:F1} maxFPS={maxFps:F1} mem={memMb:F1}MB");
    }
}
