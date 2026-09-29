using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class HungrySharkFishAI : MonoBehaviour
{
    public enum FishType { PassivePrey, AggressivePredator }
    public enum FishState { Wandering, Flocking, Fleeing, Attacking }

    [Header("Behavior Setup")]
    public FishType fishType = FishType.PassivePrey;
    public FishState currentState = FishState.Wandering;

    [Header("Movement Speeds")]
    public float wanderSpeed = 3.0f;
    public float flockSpeed = 3.6f;
    public float fleeSpeed = 4.2f; // Moderated from 5.4f so fish don't hyper-rocket away
    public float attackSpeed = 5.2f; // Balanced from 6.2f for natural predator chase
    public float turnRotationSpeed = 8.0f;

    [Header("Sensing & Detection")]
    public string sharkTag = "Player";
    public float sharkDetectionRadius = 3.6f; // Moderated from 5.0f: fish only flee when player is genuinely close
    public float predatorAttackRadius = 4.2f; // Moderated from 5.5f: predators hunt within closer range
    public float flockDetectionRadius = 4.0f;
    public float aiFleeRadius = 3.2f; // Moderated from 4.2f: prey only flee when larger AI is near

    [Header("Hunger & Satiety System")]
    [Tooltip("0 = Fully Fed Up / Satiated, 100 = Starving")]
    [Range(0f, 100f)]
    public float currentHunger = 60f;
    public float hungerThreshold = 55f; // Must reach this hunger level to actively hunt
    public float satietyThreshold = 20f; // Below this, predator is completely fed up / content
    public float hungerIncreaseRate = 3.5f; // Meal digestion speed: takes ~15-20s to get hungry again
    public float hungerReductionPerFish = 35f; // Eating 2-3 fish fills up the predator (not full too fast)
    public float postEatDigestCooldown = 4.0f; // Brief pause after each meal before next hunt
    private float postEatCooldownTimer = 0f;

    public bool IsSatiated => currentHunger <= satietyThreshold;
    public bool IsHungry => currentHunger >= hungerThreshold;

    [Header("Boids Flocking Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 1.0f;

    [Header("Obstacle Avoidance")]
    public LayerMask obstacleLayer;
    public float lookAheadLength = 2.0f;
    public float avoidanceForceMultiplier = 12.0f;

    [Header("Procedural Swim Animation")]
    public bool enableBodyTilt = false; // In 2D sprite games (Feeding Frenzy style), fish stay upright (0 deg)
    public float swimWiggleFrequency = 10f;
    public float swimWiggleMagnitude = 8f;

    // Runtime Cached Variables
    private Rigidbody2D rb;
    private Transform threatTarget;
    private Transform attackTarget;
    private Vector2 wanderDirection = Vector2.right;
    private Vector2 currentMoveDirection = Vector2.right;
    private float changeWanderTimer;
    private Fish fishData;
    private float currentSpeed;
    private float inkDisorientTimer = 0f;
    public bool IsDisorientedByInk => inkDisorientTimer > 0f;
    private float fleeCommitTimer = 0f;
    private float attackCommitTimer = 0f;
    private float flipCooldownTimer = 0f;
    private const float MIN_FLIP_COOLDOWN = 0.32f;
    private const float FLIP_HORIZONTAL_DEADZONE = 0.28f;
    private static List<HungrySharkFishAI> allFishInScene = new List<HungrySharkFishAI>();

    void OnEnable()
    {
        if (!allFishInScene.Contains(this))
            allFishInScene.Add(this);
    }

    void OnDisable()
    {
        allFishInScene.Remove(this);
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Dynamic body ensures triggers work with static colliders (harpoons, fishing lines, boundaries)
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        fishData = GetComponent<Fish>();

        // Disable older conflicting AI components if present
        var oldAI = GetComponent<FishAI>();
        if (oldAI != null) oldAI.enabled = false;
        var stateCtrl = GetComponent<StateController>();
        if (stateCtrl != null) stateCtrl.enabled = false;
        var movement = GetComponent<FishMovement>();
        if (movement != null) movement.enabled = false;

        if (obstacleLayer.value == 0)
        {
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            if (terrainLayer != -1) obstacleLayer = 1 << terrainLayer;
        }
    }

    private bool initialDirectionSet = false;

    void Start()
    {
        if (fishData == null) fishData = GetComponent<Fish>();
        currentHunger = Random.Range(45f, 75f); // Stagger initial hunger among fish
        if (!initialDirectionSet)
        {
            ChooseRandomHeading();
            currentMoveDirection = wanderDirection;
        }

        // Enforce scale matching facing on start:
        Vector3 scale = transform.localScale;
        bool faceRight = (fishData != null) ? fishData.IsFacingRight : (currentMoveDirection.x >= 0f);
        scale.x = faceRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        scale.y = Mathf.Abs(scale.y);
        transform.localScale = scale;
        if (fishData != null) fishData.IsFacingRight = faceRight;
    }

    void Update()
    {
        if (inkDisorientTimer > 0f)
            inkDisorientTimer -= Time.deltaTime;

        if (fleeCommitTimer > 0f)
            fleeCommitTimer -= Time.deltaTime;

        if (attackCommitTimer > 0f)
            attackCommitTimer -= Time.deltaTime;

        if (flipCooldownTimer > 0f)
            flipCooldownTimer -= Time.deltaTime;

        if (postEatCooldownTimer > 0f)
            postEatCooldownTimer -= Time.deltaTime;

        // Gradual digestion: hunger steadily recovers towards 100
        if (currentHunger < 100f)
            currentHunger = Mathf.Min(100f, currentHunger + hungerIncreaseRate * Time.deltaTime);

        EvaluateStateEngine();
    }

    void FixedUpdate()
    {
        Vector2 finalMoveVector = CalculateSystemVectors();
        ExecuteMovementAndSway(finalMoveVector);
    }

    // ==========================================
    // 1. FINITE STATE MACHINE (FSM) EVALUATION
    // ==========================================
    private void EvaluateStateEngine()
    {
        // Level completion stops all hunting & fleeing; fish cruise peacefully in ambient wander
        if (LevelManager.IsLevelCompleted)
        {
            currentState = FishState.Wandering;
            threatTarget = null;
            attackTarget = null;
            return;
        }

        // Disorientation from cuttlefish ink disables hunting/fleeing
        if (inkDisorientTimer > 0f)
        {
            currentState = FishState.Wandering;
            threatTarget = null;
            attackTarget = null;
            return;
        }

        // Dynamically determine predator/prey role relative to player
        DetermineDynamicFishType();

        // 1. CHECK THREATS (Shark Hazards, Dangerous Player, Larger AI Fish)
        Transform nearestThreat = FindNearestThreat();
        if (nearestThreat != null)
        {
            threatTarget = nearestThreat;
            currentState = FishState.Fleeing;
            fleeCommitTimer = 1.2f; // Healthy flee commitment so fish swims cleanly away
            attackTarget = null;
            return;
        }

        // Keep fleeing until threat is safely far away AND commit timer expires (hysteresis)
        if (fleeCommitTimer > 0f && threatTarget != null && threatTarget.gameObject.activeInHierarchy)
        {
            float safeExitDistance = sharkDetectionRadius * 1.45f;
            if (Vector2.Distance(transform.position, threatTarget.position) < safeExitDistance)
            {
                currentState = FishState.Fleeing;
                return;
            }
        }
        threatTarget = null;

        // 2. CHECK PREY / ATTACK TARGETS (Only if hungry and not in post-eat digestion pause)
        if ((fishData == null || (!fishData.IsSickFish && !fishData.IsCuttlefish)) && IsHungry && postEatCooldownTimer <= 0f)
        {
            // Maintain chase if target still valid and close enough (hysteresis)
            if (attackCommitTimer > 0f && attackTarget != null && attackTarget.gameObject.activeInHierarchy)
            {
                float chaseGiveUpDistance = predatorAttackRadius * 1.35f;
                if (Vector2.Distance(transform.position, attackTarget.position) < chaseGiveUpDistance)
                {
                    currentState = FishState.Attacking;
                    return;
                }
            }

            Transform nearestPrey = FindNearestPrey();
            if (nearestPrey != null)
            {
                attackTarget = nearestPrey;
                currentState = FishState.Attacking;
                attackCommitTimer = 1.8f;
                return;
            }
        }
        attackTarget = null;

        // 3. FLOCKING vs WANDERING
        // CRITICAL FIX: Only count peers swimming in the SAME general horizontal direction!
        // Opposite-heading fish must not try to flock together and cancel out into twitching!
        int nearbyMatchingPeerCount = 0;
        int myLevel = (fishData != null) ? fishData.Level : 1;
        float myHorizontalSign = Mathf.Sign(wanderDirection.x != 0f ? wanderDirection.x : transform.localScale.x);

        for (int i = 0; i < allFishInScene.Count; i++)
        {
            var other = allFishInScene[i];
            if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
            int otherLevel = (other.fishData != null) ? other.fishData.Level : 1;

            if (otherLevel == myLevel)
            {
                float otherHorizontalSign = Mathf.Sign(other.wanderDirection.x != 0f ? other.wanderDirection.x : other.transform.localScale.x);
                if (Mathf.Approximately(myHorizontalSign, otherHorizontalSign))
                {
                    if (Vector2.Distance(transform.position, other.transform.position) <= flockDetectionRadius)
                    {
                        nearbyMatchingPeerCount++;
                    }
                }
            }
        }

        if (nearbyMatchingPeerCount >= 2)
        {
            currentState = FishState.Flocking;
        }
        else
        {
            currentState = FishState.Wandering;

            // Manage internal wander clock
            changeWanderTimer -= Time.deltaTime;
            if (changeWanderTimer <= 0) ChooseRandomHeading();
        }
    }

    private Transform FindNearestThreat()
    {
        Transform nearest = null;
        float minDist = float.MaxValue;
        int myLevel = (fishData != null) ? fishData.Level : 1;

        // A. Shark Hazards (All AI fish flee charging sharks)
        if (SharkHazard.ActiveSharks != null)
        {
            for (int i = 0; i < SharkHazard.ActiveSharks.Count; i++)
            {
                var shark = SharkHazard.ActiveSharks[i];
                if (shark == null || !shark.gameObject.activeInHierarchy) continue;
                float d = Vector2.Distance(transform.position, shark.transform.position);
                if (d < 9.0f && d < minDist)
                {
                    nearest = shark.transform;
                    minDist = d;
                }
            }
        }

        // B. Player (If player is big enough to eat this fish)
        PlayerController player = GetPlayer();

        if (player != null && player.gameObject.activeInHierarchy && player.IsAlive)
        {
            bool playerCanEatMe = player.Level >= myLevel || fishData == null || fishData.IsGoldenFish;
            if (playerCanEatMe)
            {
                float d = Vector2.Distance(transform.position, player.transform.position);
                if (d < sharkDetectionRadius && d < minDist)
                {
                    nearest = player.transform;
                    minDist = d;
                }
            }
        }

        // C. Larger AI Fish (Prey flees from predators)
        for (int i = 0; i < allFishInScene.Count; i++)
        {
            var other = allFishInScene[i];
            if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
            if (other.fishData == null || other.fishData.IsDead) continue;
            if (other.fishData.IsSickFish || other.fishData.IsGoldenFish || other.fishData.IsCuttlefish) continue;

            if (other.fishData.Level > myLevel)
            {
                float d = Vector2.Distance(transform.position, other.transform.position);
                if (d < aiFleeRadius && d < minDist)
                {
                    nearest = other.transform;
                    minDist = d;
                }
            }
        }

        return nearest;
    }

    private Transform FindNearestPrey()
    {
        // Only actively hunt if hungry, not in post-eat digestion pause, and level not completed
        if (!IsHungry || postEatCooldownTimer > 0f || LevelManager.IsLevelCompleted) return null;

        Transform nearest = null;
        float minDist = predatorAttackRadius;
        int myLevel = (fishData != null) ? fishData.Level : 1;

        // A. Check Player (If this predator is higher level than the player)
        PlayerController player = GetPlayer();

        if (player != null && player.gameObject.activeInHierarchy && player.IsAlive && !LevelManager.IsLevelCompleted && myLevel > player.Level)
        {
            float d = Vector2.Distance(transform.position, player.transform.position);
            if (d < minDist)
            {
                nearest = player.transform;
                minDist = d;
            }
        }

        // B. Check Smaller AI Fish
        for (int i = 0; i < allFishInScene.Count; i++)
        {
            var other = allFishInScene[i];
            if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
            if (other.fishData == null || other.fishData.IsDead || other.fishData.IsHooked) continue;

            if (myLevel > other.fishData.Level)
            {
                float d = Vector2.Distance(transform.position, other.transform.position);
                if (d < minDist)
                {
                    nearest = other.transform;
                    minDist = d;
                }
            }
        }

        return nearest;
    }

    private void DetermineDynamicFishType()
    {
        if (fishData == null) return;

        if (fishData.IsSickFish || fishData.IsCuttlefish || fishData.IsGoldenFish)
        {
            fishType = FishType.PassivePrey;
            return;
        }

        int playerLevel = GameManager.PlayerLevel;
        PlayerController player = GetPlayer();
        if (player != null) playerLevel = player.Level;

        if (fishData.Level > playerLevel)
        {
            fishType = FishType.AggressivePredator;
        }
        else
        {
            fishType = FishType.PassivePrey;
        }
    }

    // ==========================================
    // 2. VECTOR ALGEBRA ENGINE (THE SWIMMING LOGIC)
    // ==========================================
    private Vector2 CalculateSystemVectors()
    {
        // Schooling integration: If part of an active FishSchool, swim in school formation unless fleeing
        if (fishData != null && fishData.school != null && currentState != FishState.Fleeing)
        {
            FishSchool school = fishData.school;
            Vector2 schoolCenter = (Vector2)school.transform.position;
            Vector2 schoolDir = school.TravelDirection;

            Vector2 slotOffset = fishData.formationOffset;
            // Trailing fish must always trail behind the school leader
            slotOffset.x = school.MovingRight ? -Mathf.Abs(slotOffset.x) : Mathf.Abs(slotOffset.x);

            // Dynamic organic undulation: subtle swimming phase offset so fish breathe naturally
            float pHash = (GetHashCode() & 0x7FFF);
            float waveX = Mathf.Sin(Time.time * 2.5f + pHash * 0.17f) * 0.14f;
            float waveY = Mathf.Cos(Time.time * 2.0f + pHash * 0.23f) * 0.12f;
            Vector2 organicUndulation = new Vector2(waveX, waveY);

            Vector2 idealPos = schoolCenter + slotOffset + organicUndulation;
            Vector2 toSlot = idealPos - (Vector2)transform.position;

            // Soft mutual repulsion between schoolmates so they never overlap
            Vector2 schoolRepulsion = Vector2.zero;
            if (school.RegisteredFishList != null)
            {
                var mates = school.RegisteredFishList;
                for (int m = 0; m < mates.Count; m++)
                {
                    var mate = mates[m];
                    if (mate != null && mate != fishData && mate.gameObject.activeInHierarchy && !mate.IsDead)
                    {
                        Vector2 diff = (Vector2)transform.position - (Vector2)mate.transform.position;
                        float distSqr = diff.sqrMagnitude;
                        if (distSqr > 0.0001f && distSqr < 0.64f) // within 0.8m
                        {
                            float dist = Mathf.Sqrt(distSqr);
                            schoolRepulsion += (diff / dist) * ((0.80f - dist) / 0.80f) * 0.45f;
                        }
                    }
                }
            }

            Vector2 schoolHeading = (schoolDir * 1.5f + toSlot * 1.3f + schoolRepulsion).normalized;
            return schoolHeading;
        }

        // When disoriented and blinded by cephalopod ink, fish loses bearings and wavers erratically
        if (inkDisorientTimer > 0f)
        {
            float pHash = (GetHashCode() & 0x7FFF);
            float wobbleY = Mathf.Sin(Time.time * 5.5f + pHash * 0.17f) * 0.70f;
            float facingSign = (fishData != null && !fishData.IsFacingRight) ? -1f : (transform.localScale.x < 0f ? -1f : 1f);
            Vector2 disorientedVector = new Vector2(facingSign * 0.55f, wobbleY).normalized;
            disorientedVector = ApplyWaterBoundsConstraint(disorientedVector);
            disorientedVector = ApplyTerrainAvoidance(disorientedVector);
            return disorientedVector;
        }

        Vector2 primaryVector = Vector2.zero;

        switch (currentState)
        {
            case FishState.Wandering:
                primaryVector = wanderDirection;
                break;

            case FishState.Fleeing:
                if (threatTarget != null)
                {
                    Vector2 away = (Vector2)transform.position - (Vector2)threatTarget.position;
                    // If threat is directly above or below, decisively escape to the side opposite threat
                    if (Mathf.Abs(away.x) < 0.35f)
                    {
                        float side = (transform.position.x >= threatTarget.position.x) ? 1f : -1f;
                        away.x = side * 0.8f;
                    }
                    primaryVector = away.normalized;
                }
                else
                {
                    primaryVector = wanderDirection;
                }
                break;

            case FishState.Attacking:
                if (attackTarget != null)
                {
                    Vector2 toward = (Vector2)attackTarget.position - (Vector2)transform.position;
                    primaryVector = toward.normalized;
                }
                else
                {
                    primaryVector = wanderDirection;
                }
                break;

            case FishState.Flocking:
                primaryVector = CalculateBoidsFlockForces();
                break;
        }

        // Environmental bound steering: keep fish within the water column
        primaryVector = ApplyWaterBoundsConstraint(primaryVector);

        // Layer Mask Constraint Injection (Always prioritize terrain over state headings)
        primaryVector = ApplyTerrainAvoidance(primaryVector);

        return primaryVector.normalized;
    }

    private Vector2 CalculateBoidsFlockForces()
    {
        Vector2 separation = Vector2.zero;
        Vector2 alignment = Vector2.zero;
        Vector2 flockCenter = Vector2.zero;

        int contextCount = 0;
        int myLevel = (fishData != null) ? fishData.Level : 1;
        float myHorizontalSign = Mathf.Sign(wanderDirection.x != 0f ? wanderDirection.x : transform.localScale.x);

        for (int i = 0; i < allFishInScene.Count; i++)
        {
            var neighbor = allFishInScene[i];
            if (neighbor == null || neighbor == this || !neighbor.gameObject.activeInHierarchy) continue;
            int neighborLevel = (neighbor.fishData != null) ? neighbor.fishData.Level : 1;
            if (neighborLevel != myLevel) continue;

            float neighborHorizontalSign = Mathf.Sign(neighbor.wanderDirection.x != 0f ? neighbor.wanderDirection.x : neighbor.transform.localScale.x);
            // CRITICAL FIX: Only flock with peers moving in the same horizontal direction!
            if (!Mathf.Approximately(myHorizontalSign, neighborHorizontalSign)) continue;

            float distance = Vector2.Distance(transform.position, neighbor.transform.position);
            if (distance <= flockDetectionRadius && distance > 0.05f)
            {
                // Rule A: Separation (Push away softly when too close, but do not overpower cruise direction)
                Vector2 away = ((Vector2)transform.position - (Vector2)neighbor.transform.position).normalized;
                float weight = Mathf.Clamp01(1f - (distance / flockDetectionRadius));
                separation += away * weight;

                // Rule B: Alignment (Match peer heading)
                alignment += neighbor.wanderDirection;

                // Rule C: Cohesion (Pull gently toward peer center)
                flockCenter += (Vector2)neighbor.transform.position;

                contextCount++;
            }
        }

        if (contextCount == 0) return wanderDirection;

        alignment /= contextCount;
        flockCenter /= contextCount;
        Vector2 cohesion = (flockCenter - (Vector2)transform.position).normalized;

        // Blend flocking forces with cruise anchor to maintain continuous forward momentum across the screen
        Vector2 blendedFlockHeading = (wanderDirection * 1.5f) +
                                      (separation.normalized * separationWeight) +
                                      (alignment.normalized * alignmentWeight) +
                                      (cohesion.normalized * cohesionWeight);

        // Ensure horizontal intent is decisively preserved
        if (Mathf.Abs(blendedFlockHeading.x) < 0.35f)
        {
            blendedFlockHeading.x = myHorizontalSign * 0.5f;
        }

        return blendedFlockHeading.normalized;
    }

    private Vector2 ApplyWaterBoundsConstraint(Vector2 currentVector)
    {
        Camera cam = Camera.main;
        float camHalfH = cam != null ? cam.orthographicSize : 11f;
        Vector3 camP = cam != null ? cam.transform.position : Vector3.zero;
        float vMargin = 0.25f; // Identical 0.25f margin to PlayerController

        float maxY = camP.y + camHalfH - vMargin;
        float minY = camP.y - camHalfH + vMargin;

        Vector2 steer = currentVector;
        if (transform.position.y >= maxY)
        {
            if (steer.y > 0f) steer.y = 0f;
        }
        else if (transform.position.y <= minY)
        {
            if (steer.y < 0f) steer.y = 0f;
        }

        // Never let bounds avoidance crush horizontal movement to 0!
        // Preserve current horizontal swimming direction:
        float curFacingSign = (fishData != null) ? (fishData.IsFacingRight ? 1f : -1f) : Mathf.Sign(transform.localScale.x);
        if (Mathf.Abs(steer.x) < 0.35f)
        {
            steer.x = curFacingSign * 0.6f;
        }

        return steer.normalized;
    }

    private Vector2 ApplyTerrainAvoidance(Vector2 baselineDirection)
    {
        if (obstacleLayer.value == 0) return baselineDirection;

        Vector2 castDir = (baselineDirection != Vector2.zero) ? baselineDirection : (Vector2)transform.right;
        RaycastHit2D environmentContact = Physics2D.Raycast(transform.position, castDir, lookAheadLength, obstacleLayer);

        if (environmentContact.collider != null)
        {
            Vector2 escapeVector = Vector2.Reflect(baselineDirection, environmentContact.normal);
            return (baselineDirection + escapeVector * avoidanceForceMultiplier).normalized;
        }

        return baselineDirection;
    }

    // ==========================================
    // 3. TRANSFORM RENDERING AND ANIMATION
    // ==========================================
    private void ExecuteMovementAndSway(Vector2 targetPathingDirection)
    {
        if (targetPathingDirection == Vector2.zero) return;

        // Current visual facing
        Vector3 scale = transform.localScale;
        bool currentlyFacingRight = (fishData != null) ? fishData.IsFacingRight : (scale.x > 0);

        // Turnaround Logic with Hysteresis & Cooldown:
        // A turnaround only initiates when target direction decisively points in opposite direction
        if (flipCooldownTimer <= 0f)
        {
            if (currentlyFacingRight && targetPathingDirection.x < -FLIP_HORIZONTAL_DEADZONE)
            {
                // Decisively turn left
                scale.x = -Mathf.Abs(scale.x);
                currentlyFacingRight = false;
                if (fishData != null) fishData.IsFacingRight = false;
                flipCooldownTimer = MIN_FLIP_COOLDOWN;
                // Instantly align horizontal movement to left so fish never moves backwards:
                currentMoveDirection.x = -Mathf.Max(0.35f, Mathf.Abs(targetPathingDirection.x));
            }
            else if (!currentlyFacingRight && targetPathingDirection.x > FLIP_HORIZONTAL_DEADZONE)
            {
                // Decisively turn right
                scale.x = Mathf.Abs(scale.x);
                currentlyFacingRight = true;
                if (fishData != null) fishData.IsFacingRight = true;
                flipCooldownTimer = MIN_FLIP_COOLDOWN;
                // Instantly align horizontal movement to right so fish never moves backwards:
                currentMoveDirection.x = Mathf.Max(0.35f, Mathf.Abs(targetPathingDirection.x));
            }
        }

        // Steer currentMoveDirection smoothly towards targetPathingDirection
        float steerSpeed = (currentState == FishState.Fleeing || currentState == FishState.Attacking) ? 7.0f : 4.5f;
        if (currentMoveDirection == Vector2.zero) currentMoveDirection = targetPathingDirection;
        currentMoveDirection = Vector2.MoveTowards(currentMoveDirection, targetPathingDirection, steerSpeed * Time.fixedDeltaTime).normalized;

        // ABSOLUTE ANTI-BACKWARDS SWIMMING ENFORCEMENT:
        // In 2D side-view arcade fish games, fish NEVER swim backward.
        // A fish facing right MUST have forward horizontal motion (x > 0).
        // A fish facing left MUST have forward horizontal motion (x < 0).
        if (currentlyFacingRight)
        {
            if (currentMoveDirection.x < 0.20f) currentMoveDirection.x = 0.25f;
            currentMoveDirection = currentMoveDirection.normalized;
        }
        else
        {
            if (currentMoveDirection.x > -0.20f) currentMoveDirection.x = -0.25f;
            currentMoveDirection = currentMoveDirection.normalized;
        }

        scale.y = Mathf.Abs(scale.y);
        transform.localScale = scale;

        float processingSpeed = wanderSpeed;
        if (currentState == FishState.Flocking)  processingSpeed = flockSpeed;
        if (currentState == FishState.Fleeing)   processingSpeed = fleeSpeed;
        if (currentState == FishState.Attacking) processingSpeed = attackSpeed;

        if (inkDisorientTimer > 0f)
        {
            processingSpeed *= 0.35f; // Heavy 65% speed penalty while blinded & disoriented by ink
        }

        if (fishData != null)
        {
            if (fishData.IsSickFish) processingSpeed *= 0.4f;
            if (fishData.IsSpiked)   processingSpeed *= 0.3f;
            if (fishData.IsPoisoned) processingSpeed *= 0.65f;
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, processingSpeed, 8.0f * Time.fixedDeltaTime);

        Vector2 executionStep = currentMoveDirection * currentSpeed;

        Camera cam = Camera.main;
        float camHalfH = cam != null ? cam.orthographicSize : 11f;
        Vector3 camP = cam != null ? cam.transform.position : Vector3.zero;
        float vMargin = 0.25f; // Identical 0.25f margin to PlayerController

        float maxY = camP.y + camHalfH - vMargin;
        float minY = camP.y - camHalfH + vMargin;

        Vector2 pos = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 vel = executionStep;

        // Exact same vertical bounds clamping as PlayerController:
        // Fishes can reach the very top and very bottom without bouncing
        if (pos.y > maxY)
        {
            pos.y = maxY;
            if (vel.y > 0f) vel.y = 0f;
            if (currentMoveDirection.y > 0f) currentMoveDirection.y = 0f;
        }
        else if (pos.y < minY)
        {
            pos.y = minY;
            if (vel.y < 0f) vel.y = 0f;
            if (currentMoveDirection.y < 0f) currentMoveDirection.y = 0f;
        }

        if (rb != null)
        {
            rb.position = pos;
            if (rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.linearVelocity = vel;
            }
            else
            {
                rb.MovePosition(pos);
            }
        }
        else
        {
            transform.position = pos;
        }

        if (enableBodyTilt)
        {
            bool isFacing = (fishData != null) ? fishData.IsFacingRight : (scale.x > 0);
            float pitchAngle = Mathf.Atan2(currentMoveDirection.y, Mathf.Abs(currentMoveDirection.x)) * Mathf.Rad2Deg;
            pitchAngle = Mathf.Clamp(pitchAngle, -15f, 15f);
            float harmonicWave = Mathf.Sin(Time.time * swimWiggleFrequency) * swimWiggleMagnitude;
            float totalAngle = isFacing ? (pitchAngle + harmonicWave) : -(pitchAngle + harmonicWave);
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, totalAngle);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnRotationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            // 2D Side-View Arcade Standard (Feeding Frenzy style):
            // Sprites stay upright (0 deg) to keep colliders and visuals crisp and level.
            transform.rotation = Quaternion.identity;
        }
    }

    // ==========================================
    // 4. UTILITIES & API
    // ==========================================
    private void ChooseRandomHeading()
    {
        // 2D Side-view arcade fish cruise across horizontally (Left or Right)
        // Retain current general direction 75% of the time for calm, stable cruising; 25% turnaround
        float currentSign = (fishData != null && !fishData.IsFacingRight) ? -1f : (transform.localScale.x < 0f ? -1f : 1f);
        float horizontalDir = (Random.value < 0.75f) ? currentSign : -currentSign;

        float verticalVariance = Random.Range(-0.25f, 0.25f);
        Camera cam = Camera.main;
        if (cam != null)
        {
            float halfH = cam.orthographicSize;
            Vector3 camP = cam.transform.position;
            if (transform.position.y >= camP.y + halfH - 0.5f && verticalVariance > 0f) verticalVariance = -0.1f;
            else if (transform.position.y <= camP.y - halfH + 0.5f && verticalVariance < 0f) verticalVariance = 0.1f;
        }

        wanderDirection = new Vector2(horizontalDir, verticalVariance).normalized;
        changeWanderTimer = Random.Range(3.5f, 7.0f);
    }

    public void SetInitialDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.001f)
        {
            if (fishData == null) fishData = GetComponent<Fish>();
            wanderDirection = direction.normalized;
            currentMoveDirection = wanderDirection;
            changeWanderTimer = Random.Range(3.5f, 6.5f);
            initialDirectionSet = true;

            // Immediately enforce facing matching direction:
            Vector3 s = transform.localScale;
            if (direction.x < 0f)
            {
                s.x = -Mathf.Abs(s.x);
                if (fishData != null) fishData.IsFacingRight = false;
            }
            else if (direction.x > 0f)
            {
                s.x = Mathf.Abs(s.x);
                if (fishData != null) fishData.IsFacingRight = true;
            }
            s.y = Mathf.Abs(s.y);
            transform.localScale = s;
        }
    }

    public void ApplyInkDisorientation(float duration = 3.5f)
    {
        inkDisorientTimer = duration;
        currentState = FishState.Wandering;
        threatTarget = null;
        attackTarget = null;
        fleeCommitTimer = 0f;
        attackCommitTimer = 0f;
        ChooseRandomHeading();
    }

    public void OnAteFish(Fish prey)
    {
        // Each fish eaten satisfies hunger (needs 2-3 fish to become fully fed up)
        currentHunger = Mathf.Max(0f, currentHunger - hungerReductionPerFish);
        postEatCooldownTimer = postEatDigestCooldown;
        attackTarget = null;

        // Drop out of attack state into wandering after swallowing
        if (currentState == FishState.Attacking)
        {
            currentState = FishState.Wandering;
            ChooseRandomHeading();
        }
    }

    private static PlayerController cachedPlayer;
    private PlayerController GetPlayer()
    {
        if (cachedPlayer == null || !cachedPlayer.gameObject.activeInHierarchy)
        {
            if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
                cachedPlayer = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            if (cachedPlayer == null)
            {
                GameObject pObj = GameObject.FindWithTag(sharkTag);
                if (pObj != null) cachedPlayer = pObj.GetComponent<PlayerController>();
            }
            if (cachedPlayer == null)
                cachedPlayer = Object.FindFirstObjectByType<PlayerController>();
        }
        return cachedPlayer;
    }
}
