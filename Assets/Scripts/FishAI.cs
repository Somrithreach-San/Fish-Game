using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class FishAI : MonoBehaviour
{
    public enum State { Wander, Chase, Flee }
    public State currentState = State.Wander;

    [Header("Movement Settings")]
    public float moveSpeed = 4f; // Constant speed
    public float turnSpeed = 200f; // Degrees per second
    public float wanderRadius = 2f;
    public float wanderDistance = 3f;
    public float wanderJitter = 1f;

    [Header("Behavior Settings")]
    public float chaseRadius = 4f;
    public float fleeRadius = 4.5f;
    public float separationRadius = 2f;
    
    // Chase Limits to prevent sticking
    public float maxChaseTime = 5f;       // Give up after 5 seconds
    public float chaseCooldownTime = 3f;  // Ignore player for 3 seconds
    private float currentChaseTimer = 0f;
    private float currentCooldownTimer = 0f;

    // Random "Leave" Behavior
    private float leaveTimer = 0f;
    private Vector2 leaveDirection = Vector2.zero;
    private bool isLeaving = false;
    private float lifeTime = 0f;

    [Header("Obstacle Avoidance")]
    public bool stayOnScreen = false; 
    public LayerMask obstacleMask;
    public float avoidDistance = 3f;
    [Tooltip("Layers to include in separation calculations (e.g. Fish, Enemy)")]
    public LayerMask separationMask = -1; 

    [Header("Visuals")]
    public Transform graphicsTransform;
    public float tailSwaySpeed = 8f;
    public float tailSwayAmount = 10f;

    private Rigidbody2D rb;
    private Transform player;
    private Transform chaseTarget; 
    private GameManager cachedGameManager; 
    private Fish fishData;
    private Vector2 currentDirection;
    private Vector2 wanderTarget;
    private System.Collections.Generic.List<Collider2D> neighborBuffer = new System.Collections.Generic.List<Collider2D>(10);

    private Vector2 cachedAvoidanceDir;
    private Vector2 cachedSeparationDir;
    private Vector2 cachedAlignmentDir;
    private Vector2 cachedCohesionDir;
    private int aiTickOffset;
    private float randomOffset; 
    private float currentSpeed;
    private float hunger;
    private float postEatCooldownTimer;
    public float minSpeed = 2f;
    public float maxSpeed = 6f;
    public float accel = 3f;
    public float decel = 2f;
    public float swimNoiseScale = 0.3f;
    public float swimNoiseSpeed = 0.5f;
    public float fleeSpeedMultiplier = 1.1f;
    public float chaseSpeedMultiplier = 1.2f;
    
    public float hungerMax = 1f;
    public float hungerChaseThreshold = 0.40f;
    public float hungerDecayPerSecond = 0.05f;
    public float eatHungerGain = 0.6f;
    public float postEatCooldown = 3.5f;
    public float hungerChaseChance = 0.85f;
    private float lastDistToPlayer;

    // Sick Fish Behavior 
    private float sickRestTimer = 0f;
    private bool isSickResting = false;
    private float nextSickCycleDuration = 6f;
    private float fleeCommitTimer = 0f;
    private Vector2 smoothedFleeDir = Vector2.zero;
    private Transform lastFleeTarget = null;

    // Goal-based flee: fish picks a destination waypoint and swims toward it
    private Vector2 fleeGoalPoint = Vector2.zero;
    private bool hasFleeGoal = false;
    private Vector2 fleeGoalThreatPosCache = Vector2.zero; // threat position when goal was computed
    private float currentMinY = -14f;
    private float currentMaxY = 14f;

    // Ink Disorientation
    private float inkDisorientTimer = 0f;
    public bool IsDisorientedByInk => inkDisorientTimer > 0f;

    public void ApplyInkDisorientation(float duration = 3.5f)
    {
        inkDisorientTimer = duration;
        chaseTarget = null;
        currentState = State.Wander;
    }

    // Individualized Organic Schooling
    private Vector2 laggedSchoolTarget;
    private float followResponsiveness = 3.5f;
    private float personalSpeedMultiplier = 1.0f;
    private float personalTurnMultiplier = 1.0f;
    private float personalFlipDeadzone = 0.22f;
    private float schoolSpeedMod = 1.0f;
    private float personalWobblePhaseX = 0f;
    private float personalWobblePhaseY = 0f;
    private float personalWobbleSpeedX = 1.0f;
    private float personalWobbleSpeedY = 1.0f;

    void Awake()
    {
        MonoBehaviour[] scripts = GetComponents<MonoBehaviour>();
        foreach (var script in scripts)
        {
            if (script.GetType().Name == "StateController")
            {
                Destroy(script);
            }
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.angularDamping = 0f;
            rb.linearDamping = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (graphicsTransform == null)
            graphicsTransform = transform.Find("Gfx") ?? transform.Find("PlayerGraphics") ?? transform.Find("Graphics") ?? transform;

        fishData = GetComponent<Fish>();

        if (obstacleMask.value == 0)
        {
            obstacleMask = ~((1 << LayerMask.NameToLayer("Ignore Raycast")));
        }
        
        float theta = Random.value * 2 * Mathf.PI;
        wanderTarget = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * wanderRadius;
    }

    void OnEnable()
    {
        player = GameManager.instance?.playerGameObject?.transform;
        currentDirection = transform.right;
        if (currentDirection == Vector2.zero) currentDirection = Vector2.right;
        aiTickOffset = Random.Range(0, 5);
        hunger = 0f;
        postEatCooldownTimer = 0f;
        currentChaseTimer = 0f;
        currentCooldownTimer = 0f;
        leaveTimer = 0f;
        isLeaving = false;
        lifeTime = 0f;
        fleeCommitTimer = 0f;
        smoothedFleeDir = Vector2.zero;
        lastFleeTarget = null;
        hasFleeGoal = false;
        fleeGoalPoint = Vector2.zero;
        currentState = State.Wander;
        sickRestTimer = Random.Range(1f, 4f);
        isSickResting = false;
        nextSickCycleDuration = Random.Range(5f, 8f);
        InitPersonality();
    }

    void Start()
    {
        player = GameManager.instance?.playerGameObject?.transform;
        currentDirection = transform.right;
        if (currentDirection == Vector2.zero) currentDirection = Vector2.right;

        aiTickOffset = Random.Range(0, 5);
        hunger = 0f;
        postEatCooldownTimer = 0f;
        sickRestTimer = Random.Range(1f, 4f);
        isSickResting = false;
        nextSickCycleDuration = Random.Range(5f, 8f);
        InitPersonality();
    }

    private void InitPersonality()
    {
        personalSpeedMultiplier = Random.Range(0.88f, 1.14f);
        personalTurnMultiplier = Random.Range(0.84f, 1.20f);
        personalFlipDeadzone = Random.Range(0.18f, 0.26f);
        followResponsiveness = Random.Range(2.2f, 4.6f);
        schoolSpeedMod = 1.0f;
        personalWobblePhaseX = Random.Range(0f, Mathf.PI * 2f);
        personalWobblePhaseY = Random.Range(0f, Mathf.PI * 2f);
        personalWobbleSpeedX = Random.Range(1.1f, 1.7f);
        personalWobbleSpeedY = Random.Range(1.0f, 1.6f);
        laggedSchoolTarget = transform.position;
    }

    void FixedUpdate()
    {
        if (player == null && GameManager.instance?.playerGameObject != null)
            player = GameManager.instance.playerGameObject.transform;

        if (hunger > 0f)
            hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * Time.fixedDeltaTime);
        if (postEatCooldownTimer > 0f)
            postEatCooldownTimer = Mathf.Max(0f, postEatCooldownTimer - Time.fixedDeltaTime);

        if (LevelManager.IsLevelCompleted)
        {
            chaseTarget = null;
            if (currentState == State.Chase || currentState == State.Flee) currentState = State.Wander;
        }

        if (inkDisorientTimer > 0f)
        {
            inkDisorientTimer = Mathf.Max(0f, inkDisorientTimer - Time.fixedDeltaTime);
            chaseTarget = null;
            if (currentState == State.Chase) currentState = State.Wander;
        }

        if (fishData != null && fishData.IsSickFish)
        {
            if (currentState == State.Flee)
            {
                isSickResting = false;
                sickRestTimer = 0f;
            }
            else
            {
                sickRestTimer += Time.fixedDeltaTime;
                if (!isSickResting && sickRestTimer >= nextSickCycleDuration)
                {
                    isSickResting = true;
                    sickRestTimer = 0f;
                    nextSickCycleDuration = Random.Range(2.0f, 3.5f); 
                }
                else if (isSickResting && sickRestTimer >= nextSickCycleDuration)
                {
                    isSickResting = false;
                    sickRestTimer = 0f;
                    nextSickCycleDuration = Random.Range(5.0f, 8.0f); 
                }
            }
        }

        UpdateState();
        UpdateMouthAnticipation();
        HandleLeavingLogic();

        bool isSchoolFish = (fishData != null && (fishData.school != null || fishData.GroupSchool != null || fishData.Level == 1));
        bool inActiveSchool = (fishData != null && fishData.school != null && currentState == State.Wander);

        Vector2 targetDir = currentDirection;
        schoolSpeedMod = 1.0f;

        if (inActiveSchool)
        {
            FishSchool school = fishData.school;
            Vector2 schoolCenter = (Vector2)school.transform.position;
            Vector2 schoolTravelDir = school.TravelDirection;

            float driftX = Mathf.Sin(Time.time * personalWobbleSpeedX + personalWobblePhaseX) * 0.12f;
            float driftY = Mathf.Cos(Time.time * personalWobbleSpeedY + personalWobblePhaseY) * 0.12f;
            Vector2 organicDrift = new Vector2(driftX, driftY);

            Vector2 slotOffset = fishData.formationOffset;
            if (school.MovingRight) slotOffset.x = -Mathf.Abs(slotOffset.x);
            else slotOffset.x = Mathf.Abs(slotOffset.x);

            Vector2 idealSlotPos = schoolCenter + slotOffset + organicDrift;
            Vector2 toSlot = idealSlotPos - (Vector2)transform.position;

            Vector2 separation = Vector2.zero;
            foreach (var mate in school.RegisteredFishList)
            {
                if (mate != null && mate != fishData && mate.gameObject.activeInHierarchy && !mate.IsDead)
                {
                    Vector2 diff = (Vector2)transform.position - (Vector2)mate.transform.position;
                    float distSqr = diff.sqrMagnitude;
                    if (distSqr > 0.0001f && distSqr < 0.64f)
                    {
                        float dist = Mathf.Sqrt(distSqr);
                        separation += (diff / dist) * ((0.80f - dist) / 0.80f) * 0.45f;
                    }
                }
            }

            Vector2 desiredHeading = schoolTravelDir + toSlot * 1.5f + separation;
            if (desiredHeading.sqrMagnitude > 0.001f) targetDir = desiredHeading.normalized;
            else targetDir = schoolTravelDir;

            float slotDistanceAhead = Vector2.Dot(toSlot, schoolTravelDir);
            schoolSpeedMod = Mathf.Clamp(1.0f + slotDistanceAhead * 0.22f, 0.85f, 1.25f);
        }
        else if (isLeaving && currentState == State.Wander)
        {
            targetDir = leaveDirection;
        }
        else
        {
            switch (currentState)
            {
                case State.Wander:
                    targetDir = GetWanderDirection();
                    break;
                case State.Chase:
                    if (chaseTarget != null) 
                        targetDir = (chaseTarget.position - transform.position).normalized;
                    else if (player != null) 
                        targetDir = (player.position - transform.position).normalized;
                    break;
                case State.Flee:
                    bool isSharkFlee = chaseTarget != null && chaseTarget.GetComponent<SharkHazard>() != null;
                    Transform activeThreat = chaseTarget ?? player;

                    if (isSharkFlee && activeThreat != null)
                    {
                        // Sharks: keep reactive per-frame evasion (fast, vertical dodge)
                        float dodgeY = (transform.position.y >= activeThreat.position.y) ? 1.0f : -1.0f;
                        Vector2 awayFromShark = ((Vector2)transform.position - (Vector2)activeThreat.position).normalized;
                        Vector2 sharkDir = new Vector2(awayFromShark.x * 0.4f, dodgeY * 1.6f).normalized;
                        smoothedFleeDir = Vector2.Lerp(smoothedFleeDir, sharkDir, 25f * Time.fixedDeltaTime);
                        if (smoothedFleeDir.sqrMagnitude < 0.001f) smoothedFleeDir = sharkDir;
                        smoothedFleeDir.Normalize();
                        targetDir = smoothedFleeDir;
                    }
                    else if (activeThreat != null)
                    {
                        // --- GOAL-BASED FLEE ---
                        // Decide whether to pick a new goal waypoint
                        bool needNewGoal = !hasFleeGoal;

                        if (hasFleeGoal)
                        {
                            // Refresh if threat has moved significantly since goal was computed
                            float threatDrift = Vector2.Distance((Vector2)activeThreat.position, fleeGoalThreatPosCache);
                            if (threatDrift > 2.5f) needNewGoal = true;

                            // Refresh if fish has nearly reached its goal
                            float distToGoal = Vector2.Distance(transform.position, fleeGoalPoint);
                            if (distToGoal < 1.5f) needNewGoal = true;

                            // Refresh if threat has somehow moved between the fish and its goal
                            Vector2 toGoal = (fleeGoalPoint - (Vector2)transform.position).normalized;
                            Vector2 toThreat = ((Vector2)activeThreat.position - (Vector2)transform.position).normalized;
                            if (Vector2.Dot(toGoal, toThreat) > 0.6f) needNewGoal = true;
                        }

                        if (needNewGoal || activeThreat != lastFleeTarget)
                        {
                            fleeGoalPoint = ComputeFleeGoal(activeThreat, isSchoolFish);
                            hasFleeGoal = true;
                            fleeGoalThreatPosCache = activeThreat.position;
                            lastFleeTarget = activeThreat;
                        }

                        // Steer toward the goal waypoint
                        Vector2 dirToGoal = (fleeGoalPoint - (Vector2)transform.position).normalized;
                        smoothedFleeDir = Vector2.Lerp(smoothedFleeDir, dirToGoal, 8f * Time.fixedDeltaTime);
                        if (smoothedFleeDir.sqrMagnitude < 0.001f) smoothedFleeDir = dirToGoal;
                        smoothedFleeDir.Normalize();
                        targetDir = smoothedFleeDir;
                    }
                    break;
            }
        }

        if ((Time.frameCount + aiTickOffset) % 5 == 0)
        {
            cachedAvoidanceDir = GetAvoidanceDirection();
            cachedSeparationDir = GetSeparationDirection();
            cachedAlignmentDir = GetAlignmentDirection();
            cachedCohesionDir = GetCohesionDirection();
        }

        if (currentState == State.Flee)
        {
            targetDir *= 4.5f;
        }

        if (cachedAvoidanceDir != Vector2.zero)
        {
            // During flee: keep avoidance BELOW the flee pre-weight (4.5f) so flee direction wins
            // but avoidance still deflects the fish away from walls gently
            float avoidMult = (currentState == State.Flee) ? 2.5f : 3.0f;

            // NEW TANGENT STEERING: Break out of local minimums when cornered against a wall
            float conflict = Vector2.Dot(targetDir.normalized, cachedAvoidanceDir);
            if (conflict < -0.3f) 
            {
                // Force pushes into the wall. Create tangent vector to slide away.
                Vector2 tangent = new Vector2(-cachedAvoidanceDir.y, cachedAvoidanceDir.x);
                float escapeSide = (Vector2.Dot(currentDirection, tangent) > 0) ? 1.0f : -1.0f;
                targetDir += tangent * escapeSide * 5.0f;
            }

            targetDir += cachedAvoidanceDir * avoidMult;
        }

        if (!inActiveSchool)
        {
            if (cachedSeparationDir != Vector2.zero)
            {
                 float sepMult = (currentState == State.Flee) ? 0.2f : 1.5f;
                 targetDir += cachedSeparationDir * sepMult;
            }

            if (currentState != State.Flee)
            {
                if (cachedAlignmentDir != Vector2.zero) targetDir += cachedAlignmentDir * 1.0f;
                if (cachedCohesionDir != Vector2.zero)
                {
                    float cohesionWeight = (fishData != null && fishData.Level == 1) ? 1.5f : 0.8f;
                    targetDir += cachedCohesionDir * cohesionWeight;
                }
            }
        }

        // 4. Vertical Bounds Constraint — use fixed world-space bounds, NOT camera position.
        // Using camera.y caused fish to be pushed up/down whenever the camera moved vertically.
        float vMargin = 0.25f;

        float floorY   = RiverBoat.BoundsCached ? RiverBoat.GlobalFloorY   + vMargin : -13.75f;
        float surfaceY = RiverBoat.BoundsCached ? RiverBoat.GlobalSurfaceY - vMargin :  13.75f;

        currentMinY = floorY;
        currentMaxY = surfaceY;

        Vector2 pos2D = transform.position;
        if (pos2D.y >= currentMaxY)
        {
            if (targetDir.y > 0f) targetDir.y = 0f;
            if (currentDirection.y > 0f) currentDirection.y = 0f;
            if (Mathf.Abs(targetDir.x) < 0.25f)
            {
                float facingX = (currentDirection.x != 0f) ? Mathf.Sign(currentDirection.x) : 1f;
                targetDir.x = facingX * 0.8f;
            }
        }
        else if (pos2D.y <= currentMinY)
        {
            if (targetDir.y < 0f) targetDir.y = 0f;
            if (currentDirection.y < 0f) currentDirection.y = 0f;
            if (Mathf.Abs(targetDir.x) < 0.25f)
            {
                float facingX = (currentDirection.x != 0f) ? Mathf.Sign(currentDirection.x) : 1f;
                targetDir.x = facingX * 0.8f;
            }
        }

        if (fishData != null && fishData.IsSickFish)
        {
            float deepMinY = floorY + 0.8f;
            float deepMaxY = floorY + (surfaceY - floorY) * 0.28f;

            if (transform.position.y > deepMaxY) targetDir.y = Mathf.Min(targetDir.y - 0.6f, -0.4f);
            else if (transform.position.y < deepMinY) targetDir.y = Mathf.Max(targetDir.y + 0.35f, 0.2f);
            else targetDir.y *= 0.3f;
        }
        else if (currentState == State.Wander)
        {
            targetDir.y *= 0.35f;
            if (Mathf.Abs(targetDir.x) < 0.35f)
            {
                float facingX = (currentDirection.x != 0f) ? Mathf.Sign(currentDirection.x) : 1f;
                targetDir.x = facingX * 0.8f;
            }
        }

        targetDir = targetDir.normalized;
        if (targetDir == Vector2.zero) targetDir = currentDirection;

        if (targetDir != Vector2.zero)
        {
            float activeTurnSpeed = turnSpeed * personalTurnMultiplier;
            if (fishData != null && fishData.IsSpiked) activeTurnSpeed = 60f; 
            else if (fishData != null && fishData.IsSickFish) activeTurnSpeed = 70f; 
            else if (currentState == State.Flee) activeTurnSpeed = (isSchoolFish ? 140f : 220f) * personalTurnMultiplier; 
            else if (inActiveSchool) activeTurnSpeed = 175f * personalTurnMultiplier; 

            bool isReversingHorizontal = (currentDirection.x * targetDir.x < -0.1f) && Mathf.Abs(targetDir.x) > 0.25f && Mathf.Abs(currentDirection.x) > 0.25f;

            if (isReversingHorizontal)
            {
                float turnRate = Mathf.Max(activeTurnSpeed * 1.5f, 300f) * Mathf.Deg2Rad;
                currentDirection.x = Mathf.MoveTowards(currentDirection.x, targetDir.x, turnRate * Time.fixedDeltaTime);
                currentDirection.y = Mathf.MoveTowards(currentDirection.y, targetDir.y, turnRate * 0.7f * Time.fixedDeltaTime);

                if (Mathf.Sign(currentDirection.x) == Mathf.Sign(targetDir.x) && Mathf.Abs(currentDirection.x) > 0.30f)
                {
                    currentDirection = currentDirection.normalized;
                }
            }
            else
            {
                currentDirection = Vector2.MoveTowards(currentDirection, targetDir, activeTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime);
                if (currentDirection.sqrMagnitude > 0.001f) currentDirection.Normalize();
            }
        }
        
        if (currentDirection == Vector2.zero) currentDirection = transform.right;

        float swimStroke = 1.0f + Mathf.Sin(Time.time * 3.8f + personalWobblePhaseX) * 0.05f;
        float levelBaseSpeed = moveSpeed;
        if (fishData != null)
        {
            if (fishData.IsGoldenFish) levelBaseSpeed = 4.0f; 
            else if (fishData.IsCuttlefish) levelBaseSpeed = 3.5f;
            else if (fishData.Level == 1) levelBaseSpeed = 2.8f;
            else if (fishData.Level == 2) levelBaseSpeed = 3.2f;
            else if (fishData.Level == 3) levelBaseSpeed = 3.5f;
            else if (fishData.Level == 4) levelBaseSpeed = 3.4f;
            else if (fishData.Level >= 5) levelBaseSpeed = 4.5f; 
        }

        float baseSpeed = (fishData != null && fishData.Level == 1 && !fishData.IsGoldenFish) 
            ? (levelBaseSpeed * personalSpeedMultiplier * swimStroke) 
            : (levelBaseSpeed * personalSpeedMultiplier);

        float targetSpeed = inActiveSchool ? (baseSpeed * schoolSpeedMod) : baseSpeed;
        if (currentState == State.Flee)
        {
            float fleeMult = isSchoolFish ? 1.03f : 1.22f;
            targetSpeed = baseSpeed * fleeMult;
        }
        if (currentState == State.Chase)
        {
            int lvl = (fishData != null) ? fishData.Level : 1;
            bool isCuttle = fishData != null && fishData.IsCuttlefish;
            GetAggressionSettings(lvl, isCuttle, out _, out float spdMult, out _, out _, out _);
            targetSpeed *= spdMult;
        }
        if (fishData != null && fishData.IsSpiked) targetSpeed *= 0.25f; 
        if (fishData != null && fishData.IsPoisoned) targetSpeed *= 0.65f; 
        if (fishData != null && fishData.IsSickFish)
        {
            if (isSickResting) targetSpeed = 0f;
            else if (currentState == State.Flee) targetSpeed = baseSpeed * 0.5f; 
            else targetSpeed = baseSpeed * 0.35f; 
        }
        if (inkDisorientTimer > 0f) targetSpeed *= 0.35f; 
        
        float activeMinSpeed = (fishData != null && (fishData.IsSpiked || fishData.IsPoisoned)) ? 0.3f : 
                               ((fishData != null && fishData.IsSickFish) ? 0f : 
                               ((fishData != null && fishData.Level == 1) ? 1.5f : minSpeed));
        targetSpeed = Mathf.Clamp(targetSpeed, activeMinSpeed, maxSpeed);
        float rate = targetSpeed > currentSpeed ? accel : decel;
        
        if (currentState == State.Flee && isSchoolFish) rate = 4.0f; 
        else if (inActiveSchool) rate = 3.5f; 
        else if (fishData != null && fishData.IsSickFish) rate = 1.5f;
        
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (fishData == null) fishData = GetComponent<Fish>();

        if (rb != null)
        {
            Vector2 vel;
            if (fishData != null && fishData.IsSickFish && isSickResting && currentSpeed < 0.2f)
            {
                float driftX = Mathf.Sin(Time.time * 0.8f + aiTickOffset) * 0.08f;
                float driftY = Mathf.Cos(Time.time * 0.6f + aiTickOffset) * 0.04f;
                vel = new Vector2(driftX, driftY);
            }
            else
            {
                vel = currentDirection * currentSpeed;
            }

            Vector2 pos = rb.position;

            // Exact same vertical bounds clamping as PlayerController:
            // Reaches the very top and very bottom without bouncing
            if (pos.y > currentMaxY)
            {
                pos.y = currentMaxY;
                if (vel.y > 0f) vel.y = 0f;
                if (currentDirection.y > 0f) currentDirection.y = 0f;
            }
            else if (pos.y < currentMinY)
            {
                pos.y = currentMinY;
                if (vel.y < 0f) vel.y = 0f;
                if (currentDirection.y < 0f) currentDirection.y = 0f;
            }

            rb.position = pos;
            rb.linearVelocity = vel;
            rb.rotation = 0f;
        }

        Vector3 scale = transform.localScale;
        bool currentlyFacingRight = (fishData != null) ? fishData.IsFacingRight : (scale.x > 0);
        float flipThreshold = personalFlipDeadzone > 0f ? personalFlipDeadzone : 0.22f;

        if (currentlyFacingRight && currentDirection.x < -flipThreshold)
        {
            scale.x = -Mathf.Abs(scale.x);
            if (fishData != null) fishData.IsFacingRight = false;
        }
        else if (!currentlyFacingRight && currentDirection.x > flipThreshold)
        {
            scale.x = Mathf.Abs(scale.x);
            if (fishData != null) fishData.IsFacingRight = true;
        }
        
        scale.y = Mathf.Abs(scale.y);
        transform.localScale = scale;

        if (graphicsTransform != null && graphicsTransform != transform)
        {
            graphicsTransform.localRotation = Quaternion.identity;
        }
    }

    void UpdateState()
    {
        if (player == null && GameManager.instance?.playerGameObject != null)
        {
            player = GameManager.instance.playerGameObject.transform;
        }

        if (fishData == null) return;

        // Cuttlefish cannot hunt (it uses ink defensively) but CAN flee from larger fish
        bool isCuttlefishAI = fishData.IsCuttlefish;
        if (isCuttlefishAI)
        {
            hunger = 0f;
        }

        if (currentState == State.Chase)
        {
            currentChaseTimer += Time.fixedDeltaTime;
            if (currentChaseTimer >= maxChaseTime)
            {
                currentCooldownTimer = chaseCooldownTime;
                currentChaseTimer = 0f;
                currentState = State.Wander;
                chaseTarget = null;
                return;
            }
        }

        if (currentCooldownTimer > 0f)
        {
            currentCooldownTimer -= Time.fixedDeltaTime;
            currentState = State.Wander; 
            chaseTarget = null;
            return;
        }

        if (player == null)
        {
            if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
                player = GameManager.instance.playerGameObject.transform;
            else
            {
                var pc = Object.FindFirstObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
            }
        }

        float distToPlayer = (player != null) ? Vector2.Distance(transform.position, player.position) : 999f;
        lastDistToPlayer = distToPlayer;

        int playerLevel = 1;
        if (player != null)
        {
            var pc = player.GetComponent<PlayerController>();
            if (pc != null) playerLevel = pc.Level;
            else playerLevel = GameManager.PlayerLevel;
        }
        else
        {
            playerLevel = GameManager.PlayerLevel;
        }
        if (playerLevel < 1) playerLevel = 1;

        if (fleeCommitTimer > 0f)
        {
            fleeCommitTimer -= Time.fixedDeltaTime;
            if (chaseTarget != null && chaseTarget.gameObject.activeInHierarchy)
            {
                currentState = State.Flee;
                return;
            }
        }

        bool isSchoolFishThreat = (fishData != null && (fishData.school != null || fishData.GroupSchool != null || fishData.Level == 1));
        float enterFleeRadius = isSchoolFishThreat ? 1.5f : Mathf.Max(fleeRadius, 3.8f);
        float exitFleeRadius = enterFleeRadius + (isSchoolFishThreat ? 0.8f : 1.6f); 
        float currentFleeRadius = (currentState == State.Flee) ? exitFleeRadius : enterFleeRadius;

        Transform nearestThreat = null;
        float nearestThreatDist = float.MaxValue;

        if (SharkHazard.ActiveSharks != null)
        {
            for (int i = 0; i < SharkHazard.ActiveSharks.Count; i++)
            {
                SharkHazard shark = SharkHazard.ActiveSharks[i];
                if (shark == null || !shark.gameObject.activeInHierarchy || !shark.IsCharging) continue;

                float d = Vector2.Distance(transform.position, shark.transform.position);
                float sharkFleeRadius = 9.0f; 
                if (d < sharkFleeRadius && d < nearestThreatDist)
                {
                    nearestThreat = shark.transform;
                    nearestThreatDist = d;
                }
            }
        }

        bool canBeEatenByBites = (fishData == null || !fishData.IsSpiked);

        if (canBeEatenByBites)
        {
            bool playerCanEatMe = player != null && playerLevel >= fishData.Level;
            if (playerCanEatMe && distToPlayer < currentFleeRadius && distToPlayer < nearestThreatDist)
            {
                nearestThreat = player;
                nearestThreatDist = distToPlayer;
            }

            if (Fish.AllFish != null)
            {
                for (int i = 0; i < Fish.AllFish.Count; i++)
                {
                    Fish other = Fish.AllFish[i];
                    if (other == null || !other.gameObject.activeInHierarchy || other.IsDead || other == fishData || other.IsHooked) continue;
                    if (other.Level > fishData.Level)
                    {
                        float d = Vector2.Distance(transform.position, other.transform.position);
                        if (d < currentFleeRadius && d < nearestThreatDist)
                        {
                            nearestThreat = other.transform;
                            nearestThreatDist = d;
                        }
                    }
                }
            }
        }

        if (nearestThreat != null)
        {
            if (currentState == State.Flee && chaseTarget != null && chaseTarget != nearestThreat && chaseTarget.gameObject.activeInHierarchy)
            {
                float currentThreatDist = Vector2.Distance(transform.position, chaseTarget.position);
                if (nearestThreatDist > currentThreatDist * 0.75f)
                {
                    nearestThreat = chaseTarget;
                }
            }

            if (currentState != State.Flee)
            {
                hasFleeGoal = false; // New flee episode - force a fresh goal waypoint
            }

            currentState = State.Flee;
            chaseTarget = nearestThreat;
            fleeCommitTimer = isSchoolFishThreat ? 0.35f : 0.85f; 
            currentChaseTimer = 0f;
            return;
        }

        if (currentState == State.Flee)
        {
            // Exiting flee - clear goal so next flee episode starts fresh
            hasFleeGoal = false;
        }

        bool isCuttlefish = fishData != null && fishData.IsCuttlefish;
        bool isHuntingEligible = fishData != null && !fishData.IsSickFish && !fishData.IsGoldenFish && (fishData.Level > 1 || isCuttlefish);

        if (isHuntingEligible)
        {
            GetAggressionSettings(fishData.Level, isCuttlefish, out float currentChaseRadius, out _, out float currentChaseChance, out float currentMaxChaseTime, out float currentChaseCooldown);

            Transform bestTarget = null;
            float bestDist = currentChaseRadius;

            if (player != null && playerLevel < fishData.Level && !isCuttlefish)
            {
                if (distToPlayer < bestDist)
                {
                    bestDist = distToPlayer;
                    bestTarget = player;
                }
            }

            if (Fish.AllFish != null)
            {
                for (int i = 0; i < Fish.AllFish.Count; i++)
                {
                    Fish f = Fish.AllFish[i];
                    if (f == null || f == fishData || f.IsDead || !f.gameObject.activeInHierarchy || f.IsHooked) continue;
                    
                    if (f.Level < fishData.Level || (isCuttlefish && f.Level == 1 && !f.IsCuttlefish))
                    {
                        if (f.IsSpiked) continue;

                        float d = Vector2.Distance(transform.position, f.transform.position);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestTarget = f.transform;
                        }
                    }
                }
            }

            if (bestTarget != null && hunger < hungerChaseThreshold && postEatCooldownTimer <= 0f && Random.value < currentChaseChance)
            {
                currentState = State.Chase;
                chaseTarget = bestTarget;
                maxChaseTime = currentMaxChaseTime;
                chaseCooldownTime = currentChaseCooldown;
                currentChaseTimer = 0f;
                return;
            }
        }

        currentState = State.Wander;
        chaseTarget = null;
        currentChaseTimer = 0f;
    }

    public void GetAggressionSettings(int lvl, bool isCuttlefish, out float chaseRad, out float chaseSpdMult, out float chaseChance, out float maxTime, out float cooldown)
    {
        if (isCuttlefish)
        {
            chaseRad = 2.6f;
            chaseSpdMult = 1.10f;
            chaseChance = 0.35f;
            maxTime = 2.5f;
            cooldown = 4.5f;
            return;
        }

        switch (lvl)
        {
            case 1:
                chaseRad = 0f;
                chaseSpdMult = 1.0f;
                chaseChance = 0f;
                maxTime = 0f;
                cooldown = 999f;
                break;
            case 2:
                chaseRad = 3.4f;
                chaseSpdMult = 1.16f;
                chaseChance = 0.52f;
                maxTime = 3.2f;
                cooldown = 3.8f;
                break;
            case 3:
                chaseRad = 4.2f;
                chaseSpdMult = 1.25f;
                chaseChance = 0.70f;
                maxTime = 4.0f;
                cooldown = 3.0f;
                break;
            case 4:
                chaseRad = 4.5f;
                chaseSpdMult = 1.25f;
                chaseChance = 0.75f;
                maxTime = 4.5f;
                cooldown = 3.0f;
                break;
            case 5:
            default:
                chaseRad = 6.8f;
                chaseSpdMult = 1.40f;
                chaseChance = 0.95f;
                maxTime = 7.0f;
                cooldown = 1.5f;
                break;
        }
    }

    void HandleLeavingLogic()
    {
        lifeTime += Time.fixedDeltaTime;

        if (fishData != null && GameManager.PlayerLevel < fishData.Level && lifeTime > 15f)
        {
             if (!isLeaving) 
             {
                 StartLeaving();
                 leaveTimer = 999f; 
             }
             if (leaveTimer < 100f) leaveTimer = 999f;
             return; 
        }

        if (currentState != State.Wander)
        {
            isLeaving = false;
            return;
        }

        if (isLeaving)
        {
            leaveTimer -= Time.fixedDeltaTime;
            if (leaveTimer <= 0)
            {
                isLeaving = false;
            }
        }
        else
        {
            if (lifeTime > 8.0f && Random.value < 0.0015f) 
            {
                StartLeaving();
            }
        }
    }

    void StartLeaving()
    {
        isLeaving = true;
        leaveTimer = Random.Range(3f, 8f); 
        
        if (player != null)
        {
            Vector2 awayFromPlayer = ((Vector2)transform.position - (Vector2)player.position).normalized;
            float angle = Random.Range(-45f, 45f);
            leaveDirection = Quaternion.Euler(0, 0, angle) * awayFromPlayer;
        }
        else
        {
             leaveDirection = Random.insideUnitCircle.normalized;
        }
    }

    /// <summary>
    /// Picks a goal waypoint in open space away from the threat.
    /// School fish each get a per-fish angle bias so they scatter in a fan rather than piling up.
    /// </summary>
    private Vector2 ComputeFleeGoal(Transform threat, bool isSchoolFish)
    {
        Vector2 myPos = transform.position;
        Vector2 awayDir = (myPos - (Vector2)threat.position).normalized;

        // Per-fish scatter: school fish fan up to ±40°, solo fish up to ±15°
        float maxScatter = isSchoolFish ? 40f : 15f;
        float individualBias = Mathf.Sin(GetInstanceID() * 137.5f) * maxScatter;
        float dynamicJitter  = Random.Range(-8f, 8f); // small random nudge per goal refresh
        float totalAngle = (individualBias + dynamicJitter) * Mathf.Deg2Rad;

        float cs = Mathf.Cos(totalAngle);
        float sn = Mathf.Sin(totalAngle);
        Vector2 scatteredDir = new Vector2(awayDir.x * cs - awayDir.y * sn,
                                           awayDir.x * sn + awayDir.y * cs).normalized;

        // Place goal 7–9 units away in that direction
        float goalDist = Random.Range(7f, 9f);
        Vector2 goalPoint = myPos + scatteredDir * goalDist;

        // Clamp goal vertically within current bounds (matching player fish)
        goalPoint.y = Mathf.Clamp(goalPoint.y, currentMinY, currentMaxY);

        return goalPoint;
    }

    public void SetInitialDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        currentDirection = direction.normalized;
        wanderTarget = currentDirection * wanderRadius;

        Vector3 s = transform.localScale;
        if (currentDirection.x < -0.08f)
        {
            s.x = -Mathf.Abs(s.x);
            if (fishData != null) fishData.IsFacingRight = false;
        }
        else if (currentDirection.x > 0.08f)
        {
            s.x = Mathf.Abs(s.x);
            if (fishData != null) fishData.IsFacingRight = true;
        }
        s.y = Mathf.Abs(s.y);
        transform.localScale = s;

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.rotation = 0f;
            rb.linearVelocity = currentDirection * (currentSpeed > 0f ? currentSpeed : moveSpeed);
        }
        transform.rotation = Quaternion.identity;
    }

    Vector2 GetWanderDirection()
    {
        float jitter = wanderJitter * Time.fixedDeltaTime * 60f;
        wanderTarget += new Vector2(Random.Range(-1f, 1f) * jitter, Random.Range(-1f, 1f) * jitter);
        wanderTarget = wanderTarget.normalized * wanderRadius;
        
        if (transform.position.y >= currentMaxY && wanderTarget.y > 0f)
        {
            wanderTarget.y = 0f;
        }
        else if (transform.position.y <= currentMinY && wanderTarget.y < 0f)
        {
            wanderTarget.y = 0f;
        }

        if (fishData != null && fishData.IsSickFish)
        {
            wanderTarget.y *= 0.3f; 
        }
        Vector2 forward = (currentDirection != Vector2.zero) ? currentDirection : Vector2.right;
        Vector2 circleCenter = (Vector2)transform.position + forward * wanderDistance;
        Vector2 targetWorld = circleCenter + wanderTarget;
        Vector2 baseDir = (targetWorld - (Vector2)transform.position).normalized;
        float t = Time.fixedTime * swimNoiseSpeed + randomOffset;
        float nx = Mathf.PerlinNoise(t, 0f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(0f, t) * 2f - 1f;
        Vector2 noise = new Vector2(nx, ny) * swimNoiseScale;
        if (fishData != null && fishData.IsSickFish)
        {
            noise.y *= 0.3f;
        }
        return (baseDir + noise).normalized;
    }

    private bool IsPrey(Collider2D col)
    {
        if (fishData != null && !fishData.IsSickFish && col.TryGetComponent<Fish>(out Fish otherFish))
        {
            if (fishData.Level > otherFish.Level && !otherFish.IsSpiked) return true;
        }
        return false;
    }

    Vector2 GetAvoidanceDirection()
    {
        Vector2 avoidForce = Vector2.zero;

        // NEW: Instead of reflecting the current direction blindly, we push against the collision normals
        RaycastHit2D hit = Physics2D.Raycast(transform.position, currentDirection, avoidDistance, obstacleMask);
        if (hit.collider != null)
        {
            if (IsPrey(hit.collider)) return Vector2.zero;
            avoidForce += hit.normal * 1.5f; 
        }
        
        Vector2 leftDir = Quaternion.Euler(0, 0, 25) * currentDirection;
        RaycastHit2D hitLeft = Physics2D.Raycast(transform.position, leftDir, avoidDistance * 0.75f, obstacleMask);
        if (hitLeft.collider != null)
        {
            if (!IsPrey(hitLeft.collider)) avoidForce += hitLeft.normal;
        }
            
        Vector2 rightDir = Quaternion.Euler(0, 0, -25) * currentDirection;
        RaycastHit2D hitRight = Physics2D.Raycast(transform.position, rightDir, avoidDistance * 0.75f, obstacleMask);
        if (hitRight.collider != null)
        {
            if (!IsPrey(hitRight.collider)) avoidForce += hitRight.normal;
        }

        return avoidForce == Vector2.zero ? Vector2.zero : avoidForce.normalized;
    }

    Vector2 GetSeparationDirection()
    {
        int count = Physics2D.OverlapCircle(transform.position, separationRadius, new ContactFilter2D { layerMask = separationMask, useLayerMask = true }, neighborBuffer);
        Vector2 separation = Vector2.zero;
        int separationCount = 0;

        for (int i = 0; i < count; i++)
        {
            var c = neighborBuffer[i];
            if (c == null || c.gameObject == gameObject) continue;

            if (c.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData != null && !fishData.IsSickFish && fishData.Level > otherFish.Level) continue;

                Vector2 awayFromNeighbor = (Vector2)transform.position - (Vector2)c.transform.position;
                float sqrMag = awayFromNeighbor.sqrMagnitude;
                if (sqrMag > 0.001f)
                {
                    float dist = Mathf.Sqrt(sqrMag);
                    float minGap = (fishData != null && (fishData.school != null || fishData.GroupSchool != null || fishData.Level == 1)) ? 1.15f : 0.85f;
                    float pushFactor = (dist < minGap) ? (minGap - dist) * 4.5f + 1.0f : 1.0f;
                    separation += (awayFromNeighbor / sqrMag) * pushFactor;
                    separationCount++;
                }
            }
        }

        return separationCount > 0 ? separation.normalized : Vector2.zero;
    }

    Vector2 GetAlignmentDirection()
    {
        int count = Physics2D.OverlapCircle(transform.position, separationRadius, new ContactFilter2D { layerMask = separationMask, useLayerMask = true }, neighborBuffer);
        Vector2 avg = Vector2.zero;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            var c = neighborBuffer[i];
            if (c == null || c.gameObject == gameObject) continue;

            if (c.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData != null && otherFish.Level != fishData.Level) continue;
            }

            var otherAI = c.GetComponent<FishAI>();
            if (otherAI != null)
            {
                avg += otherAI.rb != null ? otherAI.rb.linearVelocity.normalized : otherAI.currentDirection;
                n++;
            }
        }
        return n > 0 ? (avg / n).normalized : Vector2.zero;
    }

    public void OnAteFish(Fish prey)
    {
        hunger = Mathf.Min(hungerMax, hunger + eatHungerGain);
        postEatCooldownTimer = postEatCooldown;
    }

    Vector2 GetCohesionDirection()
    {
        int count = Physics2D.OverlapCircle(transform.position, separationRadius, new ContactFilter2D { layerMask = separationMask, useLayerMask = true }, neighborBuffer);
        Vector2 center = Vector2.zero;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            var c = neighborBuffer[i];
            if (c == null || c.gameObject == gameObject) continue;

            if (c.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData != null && otherFish.Level != fishData.Level) continue;
            }
            else continue;

            center += (Vector2)c.transform.position;
            n++;
        }
        if (n == 0) return Vector2.zero;
        center /= n;
        return (center - (Vector2)transform.position).normalized;
    }

    Vector2 GetBoundaryAvoidanceDirection()
    {
        // Fish can freely swim off the left/right edges — they spawn and despawn from both sides.
        // Only guard top and bottom so fish don't escape the world vertically.
        Vector2 pos = transform.position;
        Vector2 steer = Vector2.zero;

        float floorY   = RiverBoat.BoundsCached ? RiverBoat.GlobalFloorY   : -14f;
        float surfaceY = RiverBoat.BoundsCached ? RiverBoat.GlobalSurfaceY :  14f;
        float margin = 1.2f;

        if (pos.y > surfaceY - margin)
            steer.y = -1;
        else if (pos.y < floorY + margin)
            steer.y = 1;

        return steer;
    }

    private void UpdateMouthAnticipation()
    {
        if (fishData == null || !fishData.HasBiteSprites || fishData.IsSpiked || fishData.IsSickFish || fishData.IsCuttlefish) return;

        if (currentState == State.Chase && chaseTarget != null)
        {
            float distToTarget = Vector2.Distance(transform.position, chaseTarget.position);
            float snapDist = 1.3f + (fishData.Level * 0.35f);
            if (distToTarget <= snapDist)
            {
                fishData.TriggerBite(0.35f);
            }
        }
        else if (player != null && GameManager.PlayerLevel < fishData.Level)
        {
            float distToPlayer = Vector2.Distance(transform.position, player.position);
            float snapDist = 1.5f + (fishData.Level * 0.35f);
            if (distToPlayer <= snapDist)
            {
                Vector2 toPlayer = (player.position - transform.position).normalized;
                if (Vector2.Dot(currentDirection, toPlayer) > 0.4f)
                {
                    fishData.TriggerBite(0.35f);
                }
            }
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other == null || other.isTrigger) return;
        if (other.GetComponent<PlayerController>() != null || other.GetComponentInParent<PlayerController>() != null) return;

        // NEW: Only physically push the fish to resolve interpenetration.
        // DO NOT overwrite currentDirection here, as it conflicts with AI Steering and causes severe jitter.
        Vector2 currentPos = transform.position;
        Vector2 closest = other.ClosestPoint(currentPos);
        Vector2 diff = currentPos - closest;
        float dist = diff.magnitude;

        if (dist < 0.05f)
        {
            Vector2 colCenter = other.bounds.center;
            Vector2 pushDir = (currentPos - colCenter).normalized;
            if (pushDir == Vector2.zero) pushDir = Vector2.up;
            transform.position += (Vector3)(pushDir * (2f * Time.deltaTime)); 
        }
        else if (dist < 0.35f)
        {
            Vector2 normal = diff.normalized;
            transform.position += (Vector3)(normal * (1.5f * Time.deltaTime)); 
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // NEW: Completely disabled direction reflection on impact. 
        // Let the AI raycasts gracefully steer it away instead.
    }
}