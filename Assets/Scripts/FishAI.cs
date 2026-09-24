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
    
    // New: Chase Limits to prevent sticking
    public float maxChaseTime = 5f;       // Give up after 5 seconds
    public float chaseCooldownTime = 3f;  // Ignore player for 3 seconds
    private float currentChaseTimer = 0f;
    private float currentCooldownTimer = 0f;
    // Removed cast-style movement flags

    // New: Random "Leave" Behavior
    // Instead of wandering randomly in circles, they will sometimes pick a "Leave" direction
    private float leaveTimer = 0f;
    private Vector2 leaveDirection = Vector2.zero;
    private bool isLeaving = false;
    private float lifeTime = 0f;

    [Header("Obstacle Avoidance")]
    public bool stayOnScreen = false; // Default false (enabled for Golden Fish only)
    public LayerMask obstacleMask;
    public float avoidDistance = 3f;
    [Tooltip("Layers to include in separation calculations (e.g. Fish, Enemy)")]
    public LayerMask separationMask = -1; // Default to Everything

    [Header("Visuals")]
    public Transform graphicsTransform;
    public float tailSwaySpeed = 8f;
    public float tailSwayAmount = 10f;
    // Removed head pivot-based tilt

    private Rigidbody2D rb;
    private Transform player;
    private Transform chaseTarget; // Can be Player or another Fish
    private GameManager cachedGameManager; // Cached reference
    private Fish fishData;
    private Vector2 currentDirection;
    private Vector2 wanderTarget;
    private System.Collections.Generic.List<Collider2D> neighborBuffer = new System.Collections.Generic.List<Collider2D>(10);

    private Vector2 cachedAvoidanceDir;
    private Vector2 cachedSeparationDir;
    private Vector2 cachedAlignmentDir;
    private Vector2 cachedCohesionDir;
    private int aiTickOffset;
    private float randomOffset; // Small random offset for wobble
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
    // Removed chase burst parameters
    public float hungerMax = 1f;
    public float hungerChaseThreshold = 0.40f;
    public float hungerDecayPerSecond = 0.05f;
    public float eatHungerGain = 0.6f;
    public float postEatCooldown = 3.5f;
    public float hungerChaseChance = 0.85f;
    private float lastDistToPlayer;
    // Removed chase burst timer

    // Sick Fish Behavior (Sluggish movement, resting cycles, and deep swimming)
    private float sickRestTimer = 0f;
    private bool isSickResting = false;
    private float nextSickCycleDuration = 6f;
    private float fleeCommitTimer = 0f;

    // Ink Disorientation (from Cuttlefish ink)
    private float inkDisorientTimer = 0f;
    public bool IsDisorientedByInk => inkDisorientTimer > 0f;

    public void ApplyInkDisorientation(float duration = 3.5f)
    {
        inkDisorientTimer = duration;
        chaseTarget = null;
        currentState = State.Wander;
    }

    // Individualized Organic Schooling & Swimming Dynamics
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

    // Conflicting script handling
    void Awake()
    {
        // Removed destructive logic. We no longer destroy FishMovement automatically.
        // If both are present, we rely on the manager/setup to enable the correct one.

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
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // Smooth movement
        }

        if (graphicsTransform == null)
            graphicsTransform = transform.Find("PlayerGraphics") ?? transform;

        fishData = GetComponent<Fish>();
        
        // Initialize wander target
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
        currentState = State.Wander;
        sickRestTimer = Random.Range(1f, 4f);
        isSickResting = false;
        nextSickCycleDuration = Random.Range(5f, 8f);
        InitPersonality();
    }

    void Start()
    {
        player = GameManager.instance?.playerGameObject?.transform;
        // Give initial direction
        currentDirection = transform.right;
        if (currentDirection == Vector2.zero) currentDirection = Vector2.right;
        // No flow direction

        // Randomize tick offset to distribute load across frames
        aiTickOffset = Random.Range(0, 5);
        hunger = 0f;
        postEatCooldownTimer = 0f;
        sickRestTimer = Random.Range(1f, 4f);
        isSickResting = false;
        nextSickCycleDuration = Random.Range(5f, 8f);
        InitPersonality();
        // No head pivot setup
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

        if (inkDisorientTimer > 0f)
        {
            inkDisorientTimer = Mathf.Max(0f, inkDisorientTimer - Time.fixedDeltaTime);
            chaseTarget = null;
            if (currentState == State.Chase) currentState = State.Wander;
        }

        // Sick fish resting cycle logic
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
                    nextSickCycleDuration = Random.Range(2.0f, 3.5f); // Rest for 2.0-3.5 seconds
                }
                else if (isSickResting && sickRestTimer >= nextSickCycleDuration)
                {
                    isSickResting = false;
                    sickRestTimer = 0f;
                    nextSickCycleDuration = Random.Range(5.0f, 8.0f); // Swim for 5.0-8.0 seconds
                }
            }
        }

        UpdateState();

        // Mouth bite anticipation when closing in on prey or near player
        UpdateMouthAnticipation();

        // New: Handle "Leaving" state logic (randomly decide to swim away)
        HandleLeavingLogic();

        bool isSchoolFish = (fishData != null && (fishData.school != null || fishData.GroupSchool != null || fishData.Level == 1));
        bool inActiveSchool = (fishData != null && fishData.school != null && currentState == State.Wander);

        // 1. Determine Desired Direction based on State
        Vector2 targetDir = currentDirection;

        schoolSpeedMod = 1.0f;

        // SCHOOLING OVERRIDE (If part of a school, and just wandering, follow the school with smooth formation spring)
        if (inActiveSchool)
        {
            FishSchool school = fishData.school;
            Vector2 schoolCenter = (Vector2)school.transform.position;
            Vector2 schoolTravelDir = school.TravelDirection;

            // Individualized subtle floating undulation
            float driftX = Mathf.Sin(Time.time * personalWobbleSpeedX + personalWobblePhaseX) * 0.12f;
            float driftY = Mathf.Cos(Time.time * personalWobbleSpeedY + personalWobblePhaseY) * 0.12f;
            Vector2 organicDrift = new Vector2(driftX, driftY);

            // Compute ideal formation slot position in world space
            Vector2 slotOffset = fishData.formationOffset;
            if (!school.MovingRight)
            {
                slotOffset.x = -Mathf.Abs(slotOffset.x);
            }
            else
            {
                slotOffset.x = Mathf.Abs(slotOffset.x);
            }

            Vector2 idealSlotPos = schoolCenter + slotOffset + organicDrift;
            Vector2 toSlot = idealSlotPos - (Vector2)transform.position;

            // Soft mutual separation between schoolmates to avoid overlapping
            Vector2 separation = Vector2.zero;
            foreach (var mate in school.RegisteredFishList)
            {
                if (mate != null && mate != fishData && mate.gameObject.activeInHierarchy && !mate.IsDead)
                {
                    Vector2 diff = (Vector2)transform.position - (Vector2)mate.transform.position;
                    float distSqr = diff.sqrMagnitude;
                    if (distSqr > 0.0001f && distSqr < 0.64f) // within 0.80m
                    {
                        float dist = Mathf.Sqrt(distSqr);
                        separation += (diff / dist) * ((0.80f - dist) / 0.80f) * 0.45f;
                    }
                }
            }

            // Desired heading blends school travel direction + gentle spring pull toward slot + separation
            Vector2 desiredHeading = schoolTravelDir + toSlot * 1.5f + separation;
            if (desiredHeading.sqrMagnitude > 0.001f)
            {
                targetDir = desiredHeading.normalized;
            }
            else
            {
                targetDir = schoolTravelDir;
            }

            // Smooth speed modulation: slight speed adjustment if lagging behind or ahead of assigned slot
            float slotDistanceAhead = Vector2.Dot(toSlot, schoolTravelDir);
            schoolSpeedMod = Mathf.Clamp(1.0f + slotDistanceAhead * 0.22f, 0.85f, 1.25f);
        }
        // If we are "leaving", override normal behavior unless we are fleeing/chasing intensely
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
                    Vector2 rawFlee = Vector2.zero;
                    if (chaseTarget != null)
                    {
                        // Check if fleeing from Shark Hazard
                        if (chaseTarget.GetComponent<SharkHazard>() != null)
                        {
                            // Evade vertically away from the shark's horizontal charge path
                            float dodgeY = (transform.position.y >= chaseTarget.position.y) ? 1.0f : -1.0f;
                            Vector2 awayFromShark = ((Vector2)transform.position - (Vector2)chaseTarget.position).normalized;
                            rawFlee = new Vector2(awayFromShark.x * 0.4f, dodgeY * 1.6f);
                        }
                        else
                        {
                            rawFlee = (Vector2)transform.position - (Vector2)chaseTarget.position;
                        }
                    }
                    else if (player != null)
                    {
                        rawFlee = (Vector2)transform.position - (Vector2)player.position;
                    }

                    if (rawFlee.sqrMagnitude > 0.001f)
                    {
                        // Add individual organic scatter perturbation for school minnows
                        if (isSchoolFish)
                        {
                            float scatterAngle = Mathf.Sin(GetInstanceID() * 59.3f + Time.time * 0.6f) * 32f * Mathf.Deg2Rad;
                            float cs = Mathf.Cos(scatterAngle);
                            float sn = Mathf.Sin(scatterAngle);
                            rawFlee = new Vector2(rawFlee.x * cs - rawFlee.y * sn, rawFlee.x * sn + rawFlee.y * cs);
                        }

                        targetDir = rawFlee.normalized;
                        // Avoid vertical indecision (jittering left/right when threat is directly above/below)
                        if (Mathf.Abs(targetDir.x) < 0.25f && (chaseTarget == null || chaseTarget.GetComponent<SharkHazard>() == null))
                        {
                            float escapeX = (currentDirection.x >= 0f) ? 0.45f : -0.45f;
                            targetDir = new Vector2(targetDir.x + escapeX, targetDir.y).normalized;
                        }
                    }
                    break;
            }
        }

        // OPTIMIZATION: Throttle expensive checks (Raycasts & OverlapCircle)
        // Run only once every 5 physics frames (approx 10 times per second instead of 50)
        if ((Time.frameCount + aiTickOffset) % 5 == 0)
        {
            cachedAvoidanceDir = GetAvoidanceDirection();
            cachedSeparationDir = GetSeparationDirection();
            cachedAlignmentDir = GetAlignmentDirection();
            cachedCohesionDir = GetCohesionDirection();
        }

        // 2. Obstacle Avoidance (Overrides state)
        // Weighted blending to prevent snapping
        if (cachedAvoidanceDir != Vector2.zero)
        {
            // Add avoidance force rather than hard Lerp
            targetDir += cachedAvoidanceDir * 3.0f; 
        }

        // 3. Flocking forces (Only apply to non-school wandering fish; school fish have dedicated formation logic)
        if (!inActiveSchool)
        {
            if (cachedSeparationDir != Vector2.zero)
            {
                 // When fleeing, separation should be minimized so they focus purely on running away
                 float sepMult = (currentState == State.Flee) ? 0.5f : 1.5f;
                 targetDir += cachedSeparationDir * sepMult;
            }

            if (currentState != State.Flee)
            {
                if (cachedAlignmentDir != Vector2.zero)
                {
                    targetDir += cachedAlignmentDir * 1.0f;
                }
                if (cachedCohesionDir != Vector2.zero)
                {
                    float cohesionWeight = (fishData != null && fishData.Level == 1) ? 1.5f : 0.8f;
                    targetDir += cachedCohesionDir * cohesionWeight;
                }
            }
        }

        // 4. Boundary Avoidance (If enabled)
        if (stayOnScreen)
        {
            Vector2 boundsDir = GetBoundaryAvoidanceDirection();
            if (boundsDir != Vector2.zero)
            {
                 // Add strong force (Stronger than obstacle avoidance 3.0f) to ensure we stay in bounds
                 targetDir += boundsDir * 5.0f;
            }
        }

        if (fishData != null && fishData.IsSickFish)
        {
            float floorY = RiverBoat.BoundsCached ? RiverBoat.GlobalFloorY : -14f;
            float surfaceY = RiverBoat.BoundsCached ? RiverBoat.GlobalSurfaceY : 14f;
            float deepMinY = floorY + 0.8f;
            float deepMaxY = floorY + (surfaceY - floorY) * 0.28f;

            if (transform.position.y > deepMaxY)
            {
                // Steer downward towards the deeper water layer
                targetDir.y = Mathf.Min(targetDir.y - 0.6f, -0.4f);
            }
            else if (transform.position.y < deepMinY)
            {
                // Steer slightly up off bottom floor
                targetDir.y = Mathf.Max(targetDir.y + 0.35f, 0.2f);
            }
            else
            {
                // In deep zone: damp vertical oscillation so it wanders smoothly along the floor
                targetDir.y *= 0.3f;
            }
        }
        else if (currentState == State.Wander)
        {
            targetDir.y *= 0.6f;
        }
        
        // Normalize once after all forces are applied
        targetDir = targetDir.normalized;

        // Ensure targetDir is valid
        if (targetDir == Vector2.zero) targetDir = currentDirection;

        // 4. Steer Current Direction towards Target Direction
        if (targetDir != Vector2.zero)
        {
            float activeTurnSpeed = turnSpeed * personalTurnMultiplier;
            if (fishData != null && fishData.IsSpiked)
            {
                activeTurnSpeed = 60f; // Turn sluggishly while bloated with water
            }
            else if (fishData != null && fishData.IsSickFish)
            {
                activeTurnSpeed = 70f; // Sluggish, weak turns for sick fish
            }
            else if (currentState == State.Flee)
            {
                activeTurnSpeed = (isSchoolFish ? 260f : 220f) * personalTurnMultiplier; // Smooth, natural evasion turns
            }
            else if (inActiveSchool)
            {
                activeTurnSpeed = 175f * personalTurnMultiplier; // Cohesive, fluid schooling turns without snappy twitching
            }

            // Check if this is a horizontal reversal (changing from swimming right to left, or left to right)
            bool isReversingHorizontal = (currentDirection.x * targetDir.x < -0.1f) && Mathf.Abs(targetDir.x) > 0.25f && Mathf.Abs(currentDirection.x) > 0.25f;

            if (isReversingHorizontal)
            {
                // Reversal turnaround:
                // Smoothly and rapidly interpolate horizontal direction across 0 without ballooning Y upwards or rotating into 3D Z!
                float turnRate = Mathf.Max(activeTurnSpeed * 1.5f, 300f) * Mathf.Deg2Rad;
                currentDirection.x = Mathf.MoveTowards(currentDirection.x, targetDir.x, turnRate * Time.fixedDeltaTime);
                currentDirection.y = Mathf.MoveTowards(currentDirection.y, targetDir.y, turnRate * 0.7f * Time.fixedDeltaTime);

                // If horizontal direction has crossed and aligned with targetDir, normalize to full unit length
                if (Mathf.Sign(currentDirection.x) == Mathf.Sign(targetDir.x) && Mathf.Abs(currentDirection.x) > 0.30f)
                {
                    currentDirection = currentDirection.normalized;
                }
            }
            else
            {
                // Normal directional steering via direct 2D vector interpolation:
                currentDirection = Vector2.MoveTowards(currentDirection, targetDir, activeTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime);
                if (currentDirection.sqrMagnitude > 0.001f)
                {
                    currentDirection.Normalize();
                }
            }
        }
        
        // Safety check
        if (currentDirection == Vector2.zero) currentDirection = transform.right;

        // Fluid swimming stroke wave (gentle 5% oscillation mimicking realistic tail fin strokes, without jerky discrete speed jumps)
        float swimStroke = 1.0f + Mathf.Sin(Time.time * 3.8f + personalWobblePhaseX) * 0.05f;
        float baseSpeed = (fishData != null && fishData.Level == 1) 
            ? (2.8f * personalSpeedMultiplier * swimStroke) 
            : (moveSpeed * personalSpeedMultiplier);

        float targetSpeed = inActiveSchool ? (baseSpeed * schoolSpeedMod) : baseSpeed;
        if (currentState == State.Flee)
        {
            // School fish dart and flee with a balanced, natural boost (~3.4 m/s vs player's 5.75 m/s)
            float fleeMult = isSchoolFish ? 1.30f : 1.22f;
            targetSpeed = baseSpeed * fleeMult;
        }
        if (currentState == State.Chase) targetSpeed *= chaseSpeedMultiplier;
        if (fishData != null && fishData.IsSpiked) targetSpeed *= 0.25f; // User request: swim and move a lot slower while spiked
        if (fishData != null && fishData.IsPoisoned) targetSpeed *= 0.65f; // Sick fish penalty: 65% speed for 3.5s
        if (fishData != null && fishData.IsSickFish)
        {
            if (isSickResting)
            {
                targetSpeed = 0f; // Stopped resting
            }
            else if (currentState == State.Flee)
            {
                targetSpeed = baseSpeed * 0.5f; // Sluggish panic flee
            }
            else
            {
                targetSpeed = baseSpeed * 0.35f; // User request: sick fish swims very slow
            }
        }
        if (inkDisorientTimer > 0f) targetSpeed *= 0.35f; // Disoriented by blinding ink
        float activeMinSpeed = (fishData != null && (fishData.IsSpiked || fishData.IsPoisoned)) ? 0.3f : 
                               ((fishData != null && fishData.IsSickFish) ? 0f : 
                               ((fishData != null && fishData.Level == 1) ? 1.5f : minSpeed));
        targetSpeed = Mathf.Clamp(targetSpeed, activeMinSpeed, maxSpeed);
        float rate = targetSpeed > currentSpeed ? accel : decel;
        if (currentState == State.Flee && isSchoolFish)
        {
            rate = 4.0f; // Smooth, natural burst acceleration away from danger (reduced from 7.0f)
        }
        else if (inActiveSchool)
        {
            rate = 3.5f; // Smooth, continuous acceleration matching school speed
        }
        else if (fishData != null && fishData.IsSickFish)
        {
            rate = 1.5f;
        }
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);
        KeepVerticalInBounds();

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (fishData == null) fishData = GetComponent<Fish>();

        if (rb != null)
        {
            if (fishData != null && fishData.IsSickFish && isSickResting && currentSpeed < 0.2f)
            {
                // Gentle weak floating drift while resting in place
                float driftX = Mathf.Sin(Time.time * 0.8f + aiTickOffset) * 0.08f;
                float driftY = Mathf.Cos(Time.time * 0.6f + aiTickOffset) * 0.04f;
                rb.linearVelocity = new Vector2(driftX, driftY);
            }
            else
            {
                rb.linearVelocity = currentDirection * currentSpeed;
            }
            rb.rotation = 0f;
        }

        // 6. Physics Rotation (Standard 2D Flipping with robust personal hysteresis to prevent flickering)
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
        
        // Ensure Y is always positive (Upright)
        scale.y = Mathf.Abs(scale.y);
            
        transform.localScale = scale;
    }
    
    void KeepVerticalInBounds()
    {
        float top = 14f;
        float bottom = -14f;
        float margin = 1.2f;
        Vector3 pos = transform.position;
        if (pos.y > top - margin)
        {
            pos.y = top - margin;
            transform.position = pos;
            if (currentDirection.y > 0f) currentDirection.y = 0f;
        }
        else if (pos.y < bottom + margin)
        {
            pos.y = bottom + margin;
            transform.position = pos;
            if (currentDirection.y < 0f) currentDirection.y = 0f;
        }
    }

    void UpdateState()
    {
        if (player == null && GameManager.instance?.playerGameObject != null)
        {
            player = GameManager.instance.playerGameObject.transform;
        }

        if (fishData == null) return;

        // Cuttlefish never chases or hunts any fish (neutral autonomous hazard)
        if (fishData.IsCuttlefish)
        {
            hunger = 0f;
            currentState = State.Wander;
            chaseTarget = null;
            return;
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

        // Decrease Cooldown
        if (currentCooldownTimer > 0f)
        {
            currentCooldownTimer -= Time.fixedDeltaTime;
            currentState = State.Wander; // Force wander during cooldown
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

        // 1. Flee State Commitment & Hysteresis (Prevents rapid border flickering)
        if (fleeCommitTimer > 0f)
        {
            fleeCommitTimer -= Time.fixedDeltaTime;
            if (chaseTarget != null && chaseTarget.gameObject.activeInHierarchy)
            {
                currentState = State.Flee;
                return;
            }
        }

        // Flee Detection Radii (Balanced sensitivity: 4.2f for schools, 3.8f for normal)
        bool isSchoolFishThreat = (fishData != null && (fishData.school != null || fishData.GroupSchool != null || fishData.Level == 1));
        float enterFleeRadius = isSchoolFishThreat ? 4.2f : Mathf.Max(fleeRadius, 3.8f);
        float exitFleeRadius = enterFleeRadius + 1.2f;
        float currentFleeRadius = (currentState == State.Flee) ? exitFleeRadius : enterFleeRadius;

        Transform nearestThreat = null;
        float nearestThreatDist = float.MaxValue;

        // 0. Shark Hazard Detection (All AI fish of all levels flee and dodge charging sharks!)
        if (SharkHazard.ActiveSharks != null)
        {
            for (int i = 0; i < SharkHazard.ActiveSharks.Count; i++)
            {
                SharkHazard shark = SharkHazard.ActiveSharks[i];
                if (shark == null || !shark.gameObject.activeInHierarchy || !shark.IsCharging) continue;

                float d = Vector2.Distance(transform.position, shark.transform.position);
                float sharkFleeRadius = 9.0f; // Detect massive shark hazard early
                if (d < sharkFleeRadius && d < nearestThreatDist)
                {
                    nearestThreat = shark.transform;
                    nearestThreatDist = d;
                }
            }
        }

        // Spiked pufferfish is immune to normal bites, but vulnerable to sharks
        bool canBeEatenByBites = (fishData == null || !fishData.IsSpiked);

        if (canBeEatenByBites)
        {
            // Check if player can eat this fish
            bool playerCanEatMe = player != null && playerLevel >= fishData.Level;
            if (playerCanEatMe && distToPlayer < currentFleeRadius && distToPlayer < nearestThreatDist)
            {
                nearestThreat = player;
                nearestThreatDist = distToPlayer;
            }

            // Check if any bigger AI predator fish can eat this fish (Applies to Level 1, 2, 3, 4, 5)
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

        // If a predator, shark hazard, or dangerous player is within flee radius, FLEE!
        if (nearestThreat != null)
        {
            currentState = State.Flee;
            chaseTarget = nearestThreat;
            fleeCommitTimer = 0.5f; // Commit to fleeing for at least 0.5s to prevent state flickering
            currentChaseTimer = 0f;
            return;
        }

        // 3. CHASE OTHER FISH (Priority: Hunger)
        // If not interacting with player or fleeing predators, look for food.
        // Sick fish refuses food and never chases or hunts anything
        if (fishData == null || !fishData.IsSickFish)
        {
            Fish nearestFood = null;
            float nearestDist = chaseRadius; // Only look within chase radius

            foreach (var f in Fish.AllFish)
            {
                if (f == null || f == fishData || f.IsDead || !f.gameObject.activeInHierarchy || f.IsHooked) continue;
                
                // Can I eat it?
                if (f.Level < fishData.Level)
                {
                    // Spiked fish cannot be eaten, so do not hunt them
                    if (f.IsSpiked) continue;

                    float d = Vector2.Distance(transform.position, f.transform.position);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearestFood = f;
                    }
                }
            }

            if (nearestFood != null && hunger < hungerChaseThreshold && postEatCooldownTimer <= 0f && Random.value < hungerChaseChance)
            {
                currentState = State.Chase;
                chaseTarget = nearestFood.transform;
                currentChaseTimer = 0f;
                return;
            }
        }

        // 4. WANDER
        currentState = State.Wander;
        chaseTarget = null;
        currentChaseTimer = 0f;
    }

    void HandleLeavingLogic()
    {
        lifeTime += Time.fixedDeltaTime;

        // Force leave for old predators (High Level)
        // If they have been around for > 15 seconds, they should leave.
        // This ensures they don't stick around forever and get stuck.
        if (fishData != null && GameManager.PlayerLevel < fishData.Level && lifeTime > 15f)
        {
             if (!isLeaving) 
             {
                 StartLeaving();
                 leaveTimer = 999f; // Effectively permanent until destroyed
             }
             // Ensure they don't stop leaving
             if (leaveTimer < 100f) leaveTimer = 999f;
             return; 
        }

        // Only consider leaving if we are just wandering
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
                // Stop leaving
                isLeaving = false;
            }
        }
        else
        {
            // Do not leave if newly spawned - allow fish to enter and populate the waters
            if (lifeTime > 8.0f && Random.value < 0.0015f) 
            {
                StartLeaving();
            }
        }
    }

    void StartLeaving()
    {
        isLeaving = true;
        leaveTimer = Random.Range(3f, 8f); // Leave for 3-8 seconds
        
        // Pick a direction AWAY from the center (0,0) or just a random far direction
        // Let's pick a random direction that is roughly away from the player to look like they are "done" with you
        if (player != null)
        {
            Vector2 awayFromPlayer = ((Vector2)transform.position - (Vector2)player.position).normalized;
            // Add some randomness so it's not a perfect straight line away
            float angle = Random.Range(-45f, 45f);
            leaveDirection = Quaternion.Euler(0, 0, angle) * awayFromPlayer;
        }
        else
        {
             leaveDirection = Random.insideUnitCircle.normalized;
        }
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
        if (fishData != null && fishData.IsSickFish)
        {
            wanderTarget.y *= 0.3f; // Damp vertical wander for sick fish
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

    Vector2 GetAvoidanceDirection()
    {
        // Raycast ahead
        RaycastHit2D hit = Physics2D.Raycast(transform.position, currentDirection, avoidDistance, obstacleMask);
        if (hit.collider != null)
        {
            // CHECK FOR FOOD: If we hit a fish we can eat, DON'T avoid it! (Only if not sick)
            if (fishData != null && !fishData.IsSickFish)
            {
                // TryGetComponent is faster
                if (hit.collider.TryGetComponent<Fish>(out Fish otherFish))
                {
                    if (fishData.Level > otherFish.Level && !otherFish.IsSpiked)
                    {
                        // It's prey! Charge!
                        return Vector2.zero;
                    }
                }
            }

            // Reflect off the normal
            return Vector2.Reflect(currentDirection, hit.normal).normalized;
        }
        
        // Side feelers
        Vector2 leftDir = Quaternion.Euler(0, 0, 30) * currentDirection;
        Vector2 rightDir = Quaternion.Euler(0, 0, -30) * currentDirection;

        // Check Left
        RaycastHit2D hitLeft = Physics2D.Raycast(transform.position, leftDir, avoidDistance * 0.7f, obstacleMask);
        if (hitLeft.collider != null)
        {
            if (fishData != null && !fishData.IsSickFish && hitLeft.collider.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData.Level > otherFish.Level && !otherFish.IsSpiked) return Vector2.zero;
            }
            return Quaternion.Euler(0, 0, -45) * currentDirection; // Turn right
        }
            
        // Check Right
        RaycastHit2D hitRight = Physics2D.Raycast(transform.position, rightDir, avoidDistance * 0.7f, obstacleMask);
        if (hitRight.collider != null)
        {
            if (fishData != null && !fishData.IsSickFish && hitRight.collider.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData.Level > otherFish.Level && !otherFish.IsSpiked) return Vector2.zero;
            }
            return Quaternion.Euler(0, 0, 45) * currentDirection; // Turn left
        }

        return Vector2.zero;
    }

    Vector2 GetSeparationDirection()
    {
        // OPTIMIZATION: Use LayerMask to filter neighbors
        int count = Physics2D.OverlapCircle(transform.position, separationRadius, new ContactFilter2D { layerMask = separationMask, useLayerMask = true }, neighborBuffer);
        Vector2 separation = Vector2.zero;
        int separationCount = 0;

        for (int i = 0; i < count; i++)
        {
            var c = neighborBuffer[i];
            // Skip self or null
            if (c == null || c.gameObject == gameObject) continue;

            if (c.TryGetComponent<Fish>(out Fish otherFish))
            {
                // Don't push away from prey if I am hunting it
                if (fishData != null && !fishData.IsSickFish && fishData.Level > otherFish.Level)
                {
                    continue;
                }

                // Vector from neighbor to me (pushes me AWAY from neighbor)
                Vector2 awayFromNeighbor = (Vector2)transform.position - (Vector2)c.transform.position;
                float sqrMag = awayFromNeighbor.sqrMagnitude;
                if (sqrMag > 0.001f)
                {
                    // Strong anti-overlap repulsion if closer than minimum gap (1.1 units for school fish)
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

            // Only align with fish of the same level/species, never predators
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

            // Only group with fish of the exact same level, never predators or the player!
            if (c.TryGetComponent<Fish>(out Fish otherFish))
            {
                if (fishData != null && otherFish.Level != fishData.Level) continue;
            }
            else
            {
                continue; // Ignore non-fish (player, hazards, boats) in cohesion
            }

            center += (Vector2)c.transform.position;
            n++;
        }
        if (n == 0) return Vector2.zero;
        center /= n;
        return (center - (Vector2)transform.position).normalized;
    }

    Vector2 GetBoundaryAvoidanceDirection()
    {
        if (Camera.main == null) return Vector2.zero;

        float screenRatio = (float)Screen.width / (float)Screen.height;
        float height = Camera.main.orthographicSize;
        float width = height * screenRatio;

        // Add a margin so they turn before hitting the edge
        float margin = 1.0f; 
        
        Vector2 pos = transform.position;
        Vector2 steer = Vector2.zero;

        if (pos.x > width - margin)
            steer.x = -1;
        else if (pos.x < -width + margin)
            steer.x = 1;

        if (pos.y > height - margin)
            steer.y = -1;
        else if (pos.y < -height + margin)
            steer.y = 1;

        return steer.normalized;
    }

    private void UpdateMouthAnticipation()
    {
        if (fishData == null || !fishData.HasBiteSprites || fishData.IsSpiked || fishData.IsSickFish || fishData.IsCuttlefish) return;

        // 1. If chasing prey, open mouth when within snapping distance
        if (currentState == State.Chase && chaseTarget != null)
        {
            float distToTarget = Vector2.Distance(transform.position, chaseTarget.position);
            float snapDist = 1.3f + (fishData.Level * 0.35f);
            if (distToTarget <= snapDist)
            {
                fishData.TriggerBite(0.35f);
            }
        }
        // 2. If player is smaller and directly ahead in swimming path, snap mouth
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
}
