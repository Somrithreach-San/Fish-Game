using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Fish : MonoBehaviour
{
    private bool hasError = false;
    private Transform gfx;
    private Sprite image;

    [SerializeField]
    private int level = 0;
    public int Level => level;

    [SerializeField]
    private int xp = 0;
    // Standardized XP based on Level (Level 1 is 8 XP so eating small minnows doesn't cause instant skips)
    public int Xp
    {
        get
        {
            if (xp <= 0)
            {
                switch (level)
                {
                    case 1: return 8;
                    case 2: return 18;
                    case 3: return 32;
                    case 4: return 50;
                    case 5: return 75;
                    case 6: return 110;
                    default: return Mathf.Max(8, level * 16);
                }
            }
            return xp;
        }
    }
    [SerializeField]
    private AudioClip eatSfxOverride;
    public AudioClip EatSfxOverride => eatSfxOverride;

    [SerializeField]
    private bool isFacingRight = true;
    public bool IsFacingRight { get => isFacingRight; set => isFacingRight = value; }

    [SerializeField]
    private float speed = 1f;
    public float Speed => speed;

    [Header("Special Attributes")]
    [SerializeField]
    private bool isGoldenFish = false; // Is this the rare "Golden" fish?
    public bool IsGoldenFish => isGoldenFish;

    [Header("Fishing Rod Hook State")]
    private bool isHooked = false;
    public bool IsHooked => isHooked;
    private Hazard caughtHazard = null;
    private HarpoonHazard caughtHarpoon = null;
    private Vector3 hookStartPos;
    private float hookCurrentAngle = 0f;
    private float hookAngleVelocity = 0f;
    private float hookElapsedTime = 0f;

    [SerializeField]
    private GameObject goldenParticlePrefab; // Assign in prefab if needed, or we create one
    private bool goldenStatusActive = false;

    // Schooling
    public FishSchool school;
    public Vector2 formationOffset;
    public FishSchool GroupSchool { get; set; }
    private bool isEaten = false;
    public float SpawnTime { get; private set; }
    public bool IsDead { get; set; } = false;

    [Header("Mouth Bite Animation")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openSprite;
    public bool HasBiteSprites => openSprite != null;
    private bool isBiting = false;
    private float biteTimer = 0f;
    private float biteDuration = 0.30f;

    [Header("Bobbing Animation")]
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobAmount = 0.1f;
    private float randomBobOffset;
    private float defaultYLocal;

    [Header("Pufferfish Settings")]
    [SerializeField] private bool isPufferFish = false;
    public bool IsPufferFish => isPufferFish;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite spikeSprite;
    [SerializeField] private float spikeDetectRadius = 8.5f;
    public float SpikeDetectRadius { get => spikeDetectRadius; set => spikeDetectRadius = value; }
    [SerializeField] private float spikeDuration = 3.5f;
    [SerializeField] private float deflateCooldown = 3.0f;
    [SerializeField] private float spikeScaleMultiplier = 1.0f;
    private bool isSpiked = false;
    public bool IsSpiked => isSpiked;
    private float spikeActiveTimer = 0f;
    private float deflateCooldownTimer = 0f;
    private SpriteRenderer cachedGfxSr;
    private Vector3 gfxDefaultScale = Vector3.one;
    private Coroutine spikeTransitionCoroutine;

    [Header("Sick Fish Settings")]
    [SerializeField] private bool isSickFish = false;
    public bool IsSickFish => isSickFish;
    [SerializeField] private Sprite sickFishSprite;
    private bool defaultSickConfig = false;
    private float poisonTimer = 0f;
    public bool IsPoisoned => poisonTimer > 0f;

    [Header("Cuttlefish Settings")]
    [SerializeField] private bool isCuttlefish = false;
    public bool IsCuttlefish => isCuttlefish;
    public Transform GfxTransform => gfx != null ? gfx : transform;

    public void SetCuttlefishStatus(bool active)
    {
        isCuttlefish = active;
    }

    private Vector3 initialScale;
    private bool initialized = false;

    // OPTIMIZATION: Static list to track all active fish without FindObjectsOfType
    public static List<Fish> AllFish = new List<Fish>();
    private GameManager cachedGameManager;
    private Transform cachedPlayerTransform;

    // Food Chain Logic Vars
    private List<Collider2D> collisionBuffer = new List<Collider2D>(8);
    private ContactFilter2D contactFilter;
    private Collider2D myCollider;
    private float eatCheckTimer = 0f;

    // Particles
    private ParticleSystem eatEffect;
    private Material bubbleMaterial;
    private Texture2D bubbleTexture;
    private static Material sharedBubbleMaterial;
    private static Texture2D sharedBubbleTexture;
    private static Shader cachedParticleShader;

    public void InitializeParticles(Material mat, Texture2D tex)
    {
        this.bubbleMaterial = mat;
        this.bubbleTexture = tex;
        if (mat != null) sharedBubbleMaterial = mat;
        if (tex != null) sharedBubbleTexture = tex;
        
        CreateEatParticles();
    }

    public void EnsureEatParticles()
    {
        if (eatEffect != null) return;
        CreateEatParticles();
    }

    private void CreateEatParticles()
    {
        if (eatEffect != null) return;

        // Auto-resolve bubble material and texture if not already assigned
        if (bubbleMaterial == null)
        {
            if (sharedBubbleMaterial != null)
            {
                bubbleMaterial = sharedBubbleMaterial;
            }
            else
            {
                PlayerController pc = FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    bubbleMaterial = pc.BubbleMaterial;
                    bubbleTexture = pc.BubbleTexture;
                    if (bubbleMaterial != null) sharedBubbleMaterial = bubbleMaterial;
                    if (bubbleTexture != null) sharedBubbleTexture = bubbleTexture;
                }

                if (bubbleMaterial == null)
                {
                    Material[] allMats = Resources.FindObjectsOfTypeAll<Material>();
                    foreach (Material m in allMats)
                    {
                        if (m != null && m.name.Contains("bubbleParticleMat"))
                        {
                            bubbleMaterial = m;
                            sharedBubbleMaterial = m;
                            break;
                        }
                    }
                }
            }
        }

        if (bubbleTexture == null && sharedBubbleTexture != null)
        {
            bubbleTexture = sharedBubbleTexture;
        }

        GameObject bubbles = new GameObject("EatBubbles");
        bubbles.transform.SetParent(transform, false);
        bubbles.transform.localPosition = Vector3.zero;

        eatEffect = bubbles.AddComponent<ParticleSystem>();
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();

        // Main Settings - Identical to PlayerController eat bubble particles
        var main = eatEffect.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 50;

        // Emission
        var emission = eatEffect.emission;
        emission.rateOverTime = 0;

        // Shape (Cone/Circle)
        var shape = eatEffect.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.angle = 25f;

        // Noise
        var noise = eatEffect.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 1f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Velocity over Lifetime (Upward with lateral drift)
        var vel = eatEffect.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.radial = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.space = ParticleSystemSimulationSpace.World;

        // Color (Transparent fade)
        var col = eatEffect.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.7f, 0.0f), new GradientAlphaKey(0.5f, 0.8f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        // Material
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
        }
        else if (bubbleTexture != null)
        {
             if (cachedParticleShader == null)
             {
                 cachedParticleShader = Shader.Find("Particles/Standard Unlit");
                 if (cachedParticleShader == null) cachedParticleShader = Shader.Find("Sprites/Default");
             }
             
             if (cachedParticleShader != null)
             {
                 Material mat = new Material(cachedParticleShader);
                 mat.mainTexture = bubbleTexture;
                 renderer.material = mat;
             }
        }
        
        renderer.sortingOrder = 6; // Above fish
    }

    public void PlayEatEffect()
    {
        EnsureEatParticles();
        if (eatEffect != null)
        {
            // Burst 2 to 4 bubbles, identical to PlayerController
            int count = Random.Range(2, 4);
            eatEffect.Emit(count);
        }
    }

    public void OnEatenByPlayer()
    {
        isEaten = true;
        if (GroupSchool != null)
        {
            GroupSchool.OnFishEatenByPlayer(this, transform.position);
        }
    }

    public void OnEatenByPredator()
    {
        isEaten = true;
        if (GroupSchool != null)
        {
            GroupSchool.OnFishEatenByPredator(this);
        }
    }

    public void Die()
    {
        DespawnSelf();
    }
    
    public void DespawnSelf()
    {
        if (IsDead) return;
        IsDead = true;
        if (!isEaten && GroupSchool != null)
        {
            GroupSchool.OnFishDespawned(this);
        }
        isEaten = false;

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Despawn(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        IsDead = false;
        AllFish.Add(this);
        SpawnTime = Time.time;
        
        // Reset state for pooling
        school = null;
        GroupSchool = null;
        isEaten = false;
        formationOffset = Vector2.zero;
        ResetHookState();
        ResetSpikeState();
        ResetSickState();
        ResetBiteState();
        ConfigureAiFishRendering();
        ConfigurePufferfish();
        
        var stateCtrl = GetComponent<StateController>();
        if (stateCtrl != null) stateCtrl.enabled = false;
        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;
        
        // Restore scale in case it was shrunk by a clam or death animation
        if (initialized && initialScale != Vector3.zero)
        {
            transform.localScale = initialScale;
        }
    }

    private void OnDisable()
    {
        AllFish.Remove(this);
        ResetHookState();
        ResetSpikeState();
        ResetSickState();
        ResetBiteState();
    }

    private void Start()
    {
        // Cache references to avoid repeated singleton access
        cachedGameManager = GameManager.instance;
        if (cachedGameManager != null)
        {
            // Note: Player might respawn, so we might need to re-fetch if null
            // But for Start, this is good.
            if (cachedGameManager.playerGameObject != null)
                cachedPlayerTransform = cachedGameManager.playerGameObject.transform;
        }

        ConfigurePufferfish();

        if (isSickFish)
        {
            SetSickStatus(true);
        }

        // Ensure collider is set up and cache it immediately
        UpdateCollision();

        // Initialize Bobbing
        randomBobOffset = Random.Range(0f, 100f);
        if (gfx != null && gfx != transform)
        {
            defaultYLocal = gfx.localPosition.y;
        }

        // Auto-activate if configured in prefab
        if (isGoldenFish && !goldenStatusActive)
        {
            SetGoldenStatus(true);
        }
        var ai = GetComponent<FishAI>();
        if (ai == null) ai = gameObject.AddComponent<FishAI>();
        ai.enabled = true;
        
        // Disable legacy AI scripts to prevent them from constantly overriding FishAI's movement (e.g. StateController setting velocity to wander)
        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;
        var stateCtrl = GetComponent<StateController>();
        if (stateCtrl != null) stateCtrl.enabled = false;
        
        SetupRigidbody();

        // Setup Manual Collision Check (Bypasses Physics Matrix "Enemy vs Enemy" ignore)
        // CRITICAL FIX: Explicitly get CapsuleCollider2D to avoid grabbing a destroyed collider
        myCollider = GetComponent<CapsuleCollider2D>();
        
        contactFilter = new ContactFilter2D();
        contactFilter.useTriggers = true; 
        contactFilter.useLayerMask = false; // Check against everything, then filter by Component

        EnsureEatParticles();
    }

    private ParticleSystem goldenParticles;

    private void CreateGoldenParticles()
    {
        if (goldenParticles != null) return;

        GameObject pObj = new GameObject("GoldenParticles");
        pObj.transform.SetParent(transform);
        pObj.transform.localPosition = Vector3.zero;
        pObj.transform.localScale = Vector3.one;

        goldenParticles = pObj.AddComponent<ParticleSystem>();
        var main = goldenParticles.main;
        main.startLifetime = 1.0f;
        main.startSpeed = 0f;
        main.startSize = 0.1f; // Tiny
        main.startColor = new Color(1f, 0.8f, 0f, 1f); // Gold
        main.simulationSpace = ParticleSystemSimulationSpace.World; // Trail behavior
        main.maxParticles = 50;

        var emission = goldenParticles.emission;
        emission.rateOverTime = 8f; // Just enough, not too much

        var shape = goldenParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.3f;

        var renderer = pObj.GetComponent<ParticleSystemRenderer>();
        // Fix: Use the bubble texture/material if available to look like bubbles, otherwise default sprite
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
            // Ensure texture sheet animation works if using bubble texture
            var texSheet = goldenParticles.textureSheetAnimation;
            texSheet.enabled = true;
            texSheet.mode = ParticleSystemAnimationMode.Grid;
            texSheet.numTilesX = 8; // Standard 8x8 bubble grid assumption
            texSheet.numTilesY = 8;
            texSheet.animation = ParticleSystemAnimationType.SingleRow;
            texSheet.rowMode = ParticleSystemAnimationRowMode.Random;
        }
        else
        {
            // Fallback to generated star texture for a "Shiny/Rare" look
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = GetStarTexture();
            renderer.material = mat;
        }
        
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = 1;

        // Apply "Twinkle" behavior
        var sizeOverLifetime = goldenParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0.0f, 0.0f);
        curve.AddKey(0.5f, 1.0f); // Peak at middle
        curve.AddKey(1.0f, 0.0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, curve);

        var rotOverLifetime = goldenParticles.rotationOverLifetime;
        rotOverLifetime.enabled = true;
        rotOverLifetime.z = new ParticleSystem.MinMaxCurve(-45f, 45f);
    }

    // Helper to generate a runtime star/sparkle texture
    private static Texture2D cachedStarTexture;
    private Texture2D GetStarTexture()
    {
        if (cachedStarTexture != null) return cachedStarTexture;

        int size = 64;
        cachedStarTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float centerX = size / 2f;
        float centerY = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Create a 4-point star shape (Diamond + Cross)
                float dx = Mathf.Abs(x - centerX);
                float dy = Mathf.Abs(y - centerY);
                
                // Distance from center (Standard radial glow)
                float dist = Vector2.Distance(new Vector2(x,y), center);
                float glow = Mathf.Clamp01(1.0f - (dist / (size/2f)));

                // Cross shape (Spikes)
                float spike = Mathf.Max(0, 1.0f - (dx / (size/8f))) * Mathf.Max(0, 1.0f - (dy / (size/2.5f))) // Vertical
                            + Mathf.Max(0, 1.0f - (dy / (size/8f))) * Mathf.Max(0, 1.0f - (dx / (size/2.5f))); // Horizontal

                float alpha = Mathf.Clamp01(glow * 0.5f + spike);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        
        cachedStarTexture.SetPixels(colors);
        cachedStarTexture.Apply();
        return cachedStarTexture;
    }

    // Legacy Circle texture (kept if needed, but unused now)
    private static Texture2D cachedCircleTexture;
    private Texture2D GetCircleTexture()
    {
        if (cachedCircleTexture != null) return cachedCircleTexture;

        int size = 64;
        cachedCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = (size / 2f) - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(radius - dist); // Soft edge
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        
        cachedCircleTexture.SetPixels(colors);
        cachedCircleTexture.Apply();
        return cachedCircleTexture;
    }

    private static void GetTightVisualAspect(Sprite sprite, out float widthRatio, out float heightRatio, out Vector2 centerOffsetNorm)
    {
        widthRatio = 1f;
        heightRatio = 1f;
        centerOffsetNorm = Vector2.zero;
        if (sprite == null) return;

        string spriteName = sprite.name.ToLower();
        
        // Custom tight ratios for sprites with transparent canvas padding:
        if (spriteName.Contains("river level 3"))
        {
            widthRatio = 0.95f;
            heightRatio = 0.365f;
            centerOffsetNorm = new Vector2(0.001f, 0.012f);
        }
        else if (spriteName.Contains("river level 4"))
        {
            widthRatio = 0.96f;
            heightRatio = 0.78f;
            centerOffsetNorm = new Vector2(0.002f, 0.005f);
        }
        else if (spriteName.Contains("level 5") && !spriteName.Contains("river"))
        {
            widthRatio = 0.95f;
            heightRatio = 0.57f;
            centerOffsetNorm = new Vector2(-0.006f, 0.012f);
        }
        else if (spriteName.Contains("level 4") && !spriteName.Contains("river"))
        {
            widthRatio = 0.98f;
            heightRatio = 0.90f;
            centerOffsetNorm = Vector2.zero;
        }
        else if (spriteName.Contains("x2 xp") || spriteName.Contains("golden"))
        {
            widthRatio = 0.98f;
            heightRatio = 0.98f;
            centerOffsetNorm = Vector2.zero;
        }
    }

    private void UpdateCollision()
    {
        if (gfx == null) return;
        SpriteRenderer sr = gfx.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        // "Game Style" / "Feeding Frenzy" Fair Collision
        // 1. Fit shape to visual sprite body (Capsule is best for fish)
        // 2. Adjust for transparent canvas padding on custom sprites
        // 3. Keep danger hitboxes strictly inside the visible scales (0.78f forgiveness)

        // Optimization: Use cached buffer to avoid GC allocation
        if (collisionBuffer == null) collisionBuffer = new List<Collider2D>();
        collisionBuffer.Clear();
        GetComponents<Collider2D>(collisionBuffer);

        CapsuleCollider2D capsule = null;
        
        // Identify Capsule and Mark others for destruction
        foreach(var c in collisionBuffer)
        {
            if (c is CapsuleCollider2D cap)
            {
                capsule = cap;
            }
            else
            {
                Destroy(c);
            }
        }
        collisionBuffer.Clear();

        // Add capsule if missing
        if (capsule == null)
        {
             capsule = gameObject.AddComponent<CapsuleCollider2D>();
        }

        capsule.isTrigger = true;

        // Calculate Bounds
        Bounds b = sr.sprite.bounds;
        Vector2 spriteSize = b.size;
        Vector3 spriteCenter = b.center;

        // Adjust for gfx scale relative to root
        float scaleX = Mathf.Abs(gfx.localScale.x);
        float scaleY = Mathf.Abs(gfx.localScale.y);

        // Get tight visual aspect ratio to eliminate transparent padding
        GetTightVisualAspect(sr.sprite, out float wRatio, out float hRatio, out Vector2 offsetNorm);

        Vector2 finalSize = new Vector2(spriteSize.x * scaleX * wRatio, spriteSize.y * scaleY * hRatio);
        
        // Calculate Center Offset in Root Local Space
        Vector3 worldCenter = gfx.TransformPoint(spriteCenter + new Vector3(spriteSize.x * offsetNorm.x, spriteSize.y * offsetNorm.y, 0f));
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

        // Apply Forgiveness - Feeding Frenzy feel:
        // Level 1 minnows get a friendly 1.0f box so they are easy to scoop up.
        // Higher level predator/danger fish use 0.78f so their hitboxes stay strictly inside their visual body.
        float forgiveness = (level == 1) ? 1.0f : (isGoldenFish ? 0.95f : 0.78f);
        
        capsule.size = finalSize * forgiveness;
        capsule.offset = localCenter;
        
        // Auto-Orientation
        if (finalSize.x >= finalSize.y)
            capsule.direction = CapsuleDirection2D.Horizontal;
        else
            capsule.direction = CapsuleDirection2D.Vertical;

        myCollider = capsule;
    }

    private void Update()
    {
        // When hooked by fishing rod, all autonomous logic is suspended
        if (isHooked) return;

        // Mouth bite animation update
        UpdateBiteAnimation();

        // Poison Debuff timer (from consuming a sick fish)
        if (poisonTimer > 0f)
        {
            poisonTimer -= Time.deltaTime;
            if (poisonTimer <= 0f && cachedGfxSr != null && !isSickFish)
            {
                cachedGfxSr.color = Color.white;
            }
        }

        // Bobbing Animation (only fallback if no Animator is controlling Gfx)
        if (gfx != null && gfx != transform && GetComponent<Animator>() == null)
        {
            float newY = defaultYLocal + Mathf.Sin(Time.time * bobSpeed + randomBobOffset) * bobAmount;
            gfx.localPosition = new Vector3(gfx.localPosition.x, newY, gfx.localPosition.z);
        }

        // Despawn logic: 
        // Simply use distance from player. 
        // This allows fish to enter/exit the screen freely without hitting an invisible "world boundary" wall.
        
        // Refresh cached player if missing (e.g. after respawn)
        if (cachedPlayerTransform == null)
        {
            if (cachedGameManager == null) cachedGameManager = GameManager.instance;
            if (cachedGameManager != null && cachedGameManager.playerGameObject != null)
                cachedPlayerTransform = cachedGameManager.playerGameObject.transform;
        }

        if (cachedPlayerTransform != null)
        {
            // OPTIMIZATION: Use sqrMagnitude to avoid expensive square root calculation
            float distSqr = (transform.position - cachedPlayerTransform.position).sqrMagnitude;
            
            // Reduced distance from 80f to 35f (35*35 = 1225)
            if (distSqr > 1225f) 
            {
                DespawnSelf();
                return;
            }
        }

        // Pufferfish Threat Detection & State Update
        if (isPufferFish && !isHooked)
        {
            UpdatePufferfishLogic();
        }

        // Manual Food Chain Check
        // Fixes issue where Physics Matrix prevents Enemy-Enemy trigger events
        eatCheckTimer += Time.deltaTime;
        if (eatCheckTimer > 0.2f) // Check 5 times per second
        {
            eatCheckTimer = 0f;
            CheckFoodChain();
        }
    }

    private void CheckFoodChain()
    {
        if (IsDead || !gameObject.activeInHierarchy) return;
        if (myCollider == null) 
        {
             // Try to recover collider if lost
             myCollider = GetComponent<CapsuleCollider2D>();
             if (myCollider == null) return;
        }

        // Use cached contact filter to eliminate garbage collection
        int count = myCollider.Overlap(contactFilter, collisionBuffer);
        
        for (int i = 0; i < count; i++)
        {
             Collider2D col = collisionBuffer[i];
             if (col == null || col.gameObject == gameObject) continue;

             Fish otherFish = col.GetComponent<Fish>();
             if (otherFish != null)
             {
                 if (otherFish.IsDead || !otherFish.gameObject.activeInHierarchy || otherFish.IsHooked) continue;

                  // Spiked Pufferfish Defense:
                  // "user or ai fish attemp to eat the spike puffer should died no just being pushed away"
                  if (otherFish.IsSpiked)
                  {
                      if (this.level > otherFish.Level)
                      {
                          // This predator tried to eat the spiked pufferfish and dies on the spikes!
                          this.OnEatenByPredator();
                          this.Die();
                          PlayEatEffect();
                          return;
                      }
                  }

                  if (this.IsSpiked)
                  {
                      if (otherFish.Level > this.level)
                      {
                          // Predator tried to eat me while I am spiked -> predator dies on my spikes!
                          otherFish.OnEatenByPredator();
                          otherFish.Die();
                          PlayEatEffect();
                          continue;
                      }
                  }

                  // I am bigger. I eat the smaller fish.
                  if (this.level > otherFish.Level)
                  {
                      // SICK FISH & CUTTLEFISH REFUSE FOOD
                      if (this.isSickFish || this.IsCuttlefish)
                      {
                          continue;
                      }

                      bool wasSick = otherFish.IsSickFish;
                      otherFish.OnEatenByPredator();
                      otherFish.Die();
                      PlayEatEffect();
                      TriggerBite();
                      var ai = GetComponent<FishAI>();
                      if (ai != null) ai.OnAteFish(otherFish);

                      if (wasSick)
                      {
                          ApplyPoisonDebuff(3.5f);
                      }
                  }
             }
        }
    }
    private void Awake()
    {
        // Removed SetupRigidbody() to prevent interference with custom movement scripts (e.g. FishMovement)

        gfx = GetComponentInChildren<SpriteRenderer>()?.transform;
        if (gfx != null)
        {
            cachedGfxSr = gfx.GetComponent<SpriteRenderer>();
            image = cachedGfxSr != null ? cachedGfxSr.sprite : null;
            ConfigureAiFishRendering();
            gfxDefaultScale = gfx.localScale;
            if (normalSprite == null && cachedGfxSr != null)
            {
                normalSprite = cachedGfxSr.sprite;
            }
            if (closedSprite == null && normalSprite != null)
            {
                closedSprite = normalSprite;
            }
            if (closedSprite != null && cachedGfxSr != null && !isSpiked && !isSickFish)
            {
                cachedGfxSr.sprite = closedSprite;
            }

            if (openSprite == null && cachedGfxSr != null)
            {
                string baseName = gameObject.name.Replace("(Clone)", "").Trim();
#if UNITY_EDITOR
                string[] possiblePaths = new string[]
                {
                    $"Assets/Graphics/fish/{baseName}_mouth open.png",
                    $"Assets/Graphics/fish/{baseName}_mouth_open.png",
                    $"Assets/Graphics/fish/{baseName} mouth open.png",
                    cachedGfxSr.sprite != null ? $"Assets/Graphics/fish/{cachedGfxSr.sprite.name}_mouth open.png" : "",
                    cachedGfxSr.sprite != null ? $"Assets/Graphics/fish/{cachedGfxSr.sprite.name}_mouth_open.png" : ""
                };

                foreach (var path in possiblePaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null)
                    {
                        var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                        if (allAssets != null)
                        {
                            foreach (var asset in allAssets)
                            {
                                if (asset is Sprite s)
                                {
                                    sprite = s;
                                    break;
                                }
                            }
                        }
                    }
                    if (sprite != null)
                    {
                        openSprite = sprite;
                        break;
                    }
                }
#endif
                if (openSprite == null)
                {
                    openSprite = Resources.Load<Sprite>($"{baseName}_mouth open") ??
                                 Resources.Load<Sprite>($"{baseName}_mouth_open") ??
                                 (cachedGfxSr.sprite != null ? Resources.Load<Sprite>($"{cachedGfxSr.sprite.name}_mouth open") : null) ??
                                 (cachedGfxSr.sprite != null ? Resources.Load<Sprite>($"{cachedGfxSr.sprite.name}_mouth_open") : null);
                }
            }

            if (sickFishSprite == null)
            {
                string baseName = gameObject.name.Replace("(Clone)", "").Trim();
#if UNITY_EDITOR
                string[] sickPaths = new string[]
                {
                    $"Assets/Graphics/fish/{baseName} sick.png",
                    $"Assets/Graphics/fish/{baseName}_sick.png",
                    cachedGfxSr.sprite != null ? $"Assets/Graphics/fish/{cachedGfxSr.sprite.name} sick.png" : "",
                    "Assets/Graphics/fish/level 2 fish sick.png"
                };
                foreach (var path in sickPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null)
                    {
                        sickFishSprite = sprite;
                        break;
                    }
                }
#endif
                if (sickFishSprite == null)
                {
                    sickFishSprite = Resources.Load<Sprite>($"{baseName} sick") ??
                                     Resources.Load<Sprite>($"{baseName}_sick") ??
                                     Resources.Load<Sprite>("level 2 fish sick");
                }
            }
        }
        else
        {
            Debug.Log("Cant find child sprite renderer in fish");
            hasError = true;
        }

        if (!initialized)
        {
            initialScale = transform.localScale;
            initialized = true;
        }

        defaultSickConfig = isSickFish;
        EvaluateFish();
    }



    private void SetupRigidbody()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            // NOTE: We use Dynamic (isKinematic = false) because FishAI uses 'linearVelocity' to move.
            // If we set isKinematic = true, the fish will not move!
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    public void SetGoldenStatus(bool status)
    {
        if (goldenStatusActive && status) return; // Already active

        isGoldenFish = status;
        if (isGoldenFish)
        {
            goldenStatusActive = true;
            CreateGoldenParticles();
            // Golden fish are fast and agile
            speed = 5.5f; 

            // ENSURE IT CAN BE EATEN
            // Golden fish level is 1 so player can eat it right from Level 1
            level = 1;

            // ENSURE MOVEMENT for Golden Fish
            // Ensure Rigidbody is Dynamic and configured for FishAI
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.simulated = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // Important for smooth FishAI movement

            // Use FishAI (Add if missing)
            var ai = GetComponent<FishAI>();
            if (ai == null) ai = gameObject.AddComponent<FishAI>();
            ai.enabled = true;
            
            // Disable old legacy movement/state controllers to prevent conflicts
            var movement = GetComponent<FishMovement>();
            if (movement != null)
            {
                movement.enabled = false;
                Destroy(movement);
            }
            
            var stateCtrl = GetComponent<StateController>();
            if (stateCtrl != null) stateCtrl.enabled = false;
            if (ai != null)
            {
                ai.enabled = true; // Ensure it's enabled
                // Adjust stats for Golden Fish - Fast and agile
                ai.moveSpeed = 5.5f; 
                ai.minSpeed = 4.2f;
                ai.maxSpeed = 7.0f;
                ai.turnSpeed = 180f; 
                ai.stayOnScreen = true; // Enable boundary logic
                ai.fleeRadius = 0f; // DISABLE FLEEING
            }
        }
        else
        {
            goldenStatusActive = false;
        }
    }

    public void SetLevel(int newLevel)
    {
        // USER REQUIREMENT: 1 Fish = 1 Level.
        // If this fish prefab already has a level set (e.g. 3) in the Inspector, 
        // we should NOT allow it to be downgraded/overridden to Level 1 or 2.
        // We only allow setting level if the current level is 0 (unassigned).
        if (level > 0)
        {
            // Already has a level. Ignore override to preserve "Fixed Size/Fixed Level" identity.
            return;
        }

        level = newLevel;
        if (!initialized)
        {
            initialScale = transform.localScale;
            initialized = true;
        }
        EvaluateFish();
    }

    public void ForceLevel(int newLevel)
    {
        level = newLevel;
        if (!initialized)
        {
            initialScale = transform.localScale;
            initialized = true;
        }
        EvaluateFish();
    }

    private void EvaluateFish()
    {
        if (hasError) return;
        if (level <= 0) return;

        // USER REQUEST: Manual sizing only.
        // Removed programmatic scaling logic (GameManager.GetTargetScale).
        // The size set in the Inspector/Prefab is the final size.
    }

    public void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.localScale = new Vector3(transform.localScale.x * -1f, transform.localScale.y, transform.localScale.z);
    }

    public void TurnLeft()
    {
        //Already looking left
        if (!isFacingRight) return;

        Flip();
    }

    public void TurnRight()
    {
        //Already looking right
        if (isFacingRight) return;
        Flip();
    }

    public void FlipTowardsDestination(Vector2 _destination, bool localSpace = true)
    {
        // Add hysteresis buffer to prevent rapid flipping when target is near vertical center
        float buffer = 0.5f;

        if(localSpace)
        {
            float diff = _destination.x - transform.localPosition.x;
            if (diff < -buffer)
                TurnLeft();
            else if(diff > buffer)
                TurnRight();

            return;
        }
        else
        {
            float diff = _destination.x - transform.position.x;
            if (diff < -buffer)
                TurnLeft();
            else if (diff > buffer)
                TurnRight();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Safety check
        if (collision == null) return;
        if (IsDead || !gameObject.activeInHierarchy) return;
        if (isHooked) return;

        // Check for Fishing Rod Hazard
        Hazard hazard = collision.GetComponentInParent<Hazard>();
        if (hazard != null)
        {
            // Level 1, Level 2, and Golden Fish are too small to bite -> NO collision / ignore
            if (isGoldenFish || level < 3)
            {
                return;
            }

            if (!hazard.HasCatch)
            {
                OnHookedByHazard(hazard);
                return;
            }
        }

        // Check if we collided with another Fish
        // We use GetComponent instead of Tag to be robust across different fish types
        Fish otherFish = collision.GetComponent<Fish>();
        
        if (otherFish != null)
        {
            // Self-collision check or ignore already dead / hooked fish
            if (otherFish == this || otherFish.IsDead || !otherFish.gameObject.activeInHierarchy || otherFish.IsHooked) return;

            // Spiked Pufferfish Defense:
            // "user or ai fish attemp to eat the spike puffer should died no just being pushed away"
            if (otherFish.IsSpiked)
            {
                if (this.level > otherFish.Level)
                {
                    // This predator tried to bite the spiked pufferfish and dies!
                    this.OnEatenByPredator();
                    this.Die();
                    PlayEatEffect();
                    return;
                }
            }

            if (this.IsSpiked)
            {
                if (otherFish.Level > this.level)
                {
                    // Predator tried to bite me while I am spiked -> predator dies!
                    otherFish.OnEatenByPredator();
                    otherFish.Die();
                    PlayEatEffect();
                    return;
                }
            }

            // FOOD CHAIN LOGIC
            // I am bigger. I eat the smaller fish.
            if (this.level > otherFish.Level)
            {
                // SICK FISH & CUTTLEFISH REFUSE FOOD:
                // Sick fish refuse food, and Cuttlefish is a neutral hazard that does not eat fish.
                if (this.isSickFish || this.IsCuttlefish)
                {
                    return;
                }

                bool wasSick = otherFish.IsSickFish;
                otherFish.OnEatenByPredator();
                otherFish.Die();
                PlayEatEffect();
                TriggerBite();
                var ai = GetComponent<FishAI>();
                if (ai != null) ai.OnAteFish(otherFish);

                if (wasSick)
                {
                    ApplyPoisonDebuff(3.5f);
                }
            }
        }
    }

    /// <summary>
    /// Called when the fish is struck and impaled by a RiverBoat HarpoonHazard.
    /// Locks down movement, physics, and autonomous updates while preserving exact scale.
    /// </summary>
    public void OnHarpoonImpaled(HarpoonHazard harpoon = null)
    {
        isHooked = true;
        caughtHarpoon = harpoon;
        caughtHazard = null;

        // Leave school
        school = null;
        formationOffset = Vector2.zero;

        // Disable AI & movement components
        var ai = GetComponent<FishAI>();
        if (ai != null) ai.enabled = false;

        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }

        // Disable colliders so other fish/hazards don't interact
        Collider2D[] allCols = GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = false;
        }

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = false;
        }
    }

    public void OnHookedByHazard(Hazard hazard)
    {
        if (isHooked || hazard == null) return;
        // Level 1, Level 2, and Golden Fish cannot be hooked
        if (isGoldenFish || level < 3) return;

        // Confirm catch with hazard first before locking this fish down
        if (!hazard.HookFish(this)) return;

        // Note: Do NOT call ResetSpikeState() here so spiked pufferfish stays spiked while reeled up
        isHooked = true;
        caughtHazard = hazard;
        hookElapsedTime = 0f;

        // 0. Ensure graphics is facing right (scale.x > 0) so local 90 deg rotation is consistently Face-Up
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x);
        transform.localScale = s;
        isFacingRight = true;

        if (gfx != null)
        {
            gfx.localPosition = Vector3.zero;
        }

        // 1. Leave school
        school = null;
        formationOffset = Vector2.zero;

        // 2. Disable AI & movement components
        var ai = GetComponent<FishAI>();
        if (ai != null) ai.enabled = false;

        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }

        // 3. Disable colliders so other fish/hazards don't interfere
        Collider2D[] allCols = GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = false;
        }

        // 4. Play bite sound
        AudioClip biteClip = eatSfxOverride;
        if (biteClip == null)
        {
            PlayerController pc = FindFirstObjectByType<PlayerController>();
            if (pc != null && pc.BiteSounds != null && pc.BiteSounds.Length > 0)
            {
                biteClip = pc.BiteSounds[Random.Range(0, pc.BiteSounds.Length)];
            }
        }
        if (biteClip != null && AudioSettingsManager.IsSfxEnabled)
        {
            SFXPool.Play3D(biteClip, transform.position, 1.0f, 3.0f, 25.0f);
        }

        // 5. Disable animator if present
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = false;
        }

        // 6. Record starting state for smooth interpolation
        hookStartPos = transform.position;
        float startAngle = (gfx != null) ? gfx.localEulerAngles.z : 0f;
        if (startAngle > 180f) startAngle -= 360f;
        hookCurrentAngle = startAngle;
        hookAngleVelocity = 0f;
    }

    private void LateUpdate()
    {
        if (!isHooked) return;

        // Harpoon handling (RiverBoat)
        if (caughtHarpoon != null)
        {
            if (!caughtHarpoon.gameObject.activeInHierarchy ||
                caughtHarpoon.State == HarpoonHazard.HarpoonState.Completed)
            {
                OnReeledOutOfWater();
                return;
            }
            // Position and struggle jitter are driven by HarpoonHazard in world space
            return;
        }

        // Safety check: if hazard is gone, inactive, or hook lasts abnormally long (> 12s), reel out and clean up
        if (caughtHazard == null || !caughtHazard.gameObject.activeInHierarchy || hookElapsedTime > 12.0f)
        {
            OnReeledOutOfWater();
            return;
        }

        if (GameManager.instance != null && GameManager.Paused) return;

        hookElapsedTime += Time.deltaTime;

        // Compute local mouth offset in gfx space
        Vector3 localMouth = Vector3.zero;
        SpriteRenderer sr = (gfx != null) ? gfx.GetComponent<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            Bounds b = sr.sprite.bounds;
            // Snout / mouth is at the front (+X) and vertical center
            localMouth = new Vector3(b.max.x * 0.85f, b.center.y, 0f);
        }

        Vector3 baitPos = caughtHazard.GetBaitWorldPosition();

        // ----------------------------------------------------
        // Smoothly rotate to 90 degrees (Face-Up)
        // ----------------------------------------------------
        float targetAngle = 90f;
        hookCurrentAngle = Mathf.SmoothDampAngle(hookCurrentAngle, targetAngle, ref hookAngleVelocity, 0.20f);

        // Struggle wiggle around the Face-Up angle (reduced per user request)
        float struggleWiggle = Mathf.Sin(Time.time * 20f) * 6.5f;
        float displayAngle = hookCurrentAngle + struggleWiggle;

        if (gfx != null)
        {
            gfx.localRotation = Quaternion.Euler(0f, 0f, displayAngle);
        }

        // ----------------------------------------------------
        // Magnetism Snap & Position Pinning
        // ----------------------------------------------------
        Vector3 mouthWorld = (gfx != null) ? gfx.TransformPoint(localMouth) : transform.position;
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

    public void OnReeledOutOfWater()
    {
        if (!isHooked) return;
        ResetHookState();
        Die();
    }

    public void ResetHookState()
    {
        isHooked = false;
        caughtHazard = null;
        caughtHarpoon = null;
        hookElapsedTime = 0f;
        hookCurrentAngle = 0f;
        hookAngleVelocity = 0f;

        var ai = GetComponent<FishAI>();
        if (ai != null) ai.enabled = true;

        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        Collider2D[] allCols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = true;
        }

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = true;
        }

        if (gfx != null)
        {
            gfx.localRotation = Quaternion.identity;
            gfx.localPosition = new Vector3(gfx.localPosition.x, defaultYLocal, gfx.localPosition.z);
        }

        if (initialized)
        {
            transform.localScale = initialScale;
        }
        transform.rotation = Quaternion.identity;
        ResetSpikeState();
    }

    #region Pufferfish Spike Logic

    public void ConfigurePufferfish()
    {
        string objName = gameObject.name.ToLower();
        // River fish are never pufferfish
        if (objName.Contains("river") || (normalSprite != null && normalSprite.name.ToLower().Contains("river")))
        {
            isPufferFish = false;
            return;
        }

        // Level 4 ocean fish or any fish configured with spikeSprite is a Pufferfish
        if (isPufferFish || (level == 4 && !LevelManager.IsCurrentLakeLevel) || spikeSprite != null)
        {
            isPufferFish = true;
            if (spikeDetectRadius < 8.5f) spikeDetectRadius = 8.5f;
            if (gfx == null) gfx = transform.Find("Graphics") ?? transform.Find("Gfx") ?? transform;
            if (cachedGfxSr == null && gfx != null) cachedGfxSr = gfx.GetComponent<SpriteRenderer>();
            if (normalSprite == null && cachedGfxSr != null) normalSprite = cachedGfxSr.sprite;
            if (gfx != null && (gfxDefaultScale == Vector3.zero || gfxDefaultScale == Vector3.one))
            {
                gfxDefaultScale = gfx.localScale;
            }
        }
    }

    private void UpdatePufferfishLogic()
    {
        // 1. Resolve player reference reliably
        if (cachedPlayerTransform == null)
        {
            if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
            {
                cachedPlayerTransform = GameManager.instance.playerGameObject.transform;
            }
            else
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) cachedPlayerTransform = pc.transform;
            }
        }

        bool threatDetected = false;
        float detectRadiusSqr = spikeDetectRadius * spikeDetectRadius;

        // Threat 1: Player (ALWAYS protects itself when the player comes close, regardless of player size/level!)
        if (cachedPlayerTransform != null)
        {
            PlayerController pc = cachedPlayerTransform.GetComponent<PlayerController>();
            if (pc != null && pc.IsAlive && !pc.IsHooked)
            {
                float dSqr = ((Vector2)transform.position - (Vector2)cachedPlayerTransform.position).sqrMagnitude;
                if (dSqr <= detectRadiusSqr)
                {
                    threatDetected = true;
                }
            }
        }

        // Threat 2: Predator AI Fish (Level > this.level)
        if (!threatDetected)
        {
            for (int i = 0; i < AllFish.Count; i++)
            {
                Fish other = AllFish[i];
                if (other == null || other == this || other.IsHooked || other.IsDead || !other.gameObject.activeInHierarchy) continue;
                if (other.level > this.level)
                {
                    float dSqr = ((Vector2)transform.position - (Vector2)other.transform.position).sqrMagnitude;
                    if (dSqr <= detectRadiusSqr)
                    {
                        threatDetected = true;
                        break;
                    }
                }
            }
        }

        if (isSpiked)
        {
            // Once spiked, count down fixed spike duration.
            // Do NOT reset or extend the timer while threat remains inside radius.
            spikeActiveTimer -= Time.deltaTime;
            if (spikeActiveTimer <= 0f)
            {
                // Spike duration ended -> deflate back to normal and start mandatory 3s cooldown
                SetSpikeMode(false);
                deflateCooldownTimer = Mathf.Max(3.0f, deflateCooldown);
            }
        }
        else
        {
            // Count down cooldown after deflating
            if (deflateCooldownTimer > 0f)
            {
                deflateCooldownTimer -= Time.deltaTime;
            }

            // Only spike if cooldown has fully expired and a threat is detected
            if (deflateCooldownTimer <= 0f && threatDetected)
            {
                spikeActiveTimer = spikeDuration > 0f ? spikeDuration : 3.5f;
                SetSpikeMode(true);
            }
        }
    }

    public void SetSpikeMode(bool active)
    {
        if (isSpiked == active) return;
        isSpiked = active;

        if (spikeTransitionCoroutine != null)
        {
            StopCoroutine(spikeTransitionCoroutine);
        }
        spikeTransitionCoroutine = StartCoroutine(AnimateSpikeTransition(active));
    }

    private IEnumerator AnimateSpikeTransition(bool toSpiked)
    {
        if (cachedGfxSr == null && gfx != null)
        {
            cachedGfxSr = gfx.GetComponent<SpriteRenderer>();
        }

        Vector3 baseGfxScale = gfxDefaultScale;
        if (baseGfxScale == Vector3.zero && gfx != null)
        {
            baseGfxScale = gfx.localScale;
        }

        Vector3 targetScale = toSpiked ? (baseGfxScale * spikeScaleMultiplier) : baseGfxScale;
        float duration = 0.2f;
        float elapsed = 0f;

        if (toSpiked)
        {
            // Puff animation: squash slightly then expand outward
            while (elapsed < duration * 0.45f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (duration * 0.45f);
                float stretchX = Mathf.Lerp(1.0f, 1.15f, t);
                float squashY = Mathf.Lerp(1.0f, 0.90f, t);
                if (gfx != null)
                {
                    gfx.localScale = new Vector3(targetScale.x * stretchX, targetScale.y * squashY, targetScale.z);
                }
                yield return null;
            }

            // Swap sprite to spiked version (NO sound effect per user request)
            if (cachedGfxSr != null && spikeSprite != null)
            {
                cachedGfxSr.sprite = spikeSprite;
            }

            // Quick puff pop settling to target scale
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = (elapsed - duration * 0.45f) / (duration * 0.55f);
                float pop = Mathf.Lerp(1.15f, 1.0f, t);
                if (gfx != null)
                {
                    gfx.localScale = new Vector3(targetScale.x * pop, targetScale.y * pop, targetScale.z);
                }
                yield return null;
            }
        }
        else
        {
            // Deflate animation
            while (elapsed < duration * 0.5f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (duration * 0.5f);
                float shrink = Mathf.Lerp(1.0f, 0.88f, t);
                if (gfx != null)
                {
                    gfx.localScale = new Vector3(baseGfxScale.x * shrink, baseGfxScale.y * shrink, baseGfxScale.z);
                }
                yield return null;
            }

            // Swap back to normal sprite
            if (cachedGfxSr != null && normalSprite != null)
            {
                cachedGfxSr.sprite = normalSprite;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = (elapsed - duration * 0.5f) / (duration * 0.5f);
                float settle = Mathf.Lerp(0.88f, 1.0f, t);
                if (gfx != null)
                {
                    gfx.localScale = new Vector3(baseGfxScale.x * settle, baseGfxScale.y * settle, baseGfxScale.z);
                }
                yield return null;
            }
        }

        if (gfx != null)
        {
            gfx.localScale = targetScale;
        }

        // Re-fit CapsuleCollider2D to the new sprite shape
        UpdateCollision();
        spikeTransitionCoroutine = null;
    }

    public void ResetSpikeState()
    {
        if (spikeTransitionCoroutine != null)
        {
            StopCoroutine(spikeTransitionCoroutine);
            spikeTransitionCoroutine = null;
        }
        isSpiked = false;
        spikeActiveTimer = 0f;
        deflateCooldownTimer = 0f;
        if (cachedGfxSr == null && gfx != null)
        {
            cachedGfxSr = gfx.GetComponent<SpriteRenderer>();
        }
        if (cachedGfxSr != null)
        {
            cachedGfxSr.color = Color.white;
            if (normalSprite != null)
            {
                cachedGfxSr.sprite = normalSprite;
            }
        }
        if (gfx != null && gfxDefaultScale != Vector3.zero)
        {
            gfx.localScale = gfxDefaultScale;
        }
        UpdateCollision();
    }

    #endregion

    #region Sick Fish Logic

    private static readonly Color SICK_FISH_COLOR = new Color(0.72f, 1f, 0.72f, 1f);

    public void SetSickStatus(bool sick)
    {
        isSickFish = sick;
        if (cachedGfxSr == null && gfx != null) cachedGfxSr = gfx.GetComponent<SpriteRenderer>();
        if (normalSprite == null && cachedGfxSr != null) normalSprite = cachedGfxSr.sprite;

        if (cachedGfxSr != null)
        {
            if (sick)
            {
                if (sickFishSprite != null)
                {
                    cachedGfxSr.sprite = sickFishSprite;
                }
                cachedGfxSr.color = SICK_FISH_COLOR;
            }
            else
            {
                if (normalSprite != null)
                {
                    cachedGfxSr.sprite = normalSprite;
                }
                cachedGfxSr.color = Color.white;
            }
        }
        UpdateCollision();
    }

    public void ResetSickState()
    {
        poisonTimer = 0f;
        if (!defaultSickConfig)
        {
            SetSickStatus(false);
        }
        else
        {
            SetSickStatus(true);
        }
    }

    public void ApplyPoisonDebuff(float duration = 3.5f)
    {
        poisonTimer = duration;
        if (cachedGfxSr != null)
        {
            cachedGfxSr.color = SICK_FISH_COLOR; // Green tint
        }
    }

    public void ReducePoisonTime(float amount = 0.65f)
    {
        if (poisonTimer > 0f)
        {
            poisonTimer = Mathf.Max(0f, poisonTimer - amount);
            if (poisonTimer <= 0f && cachedGfxSr != null && !isSickFish)
            {
                cachedGfxSr.color = Color.white;
            }
        }
    }

    #endregion

    public void ApplyInkDisorientation(float duration = 3.5f)
    {
        if (IsCuttlefish || IsDead) return;
        FishAI ai = GetComponent<FishAI>();
        if (ai != null)
        {
            ai.ApplyInkDisorientation(duration);
        }
    }

    /// <summary>
    /// AI fish share the player's foreground sorting layer so reef art never
    /// renders over their sprites.
    /// </summary>
    private void ConfigureAiFishRendering()
    {
        if (cachedGfxSr == null) return;

        cachedGfxSr.sortingLayerName = "ParallaxForeground";
        cachedGfxSr.sortingOrder = 90; // Well in front of reef elements (50-55) and behind player (120).
    }

    #region Mouth Bite Animation Logic

    public void TriggerBite(float duration = 0.30f)
    {
        if (openSprite == null) return;
        if (isSpiked) return;

        isBiting = true;
        biteDuration = duration;

        if (biteTimer > biteDuration * 0.4f)
        {
            biteTimer = biteDuration * 0.15f;
        }
        else
        {
            biteTimer = 0f;
        }

        if (cachedGfxSr != null)
        {
            cachedGfxSr.sprite = openSprite;
        }
    }

    private void UpdateBiteAnimation()
    {
        if (!isBiting) return;
        if (isSpiked)
        {
            isBiting = false;
            return;
        }

        biteTimer += Time.deltaTime;
        float t = Mathf.Clamp01(biteTimer / biteDuration);

        Sprite restSprite = (isSickFish && sickFishSprite != null) ? sickFishSprite : (closedSprite != null ? closedSprite : normalSprite);

        if (cachedGfxSr != null && openSprite != null)
        {
            if (t < 0.60f)
            {
                cachedGfxSr.sprite = openSprite;
            }
            else
            {
                cachedGfxSr.sprite = restSprite;
            }
        }

        if (t >= 1f)
        {
            isBiting = false;
            if (cachedGfxSr != null && restSprite != null)
            {
                cachedGfxSr.sprite = restSprite;
            }
        }
    }

    public void ResetBiteState()
    {
        isBiting = false;
        biteTimer = 0f;
        Sprite restSprite = (isSickFish && sickFishSprite != null) ? sickFishSprite : (closedSprite != null ? closedSprite : normalSprite);
        if (cachedGfxSr != null && restSprite != null && !isSpiked)
        {
            cachedGfxSr.sprite = restSprite;
        }
    }

    #endregion
}

