using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

public class GridController : MonoBehaviour
{
    #region Inspector

    [Header("Enemy Library Object")]
    [SerializeField]
    private EnemyLibrary enemyLibrary;

    [Header("Object to track in the grid")]
    [SerializeField]
    private GameObject player;


    [Header("Enable/Disable grid runtime")]
    [SerializeField]
    private bool isActive = true;
    public bool Active => isActive;

    [Header("Arena Spawning")]
    [SerializeField]
    private int maxFishCount = 28; // Reduced from 68 to prevent screen crowding
    [SerializeField]
    private float spawnInterval = 1.3f; // Clean arrival interval
    [SerializeField]
    private GameObject goldenFishPrefab; // Custom prefab for the rare golden fish
    [SerializeField] private float sickFishChance = 0.25f; // Chance for Level 2 fish to spawn as sick
    [Header("School Settings")]
    [SerializeField] private int maxActiveSchools = 2; // Maximum concurrent schools across ocean/river
    [SerializeField] private Vector2 schoolSizeRange = new Vector2(2f, 3f); // 2 to 3 fish per school
    [SerializeField] private float schoolCooldownDuration = 6.5f; // Delay between school arrivals
    private float schoolSpawnCooldownTimer = 0f;
    private float spawnTimer = 0f;

    [Header("Hazard Settings")]
    [SerializeField]
    private GameObject hazardPrefab;
    [SerializeField]
    private Sprite hazardSprite; // Backup if prefab is missing
    [SerializeField]
    private Sprite hazardSpriteVariant; // Second Variant
    [SerializeField]
    private float hazardChance = 0.15f; // Reduced from 0.20f (User Request: "reduce spawn chance")
    [SerializeField]
    private float hazardScale = 0.50f; // Scaled down for realistic, sleek fishing rod & bait proportion

    [Header("Hazard Effects")]
    [SerializeField]
    private AudioClip hazardSound;
    [SerializeField]
    private GameObject hazardBubblePrefab;

    [Header("Hazard Boat Settings")]
    [SerializeField]
    private Sprite boatSprite;
    private static Sprite s_CachedBoatSprite = null;
    [SerializeField]
    private AudioClip boatEngineSound;
    private static AudioClip s_CachedEngineClip = null;
    private List<GameObject> activeBoats = new List<GameObject>();
    private FishermanBoat currentBoat = null;
    private RiverBoat currentRiverBoat = null;

    [Header("Ocean Harpoon Hazard Settings")]
    [SerializeField] private GameObject riverBoatPrefab;
    [SerializeField] private Sprite riverBoatSprite;
    [SerializeField] private GameObject harpoonPrefab;
    [SerializeField] private Sprite harpoonSprite;
    [SerializeField] private AudioClip harpoonShootSound;
    [SerializeField] private AudioClip harpoonReelSound;
    [SerializeField] private AudioClip harpoonStabSound;
    private GameObject riverBoatTemplate = null;
    private GameObject harpoonTemplate = null;
    private static Sprite s_CachedRiverBoatSprite = null;
    
    [Header("Hazard Dynamic Timers")]
    [Tooltip("Min and Max delay (seconds) before the first boat appears after level starts")]
    [SerializeField] private Vector2 boatInitialDelayRange = new Vector2(6f, 18f);
    [Tooltip("Min and Max cooldown (seconds) between boat departures and the next boat arrival")]
    [SerializeField] private Vector2 boatCooldownRange = new Vector2(14f, 28f);

    [Tooltip("Min and Max delay (seconds) before the first shark appears after level starts")]
    [SerializeField] private Vector2 sharkInitialDelayRange = new Vector2(10f, 25f);
    [Tooltip("Min and Max cooldown (seconds) between shark departures and the next shark arrival")]
    [SerializeField] private Vector2 sharkCooldownRange = new Vector2(18f, 35f);

    private float boatCooldownTimer = 0f;
    private float sharkCooldownTimer = 0f;

    public static GridController Instance { get; private set; }

    // Track active hazard to limit to 1
    private List<GameObject> activeHazards = new List<GameObject>();
    private bool isSpawningHazards = false; // Flag to prevent multiple coroutines


    [Header("Shark Hazard Settings")]
    [SerializeField]
    private GameObject sharkPrefab;
    [SerializeField]
    private Sprite sharkSprite;
    [SerializeField]
    private GameObject warningIconPrefab;
    [SerializeField]
    private Sprite warningIconSprite;
    [SerializeField]
    private float sharkChance = 0.03f; // Reduced from 0.05f (User Request: "shark spawn too often reduce it abit")
    [SerializeField]
    private AudioClip sharkWarningSound;
    [SerializeField]
    private AudioClip sharkAttackSound;
    [SerializeField]
    private AudioClip sharkSwimSound; // New Swim Sound
    
    [Header("Shark Animation")]
    [SerializeField]
    private RuntimeAnimatorController sharkAnimController; // Assign 'Fish.controller' here
    
    // Track active shark
    private GameObject activeShark;

    // Templates for Optimization
    private GameObject sharkTemplate;
    private GameObject hazardTemplate;
    private GameObject boatTemplate;

    // Cached references to prevent frame-rate stutter on spawn
    private bool worldBoundsCached = false;
    private float cachedWorldBgLeft = -25f;
    private float cachedWorldBgRight = 25f;
    private float cachedWorldSurfaceY = 15f;
    private float cachedWorldFloorY = -14f;
    private PlayerController cachedPlayerController = null;
    private Material cachedBubbleMat = null;
    private Texture2D cachedBubbleTex = null;

    #endregion

    //Public enemy library access point
    public EnemyLibrary EnemyLibrary => enemyLibrary;
    // Public access to tracked player GameObject
    public GameObject Player => player;
    
    #region MonoBehaviour

    private void Awake()
    {
        if (Instance == null) Instance = this;

        // Ensure ObjectPoolManager exists
        if (ObjectPoolManager.Instance == null)
        {
            GameObject poolObj = new GameObject("ObjectPoolManager");
            poolObj.AddComponent<ObjectPoolManager>();
        }
        
        _camCacheFrame = -1;
        hazardScale = Mathf.Clamp(hazardScale, 0.40f, 0.60f);

        if (boatSprite != null && boatSprite.name.Contains("Fisherman_Boat"))
        {
            s_CachedBoatSprite = boatSprite;
        }
        else
        {
            EnsureCorrectBoatSprite();
        }

        #if UNITY_EDITOR
        if (cachedBubbleMat == null)
        {
            cachedBubbleMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
        }
        #endif
        if (cachedBubbleMat != null)
        {
            FishermanBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
            RiverBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
        }

        if (boatEngineSound != null)
        {
            s_CachedEngineClip = boatEngineSound;
        }
        #if UNITY_EDITOR
        if (s_CachedEngineClip == null)
        {
            s_CachedEngineClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Boat_Engine_Drive.MP3");
            if (boatEngineSound == null) boatEngineSound = s_CachedEngineClip;
        }
        #endif
        if (s_CachedEngineClip == null)
        {
            s_CachedEngineClip = Resources.Load<AudioClip>("Boat_Engine_Drive");
            if (boatEngineSound == null) boatEngineSound = s_CachedEngineClip;
        }
        if (s_CachedEngineClip != null)
        {
            FishermanBoat.SetGlobalEngineClip(s_CachedEngineClip);
            RiverBoat.SetGlobalEngineClip(s_CachedEngineClip);
        }

        if (hazardSound == null && hazardPrefab != null)
        {
            Hazard hz = hazardPrefab.GetComponent<Hazard>();
            if (hz != null && hz.MoveSound != null) hazardSound = hz.MoveSound;
        }

        if (hazardPrefab != null && ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.PreWarm(hazardPrefab, 4);
        }
        else if (hazardSprite != null && ObjectPoolManager.Instance != null)
        {
            if (hazardTemplate == null)
            {
                hazardTemplate = new GameObject("Hazard_Hook");
                hazardTemplate.transform.SetParent(transform);
                hazardTemplate.SetActive(false);
                SpriteRenderer sr = hazardTemplate.AddComponent<SpriteRenderer>();
                sr.sprite = hazardSprite;
                BoxCollider2D col = hazardTemplate.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                Hazard hz = hazardTemplate.AddComponent<Hazard>();
                hz.autoConfigureCollider = true;
                hazardTemplate.tag = "Enemy";
                hazardTemplate.transform.localScale = Vector3.one * hazardScale;
            }
            ObjectPoolManager.Instance.PreWarm(hazardTemplate, 4);
        }

        if (sharkPrefab != null && ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.PreWarm(sharkPrefab, 2);
        }
        else if (ObjectPoolManager.Instance != null)
        {
            if (sharkTemplate == null)
            {
                sharkTemplate = new GameObject("Shark_Template");
                sharkTemplate.transform.SetParent(transform);
                sharkTemplate.SetActive(false);
                Animator anim = sharkTemplate.AddComponent<Animator>();
                if (sharkAnimController != null)
                {
                    anim.runtimeAnimatorController = sharkAnimController;
                }
                GameObject gfx = new GameObject("Gfx");
                gfx.transform.SetParent(sharkTemplate.transform);
                gfx.transform.localPosition = Vector3.zero;
                SpriteRenderer sr = gfx.AddComponent<SpriteRenderer>();
                if (sharkSprite != null) sr.sprite = sharkSprite;
                BoxCollider2D col = sharkTemplate.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                else col.size = new Vector2(2f, 1f);
                sharkTemplate.AddComponent<SharkHazard>();
            }
            ObjectPoolManager.Instance.PreWarm(sharkTemplate, 2);
        }

        EventManager.StartListening<GameObject>("PlayerSpawn", (spawnedObject) =>
        {
            if (spawnedObject != null)
            {
                player = spawnedObject;
                cachedPlayerController = player.GetComponent<PlayerController>();
                if (cachedPlayerController != null)
                {
                    cachedBubbleMat = cachedPlayerController.BubbleMaterial;
                    cachedBubbleTex = cachedPlayerController.BubbleTexture;
                    if (cachedBubbleMat != null)
                    {
                        FishermanBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
                        RiverBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
                    }
                }
                FishermanBoat.SetGlobalPlayer(player.transform);
                RiverBoat.SetGlobalPlayer(player.transform);
            }
        });
    }

    private void Start()
    {
        EnsureWorldBoundsCached();
        if (LevelManager.IsCurrentLakeLevel)
        {
            InitBoatTemplate();
        }
        else
        {
            InitRiverBoatTemplate();
        }

        // Initialize dynamic, randomized hazard delays
        boatCooldownTimer = Random.Range(boatInitialDelayRange.x, boatInitialDelayRange.y);
        sharkCooldownTimer = Random.Range(sharkInitialDelayRange.x, sharkInitialDelayRange.y);

        if (maxFishCount > 32)
        {
            maxFishCount = 28;
        }
        schoolSpawnCooldownTimer = Random.Range(2f, 4f);
    }

    private void EnsureWorldBoundsCached()
    {
        if (worldBoundsCached) return;

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
                cachedWorldBgLeft = minX;
                cachedWorldBgRight = maxX;
                cachedWorldSurfaceY = maxY;
                cachedWorldFloorY = minY;
                worldBoundsCached = true;
                FishermanBoat.SetGlobalWorldBounds(cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY);
                RiverBoat.SetGlobalWorldBounds(cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, cachedWorldFloorY);
                return;
            }
        }

        cachedWorldBgLeft = -25f;
        cachedWorldBgRight = 25f;
        cachedWorldSurfaceY = 15f;
        cachedWorldFloorY = -14f;
        worldBoundsCached = true;
        FishermanBoat.SetGlobalWorldBounds(cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY);
        RiverBoat.SetGlobalWorldBounds(cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, cachedWorldFloorY);
    }

    public static Sprite LoadFishermanBoatSprite()
    {
        if (s_CachedBoatSprite != null) return s_CachedBoatSprite;

        Sprite s = null;
        #if UNITY_EDITOR
        Object[] subAssets = UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Graphics/Hazard/Fisherman_Boat.png");
        foreach (var sa in subAssets)
        {
            if (sa is Sprite sp && sp.name.Contains("Fisherman_Boat"))
            {
                s = sp;
                break;
            }
        }
        if (s == null)
        {
            s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/Fisherman_Boat.png");
        }
        #endif

        if (s == null)
        {
            Sprite[] all = Resources.LoadAll<Sprite>("Fisherman_Boat");
            if (all != null && all.Length > 0)
            {
                s = all[0];
            }
        }
        s_CachedBoatSprite = s;
        return s;
    }

    public static Sprite LoadRiverBoatSprite()
    {
        if (s_CachedRiverBoatSprite != null) return s_CachedRiverBoatSprite;

        Sprite s = null;
        #if UNITY_EDITOR
        s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/river_fisherman_hazard_boat.png");
        #endif

        if (s == null)
        {
            Sprite[] all = Resources.LoadAll<Sprite>("river_fisherman_hazard_boat");
            if (all != null && all.Length > 0)
            {
                s = all[0];
            }
        }
        s_CachedRiverBoatSprite = s;
        return s;
    }

    private void EnsureCorrectRiverBoatSprite()
    {
        if (s_CachedRiverBoatSprite != null)
        {
            if (riverBoatSprite != s_CachedRiverBoatSprite) riverBoatSprite = s_CachedRiverBoatSprite;
            return;
        }

        if (riverBoatSprite == null)
        {
            riverBoatSprite = LoadRiverBoatSprite();
        }
        if (riverBoatSprite != null)
        {
            s_CachedRiverBoatSprite = riverBoatSprite;
        }
    }

    private static Sprite s_CachedHarpoonSprite = null;

    public static Sprite LoadHarpoonSprite()
    {
        if (s_CachedHarpoonSprite != null) return s_CachedHarpoonSprite;

        Sprite s = null;
        #if UNITY_EDITOR
        s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/river_harpoon.png");
        #endif

        if (s == null)
        {
            Sprite[] all = Resources.LoadAll<Sprite>("river_harpoon");
            if (all != null && all.Length > 0)
            {
                s = all[0];
            }
        }
        s_CachedHarpoonSprite = s;
        return s;
    }

    private void EnsureCorrectHarpoonSprite()
    {
        if (s_CachedHarpoonSprite != null)
        {
            if (harpoonSprite != s_CachedHarpoonSprite) harpoonSprite = s_CachedHarpoonSprite;
            return;
        }

        if (harpoonSprite == null)
        {
            harpoonSprite = LoadHarpoonSprite();
        }
        if (harpoonSprite != null)
        {
            s_CachedHarpoonSprite = harpoonSprite;
        }
    }

    private void EnsureCorrectBoatSprite()
    {
        if (s_CachedBoatSprite != null)
        {
            if (boatSprite != s_CachedBoatSprite) boatSprite = s_CachedBoatSprite;
            return;
        }

        if (boatSprite == null || !boatSprite.name.Contains("Fisherman_Boat"))
        {
            boatSprite = LoadFishermanBoatSprite();
        }
        if (boatSprite != null)
        {
            s_CachedBoatSprite = boatSprite;
        }
    }

    private void OnValidate()
    {
        EnsureCorrectBoatSprite();
        EnsureCorrectRiverBoatSprite();
        EnsureCorrectHarpoonSprite();
    }

    private void InitRiverBoatTemplate()
    {
        EnsureCorrectRiverBoatSprite();
        if (riverBoatTemplate != null || riverBoatSprite == null) return;

        EnsureWorldBoundsCached();

        riverBoatTemplate = new GameObject("RiverFishermanBoat");
        riverBoatTemplate.transform.SetParent(transform);
        riverBoatTemplate.SetActive(false);

        SpriteRenderer sr = riverBoatTemplate.AddComponent<SpriteRenderer>();
        sr.sprite = riverBoatSprite;
        sr.sortingOrder = 3; // Below player fish (5) - RiverBoat.Awake() also enforces this

        RiverBoat boatComp = riverBoatTemplate.AddComponent<RiverBoat>();

        if (cachedPlayerController == null)
        {
            cachedPlayerController = (player != null) ? player.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();
        }
        if (cachedPlayerController != null)
        {
            if (cachedBubbleMat == null) cachedBubbleMat = cachedPlayerController.BubbleMaterial;
            if (cachedBubbleTex == null) cachedBubbleTex = cachedPlayerController.BubbleTexture;
        }

        #if UNITY_EDITOR
        if (cachedBubbleMat == null)
        {
            cachedBubbleMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
        }
        #endif
        if (cachedBubbleMat != null)
        {
            FishermanBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
        }

        boatComp.SetupWakeParticlesTemplate(cachedBubbleMat, cachedBubbleTex);
        boatComp.SetupAudio(s_CachedEngineClip);
        boatComp.WarmUpParticles();

        if (riverBoatSprite != null && riverBoatSprite.texture != null)
        {
            var _ = riverBoatSprite.texture.GetNativeTexturePtr();
        }

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.PreWarm(riverBoatTemplate, 2);
        }
    }

    private void InitBoatTemplate()
    {
        EnsureCorrectBoatSprite();
        if (boatTemplate != null || boatSprite == null) return;

        EnsureWorldBoundsCached();

        boatTemplate = new GameObject("FishermanBoat");
        boatTemplate.transform.SetParent(transform);
        boatTemplate.SetActive(false);

        SpriteRenderer sr = boatTemplate.AddComponent<SpriteRenderer>();
        sr.sprite = boatSprite;
        sr.sortingOrder = 5;

        FishermanBoat boatComp = boatTemplate.AddComponent<FishermanBoat>();

        if (cachedPlayerController == null)
        {
            cachedPlayerController = (player != null) ? player.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();
        }
        if (cachedPlayerController != null)
        {
            if (cachedBubbleMat == null) cachedBubbleMat = cachedPlayerController.BubbleMaterial;
            if (cachedBubbleTex == null) cachedBubbleTex = cachedPlayerController.BubbleTexture;
        }

        #if UNITY_EDITOR
        if (cachedBubbleMat == null)
        {
            cachedBubbleMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
        }
        #endif
        if (cachedBubbleMat != null)
        {
            FishermanBoat.SetGlobalBubbleMaterial(cachedBubbleMat);
        }

        boatComp.SetupWakeParticlesTemplate(cachedBubbleMat, cachedBubbleTex);
        boatComp.SetupAudio(s_CachedEngineClip);
        boatComp.WarmUpParticles();

        if (boatSprite != null && boatSprite.texture != null)
        {
            // Warm texture upload to GPU without displaying any sprite in front of the camera
            var _ = boatSprite.texture.GetNativeTexturePtr();
        }

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.PreWarm(boatTemplate, 2);
        }
    }

    private void Update()
    {
        if (!isActive) return;
        if (player == null) return;

        // Arena Mode: Continuous Spawning
        HandleArenaSpawning();
    }

    private void HandleArenaSpawning()
    {
        if (enemyLibrary == null) return;
        
        UpdateCameraCache();

        if (schoolSpawnCooldownTimer > 0f)
        {
            schoolSpawnCooldownTimer -= Time.deltaTime;
        }

        // Clean inactive references for hazards and shark
        if (activeShark != null && !activeShark.activeSelf) activeShark = null;
        activeHazards.RemoveAll(h => h == null || !h.activeSelf);

        // Periodic Cleanup (Every 60 frames / ~1s) to remove far-off fish
        // This ensures high-level fish that leave the screen are eventually destroyed
        // even if the population limit hasn't been reached.
        if (Time.frameCount % 60 == 0)
        {
             CullObsoleteFish(GameManager.PlayerLevel);
        }

        LevelConfig levelCfg = LevelManager.GetCurrentConfig();

        // --- Dynamic Hazard Director: Boat Hazard ---
        if (levelCfg.enableFishingRod)
        {
            bool isBoatActive = (currentBoat != null && currentBoat.gameObject.activeInHierarchy) ||
                                (currentRiverBoat != null && currentRiverBoat.gameObject.activeInHierarchy) ||
                                activeHazards.Count > 0 || isSpawningHazards;

            if (!isBoatActive)
            {
                boatCooldownTimer -= Time.deltaTime;
                if (boatCooldownTimer <= 0f)
                {
                    boatCooldownTimer = Random.Range(boatCooldownRange.x, boatCooldownRange.y);
                    StartCoroutine(SpawnHazardsRoutine());
                }
            }
        }

        // --- Dynamic Hazard Director: Shark / Predator Hazard (Ocean only) ---
        if (levelCfg.enableShark && !levelCfg.isLake)
        {
            bool isSharkActive = (activeShark != null && activeShark.activeSelf);
            if (!isSharkActive)
            {
                sharkCooldownTimer -= Time.deltaTime;
                if (sharkCooldownTimer <= 0f)
                {
                    sharkCooldownTimer = Random.Range(sharkCooldownRange.x, sharkCooldownRange.y);
                    SpawnShark();
                }
            }
        }

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;

            // Fish Count Limit Check
            // OPTIMIZATION: Use static list from Fish class
            
            // Get player level early for culling check
            int playerLevel = GameManager.PlayerLevel;

            Fish.AllFish.RemoveAll(f => f == null);
            if (Fish.AllFish.Count >= maxFishCount)
            {
                // CULLING LOGIC:
                // If the ocean is full, check if we have "Obsolete" fish (Level < PlayerLevel)
                // that are taking up space. If so, remove them to make room for new, relevant fish.
                // Pass 'true' to force recycling of obsolete fish.
                if (CullObsoleteFish(playerLevel, true))
                {
                    // We culled a fish. It will be removed at end of frame.
                    // Return now, but keep spawnTimer high so we retry spawning immediately next frame.
                    return;
                }
                
                // If nothing to cull, we are truly full.
                return;
            }
            
            int[] activeCounts = new int[7];
            int totalActive = 0;
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish f = Fish.AllFish[i];
                if (f == null || !f.gameObject.activeInHierarchy || f.IsDead) continue;
                int lvl = Mathf.Clamp(f.Level, 1, 6);
                activeCounts[lvl]++;
                totalActive++;
            }

            SpawnArenaFish(playerLevel, activeCounts, totalActive);
        }
    }

    private void SpawnArenaFish(int playerLevel, int[] activeCounts, int totalActive)
    {
        int count = 1;
        if (Fish.AllFish.Count < 10) count = 2;
        
        // Calculate Camera View Boundaries
        if (_cam == null) return;
        EnsureWorldBoundsCached();
        
        // Spawn strictly off-screen (buffer of ~3.5f units guarantees even large fish and entire schools spawn 100% off-screen).
        float buffer = 3.5f;

        float halfHeight = _camHeight / 2f;
        float worldFloor = worldBoundsCached ? (cachedWorldFloorY + 1.2f) : -13.5f;
        float worldSurface = worldBoundsCached ? (cachedWorldSurfaceY - 2.0f) : 13.5f;

        for (int i = 0; i < count; i++)
        {
            // Pick left or right, smartly preferring whichever side has open water inside world bounds
            bool canSpawnRight = (_camPos.x + _halfWidth + buffer <= cachedWorldBgRight + 4.0f);
            bool canSpawnLeft = (_camPos.x - _halfWidth - buffer >= cachedWorldBgLeft - 4.0f);

            bool spawnOnRight;
            if (canSpawnRight && canSpawnLeft)
                spawnOnRight = (Random.value > 0.5f);
            else if (canSpawnRight)
                spawnOnRight = true;
            else if (canSpawnLeft)
                spawnOnRight = false;
            else
                spawnOnRight = (Random.value > 0.5f);

            float jitter = Random.Range(0.4f, 1.2f);
            float spawnX;
            if (spawnOnRight)
            {
                spawnX = _camPos.x + _halfWidth + buffer + jitter;
                if (spawnX < _camPos.x + _halfWidth + 2.5f)
                    spawnX = _camPos.x + _halfWidth + 2.5f;
            }
            else
            {
                spawnX = _camPos.x - _halfWidth - buffer - jitter;
                if (spawnX > _camPos.x - _halfWidth - 2.5f)
                    spawnX = _camPos.x - _halfWidth - 2.5f;
            }

            // Align Y spawn with camera's visible span clamped within actual water floor and surface
            float minY = Mathf.Max(worldFloor, _camPos.y - halfHeight + 1.2f);
            float maxY = Mathf.Min(worldSurface, _camPos.y + halfHeight - 1.2f);
            if (minY >= maxY)
            {
                minY = worldFloor;
                maxY = worldSurface;
            }
            float spawnY = Random.Range(minY, maxY);
            Vector2 spawnPos = new Vector2(spawnX, spawnY);
            
            // SPECIAL: Check for Golden Fish Spawn (Ocean only)
            float goldenChance = 0.02f;
            if (playerLevel >= 5) goldenChance = 0.08f;
            else if (playerLevel >= 3) goldenChance = 0.05f;

            LevelConfig currentCfg = LevelManager.GetCurrentConfig();

            if (!currentCfg.isLake && Random.value < goldenChance)
            {
                Fish goldenPrefab = null;
                if (goldenFishPrefab != null)
                {
                    goldenPrefab = goldenFishPrefab.GetComponent<Fish>();
                }
                
                if (goldenPrefab == null)
                {
                    goldenPrefab = enemyLibrary.GetPrefabByName(1, "level 1 fish", false);
                    if (goldenPrefab == null) goldenPrefab = enemyLibrary.GetRandomPrefab(1, false);
                }
                
                if (goldenPrefab != null)
                {
                    Fish golden = enemyLibrary.SpawnSpecific(goldenPrefab, spawnPos, 0f, 0f, -1);
                    if (golden != null)
                    {
                        golden.SetGoldenStatus(true);
                        golden.ForceLevel(1);
                        OrientFish(golden, spawnPos, new Vector2(_camPos.x, spawnPos.y));
                        return;
                    }
                }
            }

            // Natural Balanced Ecosystem Spawning
            float levelProgress = GameManager.PlayerLevelProgress;
            int spawnLevel = CalculateSpawnLevel(playerLevel, levelProgress, currentCfg.maxEnemyLevel, activeCounts, totalActive);
            
            // Determine Prefab (Support both ocean "level X fish" and river "river level X fish")
            string targetName = (currentCfg.isLake ? "river level " : "level ") + spawnLevel + " fish";
            Fish prefabToSpawn = enemyLibrary.GetPrefabByName(spawnLevel, targetName, currentCfg.isLake);
            
            if (prefabToSpawn == null)
            {
                prefabToSpawn = enemyLibrary.GetRandomPrefab(spawnLevel, currentCfg.isLake);
            }
            
            if (prefabToSpawn != null)
            {
                if (prefabToSpawn.Level != spawnLevel)
                {
                    Debug.LogWarning($"Spawn Mismatch! Intended: {spawnLevel}, Prefab: {prefabToSpawn.name} has Level {prefabToSpawn.Level}");
                }

                // Level 1 fish (both Ocean and River) spawn in small schools
                if (spawnLevel == 1)
                {
                    // If minnows already exist in abundance (>= 4), spawn only 1-2 fish to prevent minnow swamping
                    int schoolSize = (activeCounts != null && activeCounts[1] >= 4) ? Random.Range(1, 3) : Random.Range(2, 4);
                    
                    GameObject schoolObj = new GameObject("FishSchool");
                    schoolObj.transform.position = spawnPos;
                    FishSchool school = schoolObj.AddComponent<FishSchool>();
                    bool movingRight = (spawnX < _camPos.x); 
                    school.Initialize(movingRight);
                    
                    for (int s = 0; s < schoolSize; s++)
                    {
                        Vector2 schoolOffset = Random.insideUnitCircle * Random.Range(0.35f, 0.95f);
                        schoolOffset.x *= 1.3f; 
                        if (spawnOnRight) schoolOffset.x = Mathf.Abs(schoolOffset.x);
                        else schoolOffset.x = -Mathf.Abs(schoolOffset.x);

                        Vector2 finalPos = spawnPos + schoolOffset;
                        finalPos.y = Mathf.Clamp(finalPos.y, minY, maxY);
                        
                        Fish fish = enemyLibrary.SpawnSpecific(prefabToSpawn, finalPos, 0f, 0f, -1);
                        if (fish != null)
                        {
                            fish.school = school;
                            fish.formationOffset = schoolOffset;
                            school.RegisterFish(fish);
                            OrientFish(fish, finalPos, new Vector2(_camPos.x, finalPos.y));
                        }
                    }
                    if (activeCounts != null) activeCounts[1] += schoolSize;
                }
                else
                {
                    // Higher-level fish (Level >= 2) spawn individually
                    Fish fish = enemyLibrary.SpawnSpecific(prefabToSpawn, spawnPos, 0f, 0f, -1);
                    if (fish != null)
                    {
                        if (spawnLevel == 2 && !currentCfg.isLake && Random.value < sickFishChance)
                        {
                            fish.SetSickStatus(true);
                            float deepMinY = worldFloor + 0.8f;
                            float deepMaxY = worldFloor + (worldSurface - worldFloor) * 0.28f;
                            float deepSpawnY = Random.Range(deepMinY, deepMaxY);
                            Vector3 deepPos = fish.transform.position;
                            deepPos.y = deepSpawnY;
                            fish.transform.position = deepPos;
                            OrientFish(fish, deepPos, new Vector2(_camPos.x, deepSpawnY));
                        }
                        else
                        {
                            OrientFish(fish, spawnPos, new Vector2(_camPos.x, spawnY));
                        }
                    }
                    if (activeCounts != null) activeCounts[spawnLevel]++;
                }
            }
        }
    }

    //==============================| Helpers |========================//

    private int CalculateSpawnLevel(int playerLevel, float levelProgress, int maxEnemyLevel, int[] activeCounts, int totalActive)
    {
        int effectiveMax = Mathf.Clamp(maxEnemyLevel, 1, 5);
        if (effectiveMax <= 1) return 1;
        if (playerLevel < 1) playerLevel = 1;

        float[] weights = new float[7];

        // 1. Stage Baseline Distribution
        if (effectiveMax == 2)
        {
            if (playerLevel == 1)
            {
                weights[1] = 0.42f;
                weights[2] = 0.58f;
            }
            else // Lv 2+
            {
                weights[1] = 0.25f;
                weights[2] = 0.75f;
            }
        }
        else if (effectiveMax == 3)
        {
            if (playerLevel == 1)
            {
                weights[1] = 0.32f;
                weights[2] = 0.40f;
                weights[3] = 0.28f;
            }
            else if (playerLevel == 2)
            {
                weights[1] = 0.18f;
                weights[2] = 0.44f;
                weights[3] = 0.38f;
            }
            else // Lv 3+
            {
                weights[1] = 0.12f;
                weights[2] = 0.36f;
                weights[3] = 0.52f;
            }
        }
        else if (effectiveMax == 4)
        {
            if (playerLevel == 1)
            {
                weights[1] = 0.25f;
                weights[2] = 0.32f;
                weights[3] = 0.26f;
                weights[4] = 0.17f;
            }
            else if (playerLevel == 2)
            {
                weights[1] = 0.15f;
                weights[2] = 0.34f;
                weights[3] = 0.31f;
                weights[4] = 0.20f;
            }
            else if (playerLevel == 3)
            {
                weights[1] = 0.10f;
                weights[2] = 0.22f;
                weights[3] = 0.40f;
                weights[4] = 0.28f;
            }
            else // Lv 4+
            {
                weights[1] = 0.06f;
                weights[2] = 0.16f;
                weights[3] = 0.34f;
                weights[4] = 0.44f;
            }
        }
        else // effectiveMax >= 5
        {
            if (playerLevel == 1)
            {
                weights[1] = 0.22f;
                weights[2] = 0.28f;
                weights[3] = 0.24f;
                weights[4] = 0.16f;
                weights[5] = 0.10f;
            }
            else if (playerLevel == 2)
            {
                weights[1] = 0.14f;
                weights[2] = 0.28f;
                weights[3] = 0.28f;
                weights[4] = 0.18f;
                weights[5] = 0.12f;
            }
            else if (playerLevel == 3)
            {
                weights[1] = 0.08f;
                weights[2] = 0.18f;
                weights[3] = 0.34f;
                weights[4] = 0.24f;
                weights[5] = 0.16f;
            }
            else if (playerLevel == 4)
            {
                weights[1] = 0.05f;
                weights[2] = 0.12f;
                weights[3] = 0.22f;
                weights[4] = 0.36f;
                weights[5] = 0.25f;
            }
            else // Lv 5+
            {
                weights[1] = 0.03f;
                weights[2] = 0.08f;
                weights[3] = 0.17f;
                weights[4] = 0.32f;
                weights[5] = 0.40f;
            }
        }

        // 2. Active Diversity Compensation
        if (activeCounts != null)
        {
            // If any level is missing from the water (0 active fish), strongly boost its weight (+80%)
            for (int lvl = 1; lvl <= effectiveMax; lvl++)
            {
                if (activeCounts[lvl] == 0)
                {
                    weights[lvl] *= 1.80f;
                }
            }

            // If minnows (Level 1) already account for >= 30% of population or >= 5 fish, throttle Level 1 down
            if (activeCounts[1] >= 5 || (totalActive > 5 && activeCounts[1] >= totalActive * 0.30f))
            {
                weights[1] *= 0.25f;
            }
        }

        // 3. Normalize & Roll
        float totalWeight = 0f;
        for (int i = 1; i <= effectiveMax; i++)
        {
            if (weights[i] < 0f) weights[i] = 0f;
            totalWeight += weights[i];
        }

        if (totalWeight <= 0.0001f) return 1;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 1; i <= effectiveMax; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative) return i;
        }

        return effectiveMax;
    }

    private bool CullObsoleteFish(int playerLevel, bool forceRecycle = false)
    {
        UpdateCameraCache();
        if (_cam == null) return false;
        
        Bounds viewBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 5f, _camHeight + 5f, 100f));
        Bounds distantBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 30f, _camHeight + 30f, 100f));

        // 1. Universal Cleanup: any fish that wandered far away outside distant bounds
        for (int i = 0; i < Fish.AllFish.Count; i++)
        {
            Fish fish = Fish.AllFish[i];
            if (fish == null) continue;
            if (!distantBounds.Contains(fish.transform.position))
            {
                fish.DespawnSelf();
                return true; 
            }
        }

        // 2. Population Full Recycle:
        if (forceRecycle)
        {
            // First priority: recycle off-screen Level 1 fish if minnows are plentiful
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish fish = Fish.AllFish[i];
                if (fish == null) continue;
                if (fish.Level == 1 && Time.time - fish.SpawnTime >= 3.0f && !viewBounds.Contains(fish.transform.position))
                {
                    fish.DespawnSelf();
                    return true;
                }
            }

            // Second priority: recycle any eligible off-screen fish
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish fish = Fish.AllFish[i];
                if (fish == null) continue;
                if (Time.time - fish.SpawnTime >= 3.5f && !viewBounds.Contains(fish.transform.position))
                {
                    fish.DespawnSelf();
                    return true;
                }
            }
        }

        return false;
    }
    
    private Camera _cam;
    private Vector3 _camPos;
    private float _camHeight;
    private float _camWidth;
    private float _halfWidth;
    private int _camCacheFrame;
    
    private void UpdateCameraCache()
    {
        if (Time.frameCount == _camCacheFrame) return;
        _cam = Camera.main;
        if (_cam == null) return;
        _camHeight = 2f * _cam.orthographicSize;
        _camWidth = _camHeight * _cam.aspect;
        _halfWidth = _camWidth / 2f;
        _camPos = _cam.transform.position;
        _camCacheFrame = Time.frameCount;
    }

    private IEnumerator SpawnHazardsRoutine()
    {
        isSpawningHazards = true;

        // Requirement 2: Strictly 1 boat at a time
        activeHazards.RemoveAll(h => h == null || !h.activeSelf);
        if (activeHazards.Count > 0 || (currentBoat != null && currentBoat.gameObject.activeInHierarchy) || (currentRiverBoat != null && currentRiverBoat.gameObject.activeInHierarchy)) 
        {
            isSpawningHazards = false;
            yield break;
        }

        Camera cam = _cam != null ? _cam : Camera.main;
        if (cam == null)
        {
            isSpawningHazards = false;
            yield break;
        }

        EnsureWorldBoundsCached();

        // Stop position: anywhere within the inner portion of the world background
        // (not camera-relative so it's independent of player position).
        // Inset 15% from each side so the boat doesn't stop right at the background edge
        float bgWidth = cachedWorldBgRight - cachedWorldBgLeft;
        float inset = bgWidth * 0.15f;
        float stopX = Random.Range(cachedWorldBgLeft + inset, cachedWorldBgRight - inset);

        bool isRiver = LevelManager.IsCurrentLakeLevel;

        if (cachedPlayerController == null)
        {
            cachedPlayerController = (player != null) ? player.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();
        }
        if (cachedPlayerController != null)
        {
            if (cachedBubbleMat == null) cachedBubbleMat = cachedPlayerController.BubbleMaterial;
            if (cachedBubbleTex == null) cachedBubbleTex = cachedPlayerController.BubbleTexture;
        }

        if (isRiver)
        {
            // River/Lake now uses the fisherman boat with straight fishing rods.
            EnsureCorrectBoatSprite();

            if (boatSprite != null)
            {
                if (boatTemplate == null)
                {
                    InitBoatTemplate();
                }

                bool startFromLeft = (Random.value > 0.5f);
                Vector3 spawnPos;
                FishermanBoat.CalculateSpawnPlacement(true, cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, startFromLeft, out spawnPos);

                GameObject boatObj = null;
                if (boatTemplate != null && ObjectPoolManager.Instance != null)
                {
                    boatObj = ObjectPoolManager.Instance.Spawn(boatTemplate, spawnPos, Quaternion.identity);
                }
                else if (boatTemplate != null)
                {
                    boatObj = Instantiate(boatTemplate, spawnPos, Quaternion.identity);
                    boatObj.SetActive(true);
                }
                else
                {
                    boatObj = new GameObject("FishermanBoat");
                    boatObj.transform.position = spawnPos;
                    boatObj.AddComponent<FishermanBoat>();
                }

                currentBoat = boatObj.GetComponent<FishermanBoat>();
                currentBoat.Initialize(boatSprite, stopX, cruiseFromOffscreen: true, cachedBubbleMat, cachedBubbleTex, cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, startFromLeft);
            }
            else
            {
                // Fallback if no boat sprite: directly spawn 1 rod.
                SpawnFishingRodForBoat(null, stopX, 0f);
            }
        }
        else
        {
            // Ocean now uses the lasering harpoon boat.
            EnsureCorrectRiverBoatSprite();
            if (riverBoatSprite != null)
            {
                if (riverBoatTemplate == null)
                {
                    InitRiverBoatTemplate();
                }

                bool startFromLeft = (Random.value > 0.5f);
                Vector3 spawnPos;
                RiverBoat.CalculateSpawnPlacement(true, cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, startFromLeft, out spawnPos);

                GameObject boatObj = null;
                if (riverBoatTemplate != null && ObjectPoolManager.Instance != null)
                {
                    boatObj = ObjectPoolManager.Instance.Spawn(riverBoatTemplate, spawnPos, Quaternion.identity);
                }
                else if (riverBoatTemplate != null)
                {
                    boatObj = Instantiate(riverBoatTemplate, spawnPos, Quaternion.identity);
                    boatObj.SetActive(true);
                }
                else
                {
                    boatObj = new GameObject("HarpoonBoat");
                    boatObj.transform.position = spawnPos;
                    boatObj.AddComponent<RiverBoat>();
                }

                currentRiverBoat = boatObj.GetComponent<RiverBoat>();
                currentRiverBoat.Initialize(riverBoatSprite, stopX, cruiseFromOffscreen: true, cachedBubbleMat, cachedBubbleTex, cachedWorldBgLeft, cachedWorldBgRight, cachedWorldSurfaceY, startFromLeft);
            }
        }

        isSpawningHazards = false;
    }

    /// <summary>
    /// Spawns a stationary fishing line hazard instance for a boat at the given world X position and depth.
    /// </summary>
    public Hazard SpawnFishingRodForBoat(FishermanBoat boat, float dropX, float depth)
    {
        float surfaceY = (boat != null) ? boat.WaterSurfaceY : 15.0f;
        float initialSpawnY = surfaceY + 15f + 1.5f;
        Vector3 hookSpawnPos = new Vector3(dropX, initialSpawnY, 0f);

        GameObject hazardObj = null;

        if (hazardPrefab != null)
        {
            if (ObjectPoolManager.Instance != null)
                hazardObj = ObjectPoolManager.Instance.Spawn(hazardPrefab, hookSpawnPos, Quaternion.identity);
            else
                hazardObj = Instantiate(hazardPrefab, hookSpawnPos, Quaternion.identity);
        }
        else if (hazardSprite != null)
        {
            if (hazardTemplate == null)
            {
                hazardTemplate = new GameObject("Hazard_Hook");
                hazardTemplate.transform.SetParent(transform);
                hazardTemplate.SetActive(false);
                
                SpriteRenderer sr = hazardTemplate.AddComponent<SpriteRenderer>();
                sr.sprite = hazardSprite;
                
                BoxCollider2D col = hazardTemplate.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                
                Hazard hz = hazardTemplate.AddComponent<Hazard>();
                hz.autoConfigureCollider = true;
                hazardTemplate.tag = "Enemy";
                hazardTemplate.transform.localScale = Vector3.one * hazardScale;
            }

            if (ObjectPoolManager.Instance != null)
                hazardObj = ObjectPoolManager.Instance.Spawn(hazardTemplate, hookSpawnPos, Quaternion.identity);
            else
                hazardObj = Instantiate(hazardTemplate, hookSpawnPos, Quaternion.identity);
            hazardObj.SetActive(true);

            SpriteRenderer objSr = hazardObj.GetComponent<SpriteRenderer>();
            if (objSr != null)
            {
                if (hazardSpriteVariant != null && Random.value > 0.5f)
                    objSr.sprite = hazardSpriteVariant;
                else
                    objSr.sprite = hazardSprite;

            }
        }
        else
        {
            hazardObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hazardObj.name = "Hazard_Fallback";
            Destroy(hazardObj.GetComponent<Collider>());
            
            BoxCollider2D col = hazardObj.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 1f);

            Hazard hz = hazardObj.AddComponent<Hazard>();
            hz.autoConfigureCollider = true;
            hazardObj.tag = "Enemy";
            
            Renderer r = hazardObj.GetComponent<Renderer>();
            if (r != null) r.material.color = Color.red;
            hazardObj.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            hazardObj.transform.position = hookSpawnPos;
        }

        if (hazardObj == null) return null;

        // Apply sleeker fishing rod scale
        hazardObj.transform.localScale = Vector3.one * hazardScale;

        SpriteRenderer spawnedSr = hazardObj.GetComponent<SpriteRenderer>();
        if (spawnedSr != null)
        {
            // Vary bait direction so every fisherman drop does not look cloned.
            spawnedSr.flipX = Random.value < 0.5f;
        }
        float halfExtents = (spawnedSr != null && spawnedSr.bounds.extents.y > 0.1f)
            ? spawnedSr.bounds.extents.y
            : (31.0f * hazardScale);
        hazardObj.transform.position = new Vector3(dropX, surfaceY + halfExtents + 0.5f, 0f);

        activeHazards.Add(hazardObj);

        Material pMat = null;
        Texture2D pTex = null;
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null)
            {
                pMat = pc.BubbleMaterial;
                pTex = pc.BubbleTexture;
            }
        }

        Hazard h = hazardObj.GetComponent<Hazard>();
        if (h != null)
        {
            h.Initialize(hazardSound, hazardBubblePrefab, pMat, pTex, depth, boat);
        }

        SpriteRenderer hSr = hazardObj.GetComponent<SpriteRenderer>();
        float halfHeight = (hSr != null) ? hSr.bounds.extents.y : 15f;
        float finalSpawnY = surfaceY + halfHeight + 1.5f;

        hazardObj.transform.position = new Vector3(dropX, finalSpawnY, 0);
        return h;
    }

    /// <summary>
    /// Spawns a high-speed tilted HarpoonHazard instance launched from the River Fisherman Boat.
    /// </summary>
    public HarpoonHazard SpawnHarpoonForBoat(RiverBoat boat, Vector3 launchPos, float tiltAngleDeg, float targetBottomY)
    {
        EnsureCorrectHarpoonSprite();
        GameObject harpoonObj = null;
        if (harpoonPrefab != null)
        {
            if (ObjectPoolManager.Instance != null)
                harpoonObj = ObjectPoolManager.Instance.Spawn(harpoonPrefab, launchPos, Quaternion.identity);
            else
                harpoonObj = Instantiate(harpoonPrefab, launchPos, Quaternion.identity);
        }
        else
        {
            if (harpoonTemplate == null)
            {
                harpoonTemplate = new GameObject("HarpoonHazard_Template");
                harpoonTemplate.transform.SetParent(transform);
                harpoonTemplate.SetActive(false);
                harpoonTemplate.tag = "Enemy";

                SpriteRenderer sr = harpoonTemplate.AddComponent<SpriteRenderer>();
                sr.sprite = harpoonSprite;
                sr.sortingOrder = 6;

                BoxCollider2D box = harpoonTemplate.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(0.70f, 2.52f);

                harpoonTemplate.transform.localScale = Vector3.one * 1.40f;
                harpoonTemplate.AddComponent<HarpoonHazard>();
            }

            if (ObjectPoolManager.Instance != null)
                harpoonObj = ObjectPoolManager.Instance.Spawn(harpoonTemplate, launchPos, Quaternion.identity);
            else
                harpoonObj = Instantiate(harpoonTemplate, launchPos, Quaternion.identity);
        }

        if (harpoonObj == null) return null;
        harpoonObj.SetActive(true);

        HarpoonHazard hazard = harpoonObj.GetComponent<HarpoonHazard>();
        if (hazard != null)
        {
            if (cachedPlayerController == null)
            {
                cachedPlayerController = (player != null) ? player.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();
            }
            Material bMat = cachedBubbleMat;
            Texture2D bTex = cachedBubbleTex;
            if (cachedPlayerController != null)
            {
                if (bMat == null) bMat = cachedPlayerController.BubbleMaterial;
                if (bTex == null) bTex = cachedPlayerController.BubbleTexture;
            }

            if (harpoonStabSound == null)
            {
#if UNITY_EDITOR
                harpoonStabSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Harpoon_Stab.mp3");
                if (harpoonStabSound == null)
                {
                    harpoonStabSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/harpoon_stab.wav");
                }
#endif
                if (harpoonStabSound == null) harpoonStabSound = Resources.Load<AudioClip>("Harpoon_Stab");
                if (harpoonStabSound == null) harpoonStabSound = Resources.Load<AudioClip>("harpoon_stab");
            }

            hazard.Initialize(boat, launchPos, tiltAngleDeg, targetBottomY, harpoonShootSound, harpoonReelSound, bMat, bTex, harpoonStabSound);
        }

        return hazard;
    }

    /// <summary>
    /// Called when the active boat has driven offscreen and is destroyed.
    /// </summary>
    public void OnBoatDeparted(FishermanBoat boat)
    {
        if (currentBoat == boat)
        {
            currentBoat = null;
        }
        boatCooldownTimer = Random.Range(boatCooldownRange.x, boatCooldownRange.y);
    }

    public void OnRiverBoatDeparted(RiverBoat boat)
    {
        if (currentRiverBoat == boat)
        {
            currentRiverBoat = null;
        }
        boatCooldownTimer = Random.Range(boatCooldownRange.x, boatCooldownRange.y);
    }

    private void SpawnShark()
    {
        if (activeShark != null && activeShark.activeSelf) return;

        // FIXED: Spawn based on World Coordinates (Arena) instead of Camera View.
        // This prevents the shark from feeling "attached" to the player's movement.
        
        // 1. Determine Direction (Left->Right or Right->Left)
        int direction = (Random.value > 0.5f) ? 1 : -1;

        // 2. Determine Spawn Position
        // X: Spawn closer to reduce warning time (approx 1 sec less travel time)
        // Original was +/- 55f. With speed ~9.5f, 1 sec is ~9.5 units.
        // New Spawn X: +/- 45.5f (55 - 9.5)
        float spawnX = (direction > 0) ? -45.5f : 45.5f; 
        
        // Y: Random height within the fixed game world (-14 to 14).
        // This makes the shark feel like it's patrolling the ocean, not chasing the camera.
        float spawnY = Random.Range(-14f, 14f);

        Vector3 spawnPos = new Vector3(spawnX, spawnY, 0);

        GameObject sharkObj = null;

        if (sharkPrefab != null)
        {
            if (ObjectPoolManager.Instance != null)
                sharkObj = ObjectPoolManager.Instance.Spawn(sharkPrefab, spawnPos, Quaternion.identity);
            else
                sharkObj = Instantiate(sharkPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // Fallback: Create from sprite
            // OPTIMIZATION: Use Template
            if (sharkTemplate == null)
            {
                sharkTemplate = new GameObject("Shark_Template");
                sharkTemplate.transform.SetParent(transform);
                sharkTemplate.SetActive(false);
                
                // Add Animator to Root (so it can control the Gfx child)
                Animator anim = sharkTemplate.AddComponent<Animator>();
                if (sharkAnimController != null)
                {
                    anim.runtimeAnimatorController = sharkAnimController;
                }

                // Create Graphics Child "Gfx" for animation compatibility
                GameObject gfx = new GameObject("Gfx");
                gfx.transform.SetParent(sharkTemplate.transform);
                gfx.transform.localPosition = Vector3.zero;

                SpriteRenderer sr = gfx.AddComponent<SpriteRenderer>();
                if (sharkSprite != null) sr.sprite = sharkSprite;
                
                // Collider on Root
                BoxCollider2D col = sharkTemplate.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                else col.size = new Vector2(2f, 1f);
                
                sharkTemplate.AddComponent<SharkHazard>();
                
                // No tag set in original code? Adding Enemy tag just in case, though SharkHazard handles collision logic.
                // Original code didn't set tag for Shark.
            }

            if (ObjectPoolManager.Instance != null)
                sharkObj = ObjectPoolManager.Instance.Spawn(sharkTemplate, spawnPos, Quaternion.identity);
            else
                sharkObj = Instantiate(sharkTemplate, spawnPos, Quaternion.identity);
            sharkObj.name = "Shark_Hazard";
            sharkObj.SetActive(true);
            
            // Apply Sprite (already default, but just in case we add variants later)
             SpriteRenderer sharkSr = sharkObj.GetComponentInChildren<SpriteRenderer>();
             if (sharkSr != null && sharkSprite != null) sharkSr.sprite = sharkSprite;

             // Note: Original code rotated capsule if no sprite. 
             // If we have sprite, we don't rotate.
             // If we don't have sprite (Emergency Fallback), we use primitive logic which is outside this block in original code?
             // Wait, original code had: if (sharkSprite != null) ... else { // Capsule }
             // My template logic handles sprite case.
             // If sharkSprite is null, template will have null sprite.
             
             if (sharkSprite == null)
             {
                 // Revert to emergency fallback for this instance if no sprite
                 Destroy(sharkObj); // Kill template instance
                 sharkObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                 Destroy(sharkObj.GetComponent<Collider>());
                 sharkObj.transform.rotation = Quaternion.Euler(0, 0, 90);
                 Renderer r = sharkObj.GetComponent<Renderer>();
                 if (r != null) r.material.color = Color.gray;
                 
                 BoxCollider2D col = sharkObj.AddComponent<BoxCollider2D>();
                 col.isTrigger = true;
                 col.size = new Vector2(2f, 1f);
                 
                 sharkObj.AddComponent<SharkHazard>();
                 sharkObj.transform.position = spawnPos;
             }
        }

        if (sharkObj != null)
        {
            activeShark = sharkObj;
            sharkCooldownTimer = Random.Range(sharkCooldownRange.x, sharkCooldownRange.y);
            
            SharkHazard shark = sharkObj.GetComponent<SharkHazard>();
            if (shark == null) shark = sharkObj.AddComponent<SharkHazard>();

            // Inject Effects (Bubble Material from Player)
            Material pMat = null;
            Texture2D pTex = null;
            if (player != null)
            {
                PlayerController pc = player.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pMat = pc.BubbleMaterial;
                    pTex = pc.BubbleTexture;
                }
            }

            shark.Initialize(direction, warningIconPrefab, warningIconSprite, sharkWarningSound, sharkAttackSound, sharkSwimSound, pMat, pTex);
        }
    }

    private void OrientFish(Fish fish, Vector2 spawnPos, Vector2 targetPos)
    {
        if (fish == null) return;


        // Initialize Fish Particles (User Request: All fish have eating bubbles)
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null)
            {
                fish.InitializeParticles(pc.BubbleMaterial, pc.BubbleTexture);
            }
        }

        Vector2 dir = (targetPos - spawnPos).normalized;
        fish.transform.rotation = Quaternion.identity;

        FishAI ai = fish.GetComponent<FishAI>();
        if (ai != null)
        {
            ai.SetInitialDirection(dir);
        }
        else
        {
            fish.FlipTowardsDestination(targetPos, false);
        }
    }

    #endregion
}
