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
        // Ensure ObjectPoolManager exists
        if (ObjectPoolManager.Instance == null)
        {
            GameObject poolObj = new GameObject("ObjectPoolManager");
            poolObj.AddComponent<ObjectPoolManager>();
        }
        
        _camCacheFrame = -1;

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
            
            // User Request: "start spawning the fishserman hazard only when player reaches level 2"
            if (GameManager.PlayerLevel >= 2)
            {
                float effectiveHazardChance = Mathf.Max(hazardChance, 0.35f); // Reduced max from 0.5f
                
                // Further boost for low levels since they don't have sharks
                if (GameManager.PlayerLevel <= 2) 
                {
                    effectiveHazardChance = 0.5f; // Reduced from 0.7f
                }

                // Clean up nulls
                activeHazards.RemoveAll(h => h == null);

                if (activeHazards.Count == 0 && !isSpawningHazards && Random.value < effectiveHazardChance)
                {
                    StartCoroutine(SpawnHazardsRoutine());
                    return; 
                }
            }

            // 1.5 Shark Spawn Check (Priority over Fish, Independent of Fisherman)
            // Can happen alongside other things, but limit to 1 active shark
            
            // User Request: "when player is still low level around level 1 -2 dont spawn the shark hazzard yet"
            if (GameManager.PlayerLevel > 2)
            {
                // User Request: "Shark should appear more when user reaches level 4 5 6"
                float effectiveSharkChance = Mathf.Max(sharkChance, 0.10f); // Reduced boost base from 0.15f
                
                if (GameManager.PlayerLevel >= 4)
                {
                    effectiveSharkChance = 0.30f; // Reduced from 0.45f (User Request: "reduce it abit")
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
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                if (Fish.AllFish[i].Level > playerLevel) predatorCount++;
            }

            int predatorCap = 3;
            if (playerLevel <= 2) predatorCap = 2;
            else if (playerLevel <= 4) predatorCap = 3;

            bool forceEatable = (predatorCount >= predatorCap);

            SpawnArenaFish(playerLevel, forceEatable);
        }
    }

    private void SpawnArenaFish(int playerLevel, bool forceEatable)
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
            // Chance: 5% (0.05) - Game Ready Setting
            // User Request: "the chances of golden fish at the late game is higer"
            float goldenChance = 0.05f;
            if (playerLevel >= 5)
            {
                goldenChance = 0.20f; // 20% at End Game (Level 5+)
            }
            else if (playerLevel >= 4)
            {
                goldenChance = 0.15f; // 15% at Late Game (Level 4)
            }
            else if (playerLevel >= 3)
            {
                goldenChance = 0.08f; // 8% at Mid Game
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
                        
                        // Force Level 1 so it can be eaten by Level 2+ fish and the Player
                        golden.ForceLevel(1);

                        OrientFish(golden, spawnPos, new Vector2(_camPos.x, spawnPos.y));
                        return; // Done for this cycle
                    }
                }
            }

            // Logic slides with Player Level.
            
            int spawnLevel = 1;
            
        float eatableChance = (playerLevel >= 4) ? 0.92f : 0.86f; 
            
            // If we hit the predator cap, we FORCE eatable fish (100% chance)
            if (forceEatable)
            {
                eatableChance = 1.0f;
            }
            
            if (Random.value < eatableChance)
            {
                // === EATABLE POOL ===
                
                // User Request: "Spawn less and less lower level fish but keep spawning them"
                // Dynamic Distribution:
                // Base chance for Lower Level starts at 50% (Level 2) and drops to ~30% (Level 8+)
                // This ensures we always have some lower level fish (popcorn) but focus shifts to current level.
                // Adjusted min from 0.20f to 0.30f to prevent extinction.
                float lowerLevelChance = Mathf.Clamp(0.55f - (playerLevel * 0.05f), 0.30f, 0.50f);
                if (playerLevel >= 4) lowerLevelChance = Mathf.Clamp(lowerLevelChance + 0.08f, 0.30f, 0.70f);
                
                if (playerLevel > 1 && Random.value < lowerLevelChance)
                {
                     // Spawn any lower level (1 to PlayerLevel-1)
                     spawnLevel = Random.Range(1, playerLevel);
                }
                else
                {
                     // Spawn fish of the current player level
                     spawnLevel = playerLevel;
                }
            }
            else
            {
                // === PREDATOR POOL (20% Total) ===
                // Spawn fish of the next level
                spawnLevel = playerLevel + 1;
            }

            if (spawnLevel > 6) spawnLevel = 6;
            
            // Debug Log for verification
            // Debug.Log($"Spawning Level {spawnLevel} Fish (Player Level: {playerLevel}) | Eatable Chance: {eatableChance}");

            // Determine Prefab
            // User Request: Check for prefab name correctly and check their assigned level
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
                    // We continue spawning, as the user might have custom setups, but we warned them.
                }

                // SCHOOLING LOGIC: Level 1, L01-00 (level 1 fish)
                // User Request: "For the entry level i want u to spawn more schooling fish. than the level 2"
                float schoolChance = 0.1f; // Default low chance (10%) for Level 2+
                
                if (GameManager.PlayerLevel == 1)
                {
                    schoolChance = 0.6f; // High chance (60%) for Level 1
                }

                if (spawnLevel == 1 && prefabToSpawn.name.Contains("level 1 fish") && Random.value < schoolChance)
                {
                    // Create School
                    GameObject schoolObj = new GameObject("FishSchool");
                    FishSchool school = schoolObj.AddComponent<FishSchool>();
                    bool movingRight = (spawnX < 0); 
                    school.Initialize(movingRight);
                    
                    // User Request: "group of babies fish form together"
                    // Reduced count to prevent crowding/jitter (3 to 5 fish)
                    int schoolSize = Random.Range(3, 6);
                    
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

    private bool CullObsoleteFish(int playerLevel, bool forceRecycle = false)
    {
        UpdateCameraCache();
        if (_cam == null) return false;
        
        // Define a bounding box for the visible area + margin
        // Fish outside this box are candidates for culling
        Bounds viewBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 5f, _camHeight + 5f, 100f));

        // Define a larger bounding box for "Distant" culling (Cleanup)
        // Any fish that wanders this far (high level or not) should be removed to free up memory/slots
        Bounds distantBounds = new Bounds(new Vector3(_camPos.x, _camPos.y, 0), new Vector3(_camWidth + 30f, _camHeight + 30f, 100f));

        // Find a candidate
        foreach (var fish in Fish.AllFish)
        {
            if (fish == null) continue;

            // Condition 0: Is Way Off-Screen? (Universal Cleanup)
            // This handles High-Level fish that leave the screen and keep going.
            if (!distantBounds.Contains(fish.transform.position))
            {
                fish.DespawnSelf();
                return true; 
            }

            // Condition 1: Is Obsolete? (Lower level than player)
            // Only cull obsolete fish if we are FORCED to recycle (e.g. population full)
            if (forceRecycle && fish.Level < playerLevel)
            {
                // Condition 2: Is Off-Screen?
                if (!viewBounds.Contains(fish.transform.position))
                {
                    // Found one! Cull it.
                    fish.DespawnSelf();
                    return true; // Culled one, job done.
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

        // Double check count (activeHazards should be empty when calling this, but safety first)
        activeHazards.RemoveAll(h => h == null || !h.activeSelf);
        if (activeHazards.Count > 0) 
        {
            isSpawningHazards = false;
            yield break;
        }

        // Randomly decide how many to spawn: 1 or 2
        // User Request: "sometime 1 sometime 2"
        int spawnCount = (Random.value > 0.5f) ? 2 : 1;
        
        for (int i = 0; i < spawnCount; i++)
        {
            // Delay for the second one to desynchronize
            if (i > 0)
            {
                // Random delay between 0.5s and 2.5s
                yield return new WaitForSeconds(Random.Range(0.5f, 2.5f));
            }

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
                // Fallback: Create hazard from sprite
                // OPTIMIZATION: Use Template
                if (hazardTemplate == null)
                {
                    hazardTemplate = new GameObject("Hazard_Template");
                    hazardTemplate.transform.SetParent(transform);
                    hazardTemplate.SetActive(false);
                    
                    SpriteRenderer sr = hazardTemplate.AddComponent<SpriteRenderer>();
                    sr.sprite = hazardSprite; // Default
                    
                    BoxCollider2D col = hazardTemplate.AddComponent<BoxCollider2D>();
                    col.isTrigger = true;
                    if (sr.sprite != null) col.size = sr.sprite.bounds.size;
                    
                    Hazard hz = hazardTemplate.AddComponent<Hazard>();
                    hz.autoConfigureCollider = true; // Enable auto-config for procedural hazards
                    hazardTemplate.tag = "Enemy";
                    hazardTemplate.transform.localScale = Vector3.one * hazardScale;
                }

                if (ObjectPoolManager.Instance != null)
                    hazardObj = ObjectPoolManager.Instance.Spawn(hazardTemplate, Vector3.zero, Quaternion.identity);
                else
                    hazardObj = Instantiate(hazardTemplate);
                hazardObj.name = "Hazard_Hook_" + i;
                hazardObj.SetActive(true);

                // Randomly choose between default and variant if available
                SpriteRenderer objSr = hazardObj.GetComponent<SpriteRenderer>();
                if (objSr != null)
                {
                    if (hazardSpriteVariant != null && Random.value > 0.5f)
                    {
                        objSr.sprite = hazardSpriteVariant;
                    }
                    else
                    {
                        objSr.sprite = hazardSprite;
                    }
                }
            }
            else
            {
                // EMERGENCY FALLBACK: Red Quad
                Debug.LogWarning("[GridController] Hazard Prefab AND Sprite are missing! Spawning Red Quad fallback.");
                hazardObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                hazardObj.name = "Hazard_Fallback_" + i;
                Destroy(hazardObj.GetComponent<Collider>()); // Remove 3D collider
                
                BoxCollider2D col = hazardObj.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(1f, 1f);

                Hazard hz = hazardObj.AddComponent<Hazard>();
                hz.autoConfigureCollider = true; // Enable auto-config for fallback
                hazardObj.tag = "Enemy";
                
                Renderer r = hazardObj.GetComponent<Renderer>();
                if (r != null) r.material.color = Color.red;
                
                hazardObj.transform.localScale = new Vector3(1.5f, 1.5f, 1f); // Visible size
            }

            if (hazardObj != null)
            {
                activeHazards.Add(hazardObj); // Track it

                // Calculate Random Depth
                // User Request: "sometime spawn the fisherman hazard shallow or deep"
                // Range: -13f (Deep) to 5f (Shallow). 
                // We keep 12f as theoretical max shallow, but let's be more specific.
                
                float randomDepth;
                float roll = Random.value;
                
                if (roll < 0.4f) // 40% Deep
                {
                    randomDepth = Random.Range(-13f, -5f);
                }
                else if (roll < 0.8f) // 40% Mid/Shallow
                {
                    randomDepth = Random.Range(-5f, 5f);
                }
                else // 20% Very Shallow (Surface skim)
                {
                    randomDepth = Random.Range(5f, 10f);
                }

                // Inject Effects & Depth
                Hazard h = hazardObj.GetComponent<Hazard>();
                if (h != null)
                {
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
                    h.Initialize(hazardSound, hazardBubblePrefab, pMat, pTex, randomDepth);
                }

                Camera cam = Camera.main;
                if (cam != null)
                {
                    float camHeight = 2f * cam.orthographicSize;
                    float camWidth = camHeight * cam.aspect;
                    float halfWidth = camWidth / 2f;
                    
                    float bgLimit = halfWidth - 1f; 
                    float spawnX = 0f;

                    // Improved Distribution Logic for 2 Hazards
                    if (spawnCount == 2)
                    {
                        // Split screen into two zones: Left and Right
                        // i=0: Randomly pick Left or Right
                        // i=1: Pick the other side
                        
                        // We can just use 'i' to determine side if we randomize the starting side
                        // But let's be explicit.
                        
                        float quarterWidth = bgLimit / 2f;
                        
                        // RESET spawnX calculation for clarity
                        float center = cam.transform.position.x;
                        
                        if (i == 0)
                        {
                             // Pick a random side for the first one
                             bool startLeft = (Random.value > 0.5f);
                             if (startLeft) spawnX = Random.Range(center - bgLimit, center - 2f);
                             else spawnX = Random.Range(center + 2f, center + bgLimit);
                        }
                        else
                        {
                             // Second one: Check previous
                             float prevX = center;
                             if (activeHazards.Count > 1) prevX = activeHazards[activeHazards.Count - 2].transform.position.x;
                             
                             if (prevX < center)
                             {
                                 // Previous was Left -> Spawn Right
                                 spawnX = Random.Range(center + 2f, center + bgLimit);
                             }
                             else
                             {
                                 // Previous was Right -> Spawn Left
                                 spawnX = Random.Range(center - bgLimit, center - 2f);
                             }
                        }
                    }
                    else
                    {
                        // Single spawn: Pure random
                        spawnX = Random.Range(cam.transform.position.x - bgLimit, cam.transform.position.x + bgLimit);
                    }

                    float spawnY = 22f;
                    
                    // Dynamic Spawn Y: Always spawn above the camera view
                    // This prevents "popping" in if the camera moves or if the sprite is long
                    SpriteRenderer sr = hazardObj.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        float halfHeight = sr.bounds.extents.y;
                        float camTop = cam.transform.position.y + (camHeight / 2f);
                        spawnY = camTop + halfHeight + 5f; // Buffer to be safe
                    }
                    else
                    {
                        float camTop = cam.transform.position.y + (camHeight / 2f);
                        spawnY = camTop + 10f;
                    }
                    
                    hazardObj.transform.position = new Vector3(spawnX, spawnY, 0);
                }
            }
        }
        
        isSpawningHazards = false;
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
