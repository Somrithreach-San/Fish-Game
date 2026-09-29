using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// BackgroundWhaleAmbient manages majestic, atmospheric background whale silhouettes swimming far in the distance:
/// - Purely visual ambient: zero colliders, zero raycasts, no interaction with player or fish.
/// - In-engine composition: 20-60% opacity, abyssal soft-blue depth tint, slight Gaussian blur (via AmbientWhaleFog shader),
///   slow parallax drift, and sinusoidal tail/body swimming undulation.
/// - Exclusively active in Coral Coast Deep Ocean Levels (Levels 5 to 8), or triggerable via cheat menu.
/// </summary>
public class BackgroundWhaleAmbient : MonoBehaviour
{
    public static BackgroundWhaleAmbient Instance { get; private set; }

    [Header("Sprite & Material")]
    [SerializeField] private Sprite whaleSprite;
    [SerializeField] private Material customMaterial;

    [Header("Atmospheric Appearance")]
    [Range(0.20f, 1.0f)]
    [SerializeField] private float baseOpacity = 1.0f; // Pure 100% opacity to preserve original Photoshop export colors untouched

    [Header("Scale & Depth (Matching Exact User Tuning)")]
    [SerializeField] private Vector2 scaleRange = new Vector2(0.589f, 0.589f); // Locked to exact user tuning (0.58926)
    private const float backgroundZ = 0.0f;
    [SerializeField] private string sortingLayerName = "ParallaxBackground";
    [SerializeField] private int sortingOrder = 1;

    [Header("Shark-Sampled Cruising Motion & Behavior")]
    [SerializeField] private float fixedCenterlineY = 11.05f; // Locked to exact user desired altitude (Y = 11.05f)
    public float CenterlineY => (fixedCenterlineY >= 5.0f) ? fixedCenterlineY : 11.05f;
    public const float Lane2MinY = 11.0f;
    public const float Lane2MaxY = 16.0f;
    [SerializeField] private Vector2 swimSpeedRange = new Vector2(2.2f, 2.8f); // Slower, majestic ambient cruising speed
    [SerializeField] private float undulationFrequency = 0.25f; // Slower rhythmic undulation
    [SerializeField] private float undulationAmplitude = 0.008f; // Subtle organic micro-motion

    [Header("Fish Swim 2.5D Animation (Shark Hazard Style)")]
    [Tooltip("Swimming cycle frequency (Hz) for the majestic background whale body wave")]
    [SerializeField] private float swimAnimFrequency = 0.35f; // Slower, grand whale frequency
    [Tooltip("3D perspective body yaw amplitude (Y-axis rotation, matching SharkHazard's +/-10 deg idle.anim, softened for background)")]
    [Range(0f, 10f)]
    [SerializeField] private float swimAnimYawAmplitude = 5.0f; // Soft, natural 5 deg 3D perspective body flex
    [Tooltip("Subtle pitch/tilt amplitude (Z-axis rotation)")]
    [Range(0f, 5f)]
    [SerializeField] private float swimAnimPitchAmplitude = 1.0f;
    [Tooltip("Subtle vertical swimming bobbing")]
    [Range(0f, 0.5f)]
    [SerializeField] private float swimAnimYBobAmplitude = 0.04f;

    [Header("Organic Spawning Intervals")]
    [SerializeField] private float minSpawnInterval = 120f;
    [SerializeField] private float maxSpawnInterval = 240f;

    private Coroutine lifecycleRoutine;
    private readonly List<GameObject> activeWhales = new List<GameObject>();

    private static Sprite s_CachedWhaleSprite;
    private static Material s_CachedMaterial;

    public Sprite GetWhaleSprite()
    {
        if (whaleSprite != null) return whaleSprite;
        if (s_CachedWhaleSprite != null) return s_CachedWhaleSprite;

#if UNITY_EDITOR
        var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/Humped_Back_Whale_Background_Ambient.png");
        if (allAssets != null)
        {
            foreach (var asset in allAssets)
            {
                if (asset is Sprite sp)
                {
                    s_CachedWhaleSprite = sp;
                    return s_CachedWhaleSprite;
                }
            }
        }
#endif
        s_CachedWhaleSprite = Resources.Load<Sprite>("Humped_Back_Whale_Background_Ambient");
        return s_CachedWhaleSprite;
    }

    public Material GetAmbientMaterial()
    {
        if (customMaterial != null && customMaterial.shader != null && customMaterial.shader.name == "Sprites/Default") return customMaterial;
        if (s_CachedMaterial != null) return s_CachedMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            s_CachedMaterial = new Material(shader)
            {
                name = "AmbientWhale_SpritesDefaultMat"
            };
        }
        return s_CachedMaterial;
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

        SubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
        StopAllWhaleRoutines();
        if (Instance == this) Instance = null;
    }

    private void SubscribeEvents()
    {
        EventManager.StartListening("playerDeath", OnGameEnded);
        EventManager.StartListening("GameLoss", OnGameEnded);
        EventManager.StartListening("GameWin", OnGameEnded);
        EventManager.StartListening("stageClear", OnGameEnded);
    }

    private void UnsubscribeEvents()
    {
        EventManager.StopListening("playerDeath", OnGameEnded);
        EventManager.StopListening("GameLoss", OnGameEnded);
        EventManager.StopListening("GameWin", OnGameEnded);
        EventManager.StopListening("stageClear", OnGameEnded);
    }

    private void OnEnable()
    {
        if (lifecycleRoutine != null) StopCoroutine(lifecycleRoutine);
        lifecycleRoutine = StartCoroutine(WhaleAmbientLifecycleRoutine());
    }

    private void OnDisable()
    {
        StopAllWhaleRoutines();
    }

    private void StopAllWhaleRoutines()
    {
        if (lifecycleRoutine != null)
        {
            StopCoroutine(lifecycleRoutine);
            lifecycleRoutine = null;
        }
        for (int i = activeWhales.Count - 1; i >= 0; i--)
        {
            if (activeWhales[i] != null)
            {
                Destroy(activeWhales[i]);
            }
        }
        activeWhales.Clear();
    }

    private void OnGameEnded()
    {
        // Stop spawning new ambient whales, but let currently swimming whales finish their majestic pass naturally
        if (lifecycleRoutine != null)
        {
            StopCoroutine(lifecycleRoutine);
            lifecycleRoutine = null;
        }
    }

    /// <summary>
    /// Checks whether current level is eligible for organic ambient whale spawns.
    /// </summary>
    public static bool IsEligibleLevel()
    {
        if (LevelManager.IsCurrentLakeLevel) return false;
        int lvl = LevelManager.CurrentLevel;
        return lvl >= 5 && lvl <= 8;
    }

    private IEnumerator WhaleAmbientLifecycleRoutine()
    {
        // 1. If DeepWhaleAmbience is active in scene, DeepWhaleAmbience acts as the master scheduler
        // (orchestrating both the acoustic call and triggering this visual pass seamlessly).
        if (DeepWhaleAmbience.Instance != null || FindFirstObjectByType<DeepWhaleAmbience>() != null)
        {
            yield break;
        }

        // Wait until briefing is dismissed before starting periodic checks
        while (Rhinotap.LevelBriefingManager.IsBriefingActive)
        {
            yield return null;
        }

        while (true)
        {
            if (!IsEligibleLevel() || (GameManager.instance != null && GameManager.instance.IsGameOver))
            {
                yield return new WaitForSeconds(2.0f);
                continue;
            }

            // Organic wait interval between ambient passes
            float interval = Random.Range(minSpawnInterval, maxSpawnInterval);
            float timer = 0f;
            while (timer < interval)
            {
                if (!GameManager.Paused && !Rhinotap.LevelBriefingManager.IsBriefingActive && !LevelManager.IsLevelCompleted && !(GameManager.instance != null && GameManager.instance.IsGameOver))
                {
                    timer += Time.deltaTime;
                }
                yield return null;
            }

            // Only spawn if no ambient whale is currently traversing
            if (activeWhales.Count == 0 && IsEligibleLevel() && !LevelManager.IsLevelCompleted && !(GameManager.instance != null && GameManager.instance.IsGameOver) && !Rhinotap.LevelBriefingManager.IsBriefingActive)
            {
                TriggerWhalePass();
            }
        }
    }

    /// <summary>
    /// Spawns an ambient background whale swimming across the deep ocean.
    /// Plays the directional ambient whale call sound right before the whale glides into the scene.
    /// </summary>
    /// <param name="forceSwimRight">Optional: force swimming left to right (true) or right to left (false)</param>
    /// <param name="playSoundFirst">When true, plays the deep whale audio call right before the whale enters the scene</param>
    /// <param name="leadInDelay">Duration (seconds) the audio echoes before the whale begins fading into view</param>
    /// <summary>
    /// Spawns an ambient background whale swimming across the deep ocean.
    /// The deep whale ambient sound plays ONLY when the whale has 100% entered the scene.
    /// </summary>
    /// <param name="forceSwimRight">Optional: force swimming left to right (true) or right to left (false)</param>
    /// <param name="playSoundOnFullEntry">When true, plays the deep whale audio call right as the whale fully enters the viewport</param>
    public GameObject TriggerWhalePass(bool? forceSwimRight = null, bool playSoundOnFullEntry = true)
    {
        if (Rhinotap.LevelBriefingManager.IsBriefingActive) return null;

        Sprite sprite = GetWhaleSprite();
        if (sprite == null)
        {
            Debug.LogWarning("[BackgroundWhaleAmbient] Whale sprite not found!");
            return null;
        }

        bool swimRight = forceSwimRight.HasValue ? forceSwimRight.Value : (Random.value > 0.5f);

        float chosenScale = (scaleRange.x >= 0.20f && scaleRange.x <= 0.80f) ? Random.Range(scaleRange.x, scaleRange.y) : 0.48f;

        float chosenSpeed = (swimSpeedRange.x >= 0.5f && swimSpeedRange.x <= 15.0f) ? Random.Range(swimSpeedRange.x, swimSpeedRange.y) : Random.Range(2.2f, 2.8f);
        
        // Calculate world bounds from camera or default bounds
        float camViewHalfW = 25.0f;
        Camera cam = Camera.main;
        if (cam != null)
        {
            camViewHalfW = cam.orthographicSize * cam.aspect;
        }

        float camCenterX = (cam != null) ? cam.transform.position.x : 0.0f;

        // Fixed horizontal center line across the background (Photoshop composition line Y = 13.5f)
        float startY = CenterlineY;
        float targetY = CenterlineY;

        // Whale sprite extent: calculate generous world-space half-width
        float approxWhaleHalfWidth = (sprite != null && sprite.rect.width > 0)
            ? (sprite.rect.width / sprite.pixelsPerUnit * chosenScale * 0.5f)
            : (chosenScale * 15.0f);
        approxWhaleHalfWidth = Mathf.Max(approxWhaleHalfWidth, 12.0f);

        // Ample offscreen margin: starts just outside the camera viewport so it enters promptly (within 2-3s) without pop
        float offscreenMargin = camViewHalfW + approxWhaleHalfWidth + 4.0f;

        float startX = swimRight ? (camCenterX - offscreenMargin) : (camCenterX + offscreenMargin);
        float endX = swimRight ? (camCenterX + offscreenMargin) : (camCenterX - offscreenMargin);

        // Create ambient whale GameObject
        GameObject whaleObj = new GameObject("Background_Whale_Ambient");
        float actualZ = 0.0f; // Exactly on the background plane (Order 1 in front of ocean backdrop, behind all fish)
        whaleObj.transform.position = new Vector3(startX, startY, actualZ);
        whaleObj.transform.localScale = new Vector3(chosenScale, chosenScale, 1.0f);

        // Child Gfx holding the SpriteRenderer (matching SharkHazard, Player, and AI Fish architecture)
        GameObject gfxObj = new GameObject("Gfx");
        gfxObj.transform.SetParent(whaleObj.transform, false);
        gfxObj.transform.localPosition = Vector3.zero;
        gfxObj.transform.localRotation = Quaternion.identity;

        // SpriteRenderer setup (renders on ParallaxBackground order 1, directly in front of ocean backdrop, behind all gameplay fish)
        SpriteRenderer sr = gfxObj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = !string.IsNullOrEmpty(sortingLayerName) ? sortingLayerName : "ParallaxBackground";
        sr.sortingOrder = sortingOrder;

        // Note: Raw Sprite faces Left by default.
        // When swimming Left-to-Right (swimRight = true), flipX = false faces Right.
        // When swimming Right-to-Left (swimRight = false), flipX = true faces Left.
        sr.flipX = !swimRight;

        // SpriteRenderer material and color setup (clean sprite material with pure original texture colors)
        Material baseMat = GetAmbientMaterial();
        Material instanceMat = new Material(baseMat);
        Color startColor = new Color(1f, 1f, 1f, 0f); // Starts at 0 alpha for smooth fade-in
        sr.material = instanceMat;
        sr.color = startColor;

        activeWhales.Add(whaleObj);
        StartCoroutine(WhaleSwimRoutine(whaleObj, gfxObj, sr, instanceMat, startX, endX, startY, targetY, chosenSpeed, chosenScale, playSoundOnFullEntry, swimRight, approxWhaleHalfWidth));

        return whaleObj;
    }

    private IEnumerator WhaleSwimRoutine(GameObject whaleObj, GameObject gfxObj, SpriteRenderer sr, Material mat, float startX, float endX, float startY, float endY, float speed, float scale, bool playSoundOnFullEntry, bool swimRight, float initialHalfWidth)
    {
        float curX = startX;
        float elapsed = 0f;
        float fadeInDuration = 3.5f;

        float direction = swimRight ? 1f : -1f;
        float randomPhase = Random.Range(0f, Mathf.PI * 2f);
        Camera mainCam = Camera.main;

        bool soundPlayed = !playSoundOnFullEntry; // If true, triggers when front half emerges in view

        // Randomize the sound trigger offset for every whale pass:
        // - Negative offset (-6f to -2f): Echoes from the distance 2-3s BEFORE emerging into view
        // - Around 0 (-1f to +2f): Sounds right as the head breaches the camera frame
        // - Positive offset (+4f to +14f): Sounds majestically as it cruises through the screen / near center
        float soundTriggerOffset = Random.Range(-6.0f, 14.0f);

        while (true)
        {
            if (whaleObj == null) yield break;

            if (GameManager.Paused)
            {
                yield return null;
                continue;
            }

            float dt = Time.deltaTime;
            elapsed += dt;

            // Constant, completely independent world-space swimming movement (zero coupling to player/camera)
            curX += direction * speed * dt;
            
            // Subtle, living aquatic micro-undulation (very gentle macro trajectory stroke)
            float undulation = Mathf.Sin(elapsed * (undulationFrequency * Mathf.PI * 2f) + randomPhase) * undulationAmplitude;
            float curY = CenterlineY + undulation;

            float actualZ = 0.0f;
            whaleObj.transform.position = new Vector3(curX, curY, actualZ);

            // 2.5D Fish Swim Perspective Animation (Matching SharkHazard's idle.anim, softened gracefully for background)
            float animPhase = elapsed * (swimAnimFrequency * Mathf.PI * 2f) + randomPhase;
            
            // 1. Yaw: Y-axis 3D perspective body turn (matches idle.anim +/-10 deg, tuned softly for background)
            float yaw = Mathf.Cos(animPhase) * swimAnimYawAmplitude;
            if (!swimRight) yaw = -yaw;

            // 2. Pitch / Tilt: Z-axis subtle stroke tilt
            float pitch = Mathf.Sin(animPhase) * swimAnimPitchAmplitude;
            if (!swimRight) pitch = -pitch;

            // 3. Vertical stroke bob: localPosition Y
            float localY = Mathf.Sin(animPhase * 2f) * swimAnimYBobAmplitude;

            if (gfxObj != null)
            {
                gfxObj.transform.localRotation = Quaternion.Euler(0f, yaw, pitch);
                gfxObj.transform.localPosition = new Vector3(0f, localY, 0f);
            }

            // Dynamic opacity: smooth fade in at spawn, full pure original opacity across scene
            float currentAlpha = baseOpacity;
            if (elapsed < fadeInDuration)
            {
                float inT = Mathf.Clamp01(elapsed / fadeInDuration);
                currentAlpha = Mathf.Lerp(0f, baseOpacity, inT * inT * (3f - 2f * inT));
            }

            Color c = new Color(1f, 1f, 1f, currentAlpha);
            if (mat != null)
            {
                mat.SetColor("_Color", c);
            }
            if (sr != null)
            {
                sr.color = c;
            }

            // Whale half-width in world space
            float halfWhaleW = (sr != null && sr.sprite != null && sr.bounds.extents.x > 0.1f)
                ? sr.bounds.extents.x
                : initialHalfWidth;

            // Camera bounds check
            float camViewHalfW = 25.0f;
            float camX = 0f;
            if (mainCam != null)
            {
                camViewHalfW = mainCam.orthographicSize * mainCam.aspect;
                camX = mainCam.transform.position.x;
            }

            // Trigger the ambient whale call sound at the randomized timing point
            if (!soundPlayed)
            {
                float triggerX = swimRight 
                    ? (camX - camViewHalfW + soundTriggerOffset)
                    : (camX + camViewHalfW - soundTriggerOffset);

                bool triggerReached = swimRight
                    ? (curX + halfWhaleW * 0.2f >= triggerX)
                    : (curX - halfWhaleW * 0.2f <= triggerX);

                if (triggerReached)
                {
                    soundPlayed = true;
                    DeepWhaleAmbience ambience = DeepWhaleAmbience.Instance != null ? DeepWhaleAmbience.Instance : Object.FindFirstObjectByType<DeepWhaleAmbience>();
                    if (ambience == null && mainCam != null)
                    {
                        ambience = mainCam.gameObject.AddComponent<DeepWhaleAmbience>();
                    }
                    if (ambience != null)
                    {
                        ambience.PlayDirectionalWhaleCall(swimRight);
                    }
                }
            }

            // Check if the whale has completely traversed past both target endX AND the active camera screen edge
            bool pastEndX = swimRight ? (curX >= endX) : (curX <= endX);
            bool trailingEdgePastScreen = swimRight
                ? ((curX - halfWhaleW) > (camX + camViewHalfW + 10.0f))
                : ((curX + halfWhaleW) < (camX - camViewHalfW - 10.0f));

            if (pastEndX && trailingEdgePastScreen)
            {
                // Whale has 100% exited the scene and background extents
                break;
            }

            yield return null;
        }

        // Clean up when journey across screen is completely finished
        if (whaleObj != null)
        {
            activeWhales.Remove(whaleObj);
            if (mat != null) Destroy(mat);
            Destroy(whaleObj);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw Lane 2 bounds and centerline in Scene view
        // 1. Centerline Y = 13.5 (Solid Cyan line)
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(-60f, CenterlineY, 0f), new Vector3(60f, CenterlineY, 0f));

        // 2. Lane 2 vertical range Y = 11.0 to 16.0 (Semi-transparent bounds)
        Gizmos.color = new Color(0f, 0.75f, 1f, 0.45f);
        Gizmos.DrawLine(new Vector3(-60f, Lane2MinY, 0f), new Vector3(60f, Lane2MinY, 0f));
        Gizmos.DrawLine(new Vector3(-60f, Lane2MaxY, 0f), new Vector3(60f, Lane2MaxY, 0f));

        // 3. Lane 2 band wireframe box
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
        Gizmos.DrawCube(new Vector3(0f, CenterlineY, 0f), new Vector3(120f, Lane2MaxY - Lane2MinY, 0.1f));
    }
#endif
}
