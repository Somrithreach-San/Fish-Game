using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using Rhinotap.Toolkit;

public class SharkHazard : MonoBehaviour
{
    [Header("Shark Settings")]
    [SerializeField]
    private float moveSpeed = 18f; // Slightly faster pass speed
    [SerializeField]
    private float lifeTimeAfterPass = 5.0f;
    [SerializeField]
    private float sharkScale = 0.85f; // User request: reduced hazard size

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
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite halfBiteSprite;
    [SerializeField] private Sprite openSprite;

    private SpriteRenderer cachedSr;
    private bool isBiting = false;
    private float biteTimer = 0f;
    private const float BITE_DURATION = 0.35f;

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

    private void Awake()
    {
        CacheVisualComponents();
    }

    private void CacheVisualComponents()
    {
        if (graphicsTransform == null)
            graphicsTransform = transform.Find("SharkGraphics") ?? transform.Find("Gfx") ?? transform;

        if (cachedSr == null && graphicsTransform != null)
            cachedSr = graphicsTransform.GetComponent<SpriteRenderer>();
        if (cachedSr == null)
            cachedSr = GetComponentInChildren<SpriteRenderer>();

#if UNITY_EDITOR
        if (closedSprite == null)
            closedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/predator_hazard.png");
        if (openSprite == null)
            openSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/predator_hazard_mouth open.png");
#endif

        if (cachedSr != null && closedSprite != null)
        {
            cachedSr.sprite = closedSprite;
        }
    }

    private bool isEventSubscribed = false;

    private void OnEnable()
    {
        hasPassedScreen = false;
        isCharging = false;
        isBiting = false;
        biteTimer = 0f;
        AudioSettingsManager.OnSfxSettingChanged -= HandleSfxSettingChanged;
        AudioSettingsManager.OnSfxSettingChanged += HandleSfxSettingChanged;
        if (!isEventSubscribed)
        {
            try
            {
                EventManager.StartListening<bool>("gamePaused", OnGamePaused);
                EventManager.StartListening("playerDeath", StopAudioForGameEnd);
                EventManager.StartListening("GameLoss", StopAudioForGameEnd);
                EventManager.StartListening("GameWin", StopAudioForGameEnd);
                isEventSubscribed = true;
            }
            catch { }
        }
    }

    private void Start()
    {
        CacheVisualComponents();
        if (!isEventSubscribed)
        {
            try
            {
                EventManager.StartListening<bool>("gamePaused", OnGamePaused);
                EventManager.StartListening("playerDeath", StopAudioForGameEnd);
                EventManager.StartListening("GameLoss", StopAudioForGameEnd);
                EventManager.StartListening("GameWin", StopAudioForGameEnd);
                isEventSubscribed = true;
            }
            catch { }
        }
    }

    private void OnDestroy()
    {
        if (isEventSubscribed)
        {
            try
            {
                EventManager.StopListening<bool>("gamePaused", OnGamePaused);
                EventManager.StopListening("playerDeath", StopAudioForGameEnd);
                EventManager.StopListening("GameLoss", StopAudioForGameEnd);
                EventManager.StopListening("GameWin", StopAudioForGameEnd);
            }
            catch { }
            isEventSubscribed = false;
        }
        AudioSettingsManager.OnSfxSettingChanged -= HandleSfxSettingChanged;
    }

    private void HandleSfxSettingChanged(bool enabled)
    {
        if (audioSource != null)
        {
            audioSource.mute = !enabled;
            if (!enabled && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }
        if (swimSource != null)
        {
            swimSource.mute = !enabled;
            if (!enabled && swimSource.isPlaying)
            {
                swimSource.Stop();
            }
        }
    }

    private void OnDisable()
    {
        if (isEventSubscribed)
        {
            try
            {
                EventManager.StopListening<bool>("gamePaused", OnGamePaused);
                EventManager.StopListening("playerDeath", StopAudioForGameEnd);
                EventManager.StopListening("GameLoss", StopAudioForGameEnd);
                EventManager.StopListening("GameWin", StopAudioForGameEnd);
                isEventSubscribed = false;
            }
            catch { }
        }
        AudioSettingsManager.OnSfxSettingChanged -= HandleSfxSettingChanged;
        
        // Hide warning icon on disable
        if (_sharedCanvasObj != null)
        {
            _sharedCanvasObj.SetActive(false);
        }
        if (swimSource != null && swimSource.isPlaying)
        {
            swimSource.Stop();
        }
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
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
        CacheVisualComponents();
        isBiting = false;
        biteTimer = 0f;
        if (cachedSr != null && closedSprite != null)
        {
            cachedSr.sprite = closedSprite;
        }

        // Setup Audio
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        
        // FIX: Warning sound should be 2D (Global) so it's always heard regardless of shark distance
        audioSource.spatialBlend = 0.0f; // 2D Sound
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.mute = !AudioSettingsManager.IsSfxEnabled;

        // Setup Swim Source
        if (swimSource == null)
        {
            swimSource = gameObject.AddComponent<AudioSource>();
            swimSource.spatialBlend = 1.0f; // 3D Sound (Swim sound stays 3D)
            swimSource.minDistance = 5.0f;
            swimSource.maxDistance = 25.0f;
            swimSource.rolloffMode = AudioRolloffMode.Linear;
            swimSource.loop = true;
            swimSource.playOnAwake = false;
            swimSource.volume = 0.9f;
        }
        swimSource.mute = !AudioSettingsManager.IsSfxEnabled;
        
        // Setup Eat Particles
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
            SpriteRenderer sr = cachedSr != null ? cachedSr : GetComponentInChildren<SpriteRenderer>();
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

        hasPassedScreen = false;
        isCharging = false;

        // Flip Sprite if moving Left with clean base scale
        float dirSign = (direction < 0) ? -1f : 1f;
        transform.localScale = new Vector3(dirSign * sharkScale, sharkScale, 1f);

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

        // HIDE THE SHARK during the warning phase so the player cannot see it
        // parked off-screen. Move it to a guaranteed invisible world position.
        if (cachedSr != null) cachedSr.enabled = false;
        transform.position = new Vector3(transform.position.x, -9999f, 0f);

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

        // 2. Teleport Shark to "Just Outside Screen" — make it visible right as it enters
        if (cam != null)
        {
            float camHeight = 2f * cam.orthographicSize;
            float camWidth = camHeight * cam.aspect;
            float halfWidth = camWidth / 2f;
            
            float halfSharkWidth = 12f;
            if (cachedSr != null && cachedSr.sprite != null)
            {
                halfSharkWidth = cachedSr.sprite.bounds.extents.x * Mathf.Abs(transform.localScale.x);
            }
            float entryBuffer = halfSharkWidth + 1.5f; 
            float entryX = (direction > 0) ? -(halfWidth + entryBuffer) : (halfWidth + entryBuffer);
            
            // Add Camera X in case it moved
            entryX += cam.transform.position.x;
            
            // Place at the correct Y and just outside horizontal screen edge
            transform.position = new Vector3(entryX, spawnY, 0f);
        }

        // Re-enable the sprite NOW that the shark is safely just off-screen
        if (cachedSr != null) cachedSr.enabled = true;

        // 3. Start Moving
        StartCharging();
        
        // Play Swim Sound Loop
        if (swimSource != null && swimSound != null)
        {
            swimSource.clip = swimSound;
            swimSource.Play();
        }
    }

    private void StartCharging()
    {
        isCharging = true;
        chargeY = transform.position.y;
        // Always ensure the shark is visible when it starts charging
        if (cachedSr != null) cachedSr.enabled = true;
        if (trailEffect != null) trailEffect.Play();
        if (swimSource != null && swimSound != null && !swimSource.isPlaying)
        {
            swimSource.clip = swimSound;
            swimSource.Play();
        }
    }

    private void Update()
    {
        UpdateBiteAnimation();

        if (!isCharging) return;
        if (GameManager.instance != null && GameManager.Paused) return;

        // Proactively consume any prey touching the lethal mouth area
        Vector2 headPos = GetHeadWorldPosition();
        float overlapRadius = 2.4f * (sharkScale / 0.85f);
        Collider2D[] preyAhead = Physics2D.OverlapCircleAll(headPos, overlapRadius);
        for (int i = 0; i < preyAhead.Length; i++)
        {
            if (TryConsumeTarget(preyAhead[i]))
            {
                TriggerBite();
            }
        }

        // Move across screen (Strictly Horizontal)
        float newX = transform.position.x + (direction * moveSpeed * Time.deltaTime);
        transform.position = new Vector3(newX, chargeY, 0f);

        // Check bounds to destroy after passing
        if (!hasPassedScreen && cam != null)
        {
            float camHeight = 2f * cam.orthographicSize;
            float camWidth = camHeight * cam.aspect;
            float halfWidth = camWidth / 2f;
            
            float halfSharkWidth = 12f;
            if (cachedSr != null && cachedSr.sprite != null)
            {
                halfSharkWidth = cachedSr.sprite.bounds.extents.x * Mathf.Abs(transform.localScale.x);
            }
            float buffer = halfSharkWidth + 3f;
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

    public void TriggerBite()
    {
        isBiting = true;
        biteTimer = 0f;
        if (attackSound != null && audioSource != null && AudioSettingsManager.IsSfxEnabled)
        {
            audioSource.PlayOneShot(attackSound, 0.9f);
        }
    }

    private void StopAudioForGameEnd()
    {
        if (audioSource != null) audioSource.Stop();
        if (swimSource != null) swimSource.Stop();
        if (trailEffect != null) trailEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private Vector2 GetHeadWorldPosition()
    {
        if (cachedSr != null && cachedSr.sprite != null)
        {
            Bounds bounds = cachedSr.bounds;
            // The lethal snout is positioned toward the front edge of the shark in travel direction
            return (Vector2)bounds.center + Vector2.right * (direction * bounds.extents.x * 0.70f);
        }

        return (Vector2)transform.position + Vector2.right * (direction * 1.5f);
    }

    private bool IsInLethalZone(Collider2D other)
    {
        if (other == null) return false;

        Vector2 headPosition = GetHeadWorldPosition();
        Vector2 closestPoint = other.ClosestPoint(headPosition);
        
        // Tight bite circle focused specifically on the open jaws
        float biteRadius = 1.35f * (sharkScale / 0.85f);
        if (Vector2.Distance(headPosition, closestPoint) <= biteRadius)
            return true;

        // Also check if the prey's contact point is in the front mouth/head zone
        if (cachedSr != null && cachedSr.sprite != null)
        {
            Bounds bounds = cachedSr.bounds;
            float centerDistX = (closestPoint.x - bounds.center.x) * direction;
            float minMouthX = bounds.extents.x * 0.55f; // Only front 45% (snout & jaws)
            float maxForwardX = bounds.extents.x + 0.3f;
            float maxHalfHeight = bounds.extents.y * 0.55f; // Jaws vertical opening height
            
            if (centerDistX >= minMouthX && centerDistX <= maxForwardX && Mathf.Abs(closestPoint.y - bounds.center.y) <= maxHalfHeight)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryConsumeTarget(Collider2D other)
    {
        if (other == null || other.transform.IsChildOf(transform) || other.gameObject == gameObject)
            return false;

        // Fish colliders are commonly tagged Enemy, so do not reject that tag here.
        if (
            other.GetComponentInParent<Hazard>() != null ||
            other.GetComponentInParent<HarpoonHazard>() != null ||
            other.GetComponentInParent<FishermanBoat>() != null ||
            other.GetComponentInParent<RiverBoat>() != null)
        {
            return false;
        }

        if (!IsInLethalZone(other)) return false;

        PlayerController pc = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            if (pc.IsAlive && !pc.IsHooked)
            {
                pc.Death();
                PlayEatEffect();
                return true;
            }
            return false;
        }

        Fish fish = other.GetComponent<Fish>() ?? other.GetComponentInParent<Fish>();
        if (fish != null && !fish.IsDead && !fish.IsHooked && fish.gameObject.activeInHierarchy)
        {
            fish.OnEatenByPredator();
            fish.Die();
            PlayEatEffect();
            return true;
        }

        return false;
    }

    private void UpdateBiteAnimation()
    {
        if (!isBiting) return;

        if (cachedSr == null)
        {
            if (graphicsTransform != null) cachedSr = graphicsTransform.GetComponent<SpriteRenderer>();
            if (cachedSr == null) cachedSr = GetComponentInChildren<SpriteRenderer>();
            if (cachedSr == null) return;
        }

        biteTimer += Time.deltaTime;
        float t = Mathf.Clamp01(biteTimer / BITE_DURATION);

        if (halfBiteSprite != null && openSprite != null)
        {
            // 3-Frame Chomp Sequence:
            // 0% - 22%: Half-open anticipation / jaw parting
            // 22% - 60%: Full wide predatory chomp
            // 60% - 100%: Snapped shut clamped tight
            if (t < 0.22f)
            {
                cachedSr.sprite = halfBiteSprite;
            }
            else if (t < 0.60f)
            {
                cachedSr.sprite = openSprite;
            }
            else
            {
                cachedSr.sprite = closedSprite != null ? closedSprite : cachedSr.sprite;
            }
        }
        else if (openSprite != null)
        {
            // 2-Frame Chomp
            if (t < 0.60f)
            {
                cachedSr.sprite = openSprite;
            }
            else
            {
                cachedSr.sprite = closedSprite != null ? closedSprite : cachedSr.sprite;
            }
        }

        if (t >= 1f)
        {
            isBiting = false;
            if (closedSprite != null)
            {
                cachedSr.sprite = closedSprite;
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
        if (TryConsumeTarget(other)) TriggerBite();
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // The shark collider covers the body, so keep checking while overlapping
        // and only consume once the prey reaches the lethal head zone.
        if (TryConsumeTarget(other)) TriggerBite();
    }

    private void SetupTrailParticles()
    {
        if (trailEffect != null) return;

        GameObject bubbles = new GameObject("SharkTrailBubbles");
        bubbles.transform.SetParent(transform, false);
        // Offset for tail: Align with the caudal tail fin
        float tailX = -10.5f;
        float tailY = 0.8f;
        if (cachedSr != null && cachedSr.sprite != null)
        {
            tailX = -cachedSr.sprite.bounds.extents.x * 0.96f;
            tailY = cachedSr.sprite.bounds.extents.y * 0.28f;
        }
        bubbles.transform.localPosition = new Vector3(tailX, tailY, 0f);

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
        vel.x = new ParticleSystem.MinMaxCurve(-1f, 0f); // Random Between Constants
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 2f); // Random Between Constants
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);   // Explicitly set Z to match mode!
        vel.space = ParticleSystemSimulationSpace.World;

        // Size over Lifetime: Bubbles shrink or pop? Or grow?
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
