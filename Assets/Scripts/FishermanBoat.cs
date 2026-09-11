using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Rhinotap.Toolkit;

public class FishermanBoat : MonoBehaviour
{
    public enum BoatState { Arriving, StoppedWaiting, Fishing, DepartWaiting, Departing }

    [Header("Boat Appearance")]
    [SerializeField] private float boatScale = 0.80f;
    [Tooltip("How deep the lower hull dips below the top camera edge into the water")]
    [SerializeField] private float submergenceDepth = 1.85f;
    [SerializeField] private float bobFrequency = 2.2f;
    [SerializeField] private float bobAmplitude = 0.04f;
    [SerializeField] private float tiltAmplitude = 1.2f;

    [Header("Bubble Particle Materials")]
    [SerializeField] private Material bubbleMaterial;
    [SerializeField] private Texture2D bubbleTexture;

    [Header("Bubble Particle Settings")]
    [SerializeField] private float bubbleEmissionRate = 42f;
    [SerializeField] private Vector2 bubbleSizeRange = new Vector2(0.20f, 0.55f);
    [SerializeField] private Vector2 bubbleSpeedRange = new Vector2(1.5f, 3.5f);
    [SerializeField] private Vector2 bubbleLifetimeRange = new Vector2(1.0f, 1.8f);
    [SerializeField] private int bubbleMaxParticles = 120;

    [Header("Movement Settings")]
    [SerializeField] private float arriveSpeed = 4.5f;
    [SerializeField] private float departSpeed = 5.5f;
    [SerializeField] private float stopPauseDurationMin = 2.5f;
    [SerializeField] private float stopPauseDurationMax = 3.0f;

    [Header("Fishing Rod Settings")]
    [SerializeField] private int minRods = 1;
    [SerializeField] private int maxRods = 3;

    [Header("Fishing Rod Depth Tiers (World Y)")]
    [Tooltip("Deep hook target range (near ocean floor, e.g. -12.5 to -5.5)")]
    [SerializeField] private Vector2 deepDepthRange = new Vector2(-12.5f, -5.5f);
    [Tooltip("Mid hook target range (mid ocean waters, e.g. -5.0 to 1.0)")]
    [SerializeField] private Vector2 midDepthRange = new Vector2(-5.0f, 1.0f);
    [Tooltip("Shallow hook target range (upper ocean waters, calibrated to descend deeper: 1.0 to 7.5)")]
    [SerializeField] private Vector2 shallowDepthRange = new Vector2(1.0f, 7.5f);

    private BoatState currentState = BoatState.Arriving;
    private SpriteRenderer spriteRenderer;
    private ParticleSystem wakeParticleSystem;
    private float targetX;
    private bool facingRight = true;
    private float travelDirection = 1f; // +1 right, -1 left
    private float seed;
    private bool isPaused = false;
    private float worldWaterSurfaceY = 15.0f;
    private List<Hazard> activeRods = new List<Hazard>();
    private Coroutine lifecycleCoroutine;

    [Header("Engine Audio Settings")]
    [SerializeField] private AudioClip engineDriveClip;
    [SerializeField] private float maxEngineVolume = 0.85f;
    [SerializeField] private float minEngineDistance = 5.0f;
    [SerializeField] private float maxEngineDistance = 24.0f;

    private AudioSource engineAudioSource;
    private static AudioClip s_CachedEngineClip = null;
    private Transform playerTransform = null;
    private static Transform s_GlobalPlayerTransform = null;
    private float currentAudioVolume = 0f;
    private float targetAudioVolume = 0f;

    // Static cache shared across all boat instances to prevent frame drops
    private static bool s_BoundsCached = false;
    private static float s_WorldBgLeft = -25f;
    private static float s_WorldBgRight = 25f;
    private static float s_WorldSurfaceY = 15f;
    private static Material s_CachedBubbleMat = null;
    private static Shader s_CachedBubbleShader = null;
    private static AnimationCurve s_SizeCurve = null;
    private static Gradient s_ColorGradient = null;

    /// <summary>
    /// Fixed world background left edge (independent of camera position).
    /// Read from the WorldBounds PolygonCollider2D at startup, fallback to -25.
    /// </summary>
    private float worldBgLeft = -25f;
    /// <summary>
    /// Fixed world background right edge (independent of camera position).
    /// Read from the WorldBounds PolygonCollider2D at startup, fallback to +25.
    /// </summary>
    private float worldBgRight = 25f;

    public BoatState State => currentState;
    public bool IsReadyToFish => currentState == BoatState.Fishing;
    public float WaterSurfaceY => worldWaterSurfaceY;

    public static void SetGlobalWorldBounds(float bgLeft, float bgRight, float surfaceY)
    {
        s_WorldBgLeft = bgLeft;
        s_WorldBgRight = bgRight;
        s_WorldSurfaceY = surfaceY;
        s_BoundsCached = true;
    }

    public static void SetGlobalBubbleMaterial(Material mat)
    {
        if (mat != null) s_CachedBubbleMat = mat;
    }

    public static void SetGlobalEngineClip(AudioClip clip)
    {
        if (clip != null) s_CachedEngineClip = clip;
    }

    public static void SetGlobalPlayer(Transform pt)
    {
        if (pt != null) s_GlobalPlayerTransform = pt;
    }

    /// <summary>
    /// Computes the exact off-screen spawn position for the boat hazard before instantiation/spawning.
    /// This prevents the boat from ever being activated at (0, 0, 0) in front of the player.
    /// </summary>
    public static void CalculateSpawnPlacement(bool cruiseFromOffscreen, float bgLeft, float bgRight, float surfaceY, bool startFromLeft, out Vector3 spawnPos)
    {
        float spawnMargin = 11.5f; // boatHalfWidth (8.15f) + 3.0f margin
        float halfHeight = 2.944f; // 7.36f * 0.80f * 0.5f
        float sY = (surfaceY > 5f) ? surfaceY : 15.0f;
        float targetCenterY = (sY - 1.85f) + halfHeight; // 1.85f submergenceDepth
        
        float startX = startFromLeft ? (bgLeft - spawnMargin) : (bgRight + spawnMargin);
        spawnPos = new Vector3(startX, targetCenterY, 0f);
    }

    /// <summary>
    /// Warm up particle system buffers and shaders during scene load so no hitch occurs on spawn.
    /// </summary>
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
        spriteRenderer.sortingOrder = 5; // Visible above background and fish

        if (spriteRenderer.sprite == null || !spriteRenderer.sprite.name.Contains("Fisherman_Boat"))
        {
            Sprite s = GridController.LoadFishermanBoatSprite();
            if (s != null) spriteRenderer.sprite = s;
        }
        seed = Random.Range(0f, 100f);

        // Discover fixed world boundaries or use pre-cached values
        if (s_BoundsCached)
        {
            worldBgLeft = s_WorldBgLeft;
            worldBgRight = s_WorldBgRight;
            worldWaterSurfaceY = s_WorldSurfaceY;
        }
        else
        {
            DiscoverWorldBounds();
        }

        // Cache child particle system if already created
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
            if (engineAudioSource.clip != clipToUse)
            {
                engineAudioSource.clip = clipToUse;
            }
            engineAudioSource.loop = true;
            engineAudioSource.playOnAwake = false;
            engineAudioSource.spatialBlend = 0.0f; // 2D custom spatial distance attenuation
            engineAudioSource.volume = 0f;
            engineAudioSource.mute = !AudioSettingsManager.IsSfxEnabled;
            if (clipToUse.loadState != AudioDataLoadState.Loaded && clipToUse.loadState != AudioDataLoadState.Loading)
            {
                clipToUse.LoadAudioData();
            }
        }
    }

    /// <summary>
    /// Reads the WorldBounds PolygonCollider2D (used by Cinemachine Confiner) to get:
    /// - left/right edges for spawn/despawn (X = ±25)
    /// - top edge (Y = 15.0f) which is the absolute water surface ceiling of the ocean.
    /// The boat is anchored at this surface ceiling, so it never appears inside the ocean.
    /// </summary>
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
                float maxY = float.MinValue;
                foreach (var p in pts)
                {
                    Vector2 worldPt = (Vector2)wbObj.transform.TransformPoint(p);
                    if (worldPt.x < minX) minX = worldPt.x;
                    if (worldPt.x > maxX) maxX = worldPt.x;
                    if (worldPt.y > maxY) maxY = worldPt.y;
                }
                worldBgLeft = minX;
                worldBgRight = maxX;
                worldWaterSurfaceY = maxY; // 15.0f from WorldBounds
                s_WorldBgLeft = minX;
                s_WorldBgRight = maxX;
                s_WorldSurfaceY = maxY;
                s_BoundsCached = true;
                return;
            }
        }

        // Fallback: scan all PolygonCollider2D for the large confiner polygon
        PolygonCollider2D[] allPolys = FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None);
        foreach (var poly in allPolys)
        {
            if (poly.pathCount > 0)
            {
                Vector2[] pts = poly.GetPath(0);
                float minX = float.MaxValue, maxX = float.MinValue;
                float maxY = float.MinValue;
                foreach (var p in pts)
                {
                    Vector2 wp = (Vector2)poly.transform.TransformPoint(p);
                    if (wp.x < minX) minX = wp.x;
                    if (wp.x > maxX) maxX = wp.x;
                    if (wp.y > maxY) maxY = wp.y;
                }
                float width = maxX - minX;
                if (width > 20f)
                {
                    worldBgLeft = minX;
                    worldBgRight = maxX;
                    worldWaterSurfaceY = maxY;
                    s_WorldBgLeft = minX;
                    s_WorldBgRight = maxX;
                    s_WorldSurfaceY = maxY;
                    s_BoundsCached = true;
                    return;
                }
            }
        }

        // Final fallback: standard world bounds from SampleScene
        worldBgLeft = -25f;
        worldBgRight = 25f;
        worldWaterSurfaceY = 15f;
        s_WorldBgLeft = -25f;
        s_WorldBgRight = 25f;
        s_WorldSurfaceY = 15f;
        s_BoundsCached = true;
    }

    /// <summary>
    /// Computes the exact camera visible bounds in world space dynamically.
    /// </summary>
    public static void GetCameraWorldBounds(out float leftEdge, out float rightEdge, out float topEdge, out float bottomEdge)
    {
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float camY = cam.transform.position.y;
            float camX = cam.transform.position.x;
            float size = cam.orthographicSize;
            float halfWidth = size * cam.aspect;

            topEdge = camY + size;
            bottomEdge = camY - size;
            leftEdge = camX - halfWidth;
            rightEdge = camX + halfWidth;
        }
        else if (cam != null)
        {
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, 10f));
            leftEdge = Mathf.Min(bl.x, tr.x);
            rightEdge = Mathf.Max(bl.x, tr.x);
            bottomEdge = Mathf.Min(bl.y, tr.y);
            topEdge = Mathf.Max(bl.y, tr.y);
        }
        else
        {
            leftEdge = -14.22f;
            rightEdge = 14.22f;
            bottomEdge = -8f;
            topEdge = 8f;
        }

        // Failsafe: Top edge of water screen is always at or near Y = 8.0f
        if (topEdge < 4.0f)
        {
            topEdge = 8.0f;
        }
    }

    private float GetBoatHalfWidth()
    {
        if (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.5f)
        {
            return spriteRenderer.bounds.extents.x;
        }
        return (20.38f * boatScale) * 0.5f;
    }

    private float GetBoatHalfHeight()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return (spriteRenderer.sprite.rect.height / spriteRenderer.sprite.pixelsPerUnit) * boatScale * 0.5f;
        }
        return (7.36f * boatScale) * 0.5f;
    }

    /// <summary>
    /// Returns the boat's world center Y so only the lower keel peeks below the water surface.
    /// Uses the FIXED worldWaterSurfaceY (top edge of bgPixelated background sprite) — 
    /// completely camera-independent. The boat always sits at the same fixed world height.
    /// </summary>
    private float GetTargetCenterY()
    {
        float halfHeight = GetBoatHalfHeight();
        // worldWaterSurfaceY is set ONCE in Awake() from WorldBounds.maxY (top of confiner = 15.0f)
        float surfaceY = (worldWaterSurfaceY > 5f) ? worldWaterSurfaceY : 15.0f;
        // bottom of hull = surfaceY - submergenceDepth
        // center = bottom + halfHeight (because sprite pivot is at center)
        return (surfaceY - submergenceDepth) + halfHeight;
    }

    private void Start()
    {
        EventManager.StartListening<bool>("gamePaused", OnGamePaused);
        AudioSettingsManager.OnSfxSettingChanged += OnSfxSettingChanged;
    }

    private void OnDisable()
    {
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = null;
        }
        if (wakeParticleSystem != null && wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (engineAudioSource != null && engineAudioSource.isPlaying)
        {
            engineAudioSource.Stop();
        }
        currentAudioVolume = 0f;
        targetAudioVolume = 0f;
        activeRods.Clear();
    }

    private void OnDestroy()
    {
        EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        AudioSettingsManager.OnSfxSettingChanged -= OnSfxSettingChanged;
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
        }
        if (engineAudioSource != null && engineAudioSource.isPlaying)
        {
            engineAudioSource.Stop();
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

    /// <summary>
    /// Initialize the boat with sprite, randomized entry side, destination, and scale.
    /// Accepts pre-cached world bounds and materials to completely eliminate spawn stutter.
    /// </summary>
    public void Initialize(Sprite boatSprite, float destinationX, bool cruiseFromOffscreen = true, Material bubbleMat = null, Texture2D bubbleTex = null, float bgLeft = float.NaN, float bgRight = float.NaN, float surfaceY = float.NaN, bool? overrideStartFromLeft = null)
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
        if (boatSprite != null)
        {
            if (spriteRenderer.sprite != boatSprite)
            {
                spriteRenderer.sprite = boatSprite;
            }
        }
        else if (spriteRenderer.sprite == null || !spriteRenderer.sprite.name.Contains("Fisherman_Boat"))
        {
            Sprite s = GridController.LoadFishermanBoatSprite();
            if (s != null)
            {
                spriteRenderer.sprite = s;
            }
        }

        if (bubbleMat != null)
        {
            bubbleMaterial = bubbleMat;
            s_CachedBubbleMat = bubbleMat;
        }
        if (bubbleTex != null) bubbleTexture = bubbleTex;

        if (bubbleMaterial == null && s_CachedBubbleMat != null)
        {
            bubbleMaterial = s_CachedBubbleMat;
        }

        targetX = destinationX;

        // Reset runtime state
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = null;
        }
        activeRods.Clear();

        // Apply realistic scale
        transform.localScale = Vector3.one * boatScale;
        float boatHalfWidth = GetBoatHalfWidth();
        // Spawn margin: boat must be fully off the world background edge before entering
        float spawnMargin = boatHalfWidth + 3.0f;
        float targetCenterY = GetTargetCenterY();

        if (cruiseFromOffscreen)
        {
            // Pick entry side 50/50: Left or Right (or use pre-decided direction)
            bool startFromLeft = overrideStartFromLeft.HasValue ? overrideStartFromLeft.Value : (Random.value > 0.5f);
            facingRight = startFromLeft; // traveling right -> facing right
            travelDirection = startFromLeft ? 1f : -1f;

            // CRITICAL: Use fixed WORLD background edges, NOT camera view edges.
            float startX = startFromLeft
                ? (worldBgLeft - spawnMargin)   // enter from LEFT background edge
                : (worldBgRight + spawnMargin);  // enter from RIGHT background edge

            ApplyFacing();
            transform.position = new Vector3(startX, targetCenterY, 0);
            currentState = BoatState.Arriving;
        }
        else
        {
            facingRight = (Random.value > 0.5f);
            travelDirection = facingRight ? 1f : -1f;
            ApplyFacing();
            transform.position = new Vector3(destinationX, targetCenterY, 0);
            currentState = BoatState.StoppedWaiting;
        }

        SetupWakeParticles();
        if (wakeParticleSystem != null)
        {
            if (currentState == BoatState.Arriving)
            {
                wakeParticleSystem.Play();
            }
            else
            {
                wakeParticleSystem.Stop();
            }
        }

        // Initialize engine audio playback
        SetupAudio();
        currentAudioVolume = 0f;
        targetAudioVolume = 0f;
        if (engineAudioSource != null)
        {
            engineAudioSource.volume = 0f;
            if (currentState == BoatState.Arriving && AudioSettingsManager.IsSfxEnabled)
            {
                if (!engineAudioSource.isPlaying) engineAudioSource.Play();
            }
            else
            {
                if (engineAudioSource.isPlaying) engineAudioSource.Stop();
            }
        }

        ApplyWaveBobbing(0f, 0f);
    }

    private void ApplyFacing()
    {
        // Fisherman_Boat.png default: pointed bow on LEFT, motor/propeller on RIGHT.
        // When traveling right -> facingRight = true -> flipX = true (bow points right).
        // When traveling left  -> facingRight = false -> flipX = false (bow points left).
        if (spriteRenderer != null && spriteRenderer.flipX != facingRight)
        {
            spriteRenderer.flipX = facingRight;
        }
        UpdatePropellerPosition();
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

        // Fisherman_Boat.png dimensions: 2038x736, PPU 100.
        // Propeller is located at bottom RIGHT (dx = +7.30 units, dy = -2.80 units from center).
        // When facingRight = true (flipX = true): boat points right, propeller is on the LEFT (-7.30 units).
        // When facingRight = false (flipX = false): boat points left, propeller is on the RIGHT (+7.30 units).
        float localPropellerX = facingRight ? -7.30f : 7.30f;
        float localPropellerY = -2.80f;
        wakeParticleSystem.transform.localPosition = new Vector3(localPropellerX, localPropellerY, 0f);

        // Wake shoots backward (away from travel direction):
        // Rotating the transform is virtually free and avoids rebuild of the ParticleSystem ShapeModule
        float rotationY = facingRight ? -90f : 90f;
        wakeParticleSystem.transform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
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
        main.gravityModifier = -0.1f; // Float up to water surface
        main.maxParticles = bubbleMaxParticles;

        var emission = wakeParticleSystem.emission;
        emission.rateOverTime = bubbleEmissionRate;

        var shape = wakeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.32f;

        // Noise (Wiggle match to player speedEffect)
        var noise = wakeParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.5f;

        // Size over Lifetime: Grow then pop (cached static curve to eliminate GC/allocations)
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

        // Color/Alpha: Fade out (cached static gradient to eliminate GC/allocations)
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
            renderer.sortingOrder = 6;
        }

        UpdatePropellerPosition();
    }

    private void Update()
    {
        // Update audio attenuation & distance checks dynamically every frame
        UpdateAudio();

        if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

        // Wave bobbing calculation in world space
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
                // Boat stays at its stop position, gently bobbing on the waves
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

        // Requirement 4: Arrive and stop for at least 2.5 - 3.0 seconds before dropping fishing rods
        float pauseDuration = Random.Range(stopPauseDurationMin, stopPauseDurationMax);
        float elapsed = 0f;
        while (elapsed < pauseDuration)
        {
            if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
            {
                elapsed += Time.deltaTime;
            }
            yield return null;
        }

        // User Request: Rod count max is 3, randomize with variety (1, 2, or 3 rods)
        currentState = BoatState.Fishing;
        float rPick = Random.value;
        int rodCount;
        if (rPick < 0.20f) rodCount = 1;       // 20% chance: 1 rod
        else if (rPick < 0.65f) rodCount = 2;  // 45% chance: 2 rods
        else rodCount = 3;                     // 35% chance: 3 rods

        rodCount = Mathf.Clamp(rodCount, minRods, maxRods);
        activeRods.Clear();

        float sign = facingRight ? -1f : 1f;
        float[] offsets;
        if (rodCount == 1)
        {
            offsets = new float[] { sign * Random.Range(2.5f, 4.5f) };
        }
        else if (rodCount == 2)
        {
            offsets = new float[] { sign * Random.Range(4.5f, 6.2f), sign * Random.Range(1.8f, 3.2f) };
        }
        else
        {
            offsets = new float[] { sign * Random.Range(5.0f, 6.5f), sign * Random.Range(3.4f, 4.6f), sign * Random.Range(1.6f, 2.6f) };
        }

        // Generate distinct depth tiers spanning the full ocean (world Y: -15 to +15)
        // Deep: near floor (-12.5 to -5.5), Mid: mid-waters (-5.0 to 1.0), Shallow: upper waters (1.0 to 7.5)
        // Shallowest tier is now calibrated to descend significantly deeper into the active swimming space
        List<float> depthPool = new List<float>
        {
            Random.Range(deepDepthRange.x, deepDepthRange.y),
            Random.Range(midDepthRange.x, midDepthRange.y),
            Random.Range(shallowDepthRange.x, shallowDepthRange.y)
        };

        // Shuffle depth pool so the deepest isn't always at the same position
        for (int i = 0; i < depthPool.Count; i++)
        {
            int swapIdx = Random.Range(i, depthPool.Count);
            float temp = depthPool[i];
            depthPool[i] = depthPool[swapIdx];
            depthPool[swapIdx] = temp;
        }

        for (int r = 0; r < offsets.Length; r++)
        {
            float dropX = transform.position.x + offsets[r];
            float hookTargetY = depthPool[r];

            if (GridController.Instance != null)
            {
                Hazard hz = GridController.Instance.SpawnFishingRodForBoat(this, dropX, hookTargetY);
                if (hz != null) activeRods.Add(hz);
            }

            // User Request: Staggered drop delays so rods don't all drop at once
            if (r < offsets.Length - 1)
            {
                float staggerDelay = Random.Range(0.9f, 2.0f);
                float st = 0f;
                while (st < staggerDelay)
                {
                    if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
                    {
                        st += Time.deltaTime;
                    }
                    yield return null;
                }
            }
        }

        // Wait until all rods have retracted
        while (activeRods.Count > 0)
        {
            activeRods.RemoveAll(h => h == null || !h.gameObject.activeInHierarchy);
            yield return null;
        }

        // Brief delay after lines are reeled in before departing
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

        // Drive straight forward to leave
        StartDeparture();
    }

    /// <summary>
    /// Called when an active fishing line finishes retracting
    /// </summary>
    public void OnHazardRetracted(Hazard hazard = null)
    {
        if (hazard != null)
        {
            activeRods.Remove(hazard);
        }
        else if (activeRods.Count > 0)
        {
            activeRods.RemoveAt(0);
        }
    }

    private void StartDeparture()
    {
        if (currentState == BoatState.Departing) return;
        currentState = BoatState.Departing;

        // Reignite wake bubbles as boat speeds away
        if (wakeParticleSystem != null && !wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Play();
        }
    }

    private void UpdateDeparting(float bobOffset, float tiltAngle)
    {
        // Requirement 3: Must leave by driving straight forward (can't drive backward)
        float newX = transform.position.x + (travelDirection * departSpeed * Time.deltaTime);
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(newX, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));

        // Use fixed WORLD background edges for despawn (not camera edges).
        // The boat must fully exit the background before being destroyed.
        float boatHalfWidth = GetBoatHalfWidth();
        float despawnMargin = boatHalfWidth + 3.0f;

        bool isFullyOffBg = false;
        if (travelDirection > 0f) // Moving right -> exits past right background edge
        {
            float boatLeft = (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.1f)
                ? spriteRenderer.bounds.min.x
                : (transform.position.x - boatHalfWidth);

            if (boatLeft > worldBgRight || transform.position.x > worldBgRight + despawnMargin)
            {
                isFullyOffBg = true;
            }
        }
        else // Moving left -> exits past left background edge
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
                GridController.Instance.OnBoatDeparted(this);
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

    /// <summary>
    /// Keeps the boat anchored at the world water surface level with wave bobbing.
    /// Only the bottom keel dips into view from the top of the screen; the upper boat remains off-screen.
    /// </summary>
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

    /// <summary>
    /// Dynamically controls boat engine audio volume and stereo pan based on distance to the player:
    /// - Plays actively while driving (Arriving or Departing).
    /// - Cuts off to complete silence when stopped to fish.
    /// - Loud when player is near surface under boat; fades to complete silence when diving deep or far away.
    /// </summary>
    private void UpdateAudio()
    {
        if (engineAudioSource == null) return;

        // Respect global SFX settings and game pause state
        bool sfxEnabled = AudioSettingsManager.IsSfxEnabled;
        if (!sfxEnabled || isPaused || (GameManager.instance != null && GameManager.Paused))
        {
            if (engineAudioSource.isPlaying) engineAudioSource.Pause();
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

        // Only play boat engine sound when actively driving (Arriving or Departing)
        bool isDriving = (currentState == BoatState.Arriving || currentState == BoatState.Departing);

        if (!isDriving)
        {
            // When stopped to fish, smoothly cut off the audio to complete silence
            targetAudioVolume = 0f;
        }
        else
        {
            // React to user distance (closeness and furtherness)
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
                    // Smooth quadratic distance rolloff so deep diving fades to complete silence
                    float t = (dist - minEngineDistance) / (maxEngineDistance - minEngineDistance);
                    targetAudioVolume = (1f - t) * (1f - t) * maxEngineVolume;
                }

                // Dynamic stereo panning based on horizontal offset relative to the player
                float pan = Mathf.Clamp((transform.position.x - pt.position.x) / 14.0f, -0.85f, 0.85f);
                engineAudioSource.panStereo = pan;
            }
            else
            {
                targetAudioVolume = maxEngineVolume * 0.5f;
            }
        }

        // Dynamic engine pitch for throttle response (revving when departing, idling down when stopping)
        float targetPitch = 1.0f;
        if (currentState == BoatState.Departing) targetPitch = 1.08f;
        else if (currentState == BoatState.Arriving) targetPitch = 1.0f;
        else targetPitch = 0.92f;
        engineAudioSource.pitch = Mathf.MoveTowards(engineAudioSource.pitch, targetPitch, 1.5f * Time.deltaTime);

        // Smooth volume interpolation to prevent sudden clicks or pops
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

