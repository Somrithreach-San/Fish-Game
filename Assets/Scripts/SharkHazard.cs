using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using Rhinotap.Toolkit;

public class SharkHazard : MonoBehaviour
{
    [Header("Shark Settings")]
    [SerializeField]
    private float moveSpeed = 16f; // User request: Make shark faster (was 12f)
    [SerializeField]
    private float lifeTimeAfterPass = 5.0f;

    [Header("Effects")]
    [SerializeField]
    private AudioClip warningSound;
    [SerializeField]
    private AudioClip attackSound;
    [SerializeField]
    private AudioClip swimSound; // New Swim Sound
    [SerializeField]
    private ParticleSystem eatEffect;
    [SerializeField]
    private ParticleSystem trailEffect;
    [SerializeField]
    private Material bubbleMaterial;
    [SerializeField]
    private Texture2D bubbleTexture;

    [Header("Visuals")]
    public Transform graphicsTransform;

    // Dependencies
    private GameObject warningIcon; // Kept for reference, but might point to shared icon
    private AudioSource audioSource;
    private AudioSource swimSource; // Separate source for swimming loop
    
    private int direction = 1; // 1 = Right, -1 = Left
    private bool isCharging = false;
    private bool hasPassedScreen = false;

    // References
    private Camera cam;
    private Animator animator; // Add Animator reference

    // State
    private float chargeY;
    private Rigidbody2D rb;

    // Cache
    private static Shader cachedParticleShader;

    // --- STATIC WARNING UI SYSTEM ---
    private static Canvas _sharedCanvas;
    private static Image _sharedIconImage;
    private static RectTransform _sharedIconRect;
    private static GameObject _sharedCanvasObj;

    private void Start()
    {
        // Listen for Pause
        EventManager.StartListening<bool>("gamePaused", OnGamePaused);
    }

    private void OnDestroy()
    {
        EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        
        // Ensure shared canvas is hidden when shark is destroyed
        // This handles cases where shark is destroyed during warning phase (e.g. game over/restart)
        if (_sharedCanvasObj != null && _sharedCanvasObj.activeSelf)
        {
            _sharedCanvasObj.SetActive(false);
        }
    }
    
    private void OnDisable()
    {
        EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        if (_sharedCanvasObj != null && _sharedCanvasObj.activeSelf)
        {
            _sharedCanvasObj.SetActive(false);
        }
    }

    private void OnGamePaused(bool isPaused)
    {
        if (isPaused)
        {
            if (audioSource != null) audioSource.Pause();
            if (swimSource != null) swimSource.Pause();
        }
        else
        {
            if (audioSource != null) audioSource.UnPause();
            if (swimSource != null) swimSource.UnPause();
        }
    }

    public void Initialize(int dir, GameObject iconPrefab, Sprite iconSprite, AudioClip warnClip, AudioClip atkClip, AudioClip swimClip, Material mat = null, Texture2D tex = null)
    {
        direction = dir;
        warningSound = warnClip;
        attackSound = atkClip;
        swimSound = swimClip; // Assign swim sound
        bubbleMaterial = mat;
        bubbleTexture = tex;
        
        // Setup Animator
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
        }

        // Setup Visuals
        if (graphicsTransform == null)
            graphicsTransform = transform.Find("SharkGraphics") ?? transform.Find("Gfx") ?? transform;

        // Setup Audio
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        
        // FIX: Warning sound should be 2D (Global) so it's always heard regardless of shark distance
        audioSource.spatialBlend = 0.0f; // 2D Sound
        // audioSource.minDistance = 5.0f;  // Irrelevant for 2D
        // audioSource.maxDistance = 25.0f; // Irrelevant for 2D
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        // Setup Swim Source
        swimSource = gameObject.AddComponent<AudioSource>();
        swimSource.spatialBlend = 1.0f; // 3D Sound (Swim sound stays 3D)
        swimSource.minDistance = 5.0f;
        swimSource.maxDistance = 25.0f;
        swimSource.rolloffMode = AudioRolloffMode.Linear;
        swimSource.loop = true;
        swimSource.playOnAwake = false;
        swimSource.volume = 0.9f;
        
        // Setup Eat Particles
        // Optimization: Only create if null. The heavy allocation is here.
        if (eatEffect == null)
        {
             CreateEatParticles();
        }

        if (trailEffect == null)
        {
             SetupTrailParticles();
        }
        
        // Enforce Physics Constraints
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
        rb.gravityScale = 0f;

        // Optimized Collider (More Realistic)
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            // Get original sprite size if possible
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                 // Target: 90% Width, 40% Height (Body only, ignore fins/empty space)
                 Vector2 spriteSize = sr.sprite.bounds.size;
                 col.size = new Vector2(spriteSize.x * 0.9f, spriteSize.y * 0.4f);
                 
                 // Offset: Center Y, maybe slight X offset?
                 col.offset = Vector2.zero;
            }
            else
            {
                 // Fallback if no sprite
                 col.size = new Vector2(col.size.x * 0.9f, col.size.y * 0.4f);
            }
        }

        cam = Camera.main;

        // Flip Sprite if moving Left
        // AUTO-FIX: Increase shark size by 30% (User Request)
        float sizeMultiplier = 1.3f;
        Vector3 currentScale = transform.localScale;

        if (direction < 0)
        {
            currentScale.x = -Mathf.Abs(currentScale.x);
        }
        else
        {
            currentScale.x = Mathf.Abs(currentScale.x);
        }
        
        // Apply multiplier (Ensure we don't apply it if it's already large? No, assume fresh spawn)
        // Check if already scaled (just in case) - heuristic check
        if (Mathf.Abs(currentScale.x) < 2.0f) // If it's huge, don't scale again
        {
            currentScale.x *= sizeMultiplier;
            currentScale.y *= sizeMultiplier;
            currentScale.z *= sizeMultiplier;
        }

        transform.localScale = currentScale;

        // Create Warning Icon
        if ((iconPrefab != null || iconSprite != null) && cam != null)
        {
            StartCoroutine(ShowWarningRoutine(iconPrefab, iconSprite));
        }
        else
        {
            StartCharging();
        }
    }

    private void EnsureSharedCanvas()
    {
        if (_sharedCanvasObj == null)
        {
            _sharedCanvasObj = new GameObject("SharkWarningCanvas_Shared");
            DontDestroyOnLoad(_sharedCanvasObj); // Persist across scenes
            
            _sharedCanvas = _sharedCanvasObj.AddComponent<Canvas>();
            _sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _sharedCanvas.sortingOrder = 999;
            
            CanvasScaler scaler = _sharedCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // Standardize

            GameObject iconObj = new GameObject("SharedIconImage");
            iconObj.transform.SetParent(_sharedCanvasObj.transform);
            
            _sharedIconImage = iconObj.AddComponent<Image>();
            _sharedIconImage.raycastTarget = false;
            
            _sharedIconRect = iconObj.GetComponent<RectTransform>();
            _sharedIconRect.anchorMin = new Vector2(0.5f, 0.5f);
            _sharedIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            _sharedIconRect.pivot = new Vector2(0.5f, 0.5f);
            
            // Default hidden
            _sharedCanvasObj.SetActive(false);
        }
    }

    private IEnumerator ShowWarningRoutine(GameObject iconPrefab, Sprite iconSprite)
    {
        // === APPROACH 3: UI CANVAS OVERLAY (Guaranteed Visibility) ===
        // Optimized: Reuse static canvas
        EnsureSharedCanvas();
        
        // Reset State
        _sharedCanvasObj.SetActive(true);
        _sharedIconImage.enabled = true;
        _sharedIconImage.color = Color.white;
        
        // Set Sprite
        if (iconPrefab != null)
        {
             Sprite s = null;
             if (iconPrefab.TryGetComponent<SpriteRenderer>(out var sr)) s = sr.sprite;
             else if (iconPrefab.TryGetComponent<Image>(out var prefabImg)) s = prefabImg.sprite;
             
             if (s != null) _sharedIconImage.sprite = s;
        }
        else if (iconSprite != null)
        {
            _sharedIconImage.sprite = iconSprite;
        }

        // Capture Spawn Y (Fixed World Y)
        float spawnY = transform.position.y;

        // Size
        _sharedIconRect.sizeDelta = new Vector2(100, 100);
        if (_sharedIconImage.sprite != null)
        {
            float aspect = _sharedIconImage.sprite.rect.width / _sharedIconImage.sprite.rect.height;
            _sharedIconRect.sizeDelta = new Vector2(100 * aspect, 100);
        }

        // Play Warning Sound (Looping while warning is active)
        if (audioSource != null && warningSound != null)
        {
            audioSource.clip = warningSound;
            audioSource.loop = true;
            audioSource.Play();
        }

        // WAIT FOR WARNING (4 Seconds)
        // User Request: Warning should last exactly 4 seconds before shark enters.
        // During this time, shark is stationary off-screen.
        isCharging = false;
        
        float warningDuration = 4.0f;
        float timer = 0f;
        
        while (timer < warningDuration)
        {
            if (cam == null) break;

            timer += Time.deltaTime;
            
            // Pulse Alpha
            float alpha = Mathf.Abs(Mathf.Sin(timer * 5f)); // Fast flash
            if (_sharedIconImage != null)
            {
                Color col = _sharedIconImage.color;
                col.a = alpha;
                _sharedIconImage.color = col;
                
                // Pulse Scale
                float scale = Mathf.Lerp(1f, 1.3f, alpha);
                _sharedIconRect.localScale = Vector3.one * scale;
            }
            
            // Update Icon Position (in case camera moves or screen resizes)
            if (_sharedCanvas != null)
            {
                RectTransform canvasRect = _sharedCanvas.GetComponent<RectTransform>();
                float canvasWidth = canvasRect.rect.width;
                float iconWidth = _sharedIconRect.rect.width;
                float screenPadding = Mathf.Max(42f, canvasWidth * 0.02f);
                
                float targetX = 0f;
                if (direction > 0) // Coming from Left
                    targetX = -(canvasWidth / 2f) + (iconWidth / 2f) + screenPadding;
                else // Coming from Right
                    targetX = (canvasWidth / 2f) - (iconWidth / 2f) - screenPadding;
                    
                // Vertical Alignment (Match Shark Spawn Y)
                // Convert World Y to Screen Y
                float targetY = 0f;
                if (cam != null)
                {
                    Vector3 worldPos = new Vector3(0, spawnY, 0);
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    
                    // Convert Screen Point to Canvas Local Point
                    Vector2 localPoint;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, 
                        screenPos, 
                        null, // ScreenSpaceOverlay uses null camera
                        out localPoint))
                    {
                        targetY = localPoint.y;
                    }

                    // CLAMP to Screen Bounds (User Request: "visible to player always")
                    // Ensure icon doesn't go off-screen if player moves away from spawn Y
                    float halfHeight = canvasRect.rect.height / 2f;
                    float iconHalfHeight = _sharedIconRect.rect.height / 2f;
                    float verticalPadding = Mathf.Max(iconHalfHeight, 20f); // Margin from edge
                    
                    float clampLimit = halfHeight - verticalPadding;
                    targetY = Mathf.Clamp(targetY, -clampLimit, clampLimit);
                }

                _sharedIconRect.anchoredPosition = new Vector2(targetX, targetY);
            }

            yield return null;
        }
        
        // WARNING DONE -> ATTACK!
        
        // 1. Cleanup Warning
        if (_sharedCanvasObj != null) _sharedCanvasObj.SetActive(false);
        if (audioSource != null && audioSource.isPlaying && audioSource.clip == warningSound)
        {
            audioSource.Stop();
        }

        // 2. Teleport Shark to "Just Outside Screen" to ensure immediate entry
        if (cam != null)
        {
            float camHeight = 2f * cam.orthographicSize;
            float camWidth = camHeight * cam.aspect;
            float halfWidth = camWidth / 2f;
            
            // Buffer to spawn just outside
            float entryBuffer = 2.0f; 
            float entryX = (direction > 0) ? -(halfWidth + entryBuffer) : (halfWidth + entryBuffer);
            
            // Add Camera X in case it moved
            entryX += cam.transform.position.x;
            
            // Fix: Use spawnY (World Y) instead of chargeY (which might be 0)
            transform.position = new Vector3(entryX, spawnY, 0f);
        }

        // 3. Start Moving
        StartCharging();
        
        // Play Swim Sound Loop
        if (swimSource != null && swimSound != null)
        {
            swimSource.clip = swimSound;
            swimSource.Play();
        }

        // Play Attack Sound (Removed per user request)
        /*
        if (audioSource != null && attackSound != null)
        {
            audioSource.PlayOneShot(attackSound);
        }
        */
    }

    private void StartCharging()
    {
        isCharging = true;
        chargeY = transform.position.y;
        if (trailEffect != null) trailEffect.Play();
        if (swimSource != null && swimSound != null && !swimSource.isPlaying)
        {
            swimSource.clip = swimSound;
            swimSource.Play();
        }
    }

    private void Update()
    {
        if (!isCharging) return;
        if (GameManager.instance != null && GameManager.Paused) return;

        // Move across screen (Strictly Horizontal)
        float newX = transform.position.x + (direction * moveSpeed * Time.deltaTime);
        transform.position = new Vector3(newX, chargeY, 0f);

        // Check bounds to destroy after passing
        if (!hasPassedScreen && cam != null)
        {
            float camHeight = 2f * cam.orthographicSize;
            float camWidth = camHeight * cam.aspect;
            float halfWidth = camWidth / 2f;
            
            float buffer = 5f;
            float rightEdge = cam.transform.position.x + halfWidth + buffer;
            float leftEdge = cam.transform.position.x - halfWidth - buffer;

            if ((direction > 0 && transform.position.x > rightEdge) || 
                (direction < 0 && transform.position.x < leftEdge))
            {
                hasPassedScreen = true;
                StartCoroutine(DespawnAfterDelay(lifeTimeAfterPass));
            }
        }
    }

    private System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Despawn(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            if (other.TryGetComponent<PlayerController>(out var pc))
            {
                pc.Death();
            }
        }
        else if (other.CompareTag("Enemy"))
        {
            if (other.TryGetComponent<Fish>(out var fish))
            {
                fish.Die(); 
                PlayEatEffect();
            }
        }
    }

    private void SetupTrailParticles()
    {
        if (trailEffect != null) return;

        GameObject bubbles = new GameObject("SharkTrailBubbles");
        bubbles.transform.SetParent(transform, false);
        // Offset for tail. Moved further back to align with tail.
        bubbles.transform.localPosition = new Vector3(-4.8f, -0.3f, 0f);

        trailEffect = bubbles.AddComponent<ParticleSystem>();
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();

        // --- REALISTIC BUBBLE SETTINGS ---
        var main = trailEffect.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Faster bubbles for a fast shark
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.0f); 
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        // Varied sizes
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        // Bubbles float up
        main.gravityModifier = -0.1f;
        // Random rotation for realism
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f);

        // Emission: High rate for a "trail"
        var emission = trailEffect.emission;
        emission.rateOverTime = 30f; // Increased from 8f

        // Shape: Cone emitting backwards
        var shape = trailEffect.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.3f;
        // Rotate so cone faces backwards relative to shark (assuming Shark faces Right+)
        // If Shark faces Right (X+), Back is (X-).
        // Default Cone emits along Z. We need to rotate it to emit along -X.
        // Rotation: Y = -90 (Points Left)
        shape.rotation = new Vector3(0f, -90f, 0f);

        // Velocity over Lifetime: Add turbulence
        var vel = trailEffect.velocityOverLifetime;
        vel.enabled = true;
        // FIX: Ensure all curves use the same mode (Constant, Curve, RandomBetweenTwoConstants, etc.)
        // When setting individual axes (x, y, z), if one uses RandomBetweenTwoConstants, others must too.
        // Or we can just set them all to be "Random Between Two Constants".
        
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 0f); // Random Between Constants
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f); // Random Between Constants
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);   // Explicitly set Z to match mode!
        vel.space = ParticleSystemSimulationSpace.World;

        // Size over Lifetime: Bubbles shrink or pop? Or grow?
        // Usually bubbles grow slightly as pressure decreases, then pop.
        var sol = trailEffect.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0.0f, 0.5f); // Start small
        curve.AddKey(0.8f, 1.0f); // Grow
        curve.AddKey(1.0f, 0.0f); // Pop
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Noise: Wiggle
        var noise = trailEffect.noise;
        noise.enabled = true;
        noise.strength = 0.2f;
        noise.frequency = 1f;

        // Color/Alpha: Fade out
        var col = trailEffect.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0.0f), new GradientAlphaKey(0.4f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        // Reuse material logic
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
        
        renderer.sortingOrder = 4; // Behind shark
    }

    private void CreateEatParticles()
    {
        // Optimizing allocation: Check again
        if (eatEffect != null) return;

        GameObject bubbles = new GameObject("SharkEatBubbles");
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
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Velocity
        var vel = eatEffect.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.space = ParticleSystemSimulationSpace.World;

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
        
        renderer.sortingOrder = 6;
    }

    private void PlayEatEffect()
    {
        if (eatEffect != null)
        {
            int count = Random.Range(2, 4);
            eatEffect.Emit(count);
        }
    }
}
