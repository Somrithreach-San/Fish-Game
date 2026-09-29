using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// DeepWhaleAmbience manages rare, atmospheric distant whale cries echoing through the deep ocean:
/// - Exclusively active in Coral Coast Deep Ocean Levels (Levels 5 to 8: Deep Blue Dropoff through Ocean Mastery).
/// - Dynamic Rarity: Has an initial chance per session to not play at all, and long, organic randomized intervals (35s-130s) so it feels rare, surprising, and never repetitive.
/// - Atmospheric Audio: Gentle volume, subtle randomized pitch, and stereo panning across the deep ocean expanse.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class DeepWhaleAmbience : MonoBehaviour
{
    public static DeepWhaleAmbience Instance { get; private set; }

    [Header("Whale Audio Clips")]
    [SerializeField] private AudioClip whaleSound1;
    [SerializeField] private AudioClip whaleSound2;
    [SerializeField] private AudioClip whaleSound3;

    [Header("Rarity & Timing Settings")]
    [Tooltip("Percentage chance (0 to 1) that any whale call can trigger during an eligible level session")]
    [Range(0f, 1f)]
    [SerializeField] private float sessionTriggerChance = 0.40f; // 40% chance per run, 60% of runs remain tranquil

    [Tooltip("Minimum initial delay before first possible whale call (seconds)")]
    [SerializeField] private float minInitialDelay = 55f; // Starts at least ~1 minute into the level

    [Tooltip("Maximum initial delay before first possible whale call (seconds)")]
    [SerializeField] private float maxInitialDelay = 110f;

    [Tooltip("Minimum interval between subsequent calls (seconds)")]
    [SerializeField] private float minIntervalBetweenCalls = 140f;

    [Tooltip("Maximum interval between subsequent calls (seconds)")]
    [SerializeField] private float maxIntervalBetweenCalls = 240f;

    [Tooltip("Maximum number of times a whale call can sound in a single level session")]
    [SerializeField] private int maxCallsPerSession = 1; // Strict 1 pass per session so it remains a special unique event

    [Header("Playback & Acoustics")]
    [Range(0.1f, 1f)]
    [SerializeField] private float baseVolume = 0.44f; // Soft, distant, non-intrusive echo
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.03f);
    [SerializeField] private Vector2 stereoPanRange = new Vector2(-0.55f, 0.55f);

    private AudioSource audioSource;
    private Coroutine ambientRoutine;
    private bool isPaused = false;
    private int callsPlayedThisSession = 0;

    private static AudioClip s_CachedWhale1;
    private static AudioClip s_CachedWhale2;
    private static AudioClip s_CachedWhale3;

    public AudioClip GetWhaleSound1()
    {
        if (whaleSound1 != null) return whaleSound1;
        if (s_CachedWhale1 != null) return s_CachedWhale1;

#if UNITY_EDITOR
        s_CachedWhale1 = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Whale_Sound_1.mp3");
        if (s_CachedWhale1 != null) return s_CachedWhale1;
#endif
        s_CachedWhale1 = Resources.Load<AudioClip>("Whale_Sound_1");
        return s_CachedWhale1;
    }

    public AudioClip GetWhaleSound2()
    {
        if (whaleSound2 != null) return whaleSound2;
        if (s_CachedWhale2 != null) return s_CachedWhale2;

#if UNITY_EDITOR
        s_CachedWhale2 = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Whale_Sound_2.mp3");
        if (s_CachedWhale2 != null) return s_CachedWhale2;
#endif
        s_CachedWhale2 = Resources.Load<AudioClip>("Whale_Sound_2");
        return s_CachedWhale2;
    }

    public AudioClip GetWhaleSound3()
    {
        if (whaleSound3 != null) return whaleSound3;
        if (s_CachedWhale3 != null) return s_CachedWhale3;

#if UNITY_EDITOR
        s_CachedWhale3 = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Whale_Sound_3.mp3");
        if (s_CachedWhale3 != null) return s_CachedWhale3;
#endif
        s_CachedWhale3 = Resources.Load<AudioClip>("Whale_Sound_3");
        return s_CachedWhale3;
    }

    /// <summary>
    /// Checks whether the current level is an eligible Deep Ocean level (Coral Coast Levels 5 to 8).
    /// </summary>
    public static bool IsEligibleLevel()
    {
        if (LevelManager.IsCurrentLakeLevel) return false;
        int lvl = LevelManager.CurrentLevel;
        return lvl >= 5 && lvl <= 8;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f; // 2D ambient with stereo panning
        audioSource.volume = 0f;
        AudioSettingsManager.RouteToSfx(audioSource);

        SubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
        if (ambientRoutine != null)
        {
            StopCoroutine(ambientRoutine);
            ambientRoutine = null;
        }
    }

    private void SubscribeEvents()
    {
        EventManager.StartListening<bool>("gamePaused", OnGamePaused);
        EventManager.StartListening("playerDeath", OnGameEnded);
        EventManager.StartListening("GameLoss", OnGameEnded);
        EventManager.StartListening("GameWin", OnGameEnded);
        EventManager.StartListening("stageClear", OnGameEnded);
        AudioSettingsManager.OnSfxSettingChanged += OnSfxSettingChanged;
        AudioSettingsManager.OnSfxVolumeChanged += OnSfxVolumeChanged;
    }

    private void UnsubscribeEvents()
    {
        EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        EventManager.StopListening("playerDeath", OnGameEnded);
        EventManager.StopListening("GameLoss", OnGameEnded);
        EventManager.StopListening("GameWin", OnGameEnded);
        EventManager.StopListening("stageClear", OnGameEnded);
        AudioSettingsManager.OnSfxSettingChanged -= OnSfxSettingChanged;
        AudioSettingsManager.OnSfxVolumeChanged -= OnSfxVolumeChanged;
    }

    private void OnEnable()
    {
        callsPlayedThisSession = 0;
        wasPlayingBeforePause = false;
        if (ambientRoutine != null)
        {
            StopCoroutine(ambientRoutine);
        }
        ambientRoutine = StartCoroutine(WhaleAmbienceLifecycleRoutine());
    }

    private void OnDisable()
    {
        wasPlayingBeforePause = false;
        if (ambientRoutine != null)
        {
            StopCoroutine(ambientRoutine);
            ambientRoutine = null;
        }
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    private bool wasPlayingBeforePause = false;

    private void OnGamePaused(bool paused)
    {
        isPaused = paused;
        if (audioSource != null)
        {
            if (paused)
            {
                if (audioSource.isPlaying)
                {
                    wasPlayingBeforePause = true;
                    audioSource.Pause();
                }
            }
            else
            {
                if (wasPlayingBeforePause)
                {
                    wasPlayingBeforePause = false;
                    audioSource.UnPause();
                }
            }
        }
    }

    private void OnGameEnded()
    {
        if (ambientRoutine != null)
        {
            StopCoroutine(ambientRoutine);
            ambientRoutine = null;
        }
        // Let any currently echoing whale sound naturally fade out rather than cutting off abruptly
    }

    private void OnSfxSettingChanged(bool enabled)
    {
        if (audioSource != null)
        {
            audioSource.mute = !enabled;
            audioSource.volume = AudioSettingsManager.GetScaledSfxVolume(baseVolume);
            if (!enabled && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }
    }

    private void OnSfxVolumeChanged(float vol)
    {
        if (audioSource != null)
        {
            audioSource.mute = !AudioSettingsManager.IsSfxEnabled;
            audioSource.volume = AudioSettingsManager.GetScaledSfxVolume(baseVolume);
        }
    }

    private IEnumerator WhaleAmbienceLifecycleRoutine()
    {
        // 1. Level eligibility check (Only Coral Coast Levels 5 to 8)
        if (!IsEligibleLevel())
        {
            yield break;
        }

        // Wait until briefing is dismissed before starting timer
        while (Rhinotap.LevelBriefingManager.IsBriefingActive)
        {
            yield return null;
        }

        // 2. Session probability roll: chance of total silence for dynamic rarity
        float sessionRoll = Random.value;
        if (sessionRoll > sessionTriggerChance)
        {
            yield break;
        }

        // 3. Initial delay before first possible whale call (35 to 75 seconds after briefing)
        float initialWait = Random.Range(minInitialDelay, maxInitialDelay);
        yield return StartCoroutine(WaitForSecondsRespectPause(initialWait));

        // 4. Ambient call loop
        while (callsPlayedThisSession < maxCallsPerSession)
        {
            if (!IsEligibleLevel() || LevelManager.IsLevelCompleted || (GameManager.instance != null && GameManager.instance.IsGameOver) || Rhinotap.LevelBriefingManager.IsBriefingActive)
            {
                yield break;
            }

            PlayRandomWhaleCall();
            callsPlayedThisSession++;

            // Wait for audio clip duration + cooldown interval before possible next call
            float waitNext = Random.Range(minIntervalBetweenCalls, maxIntervalBetweenCalls);
            yield return StartCoroutine(WaitForSecondsRespectPause(waitNext));
        }
    }

    private static int s_LastPlayedClipIndex = -1;

    public AudioClip GetNextWhaleSound()
    {
        List<AudioClip> availableClips = new List<AudioClip>();
        AudioClip c1 = GetWhaleSound1();
        AudioClip c2 = GetWhaleSound2();
        AudioClip c3 = GetWhaleSound3();
        if (c1 != null) availableClips.Add(c1);
        if (c2 != null) availableClips.Add(c2);
        if (c3 != null) availableClips.Add(c3);

        if (availableClips.Count == 0) return null;
        if (availableClips.Count == 1) return availableClips[0];

        // Strict alternating rotation (1 -> 2 -> 3 -> 1 -> 2 -> 3) so every whale pass uses a different sound
        int nextIndex = (s_LastPlayedClipIndex + 1) % availableClips.Count;
        s_LastPlayedClipIndex = nextIndex;
        return availableClips[nextIndex];
    }

    /// <summary>
    /// Plays an organic, distant echoing whale cry with directional panning.
    /// If swimRight is true, the call pans to the Left (where the whale originates from), and vice-versa.
    /// </summary>
    public void PlayDirectionalWhaleCall(bool swimRight)
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
        if (Rhinotap.LevelBriefingManager.IsBriefingActive) return;
        if (LevelManager.IsLevelCompleted || (GameManager.instance != null && GameManager.instance.IsGameOver)) return;

        AudioClip clipToPlay = GetNextWhaleSound();
        if (clipToPlay == null) return;

        if (audioSource == null) audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        if (audioSource == null) return;

        audioSource.playOnAwake = false;
        audioSource.mute = !AudioSettingsManager.IsSfxEnabled;
        audioSource.clip = clipToPlay;
        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        
        // Panning: sound originates from the side where the whale approaches
        float pan = swimRight ? -0.60f : 0.60f;
        audioSource.panStereo = pan;
        audioSource.volume = Mathf.Clamp01(baseVolume * Random.Range(0.88f, 1.12f) * AudioSettingsManager.SfxVolume);
        audioSource.Play();
        Debug.Log($"[DeepWhaleAmbience] Playing Whale Call ({clipToPlay.name}) with stereo pan {pan:F2}");
    }

    /// <summary>
    /// Triggers an organic whale pass across the scene. The echoing deep whale cry
    /// plays once the whale has 100% entered the scene.
    /// </summary>
    public void PlayRandomWhaleCall()
    {
        if (Rhinotap.LevelBriefingManager.IsBriefingActive) return;

        bool swimRight = Random.value > 0.5f;

        if (BackgroundWhaleAmbient.Instance != null)
        {
            // Whale starts cruising and fires the sound as soon as it has 100% entered the viewport
            BackgroundWhaleAmbient.Instance.TriggerWhalePass(swimRight, playSoundOnFullEntry: true);
        }
        else
        {
            PlayDirectionalWhaleCall(swimRight);
        }
    }

    private IEnumerator WaitForSecondsRespectPause(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!isPaused && !Rhinotap.LevelBriefingManager.IsBriefingActive && (GameManager.instance == null || !GameManager.Paused) && !LevelManager.IsLevelCompleted)
            {
                elapsed += Time.deltaTime;
            }
            yield return null;
        }
    }
}
