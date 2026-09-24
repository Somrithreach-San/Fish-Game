using System.Collections.Generic;
using UnityEngine;

public class FishSchool : MonoBehaviour
{
    public static List<FishSchool> ActiveSchools { get; } = new List<FishSchool>();

    public static int ActiveSchoolCount
    {
        get
        {
            ActiveSchools.RemoveAll(s => s == null || !s.gameObject.activeInHierarchy);
            return ActiveSchools.Count;
        }
    }

    private void OnEnable()
    {
        if (!ActiveSchools.Contains(this))
        {
            ActiveSchools.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveSchools.Remove(this);
    }

    public Vector2 CurrentDestination { get; private set; }
    private bool movingRight;
    private float decisionTimer;
    
    // Bounds
    private float minX = -45f;
    private float maxX = 45f;
    private float minY = -13f;
    private float maxY = 13f;

    [Header("Group Tracking & Reward")]
    private readonly HashSet<Fish> registeredFish = new HashSet<Fish>();
    private readonly HashSet<Fish> remainingFish = new HashSet<Fish>();
    private static readonly System.Predicate<Fish> s_DeadOrInactiveFilter = f => f == null || !f.gameObject.activeInHierarchy || f.IsDead;
    private float cleanupTimer = 0f;
    private bool isDisqualified = false;
    private bool isCleared = false;

    // Bonus is dynamically set to exactly 1 extra school fish's XP
    private int singleFishXp = 8;

    public List<Fish> RegisteredFishList => new List<Fish>(registeredFish);

    public enum SchoolFormationType
    {
        Cluster,
        Wedge,
        StaggeredStream,
        Diamond,
        Crescent
    }

    /// <summary>
    /// Generates organic, randomized formation offsets for school fish with natural spacing.
    /// Never returns a rigid, fixed identical shape across spawns.
    /// </summary>
    public static Vector2[] GenerateSchoolFormation(int count, bool movingRight)
    {
        Vector2[] offsets = new Vector2[count];
        if (count <= 0) return offsets;

        float trailDir = movingRight ? -1f : 1f;

        // Choose a random formation archetype for each school
        SchoolFormationType type = (SchoolFormationType)Random.Range(0, 5);

        switch (type)
        {
            case SchoolFormationType.Cluster:
                // Organic tight cluster with natural micro-spacing
                offsets[0] = new Vector2(0f, Random.Range(-0.15f, 0.15f));
                for (int i = 1; i < count; i++)
                {
                    Vector2 candidate = Vector2.zero;
                    bool valid = false;
                    for (int attempt = 0; attempt < 25; attempt++)
                    {
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        float dist = Random.Range(0.65f, 1.25f);
                        candidate = new Vector2(
                            trailDir * (Mathf.Abs(Mathf.Cos(angle)) * dist * 1.05f + 0.18f * i),
                            Mathf.Sin(angle) * dist * 0.75f
                        );

                        valid = true;
                        for (int j = 0; j < i; j++)
                        {
                            if (Vector2.Distance(candidate, offsets[j]) < 0.60f)
                            {
                                valid = false;
                                break;
                            }
                        }
                        if (valid) break;
                    }
                    offsets[i] = candidate;
                }
                break;

            case SchoolFormationType.Wedge:
                // Tight, asymmetrical natural V-wedge
                offsets[0] = new Vector2(0f, Random.Range(-0.10f, 0.10f));
                float upperSpread = Random.Range(0.48f, 0.68f);
                float lowerSpread = Random.Range(0.48f, 0.68f);
                for (int i = 1; i < count; i++)
                {
                    bool isUpper = (i % 2 == 1);
                    int tier = (i + 1) / 2;
                    float forwardOffset = trailDir * (tier * Random.Range(0.65f, 0.88f) + Random.Range(-0.08f, 0.08f));
                    float sideOffset = isUpper
                        ? (tier * upperSpread + Random.Range(-0.10f, 0.10f))
                        : (-tier * lowerSpread + Random.Range(-0.10f, 0.10f));
                    offsets[i] = new Vector2(forwardOffset, sideOffset);
                }
                break;

            case SchoolFormationType.StaggeredStream:
                // Tight drafting stream
                offsets[0] = new Vector2(0f, Random.Range(-0.10f, 0.10f));
                for (int i = 1; i < count; i++)
                {
                    float xDist = trailDir * (i * Random.Range(0.70f, 0.95f));
                    float ySign = (i % 2 == 1) ? 1f : -1f;
                    float yDist = ySign * Random.Range(0.22f, 0.45f) + Mathf.Sin(i * 1.3f) * 0.12f;
                    offsets[i] = new Vector2(xDist, yDist);
                }
                break;

            case SchoolFormationType.Diamond:
                offsets[0] = new Vector2(0f, Random.Range(-0.08f, 0.08f)); // Leader
                if (count > 1) offsets[1] = new Vector2(trailDir * Random.Range(0.70f, 0.90f), Random.Range(0.45f, 0.65f)); // Top flank
                if (count > 2) offsets[2] = new Vector2(trailDir * Random.Range(0.70f, 0.90f), -Random.Range(0.45f, 0.65f)); // Bottom flank
                if (count > 3) offsets[3] = new Vector2(trailDir * Random.Range(1.40f, 1.70f), Random.Range(-0.12f, 0.12f)); // Center tail
                if (count > 4) offsets[4] = new Vector2(trailDir * Random.Range(2.05f, 2.35f), Random.Range(-0.18f, 0.18f)); // Rear guard
                break;

            case SchoolFormationType.Crescent:
            default:
                // Tight curved sweeping arc
                float arcRadius = Random.Range(1.4f, 2.0f);
                float arcAngleSpread = Random.Range(45f, 65f) * Mathf.Deg2Rad;
                for (int i = 0; i < count; i++)
                {
                    float t = (count > 1) ? ((float)i / (count - 1) - 0.5f) : 0f;
                    float angle = t * arcAngleSpread;
                    float x = trailDir * (arcRadius * (1f - Mathf.Cos(angle)) + Random.Range(0f, 0.18f));
                    float y = Mathf.Sin(angle) * arcRadius * 0.85f + Random.Range(-0.10f, 0.10f);
                    offsets[i] = new Vector2(x, y);
                }
                break;
        }

        return offsets;
    }

    public bool MovingRight => movingRight;
    public Vector2 TravelDirection { get; private set; } = Vector2.right;
    public float SchoolSpeed { get; set; } = 2.8f;

    public void Initialize(bool startRight)
    {
        movingRight = startRight;
        TravelDirection = movingRight ? Vector2.right : Vector2.left;
        CurrentDestination = (Vector2)transform.position + TravelDirection * 18f;
        // Auto-destroy school controller after 75 seconds (fish should be cleared or gone by then)
        Destroy(gameObject, 75f);
    }

    public void RegisterFish(Fish fish)
    {
        if (fish == null) return;
        registeredFish.Add(fish);
        remainingFish.Add(fish);
        fish.GroupSchool = this;
        if (fish.Xp > 0)
        {
            singleFishXp = fish.Xp;
        }
    }

    public Vector2 GetSchoolCentroid()
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        foreach (var fish in remainingFish)
        {
            if (fish != null && fish.gameObject.activeInHierarchy && !fish.IsDead)
            {
                sum += (Vector2)fish.transform.position;
                count++;
            }
        }
        if (count > 0)
        {
            return sum / count;
        }
        return (Vector2)transform.position;
    }

    public void OnFishEatenByPlayer(Fish fish, Vector3 eatPosition)
    {
        if (isDisqualified || isCleared || fish == null) return;

        remainingFish.Remove(fish);

        // Entire group cleared by the player! (Groups are 3 to 5 fish)
        if (remainingFish.Count == 0 && registeredFish.Count >= 2)
        {
            isCleared = true;
            PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null)
            {
                // Bonus point equivalent to just 1 extra school fish eaten
                pc.AwardSchoolBonus(singleFishXp, eatPosition);
            }
            Destroy(gameObject, 0.1f);
        }
    }

    public void OnFishEatenByPredator(Fish fish)
    {
        isDisqualified = true;
        if (fish != null) remainingFish.Remove(fish);
        if (remainingFish.Count == 0)
        {
            Destroy(gameObject, 0.1f);
        }
    }

    public void OnFishDespawned(Fish fish)
    {
        isDisqualified = true;
        if (fish != null) remainingFish.Remove(fish);
        if (remainingFish.Count == 0)
        {
            Destroy(gameObject, 0.1f);
        }
    }

    private void Update()
    {
        // 1. Clean up inactive or dead fish periodically (every 0.25s) with cached static predicate to eliminate GC allocations
        cleanupTimer -= Time.deltaTime;
        if (cleanupTimer <= 0f)
        {
            cleanupTimer = 0.25f;
            remainingFish.RemoveWhere(s_DeadOrInactiveFilter);
            if (remainingFish.Count == 0 && registeredFish.Count > 0)
            {
                Destroy(gameObject, 0.1f);
                return;
            }
        }
        else if (remainingFish.Count == 0 && registeredFish.Count > 0)
        {
            Destroy(gameObject, 0.1f);
            return;
        }

        // 2. Track actual live centroid of remaining schoolmates
        Vector2 centroid = GetSchoolCentroid();

        // 3. Gentle vertical wave motion across the arena
        float waveY = Mathf.Sin(Time.time * 0.85f) * 0.22f;
        Vector2 forwardDir = new Vector2(movingRight ? 1f : -1f, waveY).normalized;
        TravelDirection = forwardDir;

        // 4. Smooth continuous advance: school anchor moves forward steadily, tethered smoothly to fish centroid
        Vector2 desiredPos = centroid + forwardDir * 0.4f;
        Vector3 step = (Vector3)Vector2.MoveTowards(transform.position, desiredPos, 3.5f * Time.deltaTime);
        Vector3 advance = (Vector3)(forwardDir * SchoolSpeed * Time.deltaTime);
        transform.position = step + advance;

        // Keep anchor within vertical arena bounds
        Vector3 clampedPos = transform.position;
        clampedPos.y = Mathf.Clamp(clampedPos.y, minY + 1.2f, maxY - 1.2f);
        transform.position = clampedPos;

        // 5. Lookahead destination is continuously projected ahead (never collapses or points behind)
        CurrentDestination = (Vector2)transform.position + forwardDir * 18f;

        // 6. Smooth boundary turnaround before hitting world edges
        if (movingRight && transform.position.x > (maxX - 10f))
        {
            movingRight = false;
        }
        else if (!movingRight && transform.position.x < (minX + 10f))
        {
            movingRight = true;
        }
    }
}
