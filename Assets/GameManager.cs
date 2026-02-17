using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Rhinotap.Toolkit;
using Cinemachine;
using UnityEngine.InputSystem;
public class GameManager : MonoBehaviour
{
    #region Game Wide Singleton
    public static GameManager instance { get; private set; }

    private void Awake()
    {
        // Reset TimeScale on Scene Load (Fixes stuck pause state after restart)
        Time.timeScale = 1f;

        if (instance == null)
        {
            instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        // --- ENFORCE LANDSCAPE MODE (Mobile & Desktop) ---
        // 1. Disable Portrait Auto-Rotation
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        
        // 2. Enable Landscape Auto-Rotation (Left/Right)
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        
        // 3. Apply Settings
        Screen.orientation = ScreenOrientation.AutoRotation;
        
        // 4. Force Full Screen (Desktop/Mobile App only - NOT WebGL)
        #if !UNITY_WEBGL
        Screen.fullScreen = true;
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        #endif

        // 5. Emergency Kick: If stuck in Portrait, force LandscapeLeft immediately
        if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }
        // -------------------------------------------------

        // Ensure level-up sound listener is registered early
        EventManager.StartListening<int>("onLevelUp", (lvl) => {
            if (audioSource != null && levelUpClip != null)
                audioSource.PlayOneShot(levelUpClip);
        });
    }
    #endregion

    //==============| STATIC API |=====================//
    public static bool Paused { get { return instance != null && instance.isPaused; } }

    public static int PlayerLevel { 
        get
        {
            if (instance != null && instance.player != null)
                return instance.player.Level;
            else
                return 0;
        } 
    }

    //==============| INSTANCED API |======================//
    public bool isPaused { get; private set; } = false;
    public bool IsGameOver { get; private set; } = false; // Added to prevent pause during Game Over

    public GameObject playerGameObject { get; private set; }

    private PlayerController player;


    private AudioSource audioSource;
    [Header("Sounds")]
    [SerializeField]
    private AudioClip levelUpClip;
    [SerializeField]
    private AudioClip buttonSoundEffect; // Button sound (Pause/Resume)
    public AudioClip ButtonSoundEffect => buttonSoundEffect; // Public Accessor for UI
    [SerializeField]
    private AudioClip stageClearClip;    // Stage Clear / Win
    [SerializeField]
    private AudioClip waterLoopClip;     // Ambient Loop

    private AudioSource ambientSource;
    private AudioSource sfxSource;


    [Range(0f, 10f)]
    [SerializeField]
    private float restartLevelTimer = 0f;

    // Called from WebGL JS interface
    public void SetPausedFromJS(int pausedState)
    {
        bool shouldPause = (pausedState == 1);
        if (isPaused != shouldPause)
        {
            PlayPause();
        }
    }

    public  void PlayPause()
    {
        // Prevent pausing if Game Over sequence is active
        if (IsGameOver) return;

        isPaused = !isPaused;

        EventManager.Trigger<bool>("gamePaused", isPaused);

        // Toggle cursor visibility: Show when paused, Hide when playing
        Cursor.visible = isPaused;
        // User Request: Confine cursor during gameplay, Release (None) when paused (UI)
        Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Confined;
        
        if( isPaused)
        {
            Time.timeScale = 0f;
            if (audioSource != null) audioSource.Pause();
            if (ambientSource != null) ambientSource.Pause();
        } else
        {
            Time.timeScale = 1f;
            if (audioSource != null) audioSource.Play();
            if (ambientSource != null) ambientSource.Play();
        }
    }



    //Mono
    private void Start()
    {
        IsGameOver = false; // Reset Game Over state
        
        //Get Virtual Camera Defaults
        GetVcamComponents();
        if (Vcam != null)
            Vcam.m_Lens.OrthographicSize = CameraDefaultSize;
            //CameraDefaultSize = Vcam.m_Lens.OrthographicSize;

        audioSource = GetComponent<AudioSource>();

        

        EventManager.StartListening<GameObject>("PlayerSpawn", (playerObj) => { 
            if( playerObj != null)
            {
                playerGameObject = playerObj;
                player = playerObj.GetComponent<PlayerController>();
                Vcam.Follow = playerObj.transform;
            }
        });

        // Play level up sound when player levels up (if assigned)
        EventManager.StartListening<int>("onLevelUp", (lvl) => {
            if (audioSource != null && levelUpClip != null)
                audioSource.PlayOneShot(levelUpClip);
        });

        EventManager.Trigger("GameStart");
        
        // FIX: Ensure Audio settings are correct for background music
        if (audioSource != null)
        {
            audioSource.loop = true;
            audioSource.playOnAwake = true; // Try to play immediately (Autoplay policy permitting)
            if (!audioSource.isPlaying) audioSource.Play();
        }

        // Setup SFX Source
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        // Setup Ambient Source
        if (waterLoopClip != null)
        {
            ambientSource = gameObject.AddComponent<AudioSource>();
            ambientSource.clip = waterLoopClip;
            ambientSource.loop = true;
            ambientSource.playOnAwake = true; // Try to play immediately
            if (!ambientSource.isPlaying) ambientSource.Play();
        }

        EventManager.StartListening("playerDeath", PlayerDeathSequence);
        
        // FORCE AUDIO CHECK ON START (Mobile/Web)
        AudioListener.pause = false;
        AudioListener.volume = 1f;

        // FORCE MOUSE SIMULATION FOR TOUCH (Fixes UI buttons on Mobile)
        Input.simulateMouseWithTouches = true;
    }

    private bool isOrientationPaused = false;

    private void Update()
    {
        // FIX: Mobile/Web Autoplay Policy
        // Browsers often block audio until the first user interaction.
        // If audio should be playing but isn't, retry on any input.
        if (!isPaused)
        {
            bool inputDetected = false;

            // Check New Input System
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) inputDetected = true;
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) inputDetected = true;

            // Check Legacy Input / Mobile Touch (Fallback)
            // Wrapped in try-catch in case "New Input System Only" is active, which causes Input class to throw errors.
            try {
                if (Input.touchCount > 0 || Input.GetMouseButtonDown(0) || Input.anyKeyDown) inputDetected = true;
            } catch { }

            if (inputDetected)
            {
                if (audioSource != null && !audioSource.isPlaying) audioSource.Play();
                if (ambientSource != null && !ambientSource.isPlaying) ambientSource.Play();
            }
        }

        // --- MOBILE ROTATION CHECK ---
        // Pause if Portrait, Resume if Landscape (and was paused by rotation)
        if (Application.isMobilePlatform || Application.isEditor) // Testable in Editor too
        {
            if (Screen.height > Screen.width) // Portrait
            {
                // Always Force Mute in Portrait (User Request: Pause everything including audios)
                AudioListener.pause = true;

                if (!isPaused)
                {
                    PlayPause();
                    isOrientationPaused = true;
                }
            }
            else // Landscape
            {
                // Always Unmute Listener in Landscape (Game Pause handles individual sources)
                AudioListener.pause = false;

                if (isPaused && isOrientationPaused)
                {
                    PlayPause();
                    isOrientationPaused = false;
                }
            }
        }

        // Pause controls:
        // - Esc toggles pause/resume
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (sfxSource != null && buttonSoundEffect != null) 
                    sfxSource.PlayOneShot(buttonSoundEffect);
                PlayPause();
            }
        }
    }


    Coroutine restartLevelSequence;
    void PlayerDeathSequence()
    {
        // Fix: If game was paused, force unpause to allow restart sequence to run and hide pause menu
        if (isPaused)
        {
            PlayPause();
        }

        IsGameOver = true; // Flag Game Over to block pause
        
        if (restartLevelSequence == null)
            restartLevelSequence = StartCoroutine( RestartLevel() );
    }

    /// <summary>
    /// Public API to trigger game restart (e.g. on Win)
    /// </summary>
    public void TriggerGameWin()
    {
        // Fix: If game was paused, force unpause to allow restart sequence to run and hide pause menu
        if (isPaused)
        {
            PlayPause();
        }

        IsGameOver = true; // Flag Game Over to block pause
        
        if (restartLevelSequence == null)
        {
            // Play win sound
            if (sfxSource != null && stageClearClip != null)
                sfxSource.PlayOneShot(stageClearClip);

            // Optional: Play a win sound or show effect here
            restartLevelSequence = StartCoroutine(RestartLevel());
        }
    }
    
    IEnumerator RestartLevel()
    {
        float resetTime = 0f;
        while(resetTime < restartLevelTimer)
        {
            resetTime += Time.deltaTime;
            yield return null;
        }

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);

        yield return new WaitForSeconds(1f);
        
        Start();
    }


    //==============| Cinemachine |======================//
    CinemachineVirtualCamera Vcam;
    CinemachineBasicMultiChannelPerlin VcamNoise;
    Coroutine cameraShake;
    Coroutine cameraZoom;
    float CameraDefaultSize = 8f; // Reverted to original 8f
    /// <summary>
    /// Shake the camera for a duration with given settings. Requires cinemachine Virtual Camera in the scene
    /// </summary>
    /// <param name="time">Duration</param>
    /// <param name="amplitudeGain"></param>
    /// <param name="frequencyGain"></param>
    public void CameraShake(float time = 1f, float amplitudeGain = 4f, float frequencyGain = 4f)
    {
        //Verify components
        GetVcamComponents();
        if (Vcam == null || VcamNoise == null) return;

        //Verify duration
        if (time <= 0f) { time = 0f; }

        //Stop old coroutine
        if (cameraShake != null)
        {
            StopCoroutine(cameraShake);
        }
        //if time is 0, no need to shake
        if( time <= 0f)
        {
            return;
        }
        //start new coroutine
        cameraShake = StartCoroutine(CinemachineShake(time, amplitudeGain, frequencyGain));

    }
    private void GetVcamComponents()
    {
        if (Vcam == null)
            Vcam = GameObject.FindAnyObjectByType<CinemachineVirtualCamera>();

        if (VcamNoise == null && Vcam != null)
            VcamNoise = Vcam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();

        if (Vcam == null)
            Debug.Log("Missing game object: Cinemachine Virtual Camera");
        if (VcamNoise == null)
            Debug.Log("Missing Cinemachine Camera Component: Noise");
    }

    
    
    IEnumerator CinemachineShake( float time, float amplitude, float frequency)
    {
        if (Vcam == null || VcamNoise == null) yield break;

        //Start shaking
        VcamNoise.m_AmplitudeGain = amplitude;
        VcamNoise.m_FrequencyGain = frequency;

        //Wait for duration
        yield return new WaitForSeconds(time);

        //revert
        VcamNoise.m_AmplitudeGain = 0f;
        VcamNoise.m_FrequencyGain = 0f;
        yield break;
    }


    public void ResetCameraSize()
    {
        Vcam.m_Lens.OrthographicSize = CameraDefaultSize;
    }

    /// <summary>
    /// Original camera size will be multiplied with the param. If size is 10 and multiplier is 1.2 new size will be 12
    /// </summary>
    /// <param name="multiplier"></param>
    /// <param name="transitionTime">How long it should take to transition</param>
    public void CameraZoom(float multiplier, float transitionTime = 0.5f)
    {
        if( multiplier <= 0f) { multiplier = 0.1f; } //min
        if(multiplier >= 3f) { multiplier = 3f; } //max

        float currentSize = Vcam.m_Lens.OrthographicSize;
        float newSize = CameraDefaultSize * multiplier;

        if (cameraZoom != null)
            StopCoroutine(cameraZoom);

        cameraZoom = StartCoroutine(CameraZoomRoutine(currentSize, newSize, transitionTime));

    }
    IEnumerator CameraZoomRoutine(float currentSize, float targetSize, float duration)
    {
        float time = 0f;
        while( time < duration)
        {
            time += Time.deltaTime;
            float percentage = time / duration;
            float size = Mathf.Lerp(currentSize, targetSize, percentage);
            Vcam.m_Lens.OrthographicSize = size;
            yield return null;
        }
    }

    //==============| /Cinemachine |======================//
}
