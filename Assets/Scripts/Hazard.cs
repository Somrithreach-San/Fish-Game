using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Hazard : MonoBehaviour
{
    private static Shader cachedParticleShader;
    private static Dictionary<Texture2D, Material> cachedParticleMaterials = new Dictionary<Texture2D, Material>();
    
    [SerializeField]
    private float fallSpeed = 3f;
    [SerializeField]
    private float retractSpeed = 8f; // Faster speed for pulling up
    [SerializeField]
    private float roamSpeed = 1.5f;
    [SerializeField]
    private float minRoamTime = 6.0f; // Increased roam time (User request: roam longer)
    [SerializeField]
    private float maxRoamTime = 10.0f; // Increased roam time

    [Header("Effects")]
    [SerializeField]
    private AudioClip moveSound;
    [SerializeField]
    private GameObject bubbleParticlesPrefab;

    private Material bubbleMaterial;
    private Texture2D bubbleTexture;

    private AudioSource audioSource;
    private AudioSource[] audioSources;
    private SpriteRenderer spriteRenderer; // Cached reference
    private bool retractSfxPlayed = false;

    private enum State { Dropping, Roaming, Retracting }
    private State currentState = State.Dropping;

    private float targetY;
    private int roamDirection = 0; // -1 left, 1 right
    
    private float lifeTimer = 0f;
    private float currentRoamDuration = 3f;
    private bool wasPaused = false;

    // Track particle system for toggling emission
    private ParticleSystem activeParticleSystem;

    [Header("Collider Settings")]
    [Tooltip("If true, the script will automatically resize the BoxCollider2D to the bottom of the sprite.")]
    [SerializeField]
    public bool autoConfigureCollider = false; // Default to false to allow manual collider setup in Prefabs

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        // Ensure collider is configured if needed (moved from Awake to allow property setting)
        ConfigureCollider();

        // Setup Audio
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        audioSources = GetComponentsInChildren<AudioSource>(true);
        if (audioSources == null || audioSources.Length == 0) audioSources = new AudioSource[] { audioSource };
        for (int i = 0; i < audioSources.Length; i++)
        {
            var a = audioSources[i];
            if (a == null) continue;
            a.volume = 0.5f;
            a.spatialBlend = 0f;
            a.playOnAwake = false;
            a.loop = false;
            a.clip = null;
            a.Stop();
        }
        
        // Randomize Speeds for realism (desync movement)
        fallSpeed = Random.Range(2.5f, 4.0f); // Default 3
        retractSpeed = Random.Range(7.0f, 10.0f); // Default 8

        retractSfxPlayed = false;
        StartCoroutine(PlayDropSfxBurst());
        
        // Setup Particles (Continuous Trail)
        if (bubbleParticlesPrefab != null)
        {
            // If user assigned a prefab, assume it's set up correctly, but ensure we parent it to the bait
            GameObject p = Instantiate(bubbleParticlesPrefab, transform.position, Quaternion.identity, transform);
            p.name = "HazardBubbles";
            SetupParticlePosition(p);
            
            activeParticleSystem = p.GetComponent<ParticleSystem>();
            if (activeParticleSystem != null)
            {
                 var main = activeParticleSystem.main;
                 main.loop = false; // Burst
                 main.playOnAwake = false;
                 activeParticleSystem.Stop();
            }
        }
        else
        {
            // Create manually if no prefab
            CreateTrailParticles();
        }

        // Fixed World Logic (Surface based)
        // Spawn is at Y=22 (from GridController). 
        // We want it to drop to a reasonable depth in the world.
        // World Bounds are approx -14 to 14.
        
        // MODIFIED: User provided longer sprites, so we can go deeper.
        // We randomize the target depth significantly now (-13 to 12).
        targetY = Random.Range(-13f, 12f); 

        // CRITICAL FIX: Ensure the hazard does NOT go beyond the bottom boundary.
        // Even if we randomize deep, we must clamp it so the hook stays on screen.
        ClampTargetDepth();
        
        ConfigureCollider();
    }

    private void ClampTargetDepth()
    {
        if (spriteRenderer == null) return;
        
        Camera cam = Camera.main;
        if (cam == null) return;

        float camBottom = cam.transform.position.y - cam.orthographicSize;
        float spriteHalfHeight = spriteRenderer.bounds.extents.y; // World space half-height

        // The lowest point the center (transform.position) can be 
        // such that the bottom edge (center - halfHeight) is at camBottom.
        // We add a small buffer (1.0f) to keep it clearly visible.
        float minSafeY = camBottom + spriteHalfHeight + 1.0f;

        if (targetY < minSafeY)
        {
            targetY = minSafeY;
        }
    }

    private void SetupParticlePosition(GameObject particleObj)
    {
        // Position at the "Bait" (Bottom of sprite)

        // Priority 1: Use Collider Center (The "Bait" hitbox)
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            particleObj.transform.localPosition = col.offset;
            return;
        }

        // Priority 2: Use Sprite Bottom (Robust for Top or Center pivots)
        if (spriteRenderer != null)
        {
            // Calculate local Y of the bottom edge
            // World Bottom = Bounds Min Y
            float worldBottomY = spriteRenderer.bounds.min.y;
            float localBottomY = worldBottomY - transform.position.y;
            
            // Add slight offset (0.2f) so it's not barely on the edge
            particleObj.transform.localPosition = new Vector3(0, localBottomY + 0.2f, 0);
        }
    }

    private void ConfigureCollider()
    {
        if (!autoConfigureCollider) return;

        // Auto-adjust collider to only cover the "Bait" (bottom of the sprite)
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;

        // Remove PolygonCollider2D if present (it likely traces the line)
        PolygonCollider2D poly = GetComponent<PolygonCollider2D>();
        if (poly != null) Destroy(poly);

        // Remove CapsuleCollider2D if present
        CapsuleCollider2D cap = GetComponent<CapsuleCollider2D>();
        if (cap != null) Destroy(cap);

        // Get or Add BoxCollider2D
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) box = gameObject.AddComponent<BoxCollider2D>();

        // Resize to bottom 8% of the sprite, and narrower width (30%) - Tighter fit per user request
        float spriteHeight = sr.size.y;
        float spriteWidth = sr.size.x;

        box.size = new Vector2(spriteWidth * 0.3f, spriteHeight * 0.08f);
        box.offset = new Vector2(0, -(spriteHeight / 2f) + (box.size.y / 2f));
        
        box.isTrigger = true; // Ensure it's a trigger for OnTriggerEnter in Player
    }



    private void Update()
    {
        // Pause Check
        bool currentPaused = (GameManager.instance != null && GameManager.Paused);

        if (currentPaused != wasPaused)
        {
            wasPaused = currentPaused;
            if (currentPaused)
            {
                StopAllAudio();
            }
            else
            {
                // Do not auto-resume one-shots
            }
        }

        if (currentPaused) return;

        // Distance-based Volume Fading
        if (audioSource != null && GameManager.instance != null && GameManager.instance.playerGameObject != null)
        {
             float dist = Vector3.Distance(transform.position, GameManager.instance.playerGameObject.transform.position);
             float maxDist = 20f; 
             // Volume: 0.5f at 0 dist, 0f at 20 dist
             float volume = Mathf.Clamp01(1f - (dist / maxDist)) * 0.5f; 
             float minVolume = 0.2f;
             audioSource.volume = Mathf.Max(minVolume, volume);
        }

        if (currentState == State.Dropping)
        {
            // Move down
            transform.Translate(Vector3.down * fallSpeed * Time.deltaTime, Space.World);

            // Check if reached target depth
            if (transform.position.y <= targetY)
            {
                StartRoaming();
            }
        }
        else if (currentState == State.Roaming)
        {
            // Ensure absolute silence while roaming
            if (audioSources == null || audioSources.Length == 0) audioSources = GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                var a = audioSources[i];
                if (a != null && a.isPlaying) a.Stop();
            }

            // Move horizontally
            transform.Translate(Vector3.right * roamDirection * roamSpeed * Time.deltaTime, Space.World);

            // Flip Sprite based on direction
            // Assumption: Sprite faces Right by default.
            // If moving Right (1), Scale X is positive. If Left (-1), Scale X is negative.
            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * (roamDirection > 0 ? 1 : -1);
            transform.localScale = s;

            // Keep in bounds (Bounce instead of Destroy so it can pull up later)
            KeepInBounds();
            
            // Check Lifetime
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= currentRoamDuration)
            {
                StartRetracting();
            }
        }
        else if (currentState == State.Retracting)
        {
            // Move up (Retract)
            transform.Translate(Vector3.up * retractSpeed * Time.deltaTime, Space.World); 
            
            // Destroy if fully off screen top (Fixed World Position)
            // Use dynamic calculation to ensure full sprite clearance
            if (transform.position.y >= GetRetractTargetY())
            {
                if (ObjectPoolManager.Instance != null)
                {
                    ObjectPoolManager.Instance.Despawn(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }

    private void CreateTrailParticles()
    {
        GameObject bubbles = new GameObject("HazardBubbles");
        bubbles.transform.SetParent(transform, false);
        SetupParticlePosition(bubbles);

        activeParticleSystem = bubbles.AddComponent<ParticleSystem>();
        
        // Configure Particle System (Match Player Speed Boost)
        var main = activeParticleSystem.main;
        main.loop = true; 
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = 0.15f;
        main.startLifetime = 0.8f;
        main.startSpeed = 0f;
        main.gravityModifier = -0.2f; // Float up

        var emission = activeParticleSystem.emission;
        emission.rateOverTime = 8f; 
        
        var shape = activeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;


        // Assign Material
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();
        
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
        }
        else if (bubbleTexture != null)
        {
            if (cachedParticleShader == null)
            {
                cachedParticleShader = Shader.Find("Particles/Standard Unlit");
                if (cachedParticleShader == null) cachedParticleShader = Shader.Find("Mobile/Particles/Alpha Blended");
                if (cachedParticleShader == null) cachedParticleShader = Shader.Find("Sprites/Default");
            }
            
            if (cachedParticleShader != null)
            {
                Material mat;
                if (!cachedParticleMaterials.TryGetValue(bubbleTexture, out mat) || mat == null)
                {
                    mat = new Material(cachedParticleShader);
                    mat.mainTexture = bubbleTexture;
                    cachedParticleMaterials[bubbleTexture] = mat;
                }
                renderer.material = mat;
            }
        }
        
        renderer.sortingOrder = 5;
    }

    private void StartRoaming()
    {
        currentState = State.Roaming;
        lifeTimer = 0f;
        StopAllAudio();
        
        // Pick random duration
        currentRoamDuration = Random.Range(minRoamTime, maxRoamTime);

        // Pick random direction (Left or Right)
        roamDirection = (Random.value > 0.5f) ? 1 : -1;

        // Do not forcibly stop one-shot SFX; allow it to finish naturally

        // Start Particles (Roaming)
        if (activeParticleSystem != null)
        {
            activeParticleSystem.Play();
        }
    }

    private void StartRetracting()
    {
        currentState = State.Retracting;
        
        // Play Retract Sound (Reuse Move Sound)
        if (!retractSfxPlayed && (GameManager.instance == null || !GameManager.Paused))
        {
            PlayMoveSound();
            retractSfxPlayed = true;
        }

        // Stop Particles (Retracting)
        if (activeParticleSystem != null)
        {
            activeParticleSystem.Stop();
        }
    }
    
    // Safety check for Retract Logic
    private float GetRetractTargetY()
    {
        float camTop = 15f; // Fallback
        Camera cam = Camera.main;
        if (cam != null)
        {
             float camHeight = 2f * cam.orthographicSize;
             camTop = cam.transform.position.y + (camHeight / 2f);
        }

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
             float halfHeight = spriteRenderer.bounds.extents.y;
             return camTop + halfHeight + 2f; 
        }
        
        return camTop + 5f;
    }
    
    private void OnDisable()
    {
        if (audioSources == null || audioSources.Length == 0) audioSources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audioSources.Length; i++)
        {
            var a = audioSources[i];
            if (a == null) continue;
            a.Stop();
            a.clip = null;
            a.loop = false;
            a.playOnAwake = false;
        }
    }
    
    public void Initialize(AudioClip sound, GameObject particles, Material mat, Texture2D tex, float? overrideDepth = null)
    {
        if (moveSound == null) moveSound = sound;
        if (bubbleParticlesPrefab == null) bubbleParticlesPrefab = particles;
        if (bubbleMaterial == null) bubbleMaterial = mat;
        if (bubbleTexture == null) bubbleTexture = tex;
        
        if (overrideDepth.HasValue)
        {
            targetY = overrideDepth.Value;
            // Re-apply clamp to override value to ensure safety
            ClampTargetDepth();
        }
    }

    private void PlayMoveSound()
    {
        if (GameManager.instance != null && GameManager.Paused) return;
        if (audioSource != null && moveSound != null)
        {
            audioSource.loop = false;
            audioSource.clip = null;
            audioSource.Stop();
            audioSource.PlayOneShot(moveSound);
        }
    }
    
    private IEnumerator PlayDropSfxBurst()
    {
        if (audioSource == null || moveSound == null) yield break;
        if (GameManager.instance != null && GameManager.Paused) yield break;
        audioSource.loop = false;
        audioSource.clip = null;
        audioSource.Stop();
        audioSource.PlayOneShot(moveSound);
        yield return new WaitForSeconds(0.15f);
        if (GameManager.instance != null && GameManager.Paused) yield break;
        audioSource.PlayOneShot(moveSound);
    }

    private void KeepInBounds()
    {
        // Fixed World Bounds (consistent with FishSchool)
        // Restricted further to ensure hook stays on screen
        float leftBound = -8f;
        float rightBound = 8f;

        // Bounce logic
        if (transform.position.x < leftBound && roamDirection < 0)
        {
            roamDirection = 1; // Turn Right
        }
        else if (transform.position.x > rightBound && roamDirection > 0)
        {
            roamDirection = -1; // Turn Left
        }
    }
    
    private void StopAllAudio()
    {
        if (audioSources == null || audioSources.Length == 0) audioSources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audioSources.Length; i++)
        {
            var a = audioSources[i];
            if (a == null) continue;
            a.Stop();
            a.clip = null;
        }
    }

}
