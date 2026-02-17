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
    private float moveSpeed = 5f;

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
    [SerializeField]
    private AudioClip deathSound;
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

    [Header("Eat Effects")]
    [SerializeField]
    private ParticleSystem eatEffect;
    [Tooltip("Assign multiple bubble textures here for variety")]
    [SerializeField]
    private Texture2D[] eatBubbleVariants;

    [Header("Visuals")]
    [SerializeField]
    private Sprite idleSprite; // Closed mouth
    [SerializeField]
    private Sprite eatSprite; // Open mouth
    
    [Header("Manual Level Scaling")]
    [Tooltip("Define exact scale for each level (Index 0 = Level 1, Index 1 = Level 2, etc.)")]
    [SerializeField]
    private float[] levelScales = new float[] { 0.5f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f };

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
    
    [SerializeField]
    private int currentLevelXp = 100;
    private int currentXp = 0;

    public int Level { get; private set; } = 1;

    private int score = 0;

    //====================| Mind Control Hazard

    //====================| Control Input
    //Direction for keyboard controls
    // Boost fields
    [Header("Boost")]
    [SerializeField]
    private float boostMultiplier = 1.6f;
    [SerializeField]
    private float boostDuration = 0.6f;
    private float currentSpeedMultiplier = 1f;
    private float boostTimer = 0f;

    // XP Multiplier (Golden Fish Bonus)
    private float xpMultiplier = 1f;
    private float xpMultiplierTimer = 0f;
    private float xpMultiplierTotalDuration = 1f;
    
    // Visuals
    private float currentBaseScale = 1f;
    private float lastBubbleScaleApplied = -1f;
    private SpriteRenderer spriteRenderer;
    private float baseMoveSpeed;
    
    // Physics & Movement
    private Rigidbody2D rb;
    private Vector2 _moveInput; // Raw input direction * speed factor
    private Vector2 _smoothVelocity; // For SmoothDamp
    [Header("Movement Polish")]
    [SerializeField] private float smoothTime = 0.03f; // Reduced for snappier response (was 0.05f)
    [SerializeField] private float tiltAngle = 20f; // Max tilt angle in degrees
    [SerializeField] private float tiltSpeed = 10f; // How fast we tilt
    
    // Optimization: Cache the generated material to prevent lag on spawn
    private static Material _cachedGeneratedMaterial;
    
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
    }

    void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // Start is called before the first frame update
    void Start()
    {
        // USER REQUEST: "Feeding Frenzy" Style Progression
        // Easy start, but gets significantly harder at higher levels.
        baseXpRequirement = 80;
        levelXpIncreasePercentage = 0.7f;

        // FIX: Ensure MaxLevel is at least 6 (User reported Level 6 issues)
        if (maxLevel < 6)
        {
            maxLevel = 6;
        }

        // FIX: Ensure levelScales array matches MaxLevel
        if (levelScales == null || levelScales.Length < maxLevel)
        {
            System.Array.Resize(ref levelScales, maxLevel);
            
            // Fill new slots if they were empty (0)
            // Default pattern: 0.5, 0.7, 0.9, 1.1, 1.3, 1.5
            // Formula: 0.5 + (Index * 0.2)
            for (int i = 0; i < levelScales.Length; i++)
            {
                if (levelScales[i] == 0f)
                {
                    levelScales[i] = 0.5f + (i * 0.2f);
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

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        // Ensure AudioListener is on the player or camera
        audioListener = GetComponent<AudioListener>();
        if (audioListener == null)
        {
            // Usually listener is on Camera, but for 2D top-down where player is center, 
            // having it on player or camera (which follows player) is fine.
            // Let's check Main Camera first
            if (Camera.main != null && Camera.main.GetComponent<AudioListener>() == null)
            {
                 Camera.main.gameObject.AddComponent<AudioListener>();
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
        if (levelScales != null && levelScales.Length >= Level)
        {
             currentBaseScale = levelScales[Level - 1];
             float scale = currentBaseScale;
             transform.localScale = new Vector3(scale, scale, 1);
        }

        baseMoveSpeed = moveSpeed;
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

    // Unified Input (Mouse & Touch & Joystick)
    void HandleInput()
    {
        Vector3 moveDir = Vector3.zero;
        float speedFactor = 0f;
        bool hasInput = false;
        bool boostInput = false;
        // Robust Mobile Check (Matches MobileJoystick logic)
        // Fix: Removed Input.touchSupported to prevent false positives on Desktop with touch screens
        bool isMobile = Application.isMobilePlatform || 
                       UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;

        // 0. Mobile Joystick (New)
        if (MobileJoystick.Instance != null && MobileJoystick.Instance.InputDirection != Vector2.zero)
        {
            moveDir = new Vector3(MobileJoystick.Instance.InputDirection.x, MobileJoystick.Instance.InputDirection.y, 0f);
            speedFactor = moveDir.magnitude;
            moveDir.Normalize();
            hasInput = true;
        }

        // 2. Mouse Input
        else if (!hasInput && !isMobile && Mouse.current != null)
        {
            Vector2 inputScreenPos = Mouse.current.position.ReadValue();
            
            // Fix: Check for valid screen coordinates to prevent "Screen position out of view frustum" error
            if (!float.IsFinite(inputScreenPos.x) || !float.IsFinite(inputScreenPos.y))
            {
                _moveInput = Vector2.zero;
                return;
            }

            if (_mainCamera != null)
            {
                Vector3 worldTarget = _mainCamera.ScreenToWorldPoint(new Vector3(inputScreenPos.x, inputScreenPos.y, Mathf.Abs(_mainCamera.transform.position.z - transform.position.z)));
                worldTarget.z = transform.position.z;

                Vector3 diff = worldTarget - transform.position;
                float distance = diff.magnitude;
                
                float stopDistance = 0.2f; 
                if (distance >= stopDistance)
                {
                    float speedRampRadius = 4.0f; 
                    speedFactor = Mathf.Clamp01((distance - stopDistance) / speedRampRadius);
                    moveDir = diff.normalized;
                    hasInput = true;
                }
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                boostInput = true;
            }
        }

        // Set Input
        if (hasInput)
        {
            _moveInput = moveDir * speedFactor;
        }
        else
        {
            _moveInput = Vector2.zero;
        }

        // 4. Visuals: Flip Left/Right & Tilt (Based on Physics Velocity)
        if (rb.linearVelocity.magnitude > 0.1f)
        {
            // Flip: Only update facing direction if we have significant X movement
            // This prevents flipping when moving purely vertically (fixes jitter)
            if (Mathf.Abs(rb.linearVelocity.x) > 0.1f)
            {
                float directionSign = Mathf.Sign(rb.linearVelocity.x);
                
                // Calculate final scale
                float finalScale = currentBaseScale;
                Vector3 newScale = new Vector3(finalScale, finalScale, 1f);
                
                newScale.x = directionSign * finalScale; // Keep Magnitude, Flip Sign
                transform.localScale = newScale;
            }

            // Lock Rotation (No upside down) if we have a separate graphics object
            if (playerGraphics != transform)
            {
                transform.rotation = Quaternion.identity;
            }
            
            // Apply Tilt to Graphics ("Feeding Frenzy" Style)
            if (playerGraphics != null)
            {
                // Calculate target tilt based on Y velocity
                // Tilt Up if moving Up, Down if moving Down
                float yVel = rb.linearVelocity.y;
                float targetTilt = Mathf.Clamp(yVel * 3f, -tiltAngle, tiltAngle); // *3 multiplier for responsiveness
                
                float currentZ = playerGraphics.localEulerAngles.z;
                float newZ = Mathf.LerpAngle(currentZ, targetTilt, tiltSpeed * Time.deltaTime);
                
                playerGraphics.localRotation = Quaternion.Euler(0, 0, newZ);
            }
        }
        else
        {
            // Reset tilt when stopped
            if (playerGraphics != null)
            {
                float currentZ = playerGraphics.localEulerAngles.z;
                float newZ = Mathf.LerpAngle(currentZ, 0f, tiltSpeed * Time.deltaTime);
                playerGraphics.localRotation = Quaternion.Euler(0, 0, newZ);
            }
        }

        // else block removed (Animation cleanup)

        // 5. Boost
        // FIX: Ensure single-frame press logic for ALL inputs (Keyboard, Mouse, Mobile)
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        
        // Mobile Boost: Using WasPressedThisFrame logic from MobileBoostButton script
        bool mobileBoostPressed = MobileBoostButton.Instance != null && MobileBoostButton.Instance.WasPressedThisFrame;
        
        // Mouse Boost: Already checked via wasPressedThisFrame above
        
        if ((boostInput || spacePressed || mobileBoostPressed) && boostTimer <= 0)
        {
            // Play Start Sound (ONCE)
            if (audioSource != null && boostStartClip != null)
                audioSource.PlayOneShot(boostStartClip, boostStartVolume);
            
            // Play Particles
            if (speedEffect != null)
            {
                speedEffect.Play();
            }

            boostTimer = boostDuration;
            currentSpeedMultiplier = boostMultiplier;
        }
    }

    void FixedUpdate()
    {
        // FIX: Stop physics movement immediately if dead (User Request)
        if (!isAlive)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isPaused)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Apply Physics Movement
        float targetSpeed = moveSpeed * currentSpeedMultiplier;
        Vector2 targetVelocity = _moveInput * targetSpeed;
        
        rb.linearVelocity = Vector2.SmoothDamp(rb.linearVelocity, targetVelocity, ref _smoothVelocity, smoothTime);

        // Apply Boundaries (Camera Viewport)
        if (_mainCamera != null)
        {
            float camHeight = 2f * _mainCamera.orthographicSize;
            float camWidth = camHeight * _mainCamera.aspect;
            Vector3 camPos = _mainCamera.transform.position;
            
            float halfWidth = camWidth / 2f;
            float halfHeight = camHeight / 2f;

            // Margin to keep player fully on screen (approx half sprite width)
            float margin = 0.5f; 
            
            float minX = camPos.x - halfWidth + margin;
            float maxX = camPos.x + halfWidth - margin;
            float minY = camPos.y - halfHeight + margin;
            float maxY = camPos.y + halfHeight - margin;

            float clampedX = Mathf.Clamp(rb.position.x, minX, maxX);
            float clampedY = Mathf.Clamp(rb.position.y, minY, maxY);

            if (rb.position.x != clampedX || rb.position.y != clampedY)
            {
                rb.position = new Vector2(clampedX, clampedY);
                // Kill velocity into the wall
                Vector2 newVel = rb.linearVelocity;
                if (Mathf.Abs(rb.position.x - clampedX) < 0.01f) newVel.x = 0;
                if (Mathf.Abs(rb.position.y - clampedY) < 0.01f) newVel.y = 0;
                rb.linearVelocity = newVel;
            }
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

        // Apply Forgiveness (0.75f) - "Feeding Frenzy" feel (Player gets extra advantage)
        // Enemies are 0.85f. Player is 0.75f (Harder to get hit).
        float forgiveness = 0.75f;
        
        capsule.size = finalSize * forgiveness;
        capsule.offset = localCenter;
        
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


        //Movement
        if( !isPaused && isAlive )
        {
            // Use mouse-based controls (original game style). Left click gives a short speed boost.
            HandleInput();
        }
        
        //Level Management
        if( currentXp >= currentLevelXp)
        {
            //Level up
            LevelUp();
        }
        //DebugCanvas.Set("Xp: " + currentXp.ToString() + " / " + currentLevelXp.ToString(), 2);

    }
    #endregion
    //==============================| Controls |========================//
    
    /// <summary>
    /// Run on death event
    /// </summary>
    public void Death()
    {
        if (GameManager.instance != null && GameManager.instance.IsGameOver) return;
        if (!isAlive) return;
        if (audioSource != null && deathSound != null)
            audioSource.PlayOneShot(deathSound);

        GameManager.instance.CameraShake(0.2f, 7f, 2.5f);

        EventManager.Trigger("playerDeath");
        EventManager.Trigger("GameLoss"); // Trigger Loss Message
        EventManager.Trigger<int>("GameOver", score);
        
        // FIX: Stop input and movement immediately
        isAlive = false;
        _moveInput = Vector2.zero; 
        if(rb != null) rb.linearVelocity = Vector2.zero;

        playerGraphics.gameObject.SetActive(false);
        Destroy(gameObject, 1f);

    }

    void Eat(Fish fish)
    {
        if (fish == null || !fish.gameObject.activeSelf) return;

        if (audioSource != null)
        {
            AudioClip clip = null;
            if (fish != null && fish.EatSfxOverride != null) clip = fish.EatSfxOverride;
            else if (biteSounds.Length > 0) clip = biteSounds[Random.Range(0, biteSounds.Length)];
            if (clip != null) audioSource.PlayOneShot(clip);
        }


        if (spriteRenderer != null && eatSprite != null)
        {
            StartCoroutine(BiteAnimation());
        }

        //Kill the referenced fish

        fish.Die();

        // Check for Golden Fish
        if (fish.IsGoldenFish)
        {
            ActivateXpMultiplier(2f, 10f); // 2x XP for 10 seconds
            // Play Special Sound
            if (audioSource != null && boostStartClip != null) audioSource.PlayOneShot(boostStartClip, 1.2f);
            
            // Show Special Text for Golden Fish (Khmer Font Mapping: RtImas)
            GuiManager.instance.ShowFloatingText(fish.transform.position, "RtImas", new Color(1f, 0.8f, 0f, 1f));

            // Golden fish itself gives 0 XP, just the buff
            return;
        }


        // Apply Multiplier
        int finalXp = Mathf.RoundToInt(fish.Xp * xpMultiplier);

        currentXp += finalXp;

        score += (finalXp * Level);

        GuiManager.instance.SetXp(currentXp, currentLevelXp, Level, maxLevel);
        
        // Show Floating XP Text
        // Updated to Khmer format: "÷10 BinÞú" (where ÷ is + in that font)
        // User requested NO prefix change for x2, just color change.
        string prefix = "÷";
        Color xpColor = (xpMultiplier > 1f) ? new Color(1f, 0.8f, 0f, 1f) : Color.white; // Gold color for x2
        GuiManager.instance.ShowFloatingText(fish.transform.position, prefix + finalXp + " BinÞú", xpColor);

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
        // GuiManager.instance.ShowFloatingText(transform.position, "RtImas 2dg BinÞú ¡", new Color(1f, 0.8f, 0f, 1f));
    }


    private void LevelUp()
    {
        if (Level >= maxLevel)
        {
            // Max Level Reached
            
            // Check if we have filled the bar for the final level
            // Since LevelUp() is called when currentXp >= currentLevelXp, 
            // being here means the bar is effectively full.

            if (!isPaused && isAlive)
            {
                 GameManager.instance.TriggerGameWin();
                 isAlive = false;
                 _moveInput = Vector2.zero;
                 if (rb != null) rb.linearVelocity = Vector2.zero;
                 StopSpeedEffect();
                 
                 // Trigger Game Over event so UI knows?
                 EventManager.Trigger("GameWin"); // Trigger Win Message
                 EventManager.Trigger<int>("GameOver", score);
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

        // Scale logic: Use Manual Array
        if (levelScales != null && levelScales.Length >= Level)
        {
             // Trigger Evolution Animation
             float targetScale = levelScales[Level - 1];
             
             // Immediate Scale Update (No Animation)
             transform.localScale = new Vector3(targetScale, targetScale, 1f);
             currentBaseScale = targetScale;
             
             // Play sound for feedback
             if (audioSource != null && boostStartClip != null) 
                 audioSource.PlayOneShot(boostStartClip, 1.0f);
        }
        
        EventManager.Trigger<int>("onLevelUp", Level);

        float cameraMultiplier = 1f + ((Level - 1) * 0.10f);
        GameManager.instance.CameraZoom(cameraMultiplier, 0.5f);


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
        if (!isAlive) return;

        // Check for Hazard (Hook/Line) - Instant Death
        if (other.GetComponent<Hazard>() != null)
        {
            Death();
            return;
        }

        // Optimization: Check tag OR Component
        // Fix: Removed rigid tag check. We check for Fish component directly to be more robust.
        // if (other.CompareTag("Enemy"))
        {
            Fish collidedFish = other.GetComponent<Fish>();
            if (collidedFish != null)
            {
                int fishLevel = collidedFish.Level;

                // Safety Check: Can I eat this?
                // Rules: 
                // 1. Must be Level <= My Level (Standard)
                // 2. Strict check to ensure we don't accidentally eat "Next Level" fish if logic fails elsewhere
                
                if (fishLevel > Level)
                {
                    Death();
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
        // Swap to open mouth
        spriteRenderer.sprite = eatSprite;
        
        // Wait
        yield return new WaitForSeconds(0.15f);
        
        // Swap back to closed mouth (if not evolving)
        if (spriteRenderer != null && idleSprite != null)
        {
            spriteRenderer.sprite = idleSprite;
        }
    }
}
