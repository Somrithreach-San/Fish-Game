using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Rhinotap.Toolkit;

public class RiverBoat : MonoBehaviour
{
    public enum BoatState { Arriving, StoppedWaiting, Fishing, DepartWaiting, Departing }

    [Header("River Boat Appearance")]
    [SerializeField] private float boatScale = 1.38f;
    [Tooltip("How deep the lower hull dips below the water surface (4.75f slightly higher than 5.0f)")]
    [SerializeField] private float submergenceDepth = 4.75f;
    [SerializeField] private float bobFrequency = 2.2f;
    [SerializeField] private float bobAmplitude = 0.04f;
    [SerializeField] private float tiltAmplitude = 1.2f;
    [SerializeField] private Vector2 propellerOffset = new Vector2(5.13f, -1.19f);

    [Header("Bubble Particle Materials")]
    [SerializeField] private Material bubbleMaterial;
    [SerializeField] private Texture2D bubbleTexture;

    [Header("Bubble Particle Settings")]
    [SerializeField] private float bubbleEmissionRate = 42f;
    [SerializeField] private Vector2 bubbleSizeRange = new Vector2(0.14f, 0.42f);
    [SerializeField] private Vector2 bubbleSpeedRange = new Vector2(1.5f, 3.5f);
    [SerializeField] private Vector2 bubbleLifetimeRange = new Vector2(1.0f, 1.8f);
    [SerializeField] private int bubbleMaxParticles = 120;

    [Header("Movement Settings")]
    [SerializeField] private float arriveSpeed = 4.5f;
    [SerializeField] private float departSpeed = 5.5f;
    [SerializeField] private float stopPauseDurationMin = 2.5f;
    [SerializeField] private float stopPauseDurationMax = 3.0f;

    [Header("Harpoon Settings")]
    [Tooltip("Min tilt angle deviation from straight down")]
    [SerializeField] private float harpoonTiltMin = 8f;
    [Tooltip("Max tilt angle deviation from straight down (82 = covers full lake from left to right bank while maintaining downward plunge)")]
    [SerializeField] private float harpoonTiltMax = 82f;
    [Tooltip("Minimum pause before the first harpoon fires (tension build-up)")]
    [SerializeField] private float preFirPauseMin = 0.4f;
    [SerializeField] private float preFirPauseMax = 1.2f;
    [Tooltip("Delay between individual shots in multi-shot patterns")]
    [SerializeField] private float betweenShotDelayMin = 0.55f;
    [SerializeField] private float betweenShotDelayMax = 1.8f;
    [Tooltip("Fan volley spread angle between adjacent harpoons")]
    [SerializeField] private float fanSpreadAngle = 18f;
    [Tooltip("Number of hunting attempts/rounds the boat makes before departing (at least 2 tries)")]
    [SerializeField] private int huntingRounds = 2;
    [Tooltip("Pause between hunting rounds while boat stays in place before re-aiming")]
    [SerializeField] private float betweenRoundsPause = 2.2f;

    private BoatState currentState = BoatState.Arriving;
    public BoatState State => currentState;

    private SpriteRenderer spriteRenderer;
    private ParticleSystem wakeParticleSystem;
    private float targetX;
    private bool facingRight = true;
    private float travelDirection = 1f; // +1 right, -1 left
    private float seed;
    private bool isPaused = false;
    private float worldWaterSurfaceY = 15.0f;
    private List<HarpoonHazard> activeHarpoons = new List<HarpoonHazard>();
    private Coroutine lifecycleCoroutine;

    // ── Targeting Laser Sight (Aims red laser beam exclusively at the player) ──
    [Header("Laser Sight Settings")]
    [Tooltip("How long the laser tracks the player fish before deciding to shoot (~3.8 seconds)")]
    [SerializeField] private float laserTrackDuration = 3.8f;
    [Tooltip("Brief reaction delay after removing the laser before shooting harpoon (seconds)")]
    [SerializeField] private float postLockReactionDelay = 0.35f;

    [Header("Underwater Laser Look")]
    [Tooltip("Number of points in the beam — more = smoother waver curve")]
    [SerializeField] private int laserSegments = 14;
    [Tooltip("Perpendicular waver amplitude (world units) while tracking")]
    [SerializeField] private float waverAmplitude = 0.04f;
    [Tooltip("Waver shrinks to this value as the beam locks on — visible tension cue")]
    [SerializeField] private float waverAmplitudeLocked = 0.005f;
    [Tooltip("How fast the noise pattern scrolls along the beam")]
    [SerializeField] private float waverNoiseSpeed = 0.6f;
    [Tooltip("SmoothDamp lag while beam swims toward the fish (seconds). 0 = instant snap.")]
    [SerializeField] private float targetFollowLag = 0.30f;

    // Laser 1: Player fish
    private LineRenderer laserLineRenderer;
    private Vector3 currentLaserTarget;
    // Laser 2: AI fish (Level >= 2)
    private LineRenderer laserLineRendererAI;
    private Vector3 currentAILaserTarget;
    private Fish currentAITarget;
    private bool hasValidAITarget = false;

    private bool isLaserActive = false;
    private float laserTrackElapsed = 0f;
    private float lastLockedAngle = 0f;
    private float lastLockedAngleAI = 0f;

    private SpriteRenderer laserTipPlayer;
    private SpriteRenderer laserTipAI;

    // Underwater beam runtime state
    private Vector3 laserTargetVelocity;    // SmoothDamp accumulator — player beam
    private Vector3 aiTargetVelocity;       // SmoothDamp accumulator — AI beam
    private float   laserNoiseSeed;         // Per-boat noise offset so multiple boats don't waver in sync
    private ParticleSystem bubbleTrail;
    private float   bubbleTimer = 0f;
    private bool    wasLockingPhase = false; // Edge-detect for lock-on ping

    private static Material s_LaserMaterial = null;
    private static Sprite   s_LaserDotSprite = null;
    private static Shader   s_UnderwaterShader = null;
    private static Texture2D s_NoiseTex = null;


    [Header("Engine Audio Settings")]
    [SerializeField] private AudioClip engineDriveClip;
    [SerializeField] private float maxEngineVolume = 0.85f;
    [SerializeField] private float minEngineDistance = 5.0f;
    [SerializeField] private float maxEngineDistance = 24.0f;

    private AudioSource engineAudioSource;
    private float currentAudioVolume = 0f;
    private float targetAudioVolume = 0f;
    private Transform playerTransform;

    // Cache world boundaries
    private float worldBgLeft = -25f;
    private float worldBgRight = 25f;
    private float worldFloorY = -14f;

    // Static caches to eliminate GC and spawn stutter
    private static Material s_CachedBubbleMat;
    private static Shader s_CachedBubbleShader;
    private static AudioClip s_CachedEngineClip;
    private static Transform s_GlobalPlayerTransform;
    private static float s_WorldBgLeft;
    private static float s_WorldBgRight;
    private static float s_WorldSurfaceY;
    private static float s_WorldFloorY = -14f;
    private static bool s_BoundsCached = false;
    private static AnimationCurve s_SizeCurve = null;
    private static Gradient s_ColorGradient = null;

    public float WaterSurfaceY => worldWaterSurfaceY;
    public float LakeFloorY => worldFloorY;
    public float WorldBgLeft => worldBgLeft;
    public float WorldBgRight => worldBgRight;
    public float WorldFloorY => worldFloorY;

    public static float GlobalFloorY => s_WorldFloorY;
    public static float GlobalSurfaceY => s_WorldSurfaceY;
    public static bool BoundsCached => s_BoundsCached;

    public static void SetGlobalBubbleMaterial(Material mat)
    {
        if (mat != null) s_CachedBubbleMat = mat;
    }

    public static void SetGlobalEngineClip(AudioClip clip)
    {
        if (clip != null) s_CachedEngineClip = clip;
    }

    public static void SetGlobalPlayer(Transform p)
    {
        if (p != null) s_GlobalPlayerTransform = p;
    }

    public static void SetGlobalWorldBounds(float bgLeft, float bgRight, float surfaceY, float floorY = -14f)
    {
        s_WorldBgLeft = bgLeft;
        s_WorldBgRight = bgRight;
        s_WorldSurfaceY = surfaceY;
        s_WorldFloorY = floorY;
        s_BoundsCached = true;
    }

    /// <summary>
    /// Computes the exact off-screen spawn position for the river boat hazard before instantiation/spawning.
    /// </summary>
    public static void CalculateSpawnPlacement(bool cruiseFromOffscreen, float bgLeft, float bgRight, float surfaceY, bool startFromLeft, out Vector3 spawnPos)
    {
        float boatHalfWidth = (12.64f * 1.20f) * 0.5f; // ~7.58 units
        float spawnMargin = boatHalfWidth + 3.0f;
        float halfHeight = (8.42f * 1.20f) * 0.5f; // ~5.05 units
        float sY = (surfaceY > 5f) ? surfaceY : 15.0f;
        float targetCenterY = (sY - 4.75f) + halfHeight;
        
        float startX = startFromLeft ? (bgLeft - spawnMargin) : (bgRight + spawnMargin);
        spawnPos = new Vector3(startX, targetCenterY, 0f);
    }

    public void WarmUpParticles()
    {
        SetupWakeParticles();
        if (wakeParticleSystem != null)
        {
            wakeParticleSystem.Simulate(0.01f, true, true);
        }
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        spriteRenderer.sortingOrder = 3; // Below player fish (5) so player renders on top of the boat

        if (spriteRenderer.sprite == null)
        {
            Sprite s = GridController.LoadRiverBoatSprite();
            if (s != null) spriteRenderer.sprite = s;
        }
        seed = Random.Range(0f, 100f);

        if (s_BoundsCached)
        {
            worldBgLeft = s_WorldBgLeft;
            worldBgRight = s_WorldBgRight;
            worldWaterSurfaceY = s_WorldSurfaceY;
            worldFloorY = s_WorldFloorY;
        }
        else
        {
            DiscoverWorldBounds();
        }

        if (wakeParticleSystem == null)
        {
            Transform existingChild = transform.Find("MotorWakeBubbles");
            if (existingChild != null)
            {
                wakeParticleSystem = existingChild.GetComponent<ParticleSystem>();
            }
        }

        SetupAudio();
    }

    public void SetupAudio(AudioClip clip = null)
    {
        if (clip != null) s_CachedEngineClip = clip;

        if (engineAudioSource == null)
        {
            engineAudioSource = GetComponent<AudioSource>();
            if (engineAudioSource == null)
            {
                engineAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        AudioClip clipToUse = engineDriveClip != null ? engineDriveClip : s_CachedEngineClip;
        if (clipToUse == null)
        {
            clipToUse = Resources.Load<AudioClip>("Boat_Engine_Drive");
            if (clipToUse != null) s_CachedEngineClip = clipToUse;
        }

        if (clipToUse != null)
        {
            engineAudioSource.clip = clipToUse;
            engineAudioSource.loop = true;
            engineAudioSource.playOnAwake = false;
            engineAudioSource.spatialBlend = 0f;
            engineAudioSource.volume = 0f;
            engineAudioSource.pitch = 1f;
            engineAudioSource.mute = !AudioSettingsManager.IsSfxEnabled;
        }
    }

    private void DiscoverWorldBounds()
    {
        GameObject wbObj = GameObject.Find("WorldBounds");
        if (wbObj != null)
        {
            PolygonCollider2D poly = wbObj.GetComponent<PolygonCollider2D>();
            if (poly != null && poly.pathCount > 0)
            {
                Vector2[] pts = poly.GetPath(0);
                float minX = float.MaxValue, maxX = float.MinValue;
                float maxY = float.MinValue, minY = float.MaxValue;
                foreach (var p in pts)
                {
                    Vector2 wp = (Vector2)wbObj.transform.TransformPoint(p);
                    if (wp.x < minX) minX = wp.x;
                    if (wp.x > maxX) maxX = wp.x;
                    if (wp.y > maxY) maxY = wp.y;
                    if (wp.y < minY) minY = wp.y;
                }
                worldBgLeft = minX;
                worldBgRight = maxX;
                worldWaterSurfaceY = maxY;
                worldFloorY = minY;
                s_WorldBgLeft = minX;
                s_WorldBgRight = maxX;
                s_WorldSurfaceY = maxY;
                s_WorldFloorY = minY;
                s_BoundsCached = true;
                return;
            }
        }
        worldBgLeft = -25f;
        worldBgRight = 25f;
        worldWaterSurfaceY = 15f;
        worldFloorY = -14f;
    }

    private float GetBoatHalfWidth()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return spriteRenderer.bounds.extents.x;
        }
        return (12.64f * boatScale) * 0.5f;
    }

    private float GetBoatHalfHeight()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return (spriteRenderer.sprite.rect.height / spriteRenderer.sprite.pixelsPerUnit) * boatScale * 0.5f;
        }
        return (8.42f * boatScale) * 0.5f;
    }

    private float GetTargetCenterY()
    {
        float halfHeight = GetBoatHalfHeight();
        float surfaceY = (worldWaterSurfaceY > 5f) ? worldWaterSurfaceY : 15.0f;
        return (surfaceY - submergenceDepth) + halfHeight;
    }

    private bool isEventSubscribed = false;

    private void Start()
    {
        GetPlayerTransform();
        if (!isEventSubscribed)
        {
            try
            {
                EventManager.StartListening<bool>("gamePaused", OnGamePaused);
                EventManager.StartListening("playerDeath", StopEngineAudio);
                EventManager.StartListening("GameLoss", StopEngineAudio);
                EventManager.StartListening("GameWin", StopEngineAudio);
                isEventSubscribed = true;
            }
            catch { }
        }
        AudioSettingsManager.OnSfxSettingChanged += OnSfxSettingChanged;
    }

    /// <summary>
    /// Immediately halts boat engine sound effect, laser sights, and resets volume to zero.
    /// </summary>
    public void StopEngineAudio()
    {
        if (engineAudioSource != null)
        {
            engineAudioSource.Stop();
            engineAudioSource.volume = 0f;
        }
        currentAudioVolume = 0f;
        targetAudioVolume = 0f;
        StopLaserSight();
    }

    private void OnEnable()
    {
        // Enforce sort order on every spawn (pooled objects skip Awake on re-enable)
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = 3; // Always below player fish (sortingOrder 5)
        }

        // Re-sync audio mute state and reset volume on each spawn
        if (engineAudioSource != null)
        {
            engineAudioSource.mute = !AudioSettingsManager.IsSfxEnabled;
            engineAudioSource.volume = 0f;
        }
        currentAudioVolume = 0f;
        targetAudioVolume = 0f;
        activeHarpoons.Clear();
    }

    private void OnDisable()
    {
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = null;
        }

        StopLaserSight();
        CleanupActiveHarpoons();
        StopEngineAudio();

        if (wakeParticleSystem != null && wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnDestroy()
    {
        StopLaserSight();
        CleanupActiveHarpoons();
        if (isEventSubscribed)
        {
            try
            {
                EventManager.StopListening<bool>("gamePaused", OnGamePaused);
                EventManager.StopListening("playerDeath", StopEngineAudio);
                EventManager.StopListening("GameLoss", StopEngineAudio);
                EventManager.StopListening("GameWin", StopEngineAudio);
            }
            catch { }
            isEventSubscribed = false;
        }
        AudioSettingsManager.OnSfxSettingChanged -= OnSfxSettingChanged;
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = null;
        }
        StopEngineAudio();
    }

    private void CleanupActiveHarpoons()
    {
        if (activeHarpoons != null && activeHarpoons.Count > 0)
        {
            HarpoonHazard[] harpoonsToClean = activeHarpoons.ToArray();
            activeHarpoons.Clear();
            for (int i = 0; i < harpoonsToClean.Length; i++)
            {
                if (harpoonsToClean[i] != null && harpoonsToClean[i].gameObject != null)
                {
                    if (ObjectPoolManager.Instance != null)
                    {
                        ObjectPoolManager.Instance.Despawn(harpoonsToClean[i].gameObject);
                    }
                    else
                    {
                        Destroy(harpoonsToClean[i].gameObject);
                    }
                }
            }
        }
    }

    private void OnGamePaused(bool paused)
    {
        isPaused = paused;
        if (engineAudioSource != null)
        {
            if (paused) engineAudioSource.Pause();
            else engineAudioSource.UnPause();
        }
    }

    private void OnSfxSettingChanged(bool enabled)
    {
        if (engineAudioSource != null)
        {
            engineAudioSource.mute = !enabled;
        }
    }

    public void Initialize(Sprite bSprite, float destinationX, bool cruiseFromOffscreen = true, Material bubbleMat = null, Texture2D bubbleTex = null, float bgLeft = float.NaN, float bgRight = float.NaN, float surfaceY = float.NaN, bool? overrideStartFromLeft = null)
    {
        if (!float.IsNaN(bgLeft) && !float.IsNaN(bgRight) && !float.IsNaN(surfaceY))
        {
            worldBgLeft = bgLeft;
            worldBgRight = bgRight;
            worldWaterSurfaceY = surfaceY;
            s_WorldBgLeft = bgLeft;
            s_WorldBgRight = bgRight;
            s_WorldSurfaceY = surfaceY;
            s_BoundsCached = true;
        }
        else if (s_BoundsCached)
        {
            worldBgLeft = s_WorldBgLeft;
            worldBgRight = s_WorldBgRight;
            worldWaterSurfaceY = s_WorldSurfaceY;
        }

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        if (bSprite != null)
        {
            if (spriteRenderer.sprite != bSprite)
            {
                spriteRenderer.sprite = bSprite;
            }
        }
        else if (spriteRenderer.sprite == null)
        {
            Sprite s = GridController.LoadRiverBoatSprite();
            if (s != null) spriteRenderer.sprite = s;
        }

        if (bubbleMat != null)
        {
            bubbleMaterial = bubbleMat;
            s_CachedBubbleMat = bubbleMat;
        }
        if (bubbleTex != null)
        {
            bubbleTexture = bubbleTex;
        }

        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = null;
        }
        activeHarpoons.Clear();

        transform.localScale = Vector3.one * boatScale;
        float boatHalfWidth = GetBoatHalfWidth();
        float spawnMargin = boatHalfWidth + 3.0f;

        bool startFromLeft = overrideStartFromLeft.HasValue 
            ? overrideStartFromLeft.Value 
            : (Random.value > 0.5f);

        float targetCenterY = GetTargetCenterY();
        float startX = startFromLeft ? (worldBgLeft - spawnMargin) : (worldBgRight + spawnMargin);
        transform.position = new Vector3(startX, targetCenterY, 0);

        travelDirection = startFromLeft ? 1f : -1f;
        facingRight = (travelDirection > 0f);
        spriteRenderer.flipX = facingRight;
        transform.rotation = Quaternion.identity;

        targetX = destinationX;
        currentState = BoatState.Arriving;

        SetupWakeParticles();
        if (wakeParticleSystem != null && !wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Play();
        }

        SetupAudio();
        if (engineAudioSource != null && AudioSettingsManager.IsSfxEnabled)
        {
            engineAudioSource.Play();
        }

        gameObject.SetActive(true);
    }

    private void UpdatePropellerPosition()
    {
        if (wakeParticleSystem == null)
        {
            Transform existingChild = transform.Find("MotorWakeBubbles");
            if (existingChild != null)
            {
                wakeParticleSystem = existingChild.GetComponent<ParticleSystem>();
            }
        }
        if (wakeParticleSystem == null) return;

        // In local space of boat: unscaled offsets
        float localPropellerX = facingRight ? -propellerOffset.x : propellerOffset.x;
        float localPropellerY = propellerOffset.y;
        wakeParticleSystem.transform.localPosition = new Vector3(localPropellerX, localPropellerY, 0f);

        float rotationY = facingRight ? -90f : 90f;
        wakeParticleSystem.transform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    public Vector3 GetHarpoonLauncherPosition()
    {
        float offsetX = (facingRight ? 2.5f : -2.5f) * boatScale;
        float offsetY = 0.3f * boatScale; // Raised so cable anchor is above the waterline and not clipped
        return transform.position + new Vector3(offsetX, offsetY, 0f);
    }

    public void SetupWakeParticlesTemplate(Material bMat, Texture2D bTex)
    {
        if (bMat != null)
        {
            bubbleMaterial = bMat;
            s_CachedBubbleMat = bMat;
        }
        if (bTex != null) bubbleTexture = bTex;
        SetupWakeParticles();
    }

    private void SetupWakeParticles()
    {
        if (wakeParticleSystem == null)
        {
            Transform existingChild = transform.Find("MotorWakeBubbles");
            if (existingChild != null)
            {
                wakeParticleSystem = existingChild.GetComponent<ParticleSystem>();
            }
        }

        GameObject pObj;
        if (wakeParticleSystem == null)
        {
            pObj = new GameObject("MotorWakeBubbles");
            pObj.transform.SetParent(transform, false);
            wakeParticleSystem = pObj.AddComponent<ParticleSystem>();
        }
        else
        {
            pObj = wakeParticleSystem.gameObject;
        }

        var main = wakeParticleSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = new ParticleSystem.MinMaxCurve(bubbleSpeedRange.x, bubbleSpeedRange.y);
        main.startLifetime = new ParticleSystem.MinMaxCurve(bubbleLifetimeRange.x, bubbleLifetimeRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(bubbleSizeRange.x, bubbleSizeRange.y);
        main.gravityModifier = -0.1f;
        main.maxParticles = bubbleMaxParticles;

        var emission = wakeParticleSystem.emission;
        emission.rateOverTime = bubbleEmissionRate;

        var shape = wakeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.32f;

        var noise = wakeParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.5f;

        var sol = wakeParticleSystem.sizeOverLifetime;
        sol.enabled = true;
        if (s_SizeCurve == null)
        {
            s_SizeCurve = new AnimationCurve();
            s_SizeCurve.AddKey(0.0f, 0.5f);
            s_SizeCurve.AddKey(0.8f, 1.0f);
            s_SizeCurve.AddKey(1.0f, 0.0f);
        }
        sol.size = new ParticleSystem.MinMaxCurve(1f, s_SizeCurve);

        var col = wakeParticleSystem.colorOverLifetime;
        col.enabled = true;
        if (s_ColorGradient == null)
        {
            s_ColorGradient = new Gradient();
        }
        s_ColorGradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0.0f), new GradientAlphaKey(0.65f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = s_ColorGradient;

        var renderer = pObj.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            Material matToUse = bubbleMaterial != null ? bubbleMaterial : s_CachedBubbleMat;

            if (matToUse == null && bubbleTexture != null)
            {
                if (s_CachedBubbleShader == null)
                {
                    s_CachedBubbleShader = Shader.Find("Particles/Standard Unlit");
                    if (s_CachedBubbleShader == null) s_CachedBubbleShader = Shader.Find("Mobile/Particles/Alpha Blended");
                    if (s_CachedBubbleShader == null) s_CachedBubbleShader = Shader.Find("Sprites/Default");
                }
                if (s_CachedBubbleShader != null)
                {
                    s_CachedBubbleMat = new Material(s_CachedBubbleShader);
                    s_CachedBubbleMat.mainTexture = bubbleTexture;
                    matToUse = s_CachedBubbleMat;
                }
            }

            #if UNITY_EDITOR
            if (matToUse == null)
            {
                matToUse = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
                if (matToUse != null) s_CachedBubbleMat = matToUse;
            }
            #endif

            if (matToUse != null)
            {
                s_CachedBubbleMat = matToUse;
                renderer.sharedMaterial = matToUse;
            }
            renderer.sortingOrder = 4; // Wake particles above boat hull (3) but below player fish (5)
        }

        UpdatePropellerPosition();
    }

    private void Update()
    {
        UpdateAudio();
        UpdateLaserSight();

        if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

        float bobOffset = Mathf.Sin((Time.time + seed) * bobFrequency) * bobAmplitude;
        float tiltAngle = Mathf.Sin((Time.time + seed) * (bobFrequency * 0.7f)) * tiltAmplitude;

        switch (currentState)
        {
            case BoatState.Arriving:
                UpdateArriving(bobOffset, tiltAngle);
                break;

            case BoatState.StoppedWaiting:
            case BoatState.Fishing:
            case BoatState.DepartWaiting:
                ApplyWaveBobbing(bobOffset, tiltAngle);
                break;

            case BoatState.Departing:
                UpdateDeparting(bobOffset, tiltAngle);
                break;
        }
    }

    private void UpdateArriving(float bobOffset, float tiltAngle)
    {
        float currentX = transform.position.x;
        float newX = Mathf.MoveTowards(currentX, targetX, arriveSpeed * Time.deltaTime);
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(newX, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));

        if (Mathf.Abs(newX - targetX) < 0.08f)
        {
            transform.position = new Vector3(targetX, targetCenterY + bobOffset, 0);
            if (wakeParticleSystem != null && wakeParticleSystem.isPlaying)
            {
                wakeParticleSystem.Stop();
            }

            currentState = BoatState.StoppedWaiting;
            if (lifecycleCoroutine != null) StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = StartCoroutine(FishingLifecycleRoutine());
        }
    }

    private IEnumerator FishingLifecycleRoutine()
    {
        currentState = BoatState.StoppedWaiting;

        // Ensure boat stays for at least 2 rounds of hunting before departing
        int totalTries = Mathf.Max(2, huntingRounds);

        for (int round = 0; round < totalTries; round++)
        {
            if (GameManager.instance != null && GameManager.instance.IsGameOver) break;

            // USER REQUIREMENT: Target 2 fish at a time (Player fish + AI fish Level >= 2)
            StartLaserSight();

            float elapsed = 0f;
            while (elapsed < laserTrackDuration)
            {
                if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
                {
                    elapsed += Time.deltaTime;
                    laserTrackElapsed = elapsed;
                }
                yield return null;
            }

            // Intelligent predictive lead aiming calculation:
            // Predict where the player will be when the harpoon strikes!
            Vector2 playerVel = GetPlayerVelocity();
            float harpoonSpeed = 38f;
            Vector3 predictedPlayerPos = PredictTargetPosition(currentLaserTarget, playerVel, harpoonSpeed, postLockReactionDelay);
            lastLockedAngle = CalculateTiltToTarget(predictedPlayerPos);

            // Lock AI fish's predicted location if acquired
            bool hasAiShot = hasValidAITarget && currentAITarget != null;
            if (hasAiShot)
            {
                Vector2 aiVel = GetFishVelocity(currentAITarget);
                Vector3 predictedAiPos = PredictTargetPosition(currentAILaserTarget, aiVel, harpoonSpeed, postLockReactionDelay + 0.16f);
                lastLockedAngleAI = CalculateTiltToTarget(predictedAiPos);
            }

            // Remove lasers from both fish before firing
            StopLaserSight();

            // Brief reaction window: gives player and fish an intuitive window to realize laser is gone and swim away!
            yield return StartCoroutine(PauseRoutine(postLockReactionDelay));

            // Shoot harpoon(s) at predicted location(s)
            yield return StartCoroutine(HarpoonLifecycleSequence(lastLockedAngle, hasAiShot, lastLockedAngleAI));

            // If game is over (player caught/died), stop hunting further rounds
            if (GameManager.instance != null && GameManager.instance.IsGameOver) break;

            // If there is another round, stay in place and pause before tracking again
            if (round < totalTries - 1)
            {
                currentState = BoatState.StoppedWaiting;
                yield return StartCoroutine(PauseRoutine(betweenRoundsPause));
            }
        }

        currentState = BoatState.DepartWaiting;
        float departWait = 0f;
        while (departWait < 1.0f)
        {
            if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
            {
                departWait += Time.deltaTime;
            }
            yield return null;
        }

        StartDeparture();
    }

    private IEnumerator HarpoonLifecycleSequence(float playerAngle, bool hasAiShot, float aiAngle)
    {
        currentState = BoatState.Fishing;
        activeHarpoons.Clear();

        if (hasAiShot)
        {
            // 2 lasers shown -> Shoot exact 2 harpoons (1 at player, 1 at Level 2+ AI fish)
            FireHarpoon(playerAngle);
            yield return StartCoroutine(PauseRoutine(0.16f));
            FireHarpoon(aiAngle);
            yield return StartCoroutine(WaitForAllHarpoons());
        }
        else
        {
            // 1 laser shown -> Shoot exact 1 harpoon at the player
            FireHarpoon(playerAngle);
            yield return StartCoroutine(WaitForAllHarpoons());
        }
    }

    // ── Laser Sight Targeting Implementation (2 Targets: Player + AI Fish Level >= 2) ──

    private void SetupLaserSight()
    {
        laserNoiseSeed = Random.Range(0f, 1000f);

        // 1. Player Laser Sight
        if (laserLineRenderer == null)
        {
            Transform existing = transform.Find("HarpoonLaserSight");
            GameObject laserObj;
            if (existing != null)
            {
                laserObj = existing.gameObject;
            }
            else
            {
                laserObj = new GameObject("HarpoonLaserSight");
                laserObj.transform.SetParent(transform);
            }

            // Note: must use explicit == null — Unity's fake-null breaks C# ?? operator
            laserLineRenderer = laserObj.GetComponent<LineRenderer>();
            if (laserLineRenderer == null)
                laserLineRenderer = laserObj.AddComponent<LineRenderer>();

            laserLineRenderer.useWorldSpace     = true;
            laserLineRenderer.positionCount     = Mathf.Max(2, laserSegments);
            laserLineRenderer.startWidth        = 0.045f;
            laserLineRenderer.endWidth          = 0.045f;
            laserLineRenderer.numCapVertices    = 4;
            laserLineRenderer.numCornerVertices = 4;
            laserLineRenderer.textureMode       = LineTextureMode.Tile; // keeps noise scale constant
            laserLineRenderer.sortingOrder      = 7;
            laserLineRenderer.material          = GetUnderwaterLaserMaterial();
            laserLineRenderer.enabled           = false;
        }

        // 2. AI Fish Laser Sight (Level >= 2)
        if (laserLineRendererAI == null)
        {
            Transform existingAI = transform.Find("HarpoonLaserSight_AI");
            GameObject laserAIObj;
            if (existingAI != null)
            {
                laserAIObj = existingAI.gameObject;
            }
            else
            {
                laserAIObj = new GameObject("HarpoonLaserSight_AI");
                laserAIObj.transform.SetParent(transform);
            }

            laserLineRendererAI = laserAIObj.GetComponent<LineRenderer>();
            if (laserLineRendererAI == null)
                laserLineRendererAI = laserAIObj.AddComponent<LineRenderer>();

            laserLineRendererAI.useWorldSpace     = true;
            laserLineRendererAI.positionCount     = Mathf.Max(2, laserSegments);
            laserLineRendererAI.startWidth        = 0.045f;
            laserLineRendererAI.endWidth          = 0.045f;
            laserLineRendererAI.numCapVertices    = 4;
            laserLineRendererAI.numCornerVertices = 4;
            laserLineRendererAI.textureMode       = LineTextureMode.Tile;
            laserLineRendererAI.sortingOrder      = 7;
            laserLineRendererAI.material          = GetUnderwaterLaserMaterial();
            laserLineRendererAI.enabled           = false;
        }

        // 3. Small glow dot tips at the target endpoint
        if (laserTipPlayer == null)
        {
            Transform existingTip = transform.Find("LaserTip_Player");
            GameObject tipObj;
            if (existingTip != null)
            {
                tipObj = existingTip.gameObject;
            }
            else
            {
                tipObj = new GameObject("LaserTip_Player");
                tipObj.transform.SetParent(transform);
            }

            laserTipPlayer = tipObj.GetComponent<SpriteRenderer>();
            if (laserTipPlayer == null)
                laserTipPlayer = tipObj.AddComponent<SpriteRenderer>();
            laserTipPlayer.sprite       = GetLaserDotSprite();
            laserTipPlayer.sortingOrder = 8;
            tipObj.transform.localScale = new Vector3(0.032f, 0.032f, 1f);
            tipObj.SetActive(false);
        }

        if (laserTipAI == null)
        {
            Transform existingTipAI = transform.Find("LaserTip_AI");
            GameObject tipObjAI;
            if (existingTipAI != null)
            {
                tipObjAI = existingTipAI.gameObject;
            }
            else
            {
                tipObjAI = new GameObject("LaserTip_AI");
                tipObjAI.transform.SetParent(transform);
            }

            laserTipAI = tipObjAI.GetComponent<SpriteRenderer>();
            if (laserTipAI == null)
                laserTipAI = tipObjAI.AddComponent<SpriteRenderer>();
            laserTipAI.sprite       = GetLaserDotSprite();
            laserTipAI.sortingOrder = 8;
            tipObjAI.transform.localScale = new Vector3(0.032f, 0.032f, 1f);
            tipObjAI.SetActive(false);
        }

        // 4. Lightweight bubble trail along the beam
        if (bubbleTrail == null)
        {
            bubbleTrail = BuildDefaultBubbleTrail();
        }

        // Clean up old reticle dot from previous builds
        Transform oldDot = transform.Find("LaserReticleDot");
        if (oldDot != null) Destroy(oldDot.gameObject);
    }

    private void StartLaserSight()
    {
        SetupLaserSight();
        isLaserActive    = true;
        laserTrackElapsed = 0f;
        wasLockingPhase  = false;
        laserTargetVelocity = Vector3.zero;
        aiTargetVelocity    = Vector3.zero;

        Transform pt = GetPlayerTransform();
        currentLaserTarget = (pt != null) ? pt.position
            : new Vector3(transform.position.x, worldFloorY, 0f);

        currentAITarget = FindBestAITarget();
        if (currentAITarget != null)
        {
            currentAILaserTarget = currentAITarget.transform.position;
            hasValidAITarget = true;
        }
        else
        {
            hasValidAITarget = false;
        }
    }

    private void StopLaserSight()
    {
        isLaserActive = false;
        if (laserLineRenderer  != null) laserLineRenderer.enabled  = false;
        if (laserLineRendererAI != null) laserLineRendererAI.enabled = false;
        if (laserTipPlayer != null) laserTipPlayer.gameObject.SetActive(false);
        if (laserTipAI     != null) laserTipAI.gameObject.SetActive(false);
        if (bubbleTrail    != null) bubbleTrail.Stop();
    }

    private void UpdateLaserSight()
    {
        if (!isLaserActive || isPaused || (GameManager.instance != null && GameManager.Paused))
        {
            if (laserLineRenderer  != null && laserLineRenderer.enabled)  laserLineRenderer.enabled  = false;
            if (laserLineRendererAI != null && laserLineRendererAI.enabled) laserLineRendererAI.enabled = false;
            if (laserTipPlayer != null && laserTipPlayer.gameObject.activeSelf) laserTipPlayer.gameObject.SetActive(false);
            if (laserTipAI     != null && laserTipAI.gameObject.activeSelf)     laserTipAI.gameObject.SetActive(false);
            return;
        }

        Vector3 launcherPos = GetHarpoonLauncherPosition();

        // ── Charge / lock progression ──────────────────────────────────────
        float chargeProgress = Mathf.Clamp01(laserTrackElapsed / Mathf.Max(laserTrackDuration, 0.1f));
        bool isLockingPhase = (laserTrackElapsed >= (laserTrackDuration - 1.4f));

        // Edge-detect: trigger lock-on ping exactly once when phase flips
        if (isLockingPhase && !wasLockingPhase)
        {
            TriggerLockOnPing(currentLaserTarget);
            wasLockingPhase = true;
        }

        float shimmerPulse = isLockingPhase ? Mathf.Sin(Time.time * 38f) : Mathf.Sin(Time.time * 18f);
        float beamWidth = Mathf.Lerp(0.045f, 0.085f, chargeProgress) + (isLockingPhase ? shimmerPulse * 0.012f : 0f);

        // Color: deep crimson while tracking → hot orange-red pulsing on lock
        Color laserColor = isLockingPhase
            ? new Color(1.0f, 0.16f + 0.15f * Mathf.Abs(shimmerPulse), 0.08f, 1.0f)
            : new Color(1.0f, 0.04f, 0.08f, 0.92f + 0.08f * shimmerPulse);

        float dotScaleVal = Mathf.Lerp(0.028f, 0.040f, chargeProgress) + (isLockingPhase ? shimmerPulse * 0.006f : 0f);
        Vector3 dotScale  = new Vector3(dotScaleVal, dotScaleVal, 1f);

        // Waver shrinks to near-zero as lock-on approaches — beam visibly steadies, great tension cue
        float waver = Mathf.Lerp(waverAmplitude, waverAmplitudeLocked, chargeProgress);

        // ── 1. Player laser ────────────────────────────────────────────────
        Transform pt = GetPlayerTransform();
        if (pt != null)
        {
            PlayerController pc = pt.GetComponent<PlayerController>();
            Vector3 rawTarget = (pc != null && pc.IsAlive && !pc.IsHooked) ? pt.position : currentLaserTarget;
            // SmoothDamp: beam "swims" toward the fish instead of snapping
            currentLaserTarget = Vector3.SmoothDamp(currentLaserTarget, rawTarget, ref laserTargetVelocity, targetFollowLag);
        }

        // Clamp laser target inside map boundaries
        currentLaserTarget.x = Mathf.Clamp(currentLaserTarget.x, worldBgLeft + 0.8f, worldBgRight - 0.8f);
        currentLaserTarget.y = Mathf.Clamp(currentLaserTarget.y, worldFloorY + 0.5f, worldWaterSurfaceY);

        if (laserLineRenderer != null)
        {
            if (!laserLineRenderer.enabled) laserLineRenderer.enabled = true;
            laserLineRenderer.startWidth = beamWidth;
            laserLineRenderer.endWidth   = beamWidth * 0.9f;
            laserLineRenderer.startColor = laserColor;
            laserLineRenderer.endColor   = laserColor;
            DrawWaveredBeam(laserLineRenderer, launcherPos, currentLaserTarget, waver, laserNoiseSeed);
        }

        if (laserTipPlayer != null)
        {
            if (!laserTipPlayer.gameObject.activeSelf) laserTipPlayer.gameObject.SetActive(true);
            laserTipPlayer.transform.position   = currentLaserTarget;
            laserTipPlayer.transform.localScale = dotScale;
            laserTipPlayer.color                = laserColor;
        }

        // Bubble trail along player beam
        EmitBubblesAlongBeam(launcherPos, currentLaserTarget, isLockingPhase ? 2 : 1);

        // ── 2. AI laser (Level >= 2, excludes Golden Fish) ───────────────────────────────────────
        if (currentAITarget == null || !currentAITarget.gameObject.activeInHierarchy || !currentAITarget.enabled ||
            currentAITarget.IsDead || currentAITarget.IsHooked || currentAITarget.Level < 2 || currentAITarget.IsGoldenFish)
        {
            currentAITarget = FindBestAITarget();
        }

        if (currentAITarget != null)
        {
            // Different seed (offset 37) so the two beams waver independently
            currentAILaserTarget = Vector3.SmoothDamp(currentAILaserTarget, currentAITarget.transform.position, ref aiTargetVelocity, targetFollowLag);
            currentAILaserTarget.x = Mathf.Clamp(currentAILaserTarget.x, worldBgLeft + 0.8f, worldBgRight - 0.8f);
            currentAILaserTarget.y = Mathf.Clamp(currentAILaserTarget.y, worldFloorY + 0.5f, worldWaterSurfaceY);
            hasValidAITarget     = true;

            if (laserLineRendererAI != null)
            {
                if (!laserLineRendererAI.enabled) laserLineRendererAI.enabled = true;
                laserLineRendererAI.startWidth = beamWidth;
                laserLineRendererAI.endWidth   = beamWidth * 0.9f;
                laserLineRendererAI.startColor = laserColor;
                laserLineRendererAI.endColor   = laserColor;
                DrawWaveredBeam(laserLineRendererAI, launcherPos, currentAILaserTarget, waver, laserNoiseSeed + 37f);
            }

            if (laserTipAI != null)
            {
                if (!laserTipAI.gameObject.activeSelf) laserTipAI.gameObject.SetActive(true);
                laserTipAI.transform.position   = currentAILaserTarget;
                laserTipAI.transform.localScale = dotScale;
                laserTipAI.color                = laserColor;
            }
        }
        else
        {
            hasValidAITarget = false;
            if (laserLineRendererAI != null && laserLineRendererAI.enabled) laserLineRendererAI.enabled = false;
            if (laserTipAI != null && laserTipAI.gameObject.activeSelf) laserTipAI.gameObject.SetActive(false);
        }
    }

    // ── Underwater beam helpers ───────────────────────────────────────────

    // Bends a straight launcher→target line into a subtly wavering underwater beam.
    // Both endpoints stay pinned exactly; only the mid-section "swims" via Perlin noise.
    private void DrawWaveredBeam(LineRenderer lr, Vector3 from, Vector3 to, float amplitude, float seed)
    {
        int count = lr.positionCount;
        if (count < 2) return;

        Vector3 dir    = to - from;
        float   length = dir.magnitude;

        if (length < 0.001f)
        {
            for (int i = 0; i < count; i++) lr.SetPosition(i, from);
            return;
        }

        Vector3 forward      = dir / length;
        Vector3 perpendicular = new Vector3(-forward.y, forward.x, 0f); // 2-D perpendicular

        for (int i = 0; i < count; i++)
        {
            float   tNorm   = i / (float)(count - 1);
            Vector3 basePos = Vector3.Lerp(from, to, tNorm);

            // Sin envelope: both endpoints are pinned (sin(0) = sin(π) = 0)
            float endFade = Mathf.Sin(tNorm * Mathf.PI);
            float n       = Mathf.PerlinNoise(seed, tNorm * 3f + Time.time * waverNoiseSpeed) * 2f - 1f;

            lr.SetPosition(i, basePos + perpendicular * (n * amplitude * endFade));
        }
    }

    // Sprinkle a handful of bubbles along the beam at ~16/sec
    private void EmitBubblesAlongBeam(Vector3 from, Vector3 to, int countThisFrame)
    {
        if (bubbleTrail == null) return;
        bubbleTimer -= Time.deltaTime;
        if (bubbleTimer > 0f) return;
        bubbleTimer = 0.06f; // ~16 bursts per second

        for (int i = 0; i < countThisFrame; i++)
        {
            float   t   = Random.value;
            Vector3 pos = Vector3.Lerp(from, to, t) + (Vector3)(Random.insideUnitCircle * 0.03f);
            var emitParams = new ParticleSystem.EmitParams
            {
                position  = pos,
                velocity  = new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.05f, 0.15f), 0f),
                startSize = Random.Range(0.015f, 0.03f)
            };
            bubbleTrail.Emit(emitParams, 1);
        }
    }

    // Builds a minimal world-space particle system for the bubble trail
    private ParticleSystem BuildDefaultBubbleTrail()
    {
        var go = new GameObject("BubbleTrail");
        go.transform.SetParent(transform);

        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.gravityModifier = -0.12f;        // bubbles float upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 64;

        var emission = ps.emission;
        emission.rateOverTime = 0f;           // driven manually via Emit()

        var shape = ps.shape;
        shape.enabled = false;

        // Soft blue-white fade-out
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.75f, 0.92f, 1f), 0f), new GradientColorKey(new Color(0.75f, 0.92f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.sortingOrder = 6; // just under the beam (beam=7, tip dot=8)

        return ps;
    }

    // Expanding ring flash at the moment lock-on begins
    private void TriggerLockOnPing(Vector3 worldPos)
    {
        StartCoroutine(LockOnPingRoutine(worldPos));
    }

    private IEnumerator LockOnPingRoutine(Vector3 worldPos)
    {
        var go = new GameObject("LockOnPing");
        go.transform.position = worldPos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = GetLaserDotSprite();
        sr.color        = new Color(1f, 0.30f, 0.10f, 0.80f);
        sr.sortingOrder = 9;

        float duration = 0.28f;
        float elapsed  = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            go.transform.localScale = Vector3.Lerp(new Vector3(0.04f, 0.04f, 1f), new Vector3(0.28f, 0.28f, 1f), t);
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, Mathf.Lerp(0.80f, 0f, t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(go);
    }

    private Fish FindBestAITarget()
    {
        if (Fish.AllFish == null || Fish.AllFish.Count == 0) return null;

        Fish bestFish = null;
        float closestDistSqr = float.MaxValue;
        Vector3 launcherPos = GetHarpoonLauncherPosition();

        for (int i = 0; i < Fish.AllFish.Count; i++)
        {
            Fish f = Fish.AllFish[i];
            if (f == null || !f.gameObject.activeInHierarchy || !f.enabled) continue;
            if (f.IsDead || f.IsHooked) continue;
            // Harpoon only targets large predator/prey fish (Level >= 2). Level 1 fish and Golden Fish are never targeted.
            if (f.Level < 2 || f.IsGoldenFish) continue;

            Vector3 pos = f.transform.position;
            // Bound checks: ensure strictly inside playable map boundaries
            if (pos.x < worldBgLeft + 0.8f || pos.x > worldBgRight - 0.8f) continue;
            if (pos.y < worldFloorY + 0.5f || pos.y > worldWaterSurfaceY - 0.2f) continue;

            float dSqr = (pos - launcherPos).sqrMagnitude;
            if (dSqr < closestDistSqr)
            {
                closestDistSqr = dSqr;
                bestFish = f;
            }
        }

        return bestFish;
    }

    private float CalculateTiltToTarget(Vector3 targetPos)
    {
        Vector3 launcherPos = GetHarpoonLauncherPosition();

        // Strictly clamp target position within map boundaries so aiming cannot aim out of bounds
        float clampedX = Mathf.Clamp(targetPos.x, worldBgLeft + 0.8f, worldBgRight - 0.8f);
        float clampedY = Mathf.Clamp(targetPos.y, worldFloorY + 0.5f, worldWaterSurfaceY - 0.5f);

        float dx = clampedX - launcherPos.x;
        float dy = Mathf.Abs(launcherPos.y - clampedY);
        if (dy < 0.5f) dy = Mathf.Abs(launcherPos.y - worldFloorY);
        if (dy < 0.1f) dy = 10f; // safety

        // Precise true aim towards target
        float aimAngle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
        float finalAngle = Mathf.Clamp(aimAngle, -harpoonTiltMax, harpoonTiltMax);

        return finalAngle;
    }

    /// <summary>
    /// Computes a high-precision predictive lead target based on target velocity, harpoon descent velocity, and reaction delay.
    /// </summary>
    private Vector3 PredictTargetPosition(Vector3 currentPos, Vector2 targetVelocity, float descendSpeed, float delay)
    {
        Vector3 launcherPos = GetHarpoonLauncherPosition();
        float dist = Vector3.Distance(launcherPos, currentPos);
        float timeToHit = (descendSpeed > 0.01f ? (dist / descendSpeed) : 0.6f) + delay;

        // Apply intelligent predictive lead (75% lead factor provides sniper precision while giving a fair dodge window)
        Vector3 predicted = currentPos + (Vector3)(targetVelocity * timeToHit * 0.75f);

        // Clamp predicted target strictly inside playable arena boundaries
        predicted.x = Mathf.Clamp(predicted.x, worldBgLeft + 0.8f, worldBgRight - 0.8f);
        predicted.y = Mathf.Clamp(predicted.y, worldFloorY + 0.5f, worldWaterSurfaceY - 0.5f);
        return predicted;
    }

    private Vector2 GetPlayerVelocity()
    {
        Transform pt = GetPlayerTransform();
        if (pt != null)
        {
            PlayerController pc = pt.GetComponent<PlayerController>();
            if (pc != null)
            {
                return pc.Velocity;
            }
        }
        return Vector2.zero;
    }

    private Vector2 GetFishVelocity(Fish fish)
    {
        if (fish != null)
        {
            Rigidbody2D rb = fish.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                return rb.linearVelocity;
            }
        }
        return Vector2.zero;
    }

    private float AimedTilt()
    {
        return lastLockedAngle;
    }

    // Returns one shared material using the custom UnderwaterLaserBeam shader.
    // Falls back to Sprites/Default if the shader hasn't compiled yet so we never throw.
    private static Material GetUnderwaterLaserMaterial()
    {
        if (s_LaserMaterial != null) return s_LaserMaterial;

        if (s_UnderwaterShader == null)
            s_UnderwaterShader = Shader.Find("Custom/UnderwaterLaserBeam");

        Shader chosenShader = s_UnderwaterShader
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color");

        if (chosenShader == null) return null;

        s_LaserMaterial = new Material(chosenShader);
        s_LaserMaterial.SetTexture("_NoiseTex", GetNoiseTexture());
        return s_LaserMaterial;
    }

    // Procedural 64×64 tileable noise texture — two octaves of Perlin noise for the refraction look.
    // Swap for a hand-painted caustic texture later if you want a less procedural feel.
    private static Texture2D GetNoiseTexture()
    {
        if (s_NoiseTex != null) return s_NoiseTex;
        const int size = 64;
        s_NoiseTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode    = TextureWrapMode.Repeat,
            filterMode  = FilterMode.Bilinear
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.6f
                        + Mathf.PerlinNoise(x * 0.40f + 50f, y * 0.40f + 50f) * 0.4f;
                s_NoiseTex.SetPixel(x, y, new Color(n, n, n, 1f));
            }
        }
        s_NoiseTex.Apply();
        return s_NoiseTex;
    }

    private static Sprite GetLaserDotSprite()
    {
        if (s_LaserDotSprite != null) return s_LaserDotSprite;
        s_LaserDotSprite = Resources.Load<Sprite>("circle512");
#if UNITY_EDITOR
        if (s_LaserDotSprite == null)
        {
            s_LaserDotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/circle512.png");
        }
#endif
        return s_LaserDotSprite;
    }



    /// Fires one harpoon and yields until it fully retracts.
    private IEnumerator FireAndWait(float tiltAngle)
    {
        FireHarpoon(tiltAngle);
        yield return StartCoroutine(WaitForAllHarpoons());
    }

    /// Spawns a single harpoon at the given tilt angle.
    private void FireHarpoon(float tiltAngle)
    {
        Vector3 launchPos = GetHarpoonLauncherPosition();
        float lakeFloorY = worldFloorY + 0.5f;

        if (GridController.Instance != null)
        {
            HarpoonHazard harpoon = GridController.Instance.SpawnHarpoonForBoat(
                this, launchPos, tiltAngle, lakeFloorY);
            if (harpoon != null)
            {
                activeHarpoons.Add(harpoon);
            }
        }
    }

    /// Returns a randomised tilt angle (left or right of straight-down).
    private float RandomTilt()
    {
        float sign = (Random.value > 0.5f) ? 1f : -1f;
        return sign * Random.Range(harpoonTiltMin, harpoonTiltMax);
    }

    /// Yields until all active harpoons have retracted/despawned.
    private IEnumerator WaitForAllHarpoons()
    {
        while (activeHarpoons.Count > 0)
        {
            activeHarpoons.RemoveAll(h => h == null || !h.gameObject.activeInHierarchy);
            yield return null;
        }
    }

    /// Pause helper that respects game-pause state.
    private IEnumerator PauseRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
                elapsed += Time.deltaTime;
            yield return null;
        }
    }


    public void OnHarpoonRetracted(HarpoonHazard harpoon = null)
    {
        if (harpoon != null)
        {
            activeHarpoons.Remove(harpoon);
        }
        else if (activeHarpoons.Count > 0)
        {
            activeHarpoons.RemoveAt(0);
        }
    }


    private void StartDeparture()
    {
        if (currentState == BoatState.Departing) return;
        currentState = BoatState.Departing;

        if (wakeParticleSystem != null && !wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Play();
        }
    }

    private void UpdateDeparting(float bobOffset, float tiltAngle)
    {
        float newX = transform.position.x + (travelDirection * departSpeed * Time.deltaTime);
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(newX, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));

        float boatHalfWidth = GetBoatHalfWidth();
        float despawnMargin = boatHalfWidth + 3.0f;

        bool isFullyOffBg = false;
        if (travelDirection > 0f)
        {
            float boatLeft = (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.1f)
                ? spriteRenderer.bounds.min.x
                : (transform.position.x - boatHalfWidth);

            if (boatLeft > worldBgRight || transform.position.x > worldBgRight + despawnMargin)
            {
                isFullyOffBg = true;
            }
        }
        else
        {
            float boatRight = (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.1f)
                ? spriteRenderer.bounds.max.x
                : (transform.position.x + boatHalfWidth);

            if (boatRight < worldBgLeft || transform.position.x < worldBgLeft - despawnMargin)
            {
                isFullyOffBg = true;
            }
        }

        if (isFullyOffBg)
        {
            if (GridController.Instance != null)
            {
                GridController.Instance.OnRiverBoatDeparted(this);
            }
            if (wakeParticleSystem != null)
            {
                wakeParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (ObjectPoolManager.Instance != null)
            {
                ObjectPoolManager.Instance.Despawn(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    private void ApplyWaveBobbing(float bobOffset, float tiltAngle)
    {
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(transform.position.x, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));
    }

    private Transform GetPlayerTransform()
    {
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
            return playerTransform;

        if (GridController.Instance != null && GridController.Instance.Player != null)
        {
            playerTransform = GridController.Instance.Player.transform;
            s_GlobalPlayerTransform = playerTransform;
            return playerTransform;
        }

        if (s_GlobalPlayerTransform != null && s_GlobalPlayerTransform.gameObject.activeInHierarchy)
        {
            playerTransform = s_GlobalPlayerTransform;
            return playerTransform;
        }

        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            s_GlobalPlayerTransform = pc.transform;
            playerTransform = s_GlobalPlayerTransform;
            return playerTransform;
        }

        return null;
    }

    private void UpdateAudio()
    {
        if (engineAudioSource == null) return;

        bool sfxEnabled = AudioSettingsManager.IsSfxEnabled;
        bool isGameOver = (GameManager.instance != null && GameManager.instance.IsGameOver);
        if (!sfxEnabled || isPaused || (GameManager.instance != null && GameManager.Paused) || isGameOver)
        {
            if (engineAudioSource.isPlaying)
            {
                if (isGameOver) engineAudioSource.Stop();
                else engineAudioSource.Pause();
            }
            targetAudioVolume = 0f;
            currentAudioVolume = 0f;
            engineAudioSource.volume = 0f;
            return;
        }

        if (engineAudioSource.clip == null)
        {
            AudioClip clipToUse = engineDriveClip != null ? engineDriveClip : s_CachedEngineClip;
            if (clipToUse != null)
            {
                engineAudioSource.clip = clipToUse;
                engineAudioSource.loop = true;
            }
            else
            {
                return;
            }
        }

        bool isDriving = (currentState == BoatState.Arriving || currentState == BoatState.Departing);

        if (!isDriving)
        {
            targetAudioVolume = 0f;
        }
        else
        {
            Transform pt = GetPlayerTransform();
            if (pt != null)
            {
                float dist = Vector2.Distance(transform.position, pt.position);

                if (dist <= minEngineDistance)
                {
                    targetAudioVolume = maxEngineVolume;
                }
                else if (dist >= maxEngineDistance)
                {
                    targetAudioVolume = 0f;
                }
                else
                {
                    float t = (dist - minEngineDistance) / (maxEngineDistance - minEngineDistance);
                    targetAudioVolume = (1f - t) * (1f - t) * maxEngineVolume;
                }

                float pan = Mathf.Clamp((transform.position.x - pt.position.x) / 14.0f, -0.85f, 0.85f);
                engineAudioSource.panStereo = pan;
            }
            else
            {
                targetAudioVolume = maxEngineVolume * 0.5f;
            }
        }

        float targetPitch = 1.0f;
        if (currentState == BoatState.Departing) targetPitch = 1.08f;
        else if (currentState == BoatState.Arriving) targetPitch = 1.0f;
        else targetPitch = 0.92f;
        engineAudioSource.pitch = Mathf.MoveTowards(engineAudioSource.pitch, targetPitch, 1.5f * Time.deltaTime);

        currentAudioVolume = Mathf.MoveTowards(currentAudioVolume, targetAudioVolume, 4.5f * Time.deltaTime);
        engineAudioSource.volume = currentAudioVolume;

        if (currentAudioVolume > 0.001f)
        {
            if (!engineAudioSource.isPlaying)
            {
                engineAudioSource.UnPause();
                if (!engineAudioSource.isPlaying) engineAudioSource.Play();
            }
        }
        else
        {
            if (engineAudioSource.isPlaying && !isDriving)
            {
                engineAudioSource.Stop();
            }
        }
    }
}

