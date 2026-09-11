using System.Collections.Generic;
using UnityEngine;

public class FishSchool : MonoBehaviour
{
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
    private bool isDisqualified = false;
    private bool isCleared = false;

    // Bonus is dynamically set to exactly 1 extra school fish's XP
    private int singleFishXp = 8;

    public void Initialize(bool startRight)
    {
        movingRight = startRight;
        PickNewDestination();
        // Auto-destroy school controller after 60 seconds (fish should be gone by then)
        Destroy(gameObject, 60f);
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

    public void OnFishEatenByPlayer(Fish fish, Vector3 eatPosition)
    {
        if (isDisqualified || isCleared || fish == null) return;

        remainingFish.Remove(fish);

        // Entire group cleared by the player! (Groups are 3 to 5 fish)
        if (remainingFish.Count == 0 && registeredFish.Count >= 3)
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
        decisionTimer -= Time.deltaTime;
        if (decisionTimer <= 0)
        {
            PickNewDestination();
        }
    }

    private void PickNewDestination()
    {
        // NATURAL MOVEMENT:
        // Instead of swimming straight to the edge, pick a random point within the arena
        // This creates a "wandering" school effect.
        
        float targetX = Random.Range(minX, maxX);
        float targetY = Random.Range(minY, maxY);
        
        // Add some bias to keep moving in the general direction (Left or Right) initially
        // but allow turning back.
        if (Random.value < 0.7f) // 70% chance to continue in current "flow"
        {
             if (movingRight) targetX = Random.Range(0f, maxX);
             else targetX = Random.Range(minX, 0f);
        }

        CurrentDestination = new Vector2(targetX, targetY);

        // Update direction based on new target (for logic use, though FishAI handles rotation)
        movingRight = (targetX > transform.position.x);

        // Re-evaluate frequently (3-6 seconds) to change course
        decisionTimer = Random.Range(3f, 6f);
    }
}
