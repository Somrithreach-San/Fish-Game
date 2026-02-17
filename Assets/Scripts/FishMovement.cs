using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Simple movement behavior inspired by Feeding Frenzy:
// - Wander with smooth steering
// - If player is within chaseRadius, gently steer towards player
// - Avoid abrupt turns and use sine-based flick for fins
[RequireComponent(typeof(Rigidbody2D))]
public class FishMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4f;
    public float turnSpeed = 200f;
    public float wanderRadius = 2f;
    public float wanderDistance = 3f;
    public float wanderJitter = 1f;

    public enum BehaviorType { Passive, Aggressive, Flee }

    [Header("Behavior")]
    public BehaviorType behaviorType = BehaviorType.Passive;

    public float chaseRadius = 5f;
    public float fleeRadius = 4f; // Add flee radius
    public float avoidDistance = 3f;
    public LayerMask obstacleMask;

    [Header("Visuals")]
    public Transform graphicsTransform;
    public float tailSwaySpeed = 8f;
    public float tailSwayAmount = 10f;

    private Rigidbody2D rb;
    private Transform player;
    private Vector2 currentDirection;
    private Vector2 wanderTarget;
    private Fish fishData;
    private int aiTickOffset;
    private Vector2 cachedAvoidanceDir;
    private Vector2 cachedBoundsDir;

    private void Awake()
    {
        // If FishAI is present, disable this script
        if (GetComponent<FishAI>() != null)
        {
            enabled = false;
            return;
        }

        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (graphicsTransform == null)
            graphicsTransform = transform.Find("PlayerGraphics") ?? transform;

        fishData = GetComponent<Fish>();

        // Initialize wander
        float theta = Random.value * 2 * Mathf.PI;
        wanderTarget = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * wanderRadius;
    }

    private void OnEnable()
    {
        if (!enabled) return;
        player = GameManager.instance != null ? GameManager.instance.playerGameObject?.transform : null;
        currentDirection = transform.right;
        if (currentDirection == Vector2.zero) currentDirection = Vector2.right;
        aiTickOffset = Random.Range(0, 5);
        cachedAvoidanceDir = Vector2.zero;
        cachedBoundsDir = Vector2.zero;
        
        // Reset wander target
        float theta = Random.value * 2 * Mathf.PI;
        wanderTarget = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * wanderRadius;
    }

    private void Start()
    {
        if (!enabled) return;
        if (player == null)
            player = GameManager.instance != null ? GameManager.instance.playerGameObject?.transform : null;
    }

    private void FixedUpdate()
    {
        if (player == null && GameManager.instance?.playerGameObject != null)
            player = GameManager.instance.playerGameObject.transform;

        // 1. Determine Target Direction
        Vector2 targetDir = currentDirection;
        
        // Check for chase or flee
        bool isChasing = false;
        
        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            
            if (behaviorType == BehaviorType.Aggressive && dist < chaseRadius)
            {
                 // Chase
                 targetDir = (player.position - transform.position).normalized;
                 isChasing = true;
            }
            else if (behaviorType == BehaviorType.Flee && dist < fleeRadius)
            {
                 // Flee
                 targetDir = (transform.position - player.position).normalized;
                 isChasing = true; // Use chase speed boost for fleeing too
            }
        }
        
        if (!isChasing)
        {
            // Wander
            targetDir = GetWanderDirection();
        }

        if ((Time.frameCount + aiTickOffset) % 5 == 0)
        {
            cachedAvoidanceDir = GetAvoidanceDirection();
            cachedBoundsDir = GetBoundaryAvoidanceDirection();
        }
        Vector2 avoidDir = cachedAvoidanceDir;
        if (avoidDir != Vector2.zero)
        {
            targetDir = Vector2.Lerp(targetDir, avoidDir, 0.8f).normalized;
        }

        Vector2 boundsDir = cachedBoundsDir;
        if (boundsDir != Vector2.zero)
        {
            // Strong override to keep fish on screen
            targetDir = Vector2.Lerp(targetDir, boundsDir, 0.6f).normalized;
        }

        if (targetDir == Vector2.zero) targetDir = currentDirection;

        // 2. Rotate towards target
        float step = turnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime;
        currentDirection = Vector3.RotateTowards(currentDirection, targetDir, step, 0f).normalized;

        if (currentDirection == Vector2.zero) currentDirection = transform.right;

        // 3. Move
        float currentSpeed = moveSpeed;
        if (isChasing) currentSpeed *= 1.2f;

        rb.linearVelocity = currentDirection * currentSpeed;

        // 4. Visuals
        float angle = Mathf.Atan2(currentDirection.y, currentDirection.x) * Mathf.Rad2Deg;
        rb.rotation = angle;

        if (graphicsTransform != null)
        {
            float swaySpeed = tailSwaySpeed * (currentSpeed / moveSpeed);
            float wiggle = Mathf.Sin(Time.fixedTime * swaySpeed) * tailSwayAmount;

            graphicsTransform.rotation = Quaternion.Euler(0, 0, angle + wiggle);
            
            // Fix: Increase deadzone to prevent rapid flipping when moving vertically
            Vector3 scale = transform.localScale;
            if (currentDirection.x < -0.25f) scale.y = -Mathf.Abs(scale.y);
            else if (currentDirection.x > 0.25f) scale.y = Mathf.Abs(scale.y);
            transform.localScale = scale;
        }

        // ApplyTailSway(currentSpeed); // Removed
    }

    Vector2 GetWanderDirection()
    {
        float jitter = wanderJitter * Time.fixedDeltaTime * 60f;
        wanderTarget += new Vector2(Random.Range(-1f, 1f) * jitter, Random.Range(-1f, 1f) * jitter);
        wanderTarget = wanderTarget.normalized * wanderRadius;

        Vector2 targetLocal = wanderTarget + new Vector2(wanderDistance, 0);
        Vector2 targetWorld = transform.TransformPoint(targetLocal);
        
        return (targetWorld - (Vector2)transform.position).normalized;
    }

    Vector2 GetAvoidanceDirection()
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, currentDirection, avoidDistance, obstacleMask);
        if (hit.collider != null)
        {
             // Check if it's food
             if (hit.collider.GetComponent<Fish>() != null)
             {
                 Fish f = hit.collider.GetComponent<Fish>();
                 if (fishData != null && fishData.Level > f.Level) 
                 {
                     // It's food, don't reflect!
                     // Return zero so we continue straight into it
                 }
                 else
                 {
                     return Vector2.Reflect(currentDirection, hit.normal).normalized;
                 }
             }
             else
             {
                 return Vector2.Reflect(currentDirection, hit.normal).normalized;
             }
        }
            
        Vector2 leftDir = Quaternion.Euler(0, 0, 30) * currentDirection;
        Vector2 rightDir = Quaternion.Euler(0, 0, -30) * currentDirection;

        if (Physics2D.Raycast(transform.position, leftDir, avoidDistance * 0.7f, obstacleMask))
        {
            // Check if it's food (Basic check)
            RaycastHit2D hitLeft = Physics2D.Raycast(transform.position, leftDir, avoidDistance * 0.7f, obstacleMask);
            if (hitLeft.collider != null && hitLeft.collider.GetComponent<Fish>() != null)
            {
                 // If it's a fish, ignore it for now (simplified vs FishAI)
                 // Or better, check level if we can access it
                 Fish f = hitLeft.collider.GetComponent<Fish>();
                 if (fishData != null && fishData.Level > f.Level) { /* Do nothing, don't turn */ }
                 else return Quaternion.Euler(0, 0, -45) * currentDirection;
            }
            else
            {
                return Quaternion.Euler(0, 0, -45) * currentDirection;
            }
        }
            
        if (Physics2D.Raycast(transform.position, rightDir, avoidDistance * 0.7f, obstacleMask))
        {
             // Check if it's food
            RaycastHit2D hitRight = Physics2D.Raycast(transform.position, rightDir, avoidDistance * 0.7f, obstacleMask);
            if (hitRight.collider != null && hitRight.collider.GetComponent<Fish>() != null)
            {
                 Fish f = hitRight.collider.GetComponent<Fish>();
                 if (fishData != null && fishData.Level > f.Level) { /* Do nothing */ }
                 else return Quaternion.Euler(0, 0, 45) * currentDirection;
            }
            else
            {
                return Quaternion.Euler(0, 0, 45) * currentDirection;
            }
        }

        return Vector2.zero;
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
}
