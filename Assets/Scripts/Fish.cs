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
    // Standardized XP based on Level (can still be overridden in Inspector if needed, but we default it)
    public int Xp
    {
        get
        {
            if (xp <= 0) return Mathf.Max(10, level * 18);
            return xp;
        }
    }
    [SerializeField]
    private AudioClip eatSfxOverride;
    public AudioClip EatSfxOverride => eatSfxOverride;

    [SerializeField]
    private bool isFacingRight = true;
    public bool IsFacingRight => isFacingRight;

    [SerializeField]
    private float speed = 1f;
    public float Speed => speed;

    [Header("Special Attributes")]
    [SerializeField]
    private bool isGoldenFish = false; // Is this the rare "Golden" fish?
    public bool IsGoldenFish => isGoldenFish;

    [SerializeField]
    private GameObject goldenParticlePrefab; // Assign in prefab if needed, or we create one
    private bool goldenStatusActive = false;

    // Schooling
    public FishSchool school;
    public Vector2 formationOffset;

    [Header("Bobbing Animation")]
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobAmount = 0.1f;
    private float randomBobOffset;
    private float defaultYLocal;

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
    private static Shader cachedParticleShader;

    public void InitializeParticles(Material mat, Texture2D tex)
    {
        this.bubbleMaterial = mat;
        this.bubbleTexture = tex;
        
        // Pre-create to be ready
        CreateEatParticles();
    }

    private void CreateEatParticles()
    {
        if (eatEffect != null) return;

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
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 50;

        // Emission
        var emission = eatEffect.emission;
        emission.rateOverTime = 0;

        // Shape
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

        // Color
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
        
        renderer.sortingOrder = 5; // Visible
    }

    public void PlayEatEffect()
    {
        if (eatEffect != null)
        {
            eatEffect.Emit(10); // Burst 10 bubbles
        }
    }

    public void Die()
    {
        // Simple death logic
        DespawnSelf();
    }
    
    public void DespawnSelf()
    {
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
        AllFish.Add(this);
        
        // Reset state for pooling
        school = null;
        formationOffset = Vector2.zero;
        // Don't reset 'initialized' as that tracks initialScale which is constant
    }

    private void OnDisable()
    {
        AllFish.Remove(this);
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
        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;
        SetupRigidbody();

        // Setup Manual Collision Check (Bypasses Physics Matrix "Enemy vs Enemy" ignore)
        // CRITICAL FIX: Explicitly get CapsuleCollider2D to avoid grabbing a destroyed collider
        myCollider = GetComponent<CapsuleCollider2D>();
        
        contactFilter = new ContactFilter2D();
        contactFilter.useTriggers = true; 
        contactFilter.useLayerMask = false; // Check against everything, then filter by Component
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

    private void UpdateCollision()
    {
        if (gfx == null) return;
        SpriteRenderer sr = gfx.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        // "Game Style" / "Feeding Frenzy" Collision
        // 1. Fit shape to sprite (Capsule is best for fish)
        // 2. Reduce size slightly (0.85f) for forgiveness

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
        Vector2 spriteCenter = b.center;

        // Adjust for gfx scale relative to root
        // Note: We use Abs because collider size must be positive.
        // We assume gfx is a child of this transform (or the same).
        float scaleX = Mathf.Abs(gfx.localScale.x);
        float scaleY = Mathf.Abs(gfx.localScale.y);

        Vector2 finalSize = new Vector2(spriteSize.x * scaleX, spriteSize.y * scaleY);
        
        // Calculate Center Offset in Root Local Space
        // We use TransformPoint to get World Center, then InverseTransformPoint to get Root Local Center
        // This accounts for any offset of the graphics child.
        Vector3 worldCenter = gfx.TransformPoint(spriteCenter);
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

        // Apply Forgiveness (0.85f) - "Feeding Frenzy" feel
        float forgiveness = 0.85f;
        
        capsule.size = finalSize * forgiveness;
        capsule.offset = localCenter;
        
        // Auto-Orientation
        if (finalSize.x >= finalSize.y)
            capsule.direction = CapsuleDirection2D.Horizontal;
        else
            capsule.direction = CapsuleDirection2D.Vertical;
    }

    private void Update()
    {
        // Bobbing Animation
        if (gfx != null && gfx != transform)
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
        if (myCollider == null) 
        {
             // Try to recover collider if lost
             myCollider = GetComponent<CapsuleCollider2D>();
             if (myCollider == null) return;
        }

        // OverlapCollider finds anything touching 'myCollider' regardless of Physics Matrix (if configured right)
        // It uses the actual collider shape (Capsule) which is better than OverlapCircle.
        int count = Physics2D.OverlapCollider(myCollider, contactFilter, collisionBuffer);
        
        for (int i = 0; i < count; i++)
        {
             Collider2D col = collisionBuffer[i];
             if (col == null || col.gameObject == gameObject) continue;

             Fish otherFish = col.GetComponent<Fish>();
             if (otherFish != null)
             {
                 // I am bigger. I eat the smaller fish.
                 if (this.level > otherFish.Level)
                 {
                     otherFish.Die();
                     PlayEatEffect();
                    var ai = GetComponent<FishAI>();
                    if (ai != null) ai.OnAteFish(otherFish);
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
            image = gfx.GetComponent<SpriteRenderer>().sprite;
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
            // Golden fish are slightly faster
            speed *= 1.2f; 

            // ENSURE IT CAN BE EATEN
            // Golden fish are bonus/loot, so they should be low level (1) so the player can eat them early.
            // Unless manually set to something specifically lower/higher, we force it to 1 to ensure edibility.
            if (level <= 0 || level > 5) level = 1;

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
            
            if (ai != null)
            {
                ai.enabled = true; // Ensure it's enabled
                // Adjust stats for Golden Fish
                ai.moveSpeed = 3.5f; // Calm, normal speed
                ai.turnSpeed = 120f; // Relaxed turning
                ai.stayOnScreen = true; // Enable boundary logic
                ai.fleeRadius = 0f; // DISABLE FLEEING (User Request: "completely remove the fleeing")
            }

            // Remove FishMovement if it exists (Legacy cleanup)
            var movement = GetComponent<FishMovement>();
            if (movement != null)
            {
                Destroy(movement);
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

        // Check if we collided with another Fish
        // We use GetComponent instead of Tag to be robust across different fish types
        Fish otherFish = collision.GetComponent<Fish>();
        
        if (otherFish != null)
        {
            // Self-collision check
            if (otherFish == this) return;

            // FOOD CHAIN LOGIC
            // I am bigger. I eat the smaller fish.
            if (this.level > otherFish.Level)
            {
                otherFish.Die();
                PlayEatEffect();
            }
        }
    }

}

