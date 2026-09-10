using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Rhinotap.Toolkit;

public class FishermanBoat : MonoBehaviour
{
    public enum BoatState { Arriving, StoppedWaiting, Fishing, DepartWaiting, Departing }

    [Header("Boat Appearance")]
    [SerializeField] private float boatScale = 1.85f;
    [Tooltip("How deep the lower hull dips below the top camera edge into the water")]
    [SerializeField] private float submergenceDepth = 0.85f;
    [SerializeField] private float bobFrequency = 2.2f;
    [SerializeField] private float bobAmplitude = 0.04f;
    [SerializeField] private float tiltAmplitude = 1.2f;

    [Header("Bubble Particle Materials")]
    [SerializeField] private Material bubbleMaterial;
    [SerializeField] private Texture2D bubbleTexture;

    [Header("Movement Settings")]
    [SerializeField] private float arriveSpeed = 4.5f;
    [SerializeField] private float departSpeed = 5.5f;
    [SerializeField] private float stopPauseDurationMin = 2.5f;
    [SerializeField] private float stopPauseDurationMax = 3.0f;

    [Header("Fishing Rod Settings")]
    [SerializeField] private int minRods = 2;
    [SerializeField] private int maxRods = 3;
    [SerializeField] private float rodDropStagger = 1.0f;

    private BoatState currentState = BoatState.Arriving;
    private SpriteRenderer spriteRenderer;
    private ParticleSystem wakeParticleSystem;
    private float targetX;
    private bool facingRight = true;
    private float travelDirection = 1f; // +1 right, -1 left
    private float seed;
    private bool isPaused = false;
    private float worldWaterSurfaceY = 8.0f;
    private List<Hazard> activeRods = new List<Hazard>();
    private Coroutine lifecycleCoroutine;

    public BoatState State => currentState;
    public bool IsReadyToFish => currentState == BoatState.Fishing;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        spriteRenderer.sortingOrder = 5; // Visible above background and fish
        seed = Random.Range(0f, 100f);

        GetCameraWorldBounds(out _, out _, out worldWaterSurfaceY, out _);
        if (worldWaterSurfaceY < 4.0f) worldWaterSurfaceY = 8.0f;

        // Position boat at the surface immediately upon creation so it never flashes at (0, 0, 0)
        transform.position = new Vector3(transform.position.x, GetTargetCenterY(), 0f);
    }

    /// <summary>
    /// Computes the exact camera visible bounds in world space dynamically.
    /// </summary>
    public static void GetCameraWorldBounds(out float leftEdge, out float rightEdge, out float topEdge, out float bottomEdge)
    {
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float camY = cam.transform.position.y;
            float camX = cam.transform.position.x;
            float size = cam.orthographicSize;
            float halfWidth = size * cam.aspect;

            topEdge = camY + size;
            bottomEdge = camY - size;
            leftEdge = camX - halfWidth;
            rightEdge = camX + halfWidth;
        }
        else if (cam != null)
        {
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, 10f));
            leftEdge = Mathf.Min(bl.x, tr.x);
            rightEdge = Mathf.Max(bl.x, tr.x);
            bottomEdge = Mathf.Min(bl.y, tr.y);
            topEdge = Mathf.Max(bl.y, tr.y);
        }
        else
        {
            leftEdge = -14.22f;
            rightEdge = 14.22f;
            bottomEdge = -8f;
            topEdge = 8f;
        }

        // Failsafe: Top edge of water screen is always at or near Y = 8.0f
        if (topEdge < 4.0f)
        {
            topEdge = 8.0f;
        }
    }

    private float GetBoatHalfWidth()
    {
        if (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.5f)
        {
            return spriteRenderer.bounds.extents.x;
        }
        return (5.45f * boatScale) * 0.5f;
    }

    private float GetBoatHalfHeight()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return (spriteRenderer.sprite.rect.height / spriteRenderer.sprite.pixelsPerUnit) * boatScale * 0.5f;
        }
        return (1.78f * boatScale) * 0.5f;
    }

    /// <summary>
    /// Returns the target transform center Y so only the lower keel/hull is in the water,
    /// with the cabin and upper boat resting above the surface off-screen.
    /// </summary>
    private float GetTargetCenterY()
    {
        float halfHeight = GetBoatHalfHeight();
        float surfaceY = (worldWaterSurfaceY >= 4.0f) ? worldWaterSurfaceY : 8.0f;
        // The bottom of the boat hull is at (surfaceY - submergenceDepth).
        // Since the sprite pivot is at the center (0.5, 0.5), the transform center must be at bottom + halfHeight.
        return (surfaceY - submergenceDepth) + halfHeight;
    }

    private void Start()
    {
        EventManager.StartListening<bool>("gamePaused", OnGamePaused);
    }

    private void OnDestroy()
    {
        EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        if (lifecycleCoroutine != null)
        {
            StopCoroutine(lifecycleCoroutine);
        }
    }

    private void OnGamePaused(bool paused)
    {
        isPaused = paused;
    }

    /// <summary>
    /// Initialize the boat with sprite, randomized entry side, destination, and scale.
    /// </summary>
    public void Initialize(Sprite boatSprite, float destinationX, bool cruiseFromOffscreen = true, Material bubbleMat = null, Texture2D bubbleTex = null)
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (boatSprite != null) spriteRenderer.sprite = boatSprite;

        if (bubbleMat != null) bubbleMaterial = bubbleMat;
        if (bubbleTex != null) bubbleTexture = bubbleTex;

        // Auto-find from player if not injected
        if (bubbleMaterial == null || bubbleTexture == null)
        {
            PlayerController pc = FindFirstObjectByType<PlayerController>();
            if (pc != null)
            {
                if (bubbleMaterial == null) bubbleMaterial = pc.BubbleMaterial;
                if (bubbleTexture == null) bubbleTexture = pc.BubbleTexture;
            }
        }

        targetX = destinationX;

        GetCameraWorldBounds(out float screenLeft, out float screenRight, out float screenTop, out _);
        worldWaterSurfaceY = screenTop;

        // Apply realistic scale
        transform.localScale = Vector3.one * boatScale;
        float boatHalfWidth = GetBoatHalfWidth();
        float spawnMargin = boatHalfWidth + 6.0f; // Generous buffer ensuring hull and wake are 100% off-screen
        float targetCenterY = GetTargetCenterY();

        if (cruiseFromOffscreen)
        {
            // Pick entry side 50/50: Left or Right
            bool startFromLeft = (Random.value > 0.5f);
            facingRight = startFromLeft; // traveling right -> facing right
            travelDirection = startFromLeft ? 1f : -1f;

            float startX = startFromLeft 
                ? (screenLeft - spawnMargin) 
                : (screenRight + spawnMargin);

            ApplyFacing();
            transform.position = new Vector3(startX, targetCenterY, 0);
            currentState = BoatState.Arriving;
        }
        else
        {
            facingRight = (Random.value > 0.5f);
            travelDirection = facingRight ? 1f : -1f;
            ApplyFacing();
            transform.position = new Vector3(destinationX, targetCenterY, 0);
            currentState = BoatState.StoppedWaiting;
        }

        SetupWakeParticles();
        if (wakeParticleSystem != null && currentState == BoatState.Arriving)
        {
            wakeParticleSystem.Play();
        }

        ApplyWaveBobbing(0f, 0f);
    }

    private void ApplyFacing()
    {
        // Sprite default: bow on left, motor on right.
        // When traveling right -> facingRight = true -> flipX = true.
        // When traveling left  -> facingRight = false -> flipX = false.
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingRight;
        }
    }

    private void SetupWakeParticles()
    {
        if (wakeParticleSystem != null) return;

        GameObject pObj = new GameObject("MotorWakeBubbles");
        pObj.transform.SetParent(transform, false);

        // Map bubble emission right near the propeller of the boat (just right behind it)
        // Sprite dimensions: 545x178, PPU 100.
        // Propeller is located at dx = 2.655 units, dy = -0.460 units from sprite center.
        // Since transform.localScale has boatScale, localPosition is in unscaled sprite units.
        float localPropellerX = facingRight ? -2.655f : 2.655f;
        float localPropellerY = -0.460f;
        pObj.transform.localPosition = new Vector3(localPropellerX, localPropellerY, 0f);

        wakeParticleSystem = pObj.AddComponent<ParticleSystem>();
        var main = wakeParticleSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Exact match to player speedEffect:
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.0f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.gravityModifier = -0.1f; // Float up to water surface
        main.maxParticles = 50;

        var emission = wakeParticleSystem.emission;
        emission.rateOverTime = 20f;

        var shape = wakeParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.2f;
        // Wake shoots backwards away from travel direction
        float rotationY = facingRight ? -90f : 90f;
        shape.rotation = new Vector3(0f, rotationY, 0f);

        // Noise (Wiggle match to player speedEffect)
        var noise = wakeParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.5f;

        // Size over Lifetime: Grow then pop (exact match to player speedEffect)
        var sol = wakeParticleSystem.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0.0f, 0.5f);
        curve.AddKey(0.8f, 1.0f);
        curve.AddKey(1.0f, 0.0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Color/Alpha: Fade out (exact match to player speedEffect)
        var col = wakeParticleSystem.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0.0f), new GradientAlphaKey(0.4f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        var renderer = pObj.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            Material matToUse = bubbleMaterial;
            if (matToUse == null && bubbleTexture != null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    matToUse = new Material(shader);
                    matToUse.mainTexture = bubbleTexture;
                }
            }

            #if UNITY_EDITOR
            if (matToUse == null)
            {
                matToUse = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
            }
            #endif

            if (matToUse != null)
            {
                renderer.material = matToUse;
            }
            renderer.sortingOrder = 6;
        }
    }

    private void Update()
    {
        if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

        // Wave bobbing calculation in world space
        float bobOffset = Mathf.Sin((Time.time + seed) * bobFrequency) * bobAmplitude;
        float tiltAngle = Mathf.Sin((Time.time + seed) * (bobFrequency * 0.7f)) * tiltAmplitude;

        switch (currentState)
        {
            case BoatState.Arriving:
                UpdateArriving(bobOffset, tiltAngle);
                break;

            case BoatState.StoppedWaiting:
            case BoatState.Fishing:
            case BoatState.DepartWaiting:
                // Boat stays at its stop position, gently bobbing on the waves
                ApplyWaveBobbing(bobOffset, tiltAngle);
                break;

            case BoatState.Departing:
                UpdateDeparting(bobOffset, tiltAngle);
                break;
        }
    }

    private void UpdateArriving(float bobOffset, float tiltAngle)
    {
        float currentX = transform.position.x;
        float newX = Mathf.MoveTowards(currentX, targetX, arriveSpeed * Time.deltaTime);
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(newX, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));

        if (Mathf.Abs(newX - targetX) < 0.08f)
        {
            transform.position = new Vector3(targetX, targetCenterY + bobOffset, 0);
            if (wakeParticleSystem != null && wakeParticleSystem.isPlaying)
            {
                wakeParticleSystem.Stop();
            }

            currentState = BoatState.StoppedWaiting;
            if (lifecycleCoroutine != null) StopCoroutine(lifecycleCoroutine);
            lifecycleCoroutine = StartCoroutine(FishingLifecycleRoutine());
        }
    }

    private IEnumerator FishingLifecycleRoutine()
    {
        currentState = BoatState.StoppedWaiting;

        // Requirement 4: Arrive and stop for at least 2.5 - 3.0 seconds before dropping fishing rods
        float pauseDuration = Random.Range(stopPauseDurationMin, stopPauseDurationMax);
        float elapsed = 0f;
        while (elapsed < pauseDuration)
        {
            if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
            {
                elapsed += Time.deltaTime;
            }
            yield return null;
        }

        // Requirement 4: Drop 2 - 3 fishing rods per boat
        currentState = BoatState.Fishing;
        int rodCount = Random.Range(minRods, maxRods + 1); // 2 or 3
        activeRods.Clear();

        float sign = facingRight ? -1f : 1f;
        float[] offsets;
        if (rodCount == 2)
        {
            offsets = new float[] { sign * 3.4f, sign * 1.5f };
        }
        else
        {
            offsets = new float[] { sign * 3.5f, sign * 2.1f, sign * 0.8f };
        }

        for (int r = 0; r < offsets.Length; r++)
        {
            float dropX = transform.position.x + offsets[r];

            // Varied depths across lines (shallow, medium, deep)
            float depth;
            if (r == 0) depth = Random.Range(-12f, -5f);      // Deep
            else if (r == 1) depth = Random.Range(3f, 8f);     // Shallow
            else depth = Random.Range(-4f, 2f);                // Medium

            if (GridController.Instance != null)
            {
                Hazard hz = GridController.Instance.SpawnFishingRodForBoat(this, dropX, depth);
                if (hz != null) activeRods.Add(hz);
            }

            // Stagger between drops
            if (r < offsets.Length - 1)
            {
                float st = 0f;
                while (st < rodDropStagger)
                {
                    if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
                    {
                        st += Time.deltaTime;
                    }
                    yield return null;
                }
            }
        }

        // Wait until all rods have retracted
        while (activeRods.Count > 0)
        {
            activeRods.RemoveAll(h => h == null || !h.gameObject.activeInHierarchy);
            yield return null;
        }

        // Brief delay after lines are reeled in before departing
        currentState = BoatState.DepartWaiting;
        float departWait = 0f;
        while (departWait < 1.0f)
        {
            if (!isPaused && (GameManager.instance == null || !GameManager.Paused))
            {
                departWait += Time.deltaTime;
            }
            yield return null;
        }

        // Drive straight forward to leave
        StartDeparture();
    }

    /// <summary>
    /// Called when an active fishing line finishes retracting
    /// </summary>
    public void OnHazardRetracted(Hazard hazard = null)
    {
        if (hazard != null)
        {
            activeRods.Remove(hazard);
        }
        else if (activeRods.Count > 0)
        {
            activeRods.RemoveAt(0);
        }
    }

    private void StartDeparture()
    {
        if (currentState == BoatState.Departing) return;
        currentState = BoatState.Departing;

        // Reignite wake bubbles as boat speeds away
        if (wakeParticleSystem != null && !wakeParticleSystem.isPlaying)
        {
            wakeParticleSystem.Play();
        }
    }

    private void UpdateDeparting(float bobOffset, float tiltAngle)
    {
        // Requirement 3: Must leave by driving straight forward (can't drive backward)
        float newX = transform.position.x + (travelDirection * departSpeed * Time.deltaTime);
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(newX, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));

        GetCameraWorldBounds(out float screenLeft, out float screenRight, out _, out _);
        float boatHalfWidth = GetBoatHalfWidth();
        float despawnMargin = boatHalfWidth + 6.0f;

        bool isFullyOffscreen = false;
        if (travelDirection > 0f) // Moving right
        {
            // Leftmost edge of boat (rear of boat) must be completely past the right edge of screen
            float boatLeft = (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.1f) 
                ? spriteRenderer.bounds.min.x 
                : (transform.position.x - boatHalfWidth);

            if (boatLeft > screenRight + 4.0f || transform.position.x > screenRight + despawnMargin)
            {
                isFullyOffscreen = true;
            }
        }
        else // Moving left
        {
            // Rightmost edge of boat (rear of boat) must be completely past the left edge of screen
            float boatRight = (spriteRenderer != null && spriteRenderer.bounds.size.x > 0.1f) 
                ? spriteRenderer.bounds.max.x 
                : (transform.position.x + boatHalfWidth);

            if (boatRight < screenLeft - 4.0f || transform.position.x < screenLeft - despawnMargin)
            {
                isFullyOffscreen = true;
            }
        }

        if (isFullyOffscreen)
        {
            if (GridController.Instance != null)
            {
                GridController.Instance.OnBoatDeparted(this);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Keeps the boat anchored at the world water surface level with wave bobbing.
    /// Only the bottom keel dips into view from the top of the screen; the upper boat remains off-screen.
    /// </summary>
    private void ApplyWaveBobbing(float bobOffset, float tiltAngle)
    {
        float targetCenterY = GetTargetCenterY();
        transform.position = new Vector3(transform.position.x, targetCenterY + bobOffset, 0);
        transform.rotation = Quaternion.Euler(0, 0, tiltAngle * (facingRight ? 1f : -1f));
    }
}

