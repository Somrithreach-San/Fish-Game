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
    private int maxFishCount = 50;
    [SerializeField]
    private float spawnInterval = 0.7f;
    [SerializeField]
    private GameObject goldenFishPrefab; // Custom prefab for the rare golden fish
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
    private float hazardScale = 0.8f; // Scale modifier for sprite-spawned hazard

    [Header("Hazard Effects")]
    [SerializeField]
    private AudioClip hazardSound;
    [SerializeField]
    private GameObject hazardBubblePrefab;

    [Header("Hazard Boat Settings")]
    [SerializeField]
    private Sprite boatSprite;
    private List<GameObject> activeBoats = new List<GameObject>();
    private FishermanBoat currentBoat = null;
    
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

        if (boatSprite == null)
        {
            boatSprite = Resources.Load<Sprite>("fisherman_hazard_boat");
            #if UNITY_EDITOR
            if (boatSprite == null)
            {
                Object[] subAssets = UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Graphics/Hazard/fisherman_hazard_boat.png");
                foreach (var sa in subAssets)
                {
                    if (sa is Sprite s) { boatSprite = s; break; }
                }
                if (boatSprite == null)
                {
                    boatSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/fisherman_hazard_boat.png");
                }
            }
            #endif
        }

        if (hazardPrefab != null && ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.PreWarm(hazardPrefab, 4);
        }
        else if (hazardSprite != null && ObjectPoolManager.Instance != null)
        {
            if (hazardTemplate == null)
            {
                hazardTemplate = new GameObject("Hazard_Template");
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
                player = spawnedObject;
        });
    }

    private void Start()
    {
        // Ensure hazard chance is reasonable
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

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;

            // 1. Hazard Spawn Check (Priority over Fish)
            // Allowed to spawn even if Fish count is maxed out
            // User Request: "More frequent both of it" -> Boosted chances
            // User Request: "increase the chanes of the hazrd hook more"
            
            // 1. Hazard Spawn Check (Priority over Fish)
            LevelConfig levelCfg = LevelManager.GetCurrentConfig();

            if (levelCfg.enableFishingRod && GameManager.PlayerLevel >= 2)
            {
                float effectiveHazardChance = Mathf.Max(hazardChance, 0.22f); // Reduced from 0.35f
                
                // Further boost for low levels since they don't have sharks
                if (GameManager.PlayerLevel <= 2) 
                {
                    effectiveHazardChance = 0.30f; // Reduced from 0.5f
                }

                // Clean up nulls
                activeHazards.RemoveAll(h => h == null);

                // Requirement 2: Strictly 1 boat appear at a time
                if (currentBoat == null && activeHazards.Count == 0 && !isSpawningHazards && Random.value < effectiveHazardChance)
                {
                    StartCoroutine(SpawnHazardsRoutine());
                    return; 
                }
            }

            // 1.5 Shark Spawn Check (Priority over Fish, Independent of Fisherman)
            // Can happen alongside other things, but limit to 1 active shark
            if (levelCfg.enableShark && GameManager.PlayerLevel >= 2)
            {
                // Shark appears more when user reaches higher levels
                float effectiveSharkChance = Mathf.Max(sharkChance, 0.10f);
                
                if (GameManager.PlayerLevel >= 4)
                {
                    effectiveSharkChance = 0.30f;
                }

                if (activeShark == null && Random.value < effectiveSharkChance)
                {
                    SpawnShark();
                    // If we spawn a shark, maybe skip fish spawning this frame to reduce chaos?
                    return;
                }
            }

            // 1.8 Parasite Hazard (Mind Control) - MOVED TO FISH INFECTION LOGIC
            // We no longer spawn a separate flying bug. Instead, we infect random fish.

            // 2. Fish Count Limit Check
            // OPTIMIZATION: Use static list from Fish class
            
            // Get player level early for culling check
            int playerLevel = GameManager.PlayerLevel;

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
            
            int predatorCount = 0;
            int apexCount = 0;
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                if (Fish.AllFish[i] == null) continue;
                if (Fish.AllFish[i].Level > playerLevel) predatorCount++;
                if (Fish.AllFish[i].Level >= playerLevel + 2) apexCount++;
            }

            int predatorCap = (playerLevel <= 2) ? 2 : 3;
            bool forceEatable = (predatorCount >= predatorCap);
            // In early levels (playerLevel <= 2), cap large apex predators (Level >= playerLevel + 2) to at most 1 active
            bool blockApex = (apexCount >= 1 && playerLevel <= 2);

            SpawnArenaFish(playerLevel, forceEatable, blockApex);
        }
    }

    private void SpawnArenaFish(int playerLevel, bool forceEatable, bool blockApex)
    {
        int count = 2;
        if (playerLevel >= 6) count = 5;
        else if (playerLevel >= 4) count = 3;
        
        // Calculate Camera View Boundaries
        if (_cam == null) return;
        
        // Spawn just outside the camera view (buffer of 2 units)
        float buffer = 2f;
        float rightEdge = _camPos.x + _halfWidth + buffer;
        float leftEdge = _camPos.x - _halfWidth - buffer;

        for (int i = 0; i < count; i++)
        {
            // Randomly choose Left or Right side relative to Camera
            float spawnX = (Random.value > 0.5f) ? rightEdge : leftEdge;
            
            // Random Y within world vertical bounds (-14 to 14)
            // But also clamp to be near camera Y to ensure visibility? 
            // Let's keep it within world bounds but maybe biased towards camera Y?
            // For now, world bounds -14 to 14 is safe.
            float spawnY = Random.Range(-14f, 14f); 

            // Add slight randomness to X
            spawnX += Random.Range(-1f, 1f);

            Vector2 spawnPos = new Vector2(spawnX, spawnY);
            
            // Difficulty Logic:
            
            // SPECIAL: Check for Golden Fish Spawn (Rare!)
            // Reduced spawn chances so golden fish feels rare and special
            float goldenChance = 0.02f; // 2% at Early Game (Level 1-2)
            if (playerLevel >= 5)
            {
                goldenChance = 0.10f; // 10% at End Game (Level 5+)
            }
            else if (playerLevel >= 4)
            {
                goldenChance = 0.07f; // 7% at Late Game (Level 4)
            }
            else if (playerLevel >= 3)
            {
                goldenChance = 0.04f; // 4% at Mid Game (Level 3)
            }

            if (Random.value < goldenChance)
            {
                // Spawn Golden Fish!
                Fish goldenPrefab = null;

                // Priority 1: User assigned prefab
                if (goldenFishPrefab != null)
                {
                    goldenPrefab = goldenFishPrefab.GetComponent<Fish>();
                }
                
                // Priority 2: Fallback to Level 1 Fish
                if (goldenPrefab == null)
                {
                    string goldenBaseName = "level 1 fish"; // Base prefab
                    goldenPrefab = enemyLibrary.GetPrefabByName(1, goldenBaseName);
                    if (goldenPrefab == null) goldenPrefab = enemyLibrary.GetRandomPrefab(1);
                }
                
                if (goldenPrefab != null)
                {
                    // Spawn it
                    Fish golden = enemyLibrary.SpawnSpecific(goldenPrefab, spawnPos, 0f, 0f, -1);
                    if (golden != null)
                    {
                        // Apply Golden Attributes
                        // If it's the custom prefab, it might already handle this in Start(), 
                        // but calling it again is safe due to our checks.
                        golden.SetGoldenStatus(true);
                        
                        // Force Level 1 so it can be eaten by Level 2+ enemy fish, Player, and Shark
                        golden.ForceLevel(1);

                        OrientFish(golden, spawnPos, new Vector2(_camPos.x, spawnPos.y));
                        return; // Done for this cycle
                    }
                }
            }

            // Open-Ocean Natural Ecosystem:
            // All fish inside this stage level can spawn, with apex tiers starting rare and scaling up with progress
            LevelConfig currentCfg = LevelManager.GetCurrentConfig();
            float levelProgress = GameManager.PlayerLevelProgress;
            int spawnLevel = CalculateSpawnLevel(playerLevel, levelProgress, currentCfg.maxEnemyLevel, forceEatable, blockApex);
            
            // Determine Prefab
            string targetName = "level " + spawnLevel + " fish";
            Fish prefabToSpawn = enemyLibrary.GetPrefabByName(spawnLevel, targetName);
            
            // Fallback if specific name not found (just in case)
            if (prefabToSpawn == null)
            {
                prefabToSpawn = enemyLibrary.GetRandomPrefab(spawnLevel);
            }
            
            if (prefabToSpawn != null)
            {
                // Verification: Ensure the prefab's level matches our intended spawn level
                if (prefabToSpawn.Level != spawnLevel)
                {
                    Debug.LogWarning($"Spawn Mismatch! Intended: {spawnLevel}, Prefab: {prefabToSpawn.name} has Level {prefabToSpawn.Level}");
                }

                // SCHOOLING LOGIC: Level 1 fish can form schools, but toned down so they don't drown the screen
                float schoolChance = (playerLevel == 1) ? 0.25f : 0.35f;

                if (spawnLevel == 1 && prefabToSpawn.name.Contains("level 1 fish") && Random.value < schoolChance)
                {
                    // Create School
                    GameObject schoolObj = new GameObject("FishSchool");
                    FishSchool school = schoolObj.AddComponent<FishSchool>();
                    bool movingRight = (spawnX < 0); 
                    school.Initialize(movingRight);
                    
                    // Reduced count at Level 1 to prevent instant level skipping (2-3 fish)
                    int schoolSize = (playerLevel == 1) ? Random.Range(2, 4) : Random.Range(3, 5);
                    
                    for (int s = 0; s < schoolSize; s++)
                    {
                        // "Natural Formation": 
                        // Use a slightly larger, irregular spread (0.5f to 1.5f radius)
                        // This prevents them from being too perfectly circular or too tight
                        Vector2 schoolOffset = Random.insideUnitCircle * Random.Range(0.5f, 2.0f);
                        
                        // Stretch horizontally to look like they are swimming in a line/group
                        schoolOffset.x *= 1.5f; 

                        Vector2 finalPos = spawnPos + schoolOffset;
                        finalPos.y = Mathf.Clamp(finalPos.y, -14f, 14f);
                        
                        // FIX: Don't override the prefab's inherent level. 
                        // The user has carefully set up prefabs with specific levels/sprites.
                        // Passing '0' or '-1' as overrideLevel to respect the prefab's data.
                        Fish fish = enemyLibrary.SpawnSpecific(prefabToSpawn, finalPos, 0f, 0f, -1);
                        if (fish != null)
                        {
                            fish.school = school;
                            fish.formationOffset = schoolOffset;
                        OrientFish(fish, finalPos, new Vector2(_camPos.x, finalPos.y));
                        }
                    }
                }
                else
                {
                    // Spawn Single (10% chance for L01-00, or 100% for others)
                    // FIX: Don't override level. Respect Prefab settings.
                    Fish fish = enemyLibrary.SpawnSpecific(prefabToSpawn, spawnPos, 0f, 0f, -1);
                    if (fish != null) OrientFish(fish, spawnPos, new Vector2(_camPos.x, spawnY));
                }
            }
            
            // Note: We don't save these to GridBlocks because they are temporary "passers-by"
        }
    }

    //==============================| Helpers |========================//

    private int CalculateSpawnLevel(int playerLevel, float levelProgress, int maxEnemyLevel, bool forceEatable, bool blockApex)
    {
        if (maxEnemyLevel <= 1) return 1;

        // Clamping
        if (playerLevel < 1) playerLevel = 1;
        if (maxEnemyLevel > 6) maxEnemyLevel = 6;

        // If forceEatable, only pick levels <= playerLevel
        int effectiveMax = forceEatable ? Mathf.Min(playerLevel, maxEnemyLevel) : maxEnemyLevel;
        if (effectiveMax <= 1) return 1;

        // Weights array for levels 1 to 6 (1-indexed, size 7)
        float[] weights = new float[7];

        if (forceEatable)
        {
            // Biomass pyramid among eatable fish
            if (playerLevel == 1)
            {
                weights[1] = 1.0f;
            }
            else if (playerLevel == 2)
            {
                weights[1] = 0.35f;
                weights[2] = 0.65f;
            }
            else if (playerLevel == 3)
            {
                weights[1] = 0.20f;
                weights[2] = 0.35f;
                weights[3] = 0.45f;
            }
            else if (playerLevel == 4)
            {
                weights[1] = 0.15f;
                weights[2] = 0.20f;
                weights[3] = 0.30f;
                weights[4] = 0.35f;
            }
            else if (playerLevel == 5)
            {
                weights[1] = 0.10f;
                weights[2] = 0.15f;
                weights[3] = 0.20f;
                weights[4] = 0.25f;
                weights[5] = 0.30f;
            }
            else // 6+
            {
                weights[1] = 0.10f;
                weights[2] = 0.10f;
                weights[3] = 0.15f;
                weights[4] = 0.20f;
                weights[5] = 0.20f;
                weights[6] = 0.25f;
            }
        }
        else
        {
            // Open Ocean Ecosystem: all fish inside that level can spawn from the start!
            // Profiles tailored per stage:
            if (maxEnemyLevel == 2)
            {
                if (playerLevel == 1)
                {
                    float lastLevelWeight = Mathf.Lerp(0.16f, 0.24f, levelProgress);
                    weights[2] = lastLevelWeight;
                    weights[1] = 1.0f - lastLevelWeight;
                }
                else
                {
                    weights[1] = 0.40f;
                    weights[2] = 0.60f;
                }
            }
            else if (maxEnemyLevel == 3) // Stage 2
            {
                if (playerLevel == 1)
                {
                    // Initial Phase: very little of last level fish (starts ~5%, grows to ~8% with XP)
                    float lastLevelWeight = blockApex ? 0f : Mathf.Lerp(0.05f, 0.08f, levelProgress);
                    float midLevelWeight = Mathf.Lerp(0.22f, 0.26f, levelProgress);
                    weights[3] = lastLevelWeight;
                    weights[2] = midLevelWeight;
                    weights[1] = 1.0f - (lastLevelWeight + midLevelWeight);
                }
                else if (playerLevel == 2)
                {
                    // Mid Phase: Level 2 is main food (52%), Level 3 increases to 18-25%
                    float lastLevelWeight = Mathf.Lerp(0.18f, 0.25f, levelProgress);
                    weights[3] = lastLevelWeight;
                    weights[2] = 0.52f;
                    weights[1] = 1.0f - (lastLevelWeight + 0.52f);
                }
                else // Level 3+
                {
                    weights[1] = 0.15f;
                    weights[2] = 0.35f;
                    weights[3] = 0.50f;
                }
            }
            else if (maxEnemyLevel == 4) // Stage 3
            {
                if (playerLevel == 1)
                {
                    float l4 = blockApex ? 0f : Mathf.Lerp(0.03f, 0.05f, levelProgress);
                    float l3 = blockApex ? 0f : Mathf.Lerp(0.07f, 0.10f, levelProgress);
                    float l2 = Mathf.Lerp(0.20f, 0.23f, levelProgress);
                    weights[4] = l4;
                    weights[3] = l3;
                    weights[2] = l2;
                    weights[1] = 1.0f - (l4 + l3 + l2);
                }
                else if (playerLevel == 2)
                {
                    float l4 = blockApex ? 0f : Mathf.Lerp(0.06f, 0.09f, levelProgress);
                    float l3 = Mathf.Lerp(0.16f, 0.22f, levelProgress);
                    weights[4] = l4;
                    weights[3] = l3;
                    weights[2] = 0.46f;
                    weights[1] = 1.0f - (l4 + l3 + 0.46f);
                }
                else if (playerLevel == 3)
                {
                    float l4 = Mathf.Lerp(0.18f, 0.26f, levelProgress);
                    weights[4] = l4;
                    weights[3] = 0.42f;
                    weights[2] = 0.25f;
                    weights[1] = 1.0f - (l4 + 0.42f + 0.25f);
                }
                else // Level 4+
                {
                    weights[1] = 0.12f;
                    weights[2] = 0.20f;
                    weights[3] = 0.30f;
                    weights[4] = 0.38f;
                }
            }
            else // Stage 4 (maxEnemyLevel == 5 or 6)
            {
                if (playerLevel == 1)
                {
                    float l6 = (maxEnemyLevel >= 6 && !blockApex) ? 0.008f : 0f;
                    float l5 = (maxEnemyLevel >= 5 && !blockApex) ? 0.018f : 0f;
                    float l4 = (!blockApex) ? 0.04f : 0f;
                    float l3 = (!blockApex) ? 0.08f : 0f;
                    float l2 = 0.20f;
                    weights[6] = l6;
                    weights[5] = l5;
                    weights[4] = l4;
                    weights[3] = l3;
                    weights[2] = l2;
                    weights[1] = 1.0f - (l6 + l5 + l4 + l3 + l2);
                }
                else if (playerLevel == 2)
                {
                    float l6 = (maxEnemyLevel >= 6 && !blockApex) ? 0.015f : 0f;
                    float l5 = (maxEnemyLevel >= 5 && !blockApex) ? 0.035f : 0f;
                    float l4 = (!blockApex) ? 0.08f : 0f;
                    float l3 = 0.16f;
                    weights[6] = l6;
                    weights[5] = l5;
                    weights[4] = l4;
                    weights[3] = l3;
                    weights[2] = 0.44f;
                    weights[1] = 1.0f - (l6 + l5 + l4 + l3 + 0.44f);
                }
                else if (playerLevel == 3)
                {
                    float l6 = (maxEnemyLevel >= 6 && !blockApex) ? 0.03f : 0f;
                    float l5 = (maxEnemyLevel >= 5 && !blockApex) ? 0.07f : 0f;
                    float l4 = 0.18f;
                    weights[6] = l6;
                    weights[5] = l5;
                    weights[4] = l4;
                    weights[3] = 0.40f;
                    weights[2] = 0.20f;
                    weights[1] = 1.0f - (l6 + l5 + l4 + 0.40f + 0.20f);
                }
                else if (playerLevel == 4)
                {
                    float l6 = (maxEnemyLevel >= 6 && !blockApex) ? 0.06f : 0f;
                    float l5 = (maxEnemyLevel >= 5) ? 0.18f : 0f;
                    weights[6] = l6;
                    weights[5] = l5;
                    weights[4] = 0.38f;
                    weights[3] = 0.22f;
                    weights[2] = 0.10f;
                    weights[1] = 1.0f - (l6 + l5 + 0.38f + 0.22f + 0.10f);
                }
                else if (playerLevel == 5)
                {
                    float l6 = (maxEnemyLevel >= 6) ? 0.22f : 0f;
                    weights[6] = l6;
                    weights[5] = 0.38f;
                    weights[4] = 0.22f;
                    weights[3] = 0.10f;
                    weights[2] = 0.05f;
                    weights[1] = 1.0f - (l6 + 0.38f + 0.22f + 0.10f + 0.05f);
                }
                else // Level 6+
                {
                    weights[6] = 0.35f;
                    weights[5] = 0.25f;
                    weights[4] = 0.18f;
                    weights[3] = 0.12f;
                    weights[2] = 0.06f;
                    weights[1] = 0.04f;
                }
            }
        }

        // Normalize weights up to effectiveMax
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
        
        // Define a bounding box for the visible area + margin
        // Fish outside this box are candidates for culling
        Bounds viewBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 5f, _camHeight + 5f, 100f));

        // Define a larger bounding box for "Distant" culling (Cleanup)
        // Any fish that wanders this far should be removed to free up memory/slots
        Bounds distantBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 30f, _camHeight + 30f, 100f));

        // Find a candidate
        foreach (var fish in Fish.AllFish)
        {
            if (fish == null) continue;

            // Universal Cleanup: fish that has wandered far away outside distant bounds
            if (!distantBounds.Contains(fish.transform.position))
            {
                fish.DespawnSelf();
                return true; 
            }

            // Population Full: recycle any fish that is currently off-screen (Feeding Frenzy style: don't target lower level fish specifically)
            if (forceRecycle)
            {
                if (!viewBounds.Contains(fish.transform.position))
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
        if (activeHazards.Count > 0 || currentBoat != null) 
        {
            isSpawningHazards = false;
            yield break;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            isSpawningHazards = false;
            yield break;
        }

        float camHeight = 2f * cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;
        float halfWidth = camWidth / 2f;
        float bgLimit = halfWidth * 0.45f;
        float center = cam.transform.position.x;
        float stopX = Random.Range(center - bgLimit, center + bgLimit);

        if (boatSprite != null)
        {
            GameObject boatObj = new GameObject("FishermanBoat");
            currentBoat = boatObj.AddComponent<FishermanBoat>();

            PlayerController pc = (player != null) ? player.GetComponent<PlayerController>() : null;
            if (pc == null) pc = FindFirstObjectByType<PlayerController>();
            Material bMat = (pc != null) ? pc.BubbleMaterial : null;
            Texture2D bTex = (pc != null) ? pc.BubbleTexture : null;

            currentBoat.Initialize(boatSprite, stopX, cruiseFromOffscreen: true, bMat, bTex);
        }
        else
        {
            // Fallback if no boat sprite: directly spawn 1 rod
            SpawnFishingRodForBoat(null, stopX, 0f);
        }

        isSpawningHazards = false;
    }

    /// <summary>
    /// Spawns a stationary fishing line hazard instance for a boat at the given world X position and depth.
    /// </summary>
    public Hazard SpawnFishingRodForBoat(FishermanBoat boat, float dropX, float depth)
    {
        GameObject hazardObj = null;

        if (hazardPrefab != null)
        {
            if (ObjectPoolManager.Instance != null)
                hazardObj = ObjectPoolManager.Instance.Spawn(hazardPrefab, Vector3.zero, Quaternion.identity);
            else
                hazardObj = Instantiate(hazardPrefab);
        }
        else if (hazardSprite != null)
        {
            if (hazardTemplate == null)
            {
                hazardTemplate = new GameObject("Hazard_Template");
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
                hazardObj = ObjectPoolManager.Instance.Spawn(hazardTemplate, Vector3.zero, Quaternion.identity);
            else
                hazardObj = Instantiate(hazardTemplate);
            hazardObj.name = "Hazard_Hook";
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
        }

        if (hazardObj == null) return null;

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

        Camera cam = Camera.main;
        float camHeight = (cam != null) ? (2f * cam.orthographicSize) : 16f;
        float camTop = (cam != null) ? (cam.transform.position.y + camHeight / 2f) : 8.0f;
        float spawnY = camTop + 6f;
        SpriteRenderer hSr = hazardObj.GetComponent<SpriteRenderer>();
        if (hSr != null)
        {
            spawnY = camTop + hSr.bounds.extents.y + 2f;
        }

        hazardObj.transform.position = new Vector3(dropX, spawnY, 0);
        return h;
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

        // Check if fish uses Rotation-based movement (FishAI or FishMovement)
        bool usesRotation = fish.GetComponent<FishAI>() != null || fish.GetComponent<FishMovement>() != null;

        if (usesRotation)
        {
            Vector2 dir = (targetPos - spawnPos).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            fish.transform.rotation = Quaternion.Euler(0, 0, angle);

            // Fix Upside Down if facing left
            if (Mathf.Abs(angle) > 90f)
            {
                Vector3 s = fish.transform.localScale;
                s.y = -Mathf.Abs(s.y);
                fish.transform.localScale = s;
            }
            else
            {
                // Ensure Y is positive if facing right
                Vector3 s = fish.transform.localScale;
                s.y = Mathf.Abs(s.y);
                fish.transform.localScale = s;
            }
        }
        else
        {
            // Use standard Flip (Scale X)
            // Ensure rotation is zero
            fish.transform.rotation = Quaternion.identity;
            
            // Flip towards target
            fish.FlipTowardsDestination(targetPos, false);
        }
    }

    #endregion
}
