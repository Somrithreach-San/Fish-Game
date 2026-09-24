using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-performance audio pool for one-shot 2D and 3D spatial sound effects.
/// Replaces untracked AudioSource.PlayClipAtPoint calls with pooled, pause-aware, SFX-toggle-aware AudioSources.
/// </summary>
public class SFXPool : MonoBehaviour
{
    private static SFXPool s_Instance;
    public static SFXPool Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = FindFirstObjectByType<SFXPool>();
                if (s_Instance == null)
                {
                    GameObject go = new GameObject("[SFXPool]");
                    s_Instance = go.AddComponent<SFXPool>();
                    DontDestroyOnLoad(go);
                }
            }
            return s_Instance;
        }
    }

    [Header("Pool Settings")]
    [SerializeField] private int initialPoolSize = 12;

    private readonly Queue<AudioSource> availableSources = new Queue<AudioSource>();
    private readonly List<AudioSource> activeSources = new List<AudioSource>();

    private void Awake()
    {
        if (s_Instance == null)
        {
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        InitializePool();
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewSource();
        }
    }

    private AudioSource CreateNewSource()
    {
        GameObject sourceObj = new GameObject($"Pooled_AudioSource_{availableSources.Count + activeSources.Count}");
        sourceObj.transform.SetParent(transform);
        AudioSource src = sourceObj.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        
        if (AudioSettingsManager.SfxMixerGroup != null)
        {
            src.outputAudioMixerGroup = AudioSettingsManager.SfxMixerGroup;
        }

        availableSources.Enqueue(src);
        return src;
    }

    private AudioSource GetAvailableSource()
    {
        AudioSource src = availableSources.Count > 0 ? availableSources.Dequeue() : CreateNewSource();
        activeSources.Add(src);
        src.gameObject.SetActive(true);
        return src;
    }

    private void ReturnSource(AudioSource src)
    {
        if (src == null) return;

        src.Stop();
        src.clip = null;
        src.transform.SetParent(transform);
        src.transform.localPosition = Vector3.zero;
        src.gameObject.SetActive(false);

        activeSources.Remove(src);
        availableSources.Enqueue(src);
    }

    /// <summary>
    /// Plays a 3D one-shot audio clip at a specific world position with realistic distance falloff.
    /// </summary>
    public static void Play3D(AudioClip clip, Vector3 position, float volume = 1.0f, float minDistance = 8.0f, float maxDistance = 42.0f, float pitch = 1.0f)
    {
        if (clip == null || !AudioSettingsManager.IsSfxEnabled) return;

        SFXPool pool = Instance;
        if (pool == null) return;

        AudioSource src = pool.GetAvailableSource();
        src.transform.position = position;
        src.spatialBlend = 1.0f; // Pure 3D spatial
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.pitch = pitch;
        src.volume = volume;
        src.clip = clip;
        src.mute = !AudioSettingsManager.IsSfxEnabled;
        src.outputAudioMixerGroup = AudioSettingsManager.SfxMixerGroup;

        src.Play();
        pool.StartCoroutine(pool.ReleaseAfterPlay(src, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch))));
    }

    /// <summary>
    /// Plays a 2D global one-shot audio clip.
    /// </summary>
    public static void Play2D(AudioClip clip, float volume = 1.0f, float pitch = 1.0f)
    {
        if (clip == null || !AudioSettingsManager.IsSfxEnabled) return;

        SFXPool pool = Instance;
        if (pool == null) return;

        AudioSource src = pool.GetAvailableSource();
        src.transform.position = Vector3.zero;
        src.spatialBlend = 0.0f; // 2D Stereo
        src.pitch = pitch;
        src.volume = volume;
        src.clip = clip;
        src.mute = !AudioSettingsManager.IsSfxEnabled;
        src.outputAudioMixerGroup = AudioSettingsManager.SfxMixerGroup;

        src.Play();
        pool.StartCoroutine(pool.ReleaseAfterPlay(src, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch))));
    }

    private IEnumerator ReleaseAfterPlay(AudioSource src, float duration)
    {
        float elapsed = 0f;
        bool wasPaused = false;
        while (elapsed < duration)
        {
            if (src == null) yield break;
            
            // If the game is paused, wait without incrementing audio duration
            if (GameManager.Paused)
            {
                if (src.isPlaying)
                {
                    src.Pause();
                    wasPaused = true;
                }
                yield return null;
                continue;
            }
            else if (wasPaused)
            {
                if (src.clip != null) src.UnPause();
                wasPaused = false;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        ReturnSource(src);
    }
}
