using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    #region INSPECTOR
    [SerializeField]
    private float moveSpeed = 8.0f; // Organic, energetic swim speed (Feeding Frenzy style)

    [SerializeField]
    private int maxLevel = 6; // Increased to 6 to support Level 6 growth

    [SerializeField]
    private int baseXpRequirement = 80;
    [Range(0.05f, 2f)]
    [SerializeField]
    private float levelXpIncreasePercentage = 0.25f;

    [Header("Sounds")]
    [Space(20)]
    [SerializeField]
    private AudioClip[] biteSounds;
    public AudioClip[] BiteSounds => biteSounds;
    [SerializeField]
    private AudioClip deathSound;
    [SerializeField]
    private AudioClip lakeDeathSound;
    public AudioClip LakeDeathSound => lakeDeathSound;
    [SerializeField]
    private AudioClip sickFishEatSound;
    public AudioClip SickFishEatSound => sickFishEatSound;
    [SerializeField]
    private AudioClip growSound;
    [SerializeField]
    private AudioClip boostStartClip;
    [Range(0f, 1f)]
    [SerializeField]
    private float boostStartVolume = 0.6f;

    [Header("Effects")]
    [SerializeField]
    private ParticleSystem speedEffect;
    [Tooltip("Drag your 'bubbleParticleMat' here to auto-create the effect")]
    [SerializeField]
    private Material bubbleMaterial;
    public Material BubbleMaterial => bubbleMaterial; // Public accessor
    [Tooltip("Or drag your 'bubble.png' texture here if the material doesn't work")]
    [SerializeField]
    private Texture2D bubbleTexture;
    public Texture2D BubbleTexture => bubbleTexture; // Public accessor
    public ParticleSystem SpeedEffect => speedEffect;

    [Header("Eat Effects")]
    [SerializeField]
    private ParticleSystem eatEffect;
    public ParticleSystem EatEffect => eatEffect;

    public static Sprite LastKillerSprite = null;
    public static Color LastKillerColor = Color.white;
    public static bool LastKillerIsSick = false;
    [Tooltip("Assign multiple bubble textures here for variety")]
    [SerializeField]
    private Texture2D[] eatBubbleVariants;

    [Header("Visuals")]
    [SerializeField]
    private Sprite idleSprite; // Closed mouth
    [SerializeField]
    private Sprite halfBiteSprite; // Half-open mouth
    [SerializeField]
    private Sprite eatSprite; // Fully open mouth
    
    [Header("Manual Level Scaling")]
    [Tooltip("Define exact scale for each level in Ocean levels (Index 0 = Level 1, Index 1 = Level 2, etc.)")]
    [SerializeField]
    private float[] levelScales = new float[] { 0.42f, 0.58f, 0.78f, 1.02f, 1.30f, 1.60f };

    [Tooltip("Define exact scale for each level in River levels")]
    [SerializeField]
    private float[] riverLevelScales = new float[] { 0.60f, 0.78f, 0.98f, 1.40f, 1.80f, 2.10f };

    private Coroutine growthCoroutine;

    #endregion

    #region Internal Vars
    //====================| Components
    Transform playerGraphics;
    AudioSource audioSource;
    AudioListener audioListener; // Ensure listener exists
    TrailRenderer trail;
    private Camera _mainCamera;

    //====================| Game Mechanics
    [SerializeField]
    private bool isAlive = true;
    public bool IsAlive => isAlive; // Public accessor for checks

    [SerializeField]
    private bool isPaused = false;
    
    [Header("Hazard Hook Interaction")]
    private bool isHooked = false;
    public bool IsHooked => isHooked;
    private Hazard caughtHazard = null;
    private HarpoonHazard caughtHarpoon = null;
    public bool IsImpaledByHarpoon => caughtHarpoon != null;
    private float hookElapsedTime = 0f;
    private float hookCurrentAngle = 0f;
    private float hookAngleVelocity = 0f;
    private Vector3 hookStartPos;
    
    [SerializeField]
    private int currentLevelXp = 100;
    private int currentXp = 0;

    public int Level { get; private set; } = 1;
    public float LevelProgress => currentLevelXp > 0 ? Mathf.Clamp01((float)currentXp / currentLevelXp) : 0f;

    private int score = 0;

    //====================| Mind Control Hazard

    //====================| Control Input
    //Direction for keyboard controls
    // Boost fields
    [Header("Boost")]
    [SerializeField]
    private float boostMultiplier = 1.80f;
    [SerializeField]
    private float boostDuration = 0.45f;
    private float currentSpeedMultiplier = 1f;
    private float boostTimer = 0f;

    // XP Multiplier (Golden Fish Bonus)
    private float xpMultiplier = 1f;
    private float xpMultiplierTimer = 0f;
    private float xpMultiplierTotalDuration = 1f;
    
    // Visuals
    private float currentBaseScale = 1f;
    public float CurrentBaseScale => currentBaseScale;
    private float lastBubbleScaleApplied = -1f;
    private SpriteRenderer spriteRenderer;
    private float baseMoveSpeed;
    
    // Physics & Movement
    private Rigidbody2D rb;
    private Vector2 _targetVelocity = Vector2.zero;
    private Vector2 _targetWorldPos = Vector2.zero;
    private bool _hasInitializedTarget = false;
    private bool _isMouseControl = false;
    public bool IsDropping { get; set; } = false;

    public void SetTargetPosition(Vector2 pos)
    {
        _targetWorldPos = pos;
        _hasInitializedTarget = true;
    }

    [Header("Movement Polish (Feeding Frenzy Style)")]
    [SerializeField] private float swimAcceleration = 42f; // Snappy propulsion while moving mouse
    [SerializeField] private float waterDrag = 14f; // Underwater friction: slides for a bit then stops
    [SerializeField] private float tiltAngle = 24f; // Max tilt angle in degrees
    [SerializeField] private float tiltSpeed = 16f; // How fast we tilt
    
    // Optimization: Cache the generated material to prevent lag on spawn
    private static Material _cachedGeneratedMaterial;

    // Spike Bounce
    private float lastSpikeBounceTime = -1f;
    private float spikeBounceTimer = 0f;
    private Vector2 spikeBounceVelocity = Vector2.zero;

    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

    // Poison / Sick Fish Debuff (Feeding Frenzy Style)
    private float poisonTimer = 0f;
    public bool IsPoisoned => poisonTimer > 0f;

    // Ink Disorientation (from Cuttlefish ink)
    private float inkDisorientTimer = 0f;
    public bool IsDisorientedByInk => inkDisorientTimer > 0f && (InkScreenOverlay.Instance == null || InkScreenOverlay.IsBlindingActive);

    public void ApplyInkDisorientation(float duration = 5.0f)
    {
        inkDisorientTimer = duration;
    }

    public float GetActiveSpeedMultiplier()
    {
        float mult = currentSpeedMultiplier;
        if (IsPoisoned) mult *= 0.65f;
        if (IsDisorientedByInk)
        {
            float alpha = InkScreenOverlay.BlindingAlpha;
            mult *= Mathf.Lerp(1.0f, 0.35f, alpha);
        }
        return mult;
    }

    public void ApplyPoisonDebuff(float duration = 3.5f)
    {
        poisonTimer = duration;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = new Color(0.72f, 1f, 0.72f, 1f); // Sickly green tint
        }
    }

    public void ReducePoisonTime(float amount = 0.65f)
    {
        if (poisonTimer > 0f)
        {
            poisonTimer = Mathf.Max(0f, poisonTimer - amount);
            if (poisonTimer <= 0f && spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
            }
        }
    }

    public void EatPearl(float pearlXp, Vector3 pearlWorldPos, bool isBlackPearl = false)
    {
        if (!isAlive || isPaused) return;

        LevelManager.RecordPearlEaten(isBlackPearl);
        PlayEatEffect();

        if (isBlackPearl)
        {
            if (PlayerAbilitySystem.Instance != null)
            {
                PlayerAbilitySystem.Instance.TriggerBlackPearlBonus();
            }

            if (GuiManager.instance != null)
            {
                // Display sprite-based 'x5' popup matching the streak HUD style
                GuiManager.instance.ShowFloatingStreakBonus(pearlWorldPos, 5);
            }
        }
        else
        {
            int finalXp = Mathf.RoundToInt(pearlXp * xpMultiplier);
            currentXp += finalXp;
            score += (finalXp * Level);

            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
                GuiManager.instance.ShowFloatingXp(pearlWorldPos, finalXp);
            }
        }

        if (audioSource != null && biteSounds != null && biteSounds.Length > 0 && AudioSettingsManager.IsSfxEnabled)
        {
            AudioClip clip = biteSounds[Random.Range(0, biteSounds.Length)];
            if (clip != null) audioSource.PlayOneShot(clip, 1.0f);
        }
    }

    public void OnHarpoonImpaled(HarpoonHazard harpoon)
    {
        if (!isAlive || isHooked) return;

        // Cancel any active special ability immediately upon being impaled
        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive)
        {
            PlayerAbilitySystem.Instance.EndAbility();
        }

        isHooked = true;
        caughtHarpoon = harpoon;
        caughtHazard = null;
        hookElapsedTime = 0f;
        _targetVelocity = Vector2.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }
        currentSpeedMultiplier = 1f;
        boostTimer = 0f;
        StopSpeedEffect();

        hookStartPos = transform.position;
        float startAngle = (playerGraphics != null) ? playerGraphics.localEulerAngles.z : 0f;
        if (startAngle > 180f) startAngle -= 360f;
        hookCurrentAngle = startAngle;
        hookAngleVelocity = 0f;
    }
    
    #endregion

    #region Mono Behaviour
    
    // User Request: "Hidden cursor during gameplay, visible in UI"
    void OnEnable()
    {
        // Only hide if game is running (not paused)
        if (GameManager.instance != null && !GameManager.Paused)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
        }
        // If GameManager isn't ready yet, GameManager.Start will handle it or we update in Start

        AudioSettingsManager.OnSfxSettingChanged += HandleSfxSettingChanged;
    }

    void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        isHooked = false;
        Animator[] allAnimators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < allAnimators.Length; i++)
        {
            if (allAnimators[i] != null) allAnimators[i].enabled = true;
        }

        AudioSettingsManager.OnSfxSettingChanged -= HandleSfxSettingChanged;
        EventManager.StopListening("GameWin", StopMovementForGameEnd);
        EventManager.StopListening("GameLoss", StopMovementForGameEnd);
        EventManager.StopListening("playerDeath", StopMovementForGameEnd);
    }

    private void HandleSfxSettingChanged(bool enabled)
    {
        if (audioSource != null) audioSource.mute = !enabled;
    }

    // Start is called before the first frame update
    void Start()
    {
        LastKillerSprite = null;
        LastKillerColor = Color.white;
        LastKillerIsSick = false;

        // USER REQUEST: "Feeding Frenzy" Style Progression
        // Level 1 takes ~15 minnows (120 XP / 8 XP) to advance, preventing instant level skips.
        baseXpRequirement = 120;
        levelXpIncreasePercentage = 0.65f;
        currentLevelXp = baseXpRequirement;

        // Level Progression: Set target level from LevelManager configuration
        LevelConfig cfg = LevelManager.GetCurrentConfig();
        maxLevel = cfg.targetPlayerLevel;

        // Ensure levelScales array has slots for all levels
        if (levelScales == null || levelScales.Length < 6)
        {
            System.Array.Resize(ref levelScales, 6);
            
            // Fill new slots if they were empty (0)
            // Default pattern: 0.5, 0.62, 0.74, 0.86, 0.98, 1.10
            // Formula: 0.5 + (Index * 0.12)
            for (int i = 0; i < levelScales.Length; i++)
            {
                if (levelScales[i] == 0f)
                {
                    levelScales[i] = 0.5f + (i * 0.12f);
                }
            }
        }

        _mainCamera = Camera.main;
        
        // Physics Setup
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation; // Prevent physics rotation
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate; // Fix: Enable interpolation for smooth movement

        playerGraphics = transform.Find("PlayerGraphics");
        if (playerGraphics != null)
        {
             spriteRenderer = playerGraphics.GetComponent<SpriteRenderer>();

             if (LevelManager.IsCurrentLakeLevel)
             {
#if UNITY_EDITOR
                 var closedAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/river player_fish_closed mouth.png");
                 foreach (var a in closedAssets) { if (a is Sprite s) { idleSprite = s; break; } }
                 var halfAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/river player_fish_half-open mouth.png");
                 foreach (var a in halfAssets) { if (a is Sprite s) { halfBiteSprite = s; break; } }
                 var openAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/river player_fish_ open mouth.png");
                 foreach (var a in openAssets) { if (a is Sprite s) { eatSprite = s; break; } }
#endif
                 if (idleSprite == null) idleSprite = Resources.Load<Sprite>("river player_fish_closed mouth");
                 if (halfBiteSprite == null) halfBiteSprite = Resources.Load<Sprite>("river player_fish_half-open mouth");
                 if (eatSprite == null) eatSprite = Resources.Load<Sprite>("river player_fish_ open mouth");
             }
             else
             {
#if UNITY_EDITOR
                 if (idleSprite == null)
                 {
                     var closedAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/Ocean_Player_Fish_Mouth Closed.png");
                     foreach (var a in closedAssets) { if (a is Sprite s) { idleSprite = s; break; } }
                 }
                 if (halfBiteSprite == null)
                 {
                     var halfAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/Ocean_Player_Fish_Mouth_Half_Open.png");
                     foreach (var a in halfAssets) { if (a is Sprite s) { halfBiteSprite = s; break; } }
                 }
                 if (eatSprite == null)
                 {
                     var openAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/fish/Ocean_Player_Fish_Full_Mouth_Open.png");
                     foreach (var a in openAssets) { if (a is Sprite s) { eatSprite = s; break; } }
                 }
                 if (eatSprite == null)
                 {
                     eatSprite = halfBiteSprite;
                 }
#endif
                 if (idleSprite == null) idleSprite = Resources.Load<Sprite>("Ocean_Player_Fish_Mouth Closed") ?? Resources.Load<Sprite>("fish/Ocean_Player_Fish_Mouth Closed");
                 if (halfBiteSprite == null) halfBiteSprite = Resources.Load<Sprite>("Ocean_Player_Fish_Mouth_Half_Open") ?? Resources.Load<Sprite>("fish/Ocean_Player_Fish_Mouth_Half_Open");
                 if (eatSprite == null) eatSprite = Resources.Load<Sprite>("Ocean_Player_Fish_Full_Mouth_Open") ?? Resources.Load<Sprite>("fish/Ocean_Player_Fish_Full_Mouth_Open") ?? halfBiteSprite;
             }

             if (spriteRenderer != null && idleSprite != null)
             {
                 spriteRenderer.sprite = idleSprite;
             }
        }

        trail = GetComponent<TrailRenderer>();
        if (trail != null) trail.enabled = false; // Disable trail per user request

        // Auto-find or Create speed effect
        if (speedEffect == null)
        {
            // 1. Try to find existing child
            speedEffect = transform.Find("SpeedBubbles")?.GetComponent<ParticleSystem>();

            // 2. If still missing but we have a material OR texture, create it programmatically
            if (speedEffect == null && (bubbleMaterial != null || bubbleTexture != null))
            {
                CreateBubbleParticles();
            }
            
            // Ensure particles are stopped at start (fix for "only shows when speed boost")
            if (speedEffect != null && !speedEffect.isPlaying)
            {
                speedEffect.Stop();
            }
        }

        // Auto-find or Create eat effect
        if (eatEffect == null)
        {
             eatEffect = transform.Find("EatBubbles")?.GetComponent<ParticleSystem>();
             if (eatEffect == null)
             {
                 CreateEatParticles();
             }
        }

        UpdateCollision();

        //Listen to game pause event to update isPaused
        EventManager.StartListening<bool>("gamePaused", (param) => { isPaused = param; });
        EventManager.StartListening("GameWin", StopMovementForGameEnd);
        EventManager.StartListening("GameLoss", StopMovementForGameEnd);
        EventManager.StartListening("playerDeath", StopMovementForGameEnd);

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.mute = !AudioSettingsManager.IsSfxEnabled;
        AudioSettingsManager.RouteToSfx(audioSource);

        // Ensure exactly one AudioListener exists in the scene
        audioListener = GetComponent<AudioListener>();
        if (audioListener == null)
        {
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                if (Camera.main != null && Camera.main.GetComponent<AudioListener>() == null)
                {
                    Camera.main.gameObject.AddComponent<AudioListener>();
                }
                else
                {
                    audioListener = gameObject.AddComponent<AudioListener>();
                }
            }
        }
        if (audioSource == null)
            Debug.Log(gameObject.name + " is missing audio source component");

        // Hide mouse cursor for original-game feel
        Cursor.visible = false;
        // Fix: Confine cursor to window to prevent triggering browser UI/Leaving window
        Cursor.lockState = CursorLockMode.Confined;

        // Initialize XP Bar
        currentXp = 0; // Explicitly reset XP
        GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);

        // Enforce initial scale based on manual array
        float[] activeScales = GetCurrentLevelScales();
        if (activeScales != null && activeScales.Length >= Level)
        {
             currentBaseScale = activeScales[Level - 1];
             float scale = currentBaseScale;
             transform.localScale = new Vector3(scale, scale, 1);
             UpdateCollision();
        }

        baseMoveSpeed = moveSpeed;

        if (GetComponent<PlayerAbilitySystem>() == null)
        {
            gameObject.AddComponent<PlayerAbilitySystem>();
        }
    }

    //==============================| Public API for Effects |========================//

    public void PlaySpeedEffect()
    {
        if (speedEffect == null)
        {
            CreateBubbleParticles();
        }
        
        // Refresh settings based on current size
        UpdateBubbleParticles();

        if (speedEffect != null && !speedEffect.isPlaying)
        {
            speedEffect.Play();
        }
    }

    public void StopSpeedEffect()
    {
        if (speedEffect != null && speedEffect.isPlaying)
        {
            speedEffect.Stop();
        }
    }

    private void StopMovementForGameEnd()
    {
        _targetVelocity = Vector2.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
        StopSpeedEffect();
    }

    private void UpdateBubbleParticles()
    {
        if (speedEffect == null) return;
        
        if (lastBubbleScaleApplied == currentBaseScale) return;
        lastBubbleScaleApplied = currentBaseScale;

        float scale = currentBaseScale;
        speedEffect.transform.localPosition = new Vector3(-0.8f * scale, -0.1f * scale, 0f);

        var main = speedEffect.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f * scale, 0.3f * scale);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 2.0f * scale); 

        var emission = speedEffect.emission;
        emission.rateOverTime = 20f * scale; 

        var shape = speedEffect.shape;
        shape.radius = 0.2f * scale;
    }

    private void CreateBubbleParticles()
    {
        GameObject bubbles = new GameObject("SpeedBubbles");
        bubbles.transform.SetParent(transform, false);
        
        speedEffect = bubbles.AddComponent<ParticleSystem>();
        
        // Configure Particle System for Bubbles (Realistic Style adapted from Shark)
        var main = speedEffect.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.gravityModifier = -0.1f; // Float up
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f);

        var shape = speedEffect.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        // Rotate to emit backwards (assuming Right-facing sprite)
        shape.rotation = new Vector3(0f, -90f, 0f);

        // Velocity over Lifetime: Add turbulence
        var vel = speedEffect.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.space = ParticleSystemSimulationSpace.World;

        // Size over Lifetime: Grow then pop
        var sol = speedEffect.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0.0f, 0.5f); 
        curve.AddKey(0.8f, 1.0f); 
        curve.AddKey(1.0f, 0.0f); 
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Color/Alpha: Fade out
        var col = speedEffect.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0.0f), new GradientAlphaKey(0.4f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        // Assign Material
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();
        
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
        }
        else if (bubbleTexture != null)
        {
            // Optimization: Check cache first
            if (_cachedGeneratedMaterial != null)
            {
                renderer.material = _cachedGeneratedMaterial;
            }
            else
            {
                // Create a temporary material at runtime using the texture
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
                if (shader == null) shader = Shader.Find("Sprites/Default");
    
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.mainTexture = bubbleTexture;
                    
                    // Set some standard particle settings if using Standard Unlit
                    if (shader.name.Contains("Standard"))
                    {
                         mat.SetFloat("_Mode", 2); // Fade
                         mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                         mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                         mat.SetInt("_ZWrite", 0);
                         mat.DisableKeyword("_ALPHATEST_ON");
                         mat.EnableKeyword("_ALPHABLEND_ON");
                         mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                         mat.renderQueue = 3000;
                    }
                    
                    _cachedGeneratedMaterial = mat;
                    renderer.material = mat;
                }
            }
        }
        
        // Default sprite mode usually works, but ensure sorting
        renderer.sortingOrder = 5; // Ensure it's visible above background
        
        // Ensure it doesn't play immediately
        speedEffect.Stop();
    }

    private void CreateEatParticles()
    {
        GameObject bubbles = new GameObject("EatBubbles");
        bubbles.transform.SetParent(transform, false);
        bubbles.transform.localPosition = Vector3.zero;

        eatEffect = bubbles.AddComponent<ParticleSystem>();
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();

        // Main Settings
        var main = eatEffect.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f); // Random speed
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.5f); // Random lifetime
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f); // Reduced size (User request)
        main.gravityModifier = -0.05f; // Float up
        main.maxParticles = 50;

        // Emission (Burst only)
        var emission = eatEffect.emission;
        emission.rateOverTime = 0; // No continuous emission
        // Bursts will be triggered manually via Emit()

        // Shape (Cone/Circle)
        var shape = eatEffect.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.angle = 25f; // Cone angle for upward spread

        // Noise (Wiggle)
        var noise = eatEffect.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 1f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Velocity over Lifetime (More random movement)
        var vel = eatEffect.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 1f); // Drift left/right
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f); // Always go up-ish
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.space = ParticleSystemSimulationSpace.World;

        // Color/Fade
        var col = eatEffect.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            // More transparent for subtlety
            new GradientAlphaKey[] { new GradientAlphaKey(0.7f, 0.0f), new GradientAlphaKey(0.5f, 0.8f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        // Texture Sheet Animation (for variants)
        // If user provided multiple textures, we can try to texture sheet them OR just pick one if possible.
        // Since we can't easily merge textures at runtime without creating a new asset, 
        // we will use the texture sheet module if a material with sprites is assigned.
        // For now, let's use the default bubble material/texture.
        
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
        }
        else if (bubbleTexture != null)
        {
             // Optimization: Check cache first
             if (_cachedGeneratedMaterial != null)
             {
                 renderer.material = _cachedGeneratedMaterial;
             }
             else
             {
                 // Reuse the shader logic or just assign default
                 Shader shader = Shader.Find("Particles/Standard Unlit");
                 if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
                 if (shader == null) shader = Shader.Find("Sprites/Default");
                 
                 if (shader != null)
                 {
                     Material mat = new Material(shader);
                     mat.mainTexture = bubbleTexture;
                     
                     // Set some standard particle settings if using Standard Unlit
                     if (shader.name.Contains("Standard"))
                     {
                          mat.SetFloat("_Mode", 2); // Fade
                          mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                          mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                          mat.SetInt("_ZWrite", 0);
                          mat.DisableKeyword("_ALPHATEST_ON");
                          mat.EnableKeyword("_ALPHABLEND_ON");
                          mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                          mat.renderQueue = 3000;
                     }
                     
                     _cachedGeneratedMaterial = mat;
                     renderer.material = mat;
                 }
             }
        }
        
        // Sorting
        renderer.sortingOrder = 6; // Above player/speed bubbles
    }

    public void PlayEatEffect()
    {
        if (eatEffect != null)
        {
            // Randomize burst count (Reduced: 2-3 bubbles)
            int count = Random.Range(2, 4);
            
            // Emit particles
            eatEffect.Emit(count);
        }
    }

    // Unified Input (Mouse & Touch & Joystick - Feeding Frenzy Style)
    void HandleInput()
    {
        if (IsDropping || !isAlive || isHooked || isPaused || LevelManager.IsLevelCompleted || (GameManager.instance != null && GameManager.instance.IsGameOver))
        {
            _targetVelocity = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (IsDropping) _targetWorldPos = transform.position;
            return;
        }

        Vector3 moveDir = Vector3.zero;
        float speedFactor = 0f;
        bool boostInput = false;
        bool isMobile = Application.isMobilePlatform || 
                       UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;

        // Gamepad Dual-Stick & Triggers Support (Authentic Feeding Frenzy Console Style)
        bool hasGamepad = Gamepad.current != null;
        Vector2 leftStick = hasGamepad ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
        Vector2 rightStick = hasGamepad ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
        Vector2 stickInput = (leftStick.sqrMagnitude > rightStick.sqrMagnitude) ? leftStick : rightStick;

        bool gamepadBoostPressed = hasGamepad && (Gamepad.current.aButton.wasPressedThisFrame || 
                                                 Gamepad.current.leftTrigger.wasPressedThisFrame || 
                                                 Gamepad.current.leftShoulder.wasPressedThisFrame);

        bool gamepadPausePressed = hasGamepad && Gamepad.current.startButton.wasPressedThisFrame;
        if (gamepadPausePressed && GameManager.instance != null)
        {
            GameManager.instance.PlayPause();
        }

        // 0. Gamepad Dual Sticks (Left Stick or Right Stick)
        if (stickInput.sqrMagnitude > 0.04f)
        {
            _isMouseControl = false;
            moveDir = new Vector3(stickInput.x, stickInput.y, 0f);
            speedFactor = Mathf.Clamp01(moveDir.magnitude);
            moveDir.Normalize();

            if (Mathf.Abs(moveDir.x) > 0.15f)
            {
                float targetFacing = Mathf.Sign(moveDir.x);
                transform.localScale = new Vector3(targetFacing * currentBaseScale, currentBaseScale, 1f);
            }
            float speedMult = GetActiveSpeedMultiplier();
            _targetVelocity = moveDir * (moveSpeed * speedMult * speedFactor);
        }
        // 1. Mobile Joystick
        else if (MobileJoystick.Instance != null && MobileJoystick.Instance.InputDirection != Vector2.zero)
        {
            _isMouseControl = false;
            moveDir = new Vector3(MobileJoystick.Instance.InputDirection.x, MobileJoystick.Instance.InputDirection.y, 0f);
            speedFactor = Mathf.Clamp01(moveDir.magnitude);
            moveDir.Normalize();

            if (Mathf.Abs(moveDir.x) > 0.15f)
            {
                float targetFacing = Mathf.Sign(moveDir.x);
                transform.localScale = new Vector3(targetFacing * currentBaseScale, currentBaseScale, 1f);
            }
            float speedMult = GetActiveSpeedMultiplier();
            _targetVelocity = moveDir * (moveSpeed * speedMult * speedFactor);
        }
        // 2. Mouse Input (Feeding Frenzy: Fish swims toward mouse position, smoothly eases/slides to stop at cursor)
        else if (!isMobile && Mouse.current != null)
        {
            _isMouseControl = true;
            Vector2 inputScreenPos = Mouse.current.position.ReadValue();
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            if (float.IsFinite(inputScreenPos.x) && float.IsFinite(inputScreenPos.y) && _mainCamera != null)
            {
                if (mouseDelta.sqrMagnitude > 0.05f || !_hasInitializedTarget)
                {
                    Vector3 worldTarget = _mainCamera.ScreenToWorldPoint(new Vector3(inputScreenPos.x, inputScreenPos.y, Mathf.Abs(_mainCamera.transform.position.z - transform.position.z)));
                    _targetWorldPos = new Vector2(worldTarget.x, worldTarget.y);
                    _hasInitializedTarget = true;
                }

                // Instant Facing Flip
                float dx = _targetWorldPos.x - transform.position.x;
                if (dx < -0.05f)
                {
                    transform.localScale = new Vector3(-currentBaseScale, currentBaseScale, 1f);
                }
                else if (dx > 0.05f)
                {
                    transform.localScale = new Vector3(currentBaseScale, currentBaseScale, 1f);
                }
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                boostInput = true;
            }
        }
        // 3. Keyboard Input (WASD / Arrows)
        else if (Keyboard.current != null)
        {
            _isMouseControl = false;
            float kx = 0f;
            float ky = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) kx -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) kx += 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) ky += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) ky -= 1f;

            if (kx != 0f || ky != 0f)
            {
                moveDir = new Vector3(kx, ky, 0f).normalized;
                if (Mathf.Abs(kx) > 0.05f)
                {
                    float targetFacing = Mathf.Sign(kx);
                    transform.localScale = new Vector3(targetFacing * currentBaseScale, currentBaseScale, 1f);
                }
                float speedMult = GetActiveSpeedMultiplier();
                _targetVelocity = moveDir * (moveSpeed * speedMult);
            }
            else
            {
                _targetVelocity = Vector2.zero;
            }
        }
        else
        {
            _isMouseControl = false;
            _targetVelocity = Vector2.zero;
        }

        // Lock transform rotation
        if (playerGraphics != transform)
        {
            transform.rotation = Quaternion.identity;
        }

        // Visual Pitch / Tilt: Dynamic angular swimming curves based on real velocity
        if (playerGraphics != null && rb != null)
        {
            if (rb.linearVelocity.sqrMagnitude > 0.08f)
            {
                float facing = Mathf.Sign(transform.localScale.x);
                float forwardX = rb.linearVelocity.x * facing;
                float velY = rb.linearVelocity.y;
                float targetTilt = Mathf.Atan2(velY, Mathf.Max(0.5f, forwardX)) * Mathf.Rad2Deg;
                targetTilt = Mathf.Clamp(targetTilt, -tiltAngle, tiltAngle);

                float currentZ = playerGraphics.localEulerAngles.z;
                if (currentZ > 180f) currentZ -= 360f;
                float newZ = Mathf.MoveTowardsAngle(currentZ, targetTilt, tiltSpeed * 10f * Time.deltaTime);
                playerGraphics.localRotation = Quaternion.Euler(0f, 0f, newZ);
            }
            else
            {
                float currentZ = playerGraphics.localEulerAngles.z;
                if (currentZ > 180f) currentZ -= 360f;
                float newZ = Mathf.MoveTowardsAngle(currentZ, 0f, tiltSpeed * 8f * Time.deltaTime);
                playerGraphics.localRotation = Quaternion.Euler(0f, 0f, newZ);
            }
        }

        // Boost Input Check
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool mobileBoostPressed = MobileBoostButton.Instance != null && MobileBoostButton.Instance.WasPressedThisFrame;
        bool anyActionPressed = (boostInput || spacePressed || mobileBoostPressed || gamepadBoostPressed);
        
        if (anyActionPressed && boostTimer <= 0 && !IsDropping)
        {
            if (audioSource != null && boostStartClip != null && AudioSettingsManager.IsSfxEnabled)
                audioSource.PlayOneShot(boostStartClip, boostStartVolume);
            
            if (speedEffect != null)
            {
                speedEffect.Play();
            }

            boostTimer = boostDuration;
            currentSpeedMultiplier = boostMultiplier;

            // Instant burst impulse forward in current velocity or facing direction
            float facing = Mathf.Sign(transform.localScale.x);
            Vector2 boostDir = (rb != null && rb.linearVelocity.sqrMagnitude > 0.1f) 
                ? rb.linearVelocity.normalized 
                : new Vector2(facing, 0f);
            if (rb != null)
            {
                rb.linearVelocity = boostDir * (moveSpeed * boostMultiplier);
            }
        }
    }

    void FixedUpdate()
    {
        // FIX: Stop physics movement immediately if dead, hooked, dropping, paused, level completed or game over
        if (!isAlive || isHooked || IsDropping || isPaused || LevelManager.IsLevelCompleted || (GameManager.instance != null && GameManager.instance.IsGameOver))
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // River Blitz ability: automated dash routine moves transform directly
        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive && LevelManager.IsCurrentLakeLevel)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // Apply Physics Movement
        if (spikeBounceTimer > 0f)
        {
            spikeBounceTimer -= Time.fixedDeltaTime;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 16f * Time.fixedDeltaTime);
        }
        else if (_isMouseControl)
        {
            Vector2 toTarget = _targetWorldPos - rb.position;
            float dist = toTarget.magnitude;

            if (dist <= 0.04f)
            {
                // Reached cursor -> stop completely (zero sliding past cursor, zero jitter)
                rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 45f * Time.fixedDeltaTime);
            }
            else
            {
                // Maximum possible step velocity so fish NEVER overshoots the mouse cursor in one frame
                float maxStepSpeed = (dist - 0.015f) / Time.fixedDeltaTime;
                float speedMult = GetActiveSpeedMultiplier();
                float cruisingSpeed = moveSpeed * speedMult;

                // Organic arrival deceleration curve within 0.85m (slides for a bit, then stops)
                float slowRadius = 0.85f;
                float speedFactor = 1f;
                if (dist < slowRadius)
                {
                    float normDist = Mathf.Clamp01(dist / slowRadius);
                    speedFactor = normDist * (2f - normDist); // Smooth quadratic ease-out curve
                }

                float targetSpeed = Mathf.Min(cruisingSpeed * speedFactor, maxStepSpeed);
                Vector2 targetVelocity = toTarget.normalized * targetSpeed;

                // Snappy propulsion and braking
                float currentAcc = (targetSpeed < rb.linearVelocity.magnitude) ? 55f : swimAcceleration;
                rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVelocity, currentAcc * Time.fixedDeltaTime);
            }
        }
        else if (_targetVelocity.sqrMagnitude > 0.01f)
        {
            // Active swim propulsion while moving keys/joystick
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, _targetVelocity, swimAcceleration * Time.fixedDeltaTime);
        }
        else
        {
            // Underwater drag: slides for a moment then stops smoothly
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, waterDrag * Time.fixedDeltaTime);
            if (rb.linearVelocity.sqrMagnitude < 0.001f)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        // Apply Boundaries (Camera Viewport)
        if (_mainCamera != null && rb != null)
        {
            float camHeight = 2f * _mainCamera.orthographicSize;
            float camWidth = camHeight * _mainCamera.aspect;
            Vector3 camPos = _mainCamera.transform.position;
            
            float halfWidth = camWidth / 2f;
            float halfHeight = camHeight / 2f;
            float margin = 0.25f; 
            
            float minX = camPos.x - halfWidth + margin;
            float maxX = camPos.x + halfWidth - margin;
            float minY = camPos.y - halfHeight + margin;
            float maxY = camPos.y + halfHeight - margin;

            Vector2 pos = rb.position;
            Vector2 vel = rb.linearVelocity;

            // Horizontal bounds clamp
            if (pos.x < minX)
            {
                pos.x = minX;
                if (vel.x < 0f) vel.x = 0f;
            }
            else if (pos.x > maxX)
            {
                pos.x = maxX;
                if (vel.x > 0f) vel.x = 0f;
            }

            // Vertical bounds clamp
            if (pos.y < minY)
            {
                pos.y = minY;
                if (vel.y < 0f) vel.y = 0f;
            }
            else if (pos.y > maxY)
            {
                pos.y = maxY;
                if (vel.y > 0f) vel.y = 0f;
            }

            rb.position = pos;
            rb.linearVelocity = vel;
        }
    }

    private void UpdateCollision()
    {
        if (spriteRenderer == null) return;
        if (spriteRenderer.sprite == null) return;

        // "Game Style" / "Feeding Frenzy" Collision for Player
        // 1. Fit shape to sprite (Capsule is best for fish)
        // 2. Reduce size slightly (0.75f) - More forgiving for player than enemies

        // Check if we already have a CapsuleCollider2D
        CapsuleCollider2D capsule = GetComponent<CapsuleCollider2D>();
        
        // If we have other collider types (Box, Circle, Polygon), remove them to enforce Capsule
        Collider2D[] allCols = GetComponents<Collider2D>();
        foreach(var c in allCols)
        {
            if (c != capsule) Destroy(c);
        }

        // Add capsule if missing
        if (capsule == null)
        {
             capsule = gameObject.AddComponent<CapsuleCollider2D>();
        }

        // Note: Player collision is NOT a trigger if we want physical bumping, 
        // BUT current logic uses OnTriggerEnter / OnCollisionEnter interchangeably.
        // For "Feeding Frenzy" feel, Trigger is usually better to avoid "bumping" walls/fish 
        // unless we want physics interactions.
        // Current code handles both. Let's stick to Rigidbody mechanics (Collision) or Trigger?
        // Feeding Frenzy usually allows passing THROUGH fish you eat.
        // So Trigger is better for "eating", but maybe Collision for "walls"?
        // Let's set it to Trigger to ensure smooth movement through fish.
        // If user wants wall collision, we might need a composite or separate child collider.
        // For now, let's stick to what Fish.cs does: isTrigger = true.
        // However, if the player needs to stay in bounds via physics walls, this might be an issue.
        // The movement logic is Transform-based or Velocity-based? 
        // It's Velocity based (rb.velocity).
        // Let's keep isTrigger = false (Solid) so we don't fall out of world if there are walls?
        // Actually, existing code had no explicit setting in Start(), defaulting to Inspector.
        // Let's assume Trigger is safer for "eating" game feel.
        // If we want to eat fish, we must overlap them. Solid collision would "bounce" us off.
        capsule.isTrigger = false; // Keep it solid for now, but small. 
        // Wait, if it's solid, we bounce off fish!
        // Fish.cs sets isTrigger=true.
        // If Player is solid and Fish is Trigger, we can overlap! Perfect.
        // So Player = Solid (Physics), Fish = Trigger (Phantom).
        
        // Calculate Bounds
        Bounds b = spriteRenderer.sprite.bounds;
        Vector2 spriteSize = b.size;
        Vector2 spriteCenter = b.center;

        // Adjust for gfx scale relative to root
        // PlayerGraphics is a child.
        float scaleX = Mathf.Abs(playerGraphics.localScale.x);
        float scaleY = Mathf.Abs(playerGraphics.localScale.y);

        Vector2 finalSize = new Vector2(spriteSize.x * scaleX, spriteSize.y * scaleY);
        
        // Calculate Center Offset in Root Local Space
        Vector3 worldCenter = playerGraphics.TransformPoint(spriteCenter);
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

        // Aspect ratio check:
        // Ocean fish is elongated (aspect ratio ~1.9:1), while River fish is round (~1:1).
        float aspectRatio = finalSize.x / Mathf.Max(0.01f, finalSize.y);
        if (!LevelManager.IsCurrentLakeLevel || aspectRatio > 1.6f)
        {
            // Elongated Ocean Fish:
            // Extend horizontal reach forward to the snout/teeth so bites register naturally on contact.
            // Keep vertical profile sleek (0.65f) to match the torpedo body without clipping high/low hazards.
            float forgivenessX = 0.88f;
            float forgivenessY = 0.65f;
            float forwardShift = finalSize.x * 0.05f; // shifts collider forward towards the mouth
            capsule.size = new Vector2(finalSize.x * forgivenessX, finalSize.y * forgivenessY);
            capsule.offset = new Vector2(localCenter.x + forwardShift, localCenter.y);
        }
        else
        {
            // Standard / River Fish:
            float forgiveness = 0.75f;
            capsule.size = finalSize * forgiveness;
            capsule.offset = localCenter;
        }
        
        // Auto-Orientation
        if (finalSize.x >= finalSize.y)
            capsule.direction = CapsuleDirection2D.Horizontal;
        else
            capsule.direction = CapsuleDirection2D.Vertical;
    }

    // Update is called once per frame
    void Update()
    {
        // Handle XP Multiplier Timer
        if (xpMultiplierTimer > 0)
        {
            xpMultiplierTimer -= Time.deltaTime;
            GuiManager.instance.UpdateDoubleXpProgress(xpMultiplierTimer / xpMultiplierTotalDuration);

            if (xpMultiplierTimer <= 0)
            {
                xpMultiplier = 1f;
                // REMOVED TEXT: GuiManager.instance.ShowFloatingText(transform.position, "BinÞú Fmµta", Color.white);
                
                // Hide Double XP Icon
                GuiManager.instance.SetDoubleXpStatus(false);
            }
        }

        if (boostTimer > 0)
        {
            boostTimer -= Time.deltaTime;
            // Ease-out curve for energetic lunge burst smoothly settling back to normal
            float t = Mathf.Clamp01(boostTimer / boostDuration);
            currentSpeedMultiplier = Mathf.Lerp(1.0f, boostMultiplier, Mathf.Sqrt(t));

            if (boostTimer <= 0)
            {
                currentSpeedMultiplier = 1f;
                // Stop Particles
                if (speedEffect != null)
                {
                    speedEffect.Stop();
                }
            }
        }

        // Handle Poison / Sick Fish Timer
        if (poisonTimer > 0f)
        {
            poisonTimer -= Time.deltaTime;
            if (poisonTimer <= 0f && spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
            }
        }

        // Handle Ink Disorientation Timer
        if (inkDisorientTimer > 0f)
        {
            if (InkScreenOverlay.Instance != null && !InkScreenOverlay.IsBlindingActive)
            {
                inkDisorientTimer = 0f;
            }
            else
            {
                inkDisorientTimer -= Time.deltaTime;
            }
        }

        //Movement
        if (!isPaused && isAlive && !isHooked && !LevelManager.IsLevelCompleted && !(GameManager.instance != null && GameManager.instance.IsGameOver) && !(PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive && LevelManager.IsCurrentLakeLevel))
        {
            // Use mouse-based controls (original game style). Left click gives a short speed boost.
            HandleInput();
        }
        else
        {
            _targetVelocity = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
        
        // Level Management
        while (currentXp >= currentLevelXp && isAlive && !LevelManager.IsLevelCompleted)
        {
            if (Level >= maxLevel)
            {
                currentXp = currentLevelXp; // Keep bar full for final level
                LevelUp();
                break;
            }
            LevelUp();
        }
        //DebugCanvas.Set("Xp: " + currentXp.ToString() + " / " + currentLevelXp.ToString(), 2);
    }
    #endregion
    //==============================| Controls |========================//
    
    /// <summary>
    /// Run on death event
    /// </summary>
    public void Death(Sprite killerSprite = null, Color? killerColor = null, bool isSick = false)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameOver) return;
        if (!isAlive) return;

        if (killerSprite != null)
        {
            LastKillerSprite = killerSprite;
            LastKillerColor = killerColor ?? (isSick ? new Color(0.72f, 1f, 0.72f, 1f) : Color.white);
            LastKillerIsSick = isSick;
        }

        // Cancel any active special ability immediately on death
        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive)
        {
            PlayerAbilitySystem.Instance.EndAbility();
        }

        isHooked = false;
        caughtHazard = null;
        caughtHarpoon = null;
        Animator[] allAnimators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < allAnimators.Length; i++)
        {
            if (allAnimators[i] != null) allAnimators[i].enabled = true;
        }

        AudioClip targetDeathClip = GetDeathSoundClip();
        if (audioSource != null && targetDeathClip != null && AudioSettingsManager.IsSfxEnabled)
            audioSource.PlayOneShot(targetDeathClip, 1.0f);

        GameManager.instance.CameraShake(0.2f, 7f, 2.5f);

        EventManager.Trigger("playerDeath");
        EventManager.Trigger("GameLoss"); // Trigger Loss Message
        
        // FIX: Stop input and movement immediately
        isAlive = false;
        _targetVelocity = Vector2.zero;
        if(rb != null) rb.linearVelocity = Vector2.zero;

        // Immediately disable all colliders so dead invisible player cannot trigger physics or hazards
        Collider2D[] allCols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = false;
        }

        playerGraphics.gameObject.SetActive(false);
        Destroy(gameObject, 2.5f);

    }

    public AudioClip GetDeathSoundClip()
    {
        if (LevelManager.IsCurrentLakeLevel)
        {
            if (lakeDeathSound != null) return lakeDeathSound;
            #if UNITY_EDITOR
            lakeDeathSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/gameOver_Lake.wav");
            if (lakeDeathSound != null) return lakeDeathSound;
            #endif
            lakeDeathSound = Resources.Load<AudioClip>("gameOver_Lake");
            if (lakeDeathSound != null) return lakeDeathSound;
        }

        if (deathSound != null) return deathSound;
        #if UNITY_EDITOR
        deathSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/playerDie.ogg");
        if (deathSound != null) return deathSound;
        #endif
        deathSound = Resources.Load<AudioClip>("playerDie");
        return deathSound;
    }

    public AudioClip GetSickFishEatSoundClip()
    {
        if (sickFishEatSound != null) return sickFishEatSound;
        #if UNITY_EDITOR
        sickFishEatSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/player_Ate_Sick_Fish.wav");
        if (sickFishEatSound != null) return sickFishEatSound;
        #endif
        sickFishEatSound = Resources.Load<AudioClip>("player_Ate_Sick_Fish");
        return sickFishEatSound;
    }

    /// <summary>
    /// Cleanly halts player movement and physics when the result or game over modal appears.
    /// </summary>
    public void TerminateMovement()
    {
        _targetVelocity = Vector2.zero;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        StopSpeedEffect();
    }

    #region Hazard Hook Interaction

    /// <summary>
    /// Triggered when the player's mouth touches the fishing rod hazard bait.
    /// Locks player input, disables animators, snaps mouth to bait, and triggers the rod pulling upwards.
    /// </summary>
    public void OnHookedByHazard(Hazard hazard)
    {
        if (!isAlive || isHooked || hazard == null) return;

        // Immediately cancel any active special ability upon being hooked
        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive)
        {
            PlayerAbilitySystem.Instance.EndAbility();
        }

        if (!hazard.HookPlayer(this)) return;

        isHooked = true;
        caughtHazard = hazard;
        caughtHarpoon = null;
        hookElapsedTime = 0f;

        // 1. Stop input, velocity, boost and physics simulation
        _targetVelocity = Vector2.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false; // Kinematic mode so player doesn't fight physics/walls
        }
        currentSpeedMultiplier = 1f;
        boostTimer = 0f;
        StopSpeedEffect();

        // 2. Disable animator flapping while hooked
        Animator[] anims = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
        {
            if (anims[i] != null) anims[i].enabled = false;
        }

        // 3. Play visual struggle effects
        if (eatEffect != null)
        {
            eatEffect.Play();
        }

        // 4. Play hook bite / splash sound
        if (audioSource != null && AudioSettingsManager.IsSfxEnabled)
        {
            AudioClip biteClip = null;
            if (biteSounds != null && biteSounds.Length > 0)
            {
                biteClip = biteSounds[Random.Range(0, biteSounds.Length)];
            }
            if (biteClip != null) audioSource.PlayOneShot(biteClip, 1.0f);
        }

        // 5. Open mouth bite sprite if available
        if (spriteRenderer != null && eatSprite != null)
        {
            spriteRenderer.sprite = eatSprite;
        }

        // 6. Record starting state for smooth interpolation
        hookStartPos = transform.position;
        float startAngle = (playerGraphics != null) ? playerGraphics.localEulerAngles.z : 0f;
        if (startAngle > 180f) startAngle -= 360f;
        hookCurrentAngle = startAngle;
        hookAngleVelocity = 0f;
    }

    private void LateUpdate()
    {
        if (!isHooked) return;

        if (caughtHazard != null)
        {
            if (!caughtHazard.gameObject.activeInHierarchy || hookElapsedTime > 12.0f)
            {
                OnReeledOutOfWater();
                return;
            }

            if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

            hookElapsedTime += Time.deltaTime;

            // Compute local mouth offset in playerGraphics space
            Vector3 localMouth = Vector3.zero;
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Bounds b = spriteRenderer.sprite.bounds;
                // Snout / mouth is at the front (+X) and vertical center
                localMouth = new Vector3(b.max.x * 0.85f, b.center.y, 0f);
            }

            Vector3 baitPos = caughtHazard.GetBaitWorldPosition();

            // ----------------------------------------------------
            // Smoothly rotate to 90 degrees (Face-Up)
            // ----------------------------------------------------
            float targetAngle = 90f;
            hookCurrentAngle = Mathf.SmoothDampAngle(hookCurrentAngle, targetAngle, ref hookAngleVelocity, 0.20f);

            // Struggle wiggle around the Face-Up angle
            float struggleWiggle = Mathf.Sin(Time.time * 20f) * 6.5f;
            float displayAngle = hookCurrentAngle + struggleWiggle;

            if (playerGraphics != null)
            {
                playerGraphics.localRotation = Quaternion.Euler(0f, 0f, displayAngle);
            }

            // ----------------------------------------------------
            // Magnetism Snap & Position Pinning
            // ----------------------------------------------------
            Vector3 mouthWorld = (playerGraphics != null) ? playerGraphics.TransformPoint(localMouth) : transform.position;
            Vector3 rootToMouth = mouthWorld - transform.position;
            Vector3 targetRootPos = baitPos - rootToMouth;

            float snapDuration = 0.15f;
            if (hookElapsedTime < snapDuration)
            {
                float t = Mathf.Clamp01(hookElapsedTime / snapDuration);
                float smoothT = Mathf.Sin(t * Mathf.PI * 0.5f);
                transform.position = Vector3.Lerp(hookStartPos, targetRootPos, smoothT);
            }
            else
            {
                transform.position = targetRootPos;
            }
        }
        else if (caughtHarpoon != null)
        {
            if (!caughtHarpoon.gameObject.activeInHierarchy || hookElapsedTime > 12.0f)
            {
                OnReeledOutOfWater();
                return;
            }

            if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

            hookElapsedTime += Time.deltaTime;

            // Follow harpoon position directly in world space
            transform.position = caughtHarpoon.transform.position;

            // Align player rotation with harpoon angle + struggle tremor
            float harpoonAngle = caughtHarpoon.transform.eulerAngles.z;
            float struggleWiggle = Mathf.Sin(Time.time * 24f) * 4.5f;
            if (playerGraphics != null)
            {
                playerGraphics.localRotation = Quaternion.Euler(0f, 0f, harpoonAngle + struggleWiggle);
            }
        }
    }

    /// <summary>
    /// Called when the fishing rod or harpoon pulls the player all the way out of the water.
    /// Completes the catch and triggers the Game Over sequence.
    /// </summary>
    public void OnReeledOutOfWater()
    {
        if (!isAlive) return;

        Sprite killer = null;
        Color killerCol = Color.white;
        if (caughtHarpoon != null)
        {
            SpriteRenderer sr = caughtHarpoon.GetComponentInChildren<SpriteRenderer>();
            killer = sr?.sprite;
            if (sr != null) killerCol = sr.color;
        }
        if (killer == null && caughtHazard != null)
        {
            SpriteRenderer sr = caughtHazard.GetComponentInChildren<SpriteRenderer>();
            killer = sr?.sprite;
            if (sr != null) killerCol = sr.color;
        }

        isHooked = false;
        caughtHazard = null;
        caughtHarpoon = null;

        // Complete the catch: trigger game over
        Death(killer, killerCol, false);
    }

    #endregion

    void Eat(Fish fish)
    {
        if (fish == null || !fish.gameObject.activeSelf) return;

        if (audioSource != null)
        {
            AudioClip clip = null;
            if (fish != null && fish.IsSickFish) clip = GetSickFishEatSoundClip();
            else if (fish != null && fish.EatSfxOverride != null) clip = fish.EatSfxOverride;
            else if (biteSounds.Length > 0) clip = biteSounds[Random.Range(0, biteSounds.Length)];
            if (clip != null && AudioSettingsManager.IsSfxEnabled) audioSource.PlayOneShot(clip, 1.0f);
        }


        if (spriteRenderer != null && (eatSprite != null || halfBiteSprite != null))
        {
            StartCoroutine(BiteAnimation());
        }

        // Check if part of a fish school
        if (fish != null)
        {
            fish.OnEatenByPlayer();
        }

        bool wasSickFish = (fish != null && fish.IsSickFish);

        // Kill the referenced fish
        LevelManager.RecordFishEaten(fish.Level, fish.IsGoldenFish, wasSickFish);
        fish.Die();

        // Check for Sick Fish (applies poison debuff and points deduction without regressing XP progress)
        // Apex Frenzy Rule: During active ability (IsPlayerInvulnerable), frenzy cleanses toxins!
        // No poison debuff, no point deduction, no streak reset, awards normal XP.
        if (wasSickFish)
        {
            if (PlayerAbilitySystem.IsPlayerInvulnerable)
            {
                // Purified sick fish consumption during ultimate frenzy
                if (PlayerAbilitySystem.Instance != null)
                {
                    PlayerAbilitySystem.Instance.OnFishEaten(fish);
                }

                int purifiedXp = Mathf.RoundToInt((fish.Xp > 0 ? fish.Xp : 20) * xpMultiplier);
                currentXp += purifiedXp;
                score += (purifiedXp * Level);

                if (GuiManager.instance != null)
                {
                    GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
                    GuiManager.instance.ShowFloatingXp(fish.transform.position, purifiedXp);
                }
                return;
            }

            ApplyPoisonDebuff(3.5f); // Poison debuff from Sick Fish (Feeding Frenzy style)
            int penaltyPoints = (fish != null && fish.Xp > 0) ? fish.Xp : 18;
            score = Mathf.Max(0, score - (penaltyPoints * Level));

            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
                GuiManager.instance.ShowFloatingXp(fish.transform.position, penaltyPoints, true);
            }

            // Deplete streak counts back to 0 when eating a sick fish
            if (PlayerAbilitySystem.Instance != null)
            {
                PlayerAbilitySystem.Instance.ResetStreak();
            }

            return;
        }

        // Check for Golden Fish
        if (fish.IsGoldenFish)
        {
            ActivateXpMultiplier(2f, 10f); // 2x XP for 10 seconds
            
            // Show Special Banner for Golden Fish
            if (GuiManager.instance != null)
            {
                GuiManager.instance.ShowFloatingDoubleXp(fish.transform.position);
            }

            // Apply Ability System Streak & Energy for Golden Fish as well
            if (PlayerAbilitySystem.Instance != null)
            {
                PlayerAbilitySystem.Instance.OnFishEaten(fish);
            }

            // Golden fish itself gives 0 XP, just the buff
            return;
        }


        // Apply Ability System Streak & Energy
        if (PlayerAbilitySystem.Instance != null)
        {
            PlayerAbilitySystem.Instance.OnFishEaten(fish);
        }

        // Standardized XP based on fish type (Streak multiplier does NOT affect XP)
        int baseFishXp = (fish != null) ? fish.Xp : 20;
        int spikeBonusXp = (fish != null && fish.IsSpiked) ? 50 : 0;
        int finalXp = Mathf.RoundToInt((baseFishXp + spikeBonusXp) * xpMultiplier);

        currentXp += finalXp;

        score += (finalXp * Level);

        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
            GuiManager.instance.ShowFloatingXp(fish.transform.position, finalXp);
        }

    }

    /// <summary>
    /// Eats fish directly during active player abilities (Vortex Vacuum or River Apex Blitz).
    /// </summary>
    public void EatFromAbility(Fish fish)
    {
        if (fish == null || !fish.gameObject.activeSelf || fish.IsDead) return;
        Eat(fish);
        PlayEatEffect();
    }

    /// <summary>
    /// Calculates the world position of the player's mouth for suction vacuum physics.
    /// </summary>
    public Vector3 GetMouthPosition()
    {
        float dir = transform.localScale.x >= 0f ? 1f : -1f;
        float forwardOffset = LevelManager.IsCurrentLakeLevel ? 3.30f : 2.85f;
        float verticalOffset = LevelManager.IsCurrentLakeLevel ? -0.25f : -0.17f;
        Vector3 offset = new Vector3(dir * forwardOffset * currentBaseScale, verticalOffset * currentBaseScale, 0f);
        if (playerGraphics != null)
        {
            offset = playerGraphics.localRotation * offset;
        }
        return transform.position + offset;
    }

    /// <summary>
    /// Opens or closes mouth sprite during special ability executions.
    /// </summary>
    public void SetMouthOpen(bool open)
    {
        if (spriteRenderer == null) return;
        if (open)
        {
            if (eatSprite != null) spriteRenderer.sprite = eatSprite;
            else if (halfBiteSprite != null) spriteRenderer.sprite = halfBiteSprite;
        }
        else
        {
            if (idleSprite != null) spriteRenderer.sprite = idleSprite;
        }
    }

    /// <summary>
    /// Sets facing direction during automated blitz dashing.
    /// </summary>
    public void SetFacingDirection(bool faceRight)
    {
        float sign = faceRight ? 1f : -1f;
        transform.localScale = new Vector3(sign * currentBaseScale, currentBaseScale, 1f);
    }

    /// <summary>
    /// Awarded when the user eats all fish in a spawned school/group.
    /// </summary>
    public void AwardSchoolBonus(int bonusXp, Vector3 position)
    {
        int finalBonus = Mathf.RoundToInt(bonusXp * xpMultiplier);
        currentXp += finalBonus;
        score += (finalBonus * Level);

        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
            GuiManager.instance.ShowFloatingXp(position + Vector3.up * 0.9f, finalBonus);
        }
    }

    public void ActivateXpMultiplier(float multiplier, float duration)
    {
        xpMultiplier = multiplier;
        xpMultiplierTimer = duration;
        xpMultiplierTotalDuration = duration;
        
        // Notify UI to show Double XP Icon
        GuiManager.instance.SetDoubleXpStatus(true);
        
        // Limon Font Mapping: x -> ខ, * -> 8.
        // Solution: Use '2dg' which renders as '2ដង' (2 Times) in Khmer Limon.
        // REMOVED: Duplicate text call here, as it's already handled in Eat()
    }

    private void LevelUp()
    {
        if (Level >= maxLevel)
        {
            // Max Level Reached
            currentXp = currentLevelXp;
            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
            }

            if (!isPaused && isAlive && !LevelManager.IsLevelCompleted)
            {
                 LevelManager.CompleteCurrentLevel();
                 // Trigger Game Win event (modal will open after a brief delay, player continues moving until then)
                 EventManager.Trigger("GameWin"); 
            }
            return;
        }

        // Carry over excess XP instead of resetting to 0
        currentXp -= currentLevelXp;
        if (currentXp < 0) currentXp = 0;

        // Calculate requirement for the NEXT level
        // We use the current 'Level' (e.g., 1) to calculate what we need for Level 2?
        // Actually, if we are becoming Level 2, we should calculate based on Level 2?
        // Original code used 'Level' (1) to get 130.
        // If we use 'Level + 1' (2), we get 160.
        // Let's stick to the current Level index to maintain the curve start point.
        currentLevelXp = RequiredXpForLevel(Level, baseXpRequirement);
        
        Level++; // Increment level BEFORE updating GUI so we show progress into next level

        GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);

        // Scale logic: Use Manual Array for current environment
        float[] activeScales = GetCurrentLevelScales();
        if (activeScales != null && activeScales.Length >= Level)
        {
             float targetScale = activeScales[Level - 1];
             
             // Trigger Evolution Animation with juicy growth pop
             if (growthCoroutine != null) StopCoroutine(growthCoroutine);
             growthCoroutine = StartCoroutine(GrowthPulseAnimation(currentBaseScale, targetScale));
             
             // Play growth sound for level up feedback
             if (audioSource != null && AudioSettingsManager.IsSfxEnabled)
             {
                 AudioClip clip = (growSound != null) ? growSound : Resources.Load<AudioClip>("playerGrow");
                 if (clip != null) audioSource.PlayOneShot(clip, 1.0f);
             }
        }
        
        EventManager.Trigger<int>("onLevelUp", Level);

        // Option 1 (Feeding Frenzy style): Keep camera viewport fixed at default framing
        if (GameManager.instance != null)
        {
            GameManager.instance.CameraZoom(1f, 0.5f);
        }


        //Increase speed
        // User requested slower progression for higher levels (Level 1 is fastest)
        // Changed from 0.9f to 0.95f per user request ("too slow")
        // UPDATE: User reported movement feels "heavy/slow" at higher levels. 
        // Disabling speed reduction to keep gameplay responsive.
        // moveSpeed = moveSpeed * 0.95f;
        // turnSpeed = turnSpeed * 0.95f;
        moveSpeed = baseMoveSpeed;

        //increase trail - DISABLED
        //trail.widthMultiplier += 0.3f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleCollision(collision.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleCollision(other.gameObject);
    }

    private void HandleCollision(GameObject other)
    {
        if (!isAlive || isHooked || LevelManager.IsLevelCompleted) return;

        // Check for Hazard (Fishing Hook / Bait) - Lethal hook sequence catches player even during ability
        Hazard hazard = other.GetComponentInParent<Hazard>();
        if (hazard != null)
        {
            OnHookedByHazard(hazard);
            return;
        }

        // Invulnerability Safeguard: during 5s special abilities, ignore all lethal damage from regular predators
        if (PlayerAbilitySystem.IsPlayerInvulnerable)
        {
            Fish fishTarget = other.GetComponent<Fish>();
            if (fishTarget != null && !fishTarget.IsHooked)
            {
                if (fishTarget.Level <= Level)
                {
                    Eat(fishTarget);
                    PlayEatEffect();
                }
            }
            return;
        }

        // Optimization: Check tag OR Component
        // Fix: Removed rigid tag check. We check for Fish component directly to be more robust.
        // if (other.CompareTag("Enemy"))
        {
            Fish collidedFish = other.GetComponent<Fish>();
            if (collidedFish != null)
            {
                // If fish is already hooked by a fishing line, do not interact
                if (collidedFish.IsHooked) return;

                // Defense Rule for Spiked Pufferfish:
                // User requirement: "user or ai fish attemp to eat the spike puffer should died no just being pushed away"
                // Exception: When active ability is triggered (IsPlayerInvulnerable), frenzy overpowers spikes!
                if (collidedFish.IsSpiked)
                {
                    if (PlayerAbilitySystem.IsPlayerInvulnerable)
                    {
                        if (collidedFish.Level <= Level)
                        {
                            Eat(collidedFish);
                            PlayEatEffect();
                        }
                        return;
                    }

                    SpriteRenderer sr = collidedFish.GetComponentInChildren<SpriteRenderer>();
                    Sprite killerSp = sr != null ? sr.sprite : null;
                    Color killerCol = (sr != null) ? sr.color : (collidedFish.IsSickFish ? new Color(0.72f, 1f, 0.72f, 1f) : Color.white);
                    Death(killerSp, killerCol, collidedFish.IsSickFish);
                    return;
                }

                int fishLevel = collidedFish.Level;

                // Safety Check: Can I eat this?
                // Rules: 
                // 1. Must be Level <= My Level (Standard)
                // 2. Strict check to ensure we don't accidentally eat "Next Level" fish if logic fails elsewhere
                
                if (fishLevel > Level)
                {
                    if (collidedFish.IsGoldenFish || collidedFish.IsCuttlefish)
                    {
                        // Golden fish and Cuttlefish are NOT predator fish, so they never bite or kill the player.
                        if (collidedFish.IsCuttlefish)
                        {
                            var cf = collidedFish.GetComponent<Cuttlefish>();
                            if (cf != null) cf.TriggerInkAndJetEscape((Vector2)transform.position - (Vector2)collidedFish.transform.position, targetPlayer: this);
                        }
                        return;
                    }

                    // Fair Predator Bite Check:
                    // A predator fish eats with its mouth / front head.
                    // If the player touches the predator from behind its tail, the predator doesn't bite!
                    if (IsPredatorBiteContact(collidedFish, other))
                    {
                        SpriteRenderer sr = collidedFish.GetComponentInChildren<SpriteRenderer>();
                        Sprite killerSp = sr != null ? sr.sprite : null;
                        Color killerCol = (sr != null) ? sr.color : (collidedFish.IsSickFish ? new Color(0.72f, 1f, 0.72f, 1f) : Color.white);
                        Death(killerSp, killerCol, collidedFish.IsSickFish);
                    }
                }
                else
                {
                    //Eat
                    Eat(collidedFish);
                    PlayEatEffect();
                }
            }
        }
    }

    /// <summary>
    /// Checks if a predator fish made contact with its mouth/head facing towards the player.
    /// Prevents unfair deaths when player brushes past a large predator's tail or rear fin.
    /// </summary>
    private bool IsPredatorBiteContact(Fish predator, GameObject other)
    {
        if (predator == null) return true;

        // Facing direction of predator
        float facingDir = predator.IsFacingRight ? 1f : -1f;
        
        // Find closest point of contact on predator's collider to the player's center
        Collider2D predCol = other.GetComponent<Collider2D>();
        Vector2 contactPoint = predCol != null ? predCol.ClosestPoint(transform.position) : (Vector2)predator.transform.position;
        
        // Relative horizontal offset from predator's center to contact point in facing direction
        float relX = (contactPoint.x - predator.transform.position.x) * facingDir;
        
        // If the contact is at or ahead of the predator's center (head/mouth quadrant), it's a lethal bite!
        // We allow a slight -0.2f margin for body overlap. Tail contact (relX < -0.3f) is safe.
        return relX >= -0.25f;
    }

    /// <summary>
    /// Repels the player when bumping into an inflated spiky pufferfish.
    /// Provides tactile bounce impulse, sting/thud sound, and bubble particle feedback.
    /// </summary>
    public void BounceOffSpikes(Vector3 spikeSource)
    {
        if (Time.time - lastSpikeBounceTime < 0.25f) return;
        lastSpikeBounceTime = Time.time;

        Vector2 bounceDir = (Vector2)(transform.position - spikeSource);
        if (bounceDir.sqrMagnitude < 0.001f)
        {
            bounceDir = (rb != null && rb.linearVelocity.sqrMagnitude > 0.01f) ? -rb.linearVelocity.normalized : Vector2.up;
        }
        else
        {
            bounceDir = bounceDir.normalized;
        }

        // Apply physical bounce impulse
        float bounceForce = 8.5f;
        spikeBounceVelocity = bounceDir * bounceForce;
        spikeBounceTimer = 0.22f;
        if (rb != null)
        {
            rb.linearVelocity = spikeBounceVelocity;
        }

        // Emit bubble burst at contact point
        if (eatEffect != null)
        {
            Vector3 contactPoint = Vector3.Lerp(transform.position, spikeSource, 0.5f);
            eatEffect.transform.position = contactPoint;
            eatEffect.Emit(12);
        }

        // Subtle camera shake for tactile punch
        if (GameManager.instance != null)
        {
            GameManager.instance.CameraShake(0.12f, 3.5f, 1.2f);
        }
    }

    //==============================| Calculations  |========================//

    private int RequiredXpForLevel(int currentLevel, int xpForFirstLevel)
    {
        if(currentLevel < 1 || xpForFirstLevel < 1)
        {
            return baseXpRequirement;
        }
        float result;
        
        // SWITCHED TO EXPONENTIAL SCALING (User Request: "Feeding Frenzy Style")
        // Linear was making high levels too fast because fish XP scaled faster than the requirement.
        // New Formula: Base * (1.5 ^ Level)
        
        // Example with Base 50, Rate 0.5:
        // Lvl 1: 50 * 1.5 = 75
        // Lvl 2: 75 * 1.5 = 112
        // ...
        // Lvl 5: ~380 (Requires eating ~8 big fish instead of 2)
        
        result = (float)xpForFirstLevel * Mathf.Pow(1f + levelXpIncreasePercentage, currentLevel);

        return Mathf.RoundToInt(result);
    }

    IEnumerator BiteAnimation()
    {
        if (eatSprite != null && eatSprite != halfBiteSprite)
        {
            // 3-stage bite when separate fully-open mouth sprite exists
            if (halfBiteSprite != null)
            {
                spriteRenderer.sprite = halfBiteSprite;
                yield return new WaitForSeconds(0.04f);
            }

            spriteRenderer.sprite = eatSprite;
            yield return new WaitForSeconds(0.10f);

            if (halfBiteSprite != null)
            {
                spriteRenderer.sprite = halfBiteSprite;
                yield return new WaitForSeconds(0.04f);
            }
        }
        else if (halfBiteSprite != null)
        {
            // 2-stage bite when only closed and half-open mouth sprites exist
            spriteRenderer.sprite = halfBiteSprite;
            yield return new WaitForSeconds(0.14f);
        }

        // Return to closed mouth (only if special ability is NOT actively holding mouth open)
        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityActive)
        {
            if (spriteRenderer != null)
            {
                if (eatSprite != null) spriteRenderer.sprite = eatSprite;
                else if (halfBiteSprite != null) spriteRenderer.sprite = halfBiteSprite;
            }
        }
        else if (spriteRenderer != null && idleSprite != null)
        {
            spriteRenderer.sprite = idleSprite;
        }
    }

    public float[] GetCurrentLevelScales()
    {
        if (LevelManager.IsCurrentLakeLevel && riverLevelScales != null && riverLevelScales.Length > 0)
        {
            return riverLevelScales;
        }
        return levelScales;
    }

    private IEnumerator GrowthPulseAnimation(float fromScale, float toScale)
    {
        float duration = 0.32f;
        float elapsed = 0f;

        // Visual growth burst feedback
        PlayEatEffect();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth cubic ease-out growth curve with a tactile 15% pulse pop
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            float pulse = Mathf.Sin(t * Mathf.PI) * 0.15f;
            float s = Mathf.Lerp(fromScale, toScale, easeOut) + (pulse * toScale);

            currentBaseScale = s;
            float dir = Mathf.Sign(transform.localScale.x);
            transform.localScale = new Vector3(dir * s, s, 1f);

            yield return null;
        }

        currentBaseScale = toScale;
        float finalDir = Mathf.Sign(transform.localScale.x);
        transform.localScale = new Vector3(finalDir * toScale, toScale, 1f);

        UpdateCollision();
        growthCoroutine = null;
    }

    #region Cheat / Testing Helpers

    public void CheatLevelUp()
    {
        if (Level < maxLevel)
        {
            LevelUp();
        }
    }

    public void CheatLevelDown()
    {
        if (Level > 1)
        {
            Level--;
            currentXp = 0;
            currentLevelXp = RequiredXpForLevel(Level, baseXpRequirement);
            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
            }
            float[] activeScales = GetCurrentLevelScales();
            if (activeScales != null && activeScales.Length >= Level)
            {
                currentBaseScale = activeScales[Level - 1];
            }
            float dir = Mathf.Sign(transform.localScale.x);
            transform.localScale = new Vector3(dir * currentBaseScale, currentBaseScale, 1f);
            UpdateCollision();
            EventManager.Trigger<int>("onLevelUp", Level);
        }
    }

    public void CheatAddScore(int amount = 1000)
    {
        score += amount;
    }

    #endregion
}