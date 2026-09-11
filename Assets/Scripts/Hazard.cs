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
    private float minRoamTime = 3.5f; // Duration rod stays stationary in the water
    [SerializeField]
    private float maxRoamTime = 8.5f;

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

    [Header("Boat Link")]
    private FishermanBoat linkedBoat;
    public FishermanBoat LinkedBoat
    {
        get => linkedBoat;
        set => linkedBoat = value;
    }

    private float targetY;
    private int roamDirection = 0; // -1 left, 1 right
    
    private float lifeTimer = 0f;
    private float currentRoamDuration = 3f;
    private bool wasPaused = false;
    private bool hasCustomTargetDepth = false;

    // Track particle system for toggling emission
    private ParticleSystem activeParticleSystem;
    private static AnimationCurve s_HazardSizeCurve = null;
    private static Gradient s_HazardColorGradient = null;

    [Header("Collider Settings")]
    [Tooltip("If true, the script will automatically resize the BoxCollider2D to the bottom of the sprite.")]
    [SerializeField]
    public bool autoConfigureCollider = false; // Default to false to allow manual collider setup in Prefabs

    [Header("Caught Target State")]
    private PlayerController hookedPlayer = null;
    public bool HasHookedPlayer => hookedPlayer != null;
    private Fish hookedFish = null;
    public bool HasHookedFish => hookedFish != null;
    public bool HasCatch => hookedPlayer != null || hookedFish != null;

    public Vector3 GetBaitWorldPosition()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            return transform.TransformPoint(box.offset);
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            return transform.TransformPoint(col.offset);
        }

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            float halfHeight = spriteRenderer.sprite.bounds.extents.y;
            return transform.TransformPoint(new Vector3(0, -halfHeight + 0.3f, 0));
        }

        return transform.position;
    }

    public bool HookPlayer(PlayerController player)
    {
        if (HasCatch || player == null) return false;
        hookedPlayer = player;

        // Smooth reel up speed consistent with fish
        retractSpeed = Mathf.Clamp(retractSpeed, 7.5f, 9.0f);

        if (currentState != State.Retracting)
        {
            StartRetracting();
        }
        else
        {
            if (!retractSfxPlayed)
            {
                StartReelSound();
                retractSfxPlayed = true;
            }
        }
        return true;
    }

    public bool HookFish(Fish fish)
    {
        if (HasCatch || fish == null) return false;
        hookedFish = fish;

        // Smooth reel up speed consistent with fish
        retractSpeed = Mathf.Clamp(retractSpeed, 7.5f, 9.0f);

        if (currentState != State.Retracting)
        {
            StartRetracting();
        }
        else
        {
            if (!retractSfxPlayed)
            {
                StartReelSound();
                retractSfxPlayed = true;
            }
        }
        return true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // One catch per hook
        if (HasCatch) return;

        // 1. Check Player
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            pc.OnHookedByHazard(this);
            return;
        }

        // 2. Check AI Fish
        Fish fish = other.GetComponentInParent<Fish>();
        if (fish != null)
        {
            // Level 1, Level 2, and Golden Fish are too small to bite -> NO collision / ignore
            if (fish.IsGoldenFish || fish.Level < 3)
            {
                return;
            }

            // Big fish (Level 3+) bites the bait
            fish.OnHookedByHazard(this);
        }
    }

    public AudioClip MoveSound => moveSound;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.mute = !AudioSettingsManager.IsSfxEnabled;
        audioSources = new AudioSource[] { audioSource };
    }

    private void Start()
    {
        // Ensure collider is configured if needed (moved from Awake to allow property setting)
        ConfigureCollider();

        // Setup Audio listeners
        AudioSettingsManager.OnSfxSettingChanged += OnSfxSettingChanged;
        
        // Randomize Speeds for realism (desync movement)
        fallSpeed = Random.Range(2.5f, 4.0f); // Default 3
        retractSpeed = Random.Range(7.0f, 10.0f); // Default 8

        retractSfxPlayed = false;
        
        // Setup Particles (Continuous Trail) - reuse child if pooled
        if (activeParticleSystem == null)
        {
            Transform existing = transform.Find("HazardBubbles");
            if (existing != null)
            {
                activeParticleSystem = existing.GetComponent<ParticleSystem>();
            }
        }

        if (activeParticleSystem == null)
        {
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
        }

        if (currentState == State.Dropping && activeParticleSystem != null)
        {
            ConfigureParticlesForDropping();
            activeParticleSystem.Play();
        }

        // Fixed World Logic (Surface based)
        if (!hasCustomTargetDepth)
        {
            SetTargetHookDepth(Random.Range(-5f, 4f));
        }
        
        ConfigureCollider();
    }

    public void SetTargetHookDepth(float targetHookWorldY)
    {
        hasCustomTargetDepth = true;

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        float spriteHalfHeight = (spriteRenderer != null && spriteRenderer.bounds.size.y > 0.1f)
            ? spriteRenderer.bounds.extents.y
            : 15f;

        // Clamp desired hook Y: safely above ocean floor (-13.5f) and safely below water surface (+13.5f)
        float clampedHookY = Mathf.Clamp(targetHookWorldY, -13.5f, 13.5f);

        // Transform center target such that the bottom (hook) arrives exactly at clampedHookY
        targetY = clampedHookY + spriteHalfHeight;
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

        float spriteHeight = (sr.sprite != null) ? sr.sprite.bounds.size.y : sr.size.y;
        float spriteWidth = (sr.sprite != null) ? sr.sprite.bounds.size.x : sr.size.x;

        box.size = new Vector2(Mathf.Min(spriteWidth, 2.7f), 0.8f);
        box.offset = new Vector2(0f, -(spriteHeight / 2f) + (box.size.y / 2f) + 0.3f);
        
        box.isTrigger = true; // Ensure it's a trigger for OnTriggerEnter in Player
    }



    private void OnDestroy()
    {
        AudioSettingsManager.OnSfxSettingChanged -= OnSfxSettingChanged;
    }

    private void OnSfxSettingChanged(bool enabled)
    {
        if (audioSource != null)
        {
            audioSource.mute = !enabled;
            if (enabled && (currentState == State.Dropping || currentState == State.Retracting))
            {
                if (!audioSource.isPlaying && (GameManager.instance == null || !GameManager.Paused))
                {
                    StartReelSound();
                }
            }
            else if (!enabled)
            {
                if (audioSource.isPlaying) audioSource.Stop();
            }
        }
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
                if (audioSource != null && audioSource.isPlaying)
                {
                    audioSource.Pause();
                }
            }
            else
            {
                if (audioSource != null && (currentState == State.Dropping || currentState == State.Retracting))
                {
                    if (AudioSettingsManager.IsSfxEnabled)
                    {
                        audioSource.UnPause();
                        if (!audioSource.isPlaying) StartReelSound();
                    }
                }
            }
        }

        if (currentPaused) return;

        // Dynamic distance attenuation & stereo panning based on true bait/hook world position
        if (audioSource != null && GameManager.instance != null && GameManager.instance.playerGameObject != null)
        {
            Vector3 baitPos = GetBaitWorldPosition();
            Vector3 playerPos = GameManager.instance.playerGameObject.transform.position;
            float dist = Vector2.Distance(baitPos, playerPos);
            float maxDist = 22f; 
            float normDist = Mathf.Clamp01(dist / maxDist);
            // Smooth attenuation from 0.80f close up down to 0.12f far away
            float volume = Mathf.Lerp(0.80f, 0.12f, normDist * normDist);
            audioSource.volume = volume;

            // Directional stereo panning based on horizontal offset relative to the player
            float pan = Mathf.Clamp((baitPos.x - playerPos.x) / 12.0f, -0.80f, 0.80f);
            audioSource.panStereo = pan;
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
            // Ensure absolute silence while still/fishing
            if (audioSources == null || audioSources.Length == 0) audioSources = GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                var a = audioSources[i];
                if (a != null && a.isPlaying) a.Stop();
            }

            // User Request: The fishing rod moving left-right logic is no longer active.
            // It stays still in place at its target depth.
            
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
                if (hookedPlayer != null)
                {
                    hookedPlayer.OnReeledOutOfWater();
                    hookedPlayer = null;
                }

                if (hookedFish != null)
                {
                    hookedFish.OnReeledOutOfWater();
                    hookedFish = null;
                }

                if (linkedBoat != null)
                {
                    linkedBoat.OnHazardRetracted(this);
                }

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
        
        var main = activeParticleSystem.main;
        main.loop = true; 
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Apply dropping parameters by default
        ConfigureParticlesForDropping();

        // Noise
        var noise = activeParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.5f;

        // Size over Lifetime: Grow then pop
        var sol = activeParticleSystem.sizeOverLifetime;
        sol.enabled = true;
        if (s_HazardSizeCurve == null)
        {
            s_HazardSizeCurve = new AnimationCurve();
            s_HazardSizeCurve.AddKey(0.0f, 0.5f);
            s_HazardSizeCurve.AddKey(0.8f, 1.0f);
            s_HazardSizeCurve.AddKey(1.0f, 0.0f);
        }
        sol.size = new ParticleSystem.MinMaxCurve(1f, s_HazardSizeCurve);

        // Color/Alpha: Fade out
        var col = activeParticleSystem.colorOverLifetime;
        col.enabled = true;
        if (s_HazardColorGradient == null)
        {
            s_HazardColorGradient = new Gradient();
            s_HazardColorGradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.80f, 0.0f), new GradientAlphaKey(0.60f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
            );
        }
        col.color = s_HazardColorGradient;

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

    private void ConfigureParticlesForDropping()
    {
        if (activeParticleSystem == null) return;
        activeParticleSystem.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // Pointing UP (trailing the sinking bait)

        var main = activeParticleSystem.main;
        main.loop = true;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.26f);
        main.gravityModifier = -0.15f; // Float gently upwards
        main.maxParticles = 60;

        var emission = activeParticleSystem.emission;
        emission.rateOverTime = 12f;

        var shape = activeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.15f;
    }

    private void ConfigureParticlesForRetracting()
    {
        if (activeParticleSystem == null) return;
        activeParticleSystem.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Pointing DOWN (trailing in wake below rising bait)

        var main = activeParticleSystem.main;
        main.loop = true;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.35f);
        main.gravityModifier = -0.08f; // Wake turbulence below the rising bait
        main.maxParticles = 100;

        var emission = activeParticleSystem.emission;
        emission.rateOverTime = 28f; // Rapid bubble stream as bait is reeled up

        var shape = activeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.22f;
    }

    private void StartRoaming()
    {
        currentState = State.Roaming;
        lifeTimer = 0f;
        StopReelSound();
        
        // Pick random duration so rods don't retract at the same time
        currentRoamDuration = Random.Range(minRoamTime, maxRoamTime);
        retractSpeed = Random.Range(6.0f, 10.0f);

        // Pick random direction (Left or Right)
        roamDirection = (Random.value > 0.5f) ? 1 : -1;

        // User Request: Idling at the bottom NO need bubbles
        if (activeParticleSystem != null)
        {
            activeParticleSystem.Stop();
        }
    }

    private void StartRetracting()
    {
        currentState = State.Retracting;
        
        // Play Retract Sound (Smooth reel up audio)
        if (!retractSfxPlayed)
        {
            StartReelSound();
            retractSfxPlayed = true;
        }

        // User Request: Pulling back up NEEDS bubbles trailing the bait
        ConfigureParticlesForRetracting();
        if (activeParticleSystem != null)
        {
            activeParticleSystem.Play();
        }
    }
    
    // Safety check for Retract Logic
    private float GetRetractTargetY()
    {
        float surfaceY = (linkedBoat != null) ? linkedBoat.WaterSurfaceY : 15f;
        float halfHeight = (spriteRenderer != null && spriteRenderer.sprite != null)
            ? spriteRenderer.bounds.extents.y
            : 15f;
        return surfaceY + halfHeight + 2f;
    }
    
    private void OnEnable()
    {
        ResetHazardState();
    }

    public void ResetHazardState()
    {
        currentState = State.Dropping;
        lifeTimer = 0f;
        hasCustomTargetDepth = false;
        retractSfxPlayed = false;
        hookedPlayer = null;
        hookedFish = null;
        fallSpeed = Random.Range(2.5f, 4.0f);
        retractSpeed = Random.Range(7.0f, 10.0f);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        ConfigureParticlesForDropping();
        if (activeParticleSystem != null)
        {
            activeParticleSystem.Play();
        }

        StartReelSound();
    }
    
    private void OnDisable()
    {
        hookedPlayer = null;
        hookedFish = null;
        if (linkedBoat != null)
        {
            linkedBoat.OnHazardRetracted(this);
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        StopReelSound();
        if (activeParticleSystem != null && activeParticleSystem.isPlaying)
        {
            activeParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
    
    public void Initialize(AudioClip sound, GameObject particles, Material mat, Texture2D tex, float? overrideDepth = null, FishermanBoat boat = null)
    {
        if (boat != null) linkedBoat = boat;
        if (moveSound == null && sound != null) moveSound = sound;
        if (bubbleParticlesPrefab == null) bubbleParticlesPrefab = particles;
        if (bubbleMaterial == null) bubbleMaterial = mat;
        if (bubbleTexture == null) bubbleTexture = tex;

        // Ensure clean state when spawned from pool
        ResetHazardState();
        
        if (overrideDepth.HasValue)
        {
            SetTargetHookDepth(overrideDepth.Value);
        }
    }

    private void StartReelSound()
    {
        if (audioSource == null) return;
        audioSource.mute = !AudioSettingsManager.IsSfxEnabled;
        if (moveSound != null)
        {
            audioSource.clip = moveSound;
            audioSource.loop = true;
            if (!audioSource.isPlaying && AudioSettingsManager.IsSfxEnabled && (GameManager.instance == null || !GameManager.Paused))
            {
                audioSource.Play();
            }
        }
    }

    private void StopReelSound()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    private void StopAllAudio()
    {
        StopReelSound();
    }

}
