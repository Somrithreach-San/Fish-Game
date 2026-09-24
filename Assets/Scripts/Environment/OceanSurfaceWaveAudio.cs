using System.Collections;
using UnityEngine;

/// <summary>
/// OceanSurfaceWaveAudio manages realistic, gentle ocean wave ambient audio near the water surface:
/// - Only active during Ocean levels (!LevelManager.IsCurrentLakeLevel).
/// - Proximity-based volume: loud when swimming near the surface ceiling (Y ~ 15.0f), smoothly fading to silence when diving deep (Y <= 4.0f).
/// - Race-condition safe: alternates between wave1 and wave2 with organic intervals so they never overlap or play continuously.
/// </summary>
public class OceanSurfaceWaveAudio : MonoBehaviour
    {
        public static OceanSurfaceWaveAudio Instance { get; private set; }

        [Header("Audio Clips")]
        [SerializeField] private AudioClip wave1Clip;
        [SerializeField] private AudioClip wave2Clip;

        [Header("Surface & Distance Settings")]
        [Tooltip("Water surface ceiling Y level in world units")]
        [SerializeField] private float waterSurfaceY = 15.0f;
        [Tooltip("Depth below surface at which wave sound becomes completely inaudible")]
        [SerializeField] private float maxAudibleDepth = 11.0f; // Audible from Y = 4.0f to 15.0f

        [Header("Playback Settings")]
        [Range(0.1f, 1f)]
        [SerializeField] private float maxWaveVolume = 0.75f;
        [SerializeField] private float minIntervalBetweenWaves = 1.2f;
        [SerializeField] private float maxIntervalBetweenWaves = 3.0f;
        [SerializeField] private float volumeFadeSpeed = 3.0f;

        private AudioSource audioSource;
        private Transform playerTransform;
        private Coroutine waveLoopRoutine;
        private int lastPlayedWaveIndex = 0;
        private float calculatedProximityFactor = 0f;
        private float currentVolume = 0f;
        private bool isAudioPaused = false;

        private static AudioClip s_CachedWave1;
        private static AudioClip s_CachedWave2;

        public AudioClip GetWave1Clip()
        {
            if (wave1Clip != null) return wave1Clip;
            if (s_CachedWave1 != null) return s_CachedWave1;

#if UNITY_EDITOR
            s_CachedWave1 = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/wave1.wav");
            if (s_CachedWave1 != null) return s_CachedWave1;
#endif
            s_CachedWave1 = Resources.Load<AudioClip>("wave1");
            return s_CachedWave1;
        }

        public AudioClip GetWave2Clip()
        {
            if (wave2Clip != null) return wave2Clip;
            if (s_CachedWave2 != null) return s_CachedWave2;

#if UNITY_EDITOR
            s_CachedWave2 = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/wave2.wav");
            if (s_CachedWave2 != null) return s_CachedWave2;
#endif
            s_CachedWave2 = Resources.Load<AudioClip>("wave2");
            return s_CachedWave2;
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

            audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D ambient with calculated proximity volume
            audioSource.volume = 0f;
            AudioSettingsManager.RouteToSfx(audioSource);
        }

        private void Start()
        {
            waveLoopRoutine = StartCoroutine(WaveCycleLoop());
        }

        private void Update()
        {
            UpdateProximityAndVolume();
        }

        private Transform GetPlayerTransform()
        {
            if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
            {
                return playerTransform;
            }

            if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
            {
                playerTransform = GameManager.instance.playerGameObject.transform;
                return playerTransform;
            }

            PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null)
            {
                playerTransform = pc.transform;
                return playerTransform;
            }

            return null;
        }

        private void UpdateProximityAndVolume()
        {
            // Only active in Ocean levels
            bool isOceanLevel = !LevelManager.IsCurrentLakeLevel;
            bool sfxEnabled = AudioSettingsManager.IsSfxEnabled;
            bool isGameOver = GameManager.instance != null && GameManager.instance.IsGameOver;
            bool isPaused = GameManager.Paused;

            if (!isOceanLevel || !sfxEnabled || isGameOver)
            {
                calculatedProximityFactor = 0f;
                targetVolume = 0f;
                currentVolume = Mathf.MoveTowards(currentVolume, 0f, Time.unscaledDeltaTime * (volumeFadeSpeed * 1.5f));
                if (audioSource != null)
                {
                    audioSource.volume = currentVolume;
                    if (isGameOver && audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                }
                return;
            }

            if (isPaused)
            {
                if (audioSource != null && audioSource.isPlaying)
                {
                    audioSource.Pause();
                    isAudioPaused = true;
                }
                return;
            }
            else if (isAudioPaused)
            {
                if (audioSource != null && audioSource.clip != null)
                {
                    audioSource.UnPause();
                }
                isAudioPaused = false;
            }

            // Calculate surface distance
            Transform p = GetPlayerTransform();
            if (p != null)
            {
                float playerY = p.position.y;
                float depthDistance = Mathf.Max(0f, waterSurfaceY - playerY);

                if (depthDistance >= maxAudibleDepth)
                {
                    calculatedProximityFactor = 0f;
                }
                else
                {
                    // Normalized 0 (at max depth) to 1 (at surface)
                    float t = Mathf.Clamp01(1.0f - (depthDistance / maxAudibleDepth));
                    // Smoothstep for organic, natural attenuation
                    calculatedProximityFactor = t * t * (3f - 2f * t);
                }
            }
            else
            {
                calculatedProximityFactor = 0f;
            }

            float targetVol = calculatedProximityFactor * maxWaveVolume * AudioSettingsManager.SfxVolume;
            currentVolume = Mathf.MoveTowards(currentVolume, targetVol, Time.unscaledDeltaTime * volumeFadeSpeed);

            if (audioSource != null)
            {
                audioSource.volume = currentVolume;
            }
        }

        private float targetVolume = 0f;

        private IEnumerator WaveCycleLoop()
        {
            // Initial gentle stagger
            yield return new WaitForSeconds(0.5f);

            while (true)
            {
                // Ocean check & game active check
                if (LevelManager.IsCurrentLakeLevel || (GameManager.instance != null && GameManager.instance.IsGameOver))
                {
                    if (audioSource != null && audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                    yield return new WaitForSeconds(1.0f);
                    continue;
                }

                while (GameManager.Paused)
                {
                    yield return null;
                }

                // If player is in audible range near the surface, trigger wave
                if (calculatedProximityFactor > 0.01f && AudioSettingsManager.IsSfxEnabled)
                {
                    // Alternate between wave1 and wave2 to ensure variety and prevent identical repeats
                    AudioClip clipToPlay = (lastPlayedWaveIndex == 1) ? GetWave2Clip() : GetWave1Clip();
                    lastPlayedWaveIndex = (lastPlayedWaveIndex == 1) ? 2 : 1;

                    if (clipToPlay != null && audioSource != null)
                    {
                        audioSource.clip = clipToPlay;
                        audioSource.loop = false;
                        audioSource.Play();

                        // Wait until clip finishes or ocean mode ends
                        while (audioSource.isPlaying && !LevelManager.IsCurrentLakeLevel && !(GameManager.instance != null && GameManager.instance.IsGameOver))
                        {
                            if (GameManager.Paused && audioSource.isPlaying)
                            {
                                audioSource.Pause();
                                while (GameManager.Paused) yield return null;
                                audioSource.UnPause();
                            }
                            yield return null;
                        }
                    }

                    // Organic, gentle interval between waves (simulates realistic ocean swells)
                    float restDuration = Random.Range(minIntervalBetweenWaves, maxIntervalBetweenWaves);
                    float restTimer = 0f;
                    while (restTimer < restDuration)
                    {
                        if (!GameManager.Paused)
                        {
                            restTimer += Time.deltaTime;
                        }
                        yield return null;
                    }
                }
                else
                {
                    // Player is deep down or sound disabled; poll intermittently
                    yield return new WaitForSeconds(0.4f);
                }
            }
        }

        private void OnDestroy()
        {
            if (waveLoopRoutine != null)
            {
                StopCoroutine(waveLoopRoutine);
                waveLoopRoutine = null;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
