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

    public void Initialize(bool startRight)
    {
        movingRight = startRight;
        PickNewDestination();
        // Auto-destroy school controller after 60 seconds (fish should be gone by then)
        Destroy(gameObject, 60f);
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
