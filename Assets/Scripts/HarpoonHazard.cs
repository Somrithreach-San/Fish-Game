using System.Collections;
using UnityEngine;
using Rhinotap.Toolkit;

public class HarpoonHazard : MonoBehaviour
{
    public enum HarpoonState { Descending, ImpactAbsorb, AtBottom, Retracting, Completed }

    [Header("Movement Settings")]
    [SerializeField] private float descendSpeed = 38f; // High-velocity punchy plunge (boosted from 27f)
    [SerializeField] private float retractSpeed = 18f; // Fast winch reel speed (boosted from 12f)
    [SerializeField] private float bottomPauseDuration = 0.15f; // Snappy turn-around
    [SerializeField] private float impactAbsorbDuration = 0.08f; // Brief micro-nudge as fish absorbs momentum

    [Header("Visuals & Audio")]
    [SerializeField] private Sprite harpoonSprite;
    [SerializeField] private AudioClip shootSound;
    [SerializeField] private AudioClip reelSound;
    [SerializeField] private AudioClip hitFloorSound;
    [SerializeField] private AudioClip stabSound;

    private HarpoonState currentState = HarpoonState.Descending;
    public HarpoonState State => currentState;

    private RiverBoat linkedBoat;
    private Vector3 launchOrigin;
    private Vector2 launchDir;
    private float floorY = -13.5f;
    private float boundLeft = -25f;
    private float boundRight = 25f;
    private float maxTravelDistance = 45f;
    private float descendElapsed = 0f;
    private float bottomTimer = 0f;
    private float impactAbsorbTimer = 0f;
    private Vector3 impactStartPos;
    private Vector3 impactTargetPos;
    private bool isPaused = false;
    private Fish caughtFish = null;             // AI fish being pulled up
    private PlayerController caughtPlayer = null; // Player being pulled up
    private Quaternion caughtFishWorldRot = Quaternion.identity;
    private System.Collections.Generic.List<SpriteRenderer> elevatedRenderers = new System.Collections.Generic.List<SpriteRenderer>();
    private System.Collections.Generic.List<int> originalSortingOrders = new System.Collections.Generic.List<int>();

    private SpriteRenderer sr;
    private Collider2D col;
    private LineRenderer cableLine;
    [SerializeField] private Material cableMaterial;
    private ParticleSystem bubbleTrail;
    private AudioSource audioSource;
    private AudioSource stabAudioSource;

    private static Sprite s_DefaultHarpoonSprite;
    private static Material s_DefaultCableMaterial;
    private static AudioClip s_DefaultStabSound;

    private void Awake()
    {
        EnsureComponents();
    }

    public void EnsureComponents()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 6; // Render in front of boat hull and underwater fish
        sr.enabled = true;

        if (harpoonSprite == null)
        {
            if (s_DefaultHarpoonSprite == null)
            {
#if UNITY_EDITOR
                s_DefaultHarpoonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/river_harpoon.png");
#endif
                if (s_DefaultHarpoonSprite == null)
                {
                    s_DefaultHarpoonSprite = Resources.Load<Sprite>("river_harpoon");
                }
            }
            if (s_DefaultHarpoonSprite != null)
            {
                harpoonSprite = s_DefaultHarpoonSprite;
            }
        }

        if (sr != null && harpoonSprite != null)
        {
            sr.sprite = harpoonSprite;
        }

        if (col == null) col = GetComponent<Collider2D>();
        if (col == null)
        {
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            if (sr != null && sr.sprite != null)
            {
                box.size = new Vector2(sr.sprite.bounds.size.x * 1.15f, sr.sprite.bounds.size.y * 1.05f);
                box.offset = sr.sprite.bounds.center;
            }
            else
            {
                box.size = new Vector2(0.65f, 2.2f);
            }
            col = box;
        }

        if (cableLine == null) cableLine = GetComponent<LineRenderer>();
        if (cableLine == null)
        {
            cableLine = gameObject.AddComponent<LineRenderer>();
        }

        if (cableMaterial == null)
        {
            if (s_DefaultCableMaterial == null)
            {
#if UNITY_EDITOR
                s_DefaultCableMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/Hazard/HarpoonCableMat.mat");
#endif
                if (s_DefaultCableMaterial == null)
                {
                    Texture2D tex = null;
#if UNITY_EDITOR
                    tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Graphics/Hazard/harpoon_cable.png");
#endif
                    if (tex == null) tex = Resources.Load<Texture2D>("harpoon_cable");

                    Shader s = Shader.Find("Sprites/Default");
                    if (s != null)
                    {
                        s_DefaultCableMaterial = new Material(s);
                        s_DefaultCableMaterial.name = "HarpoonCableRuntimeMat";
                        if (tex != null) s_DefaultCableMaterial.mainTexture = tex;
                    }
                }
            }
            cableMaterial = s_DefaultCableMaterial;
        }

        cableLine.positionCount = 2;
        cableLine.startWidth = 0.048f;
        cableLine.endWidth = 0.048f;
        cableLine.numCapVertices = 4;
        cableLine.numCornerVertices = 4;
        cableLine.textureMode = LineTextureMode.Stretch;
        if (cableMaterial != null)
        {
            cableLine.material = cableMaterial;
        }
        cableLine.startColor = Color.white;
        cableLine.endColor = Color.white;
        cableLine.sortingOrder = 7; // Render in front of boat hull and underwater fish

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        if (stabAudioSource == null)
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            if (sources.Length > 1)
            {
                stabAudioSource = sources[1];
            }
            else
            {
                stabAudioSource = gameObject.AddComponent<AudioSource>();
            }
            stabAudioSource.playOnAwake = false;
            stabAudioSource.spatialBlend = 0f;
        }

        if (stabSound == null)
        {
            if (s_DefaultStabSound == null)
            {
#if UNITY_EDITOR
                s_DefaultStabSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Harpoon_Stab.mp3");
                if (s_DefaultStabSound == null)
                {
                    s_DefaultStabSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/harpoon_stab.wav");
                }
#endif
                if (s_DefaultStabSound == null)
                {
                    s_DefaultStabSound = Resources.Load<AudioClip>("Harpoon_Stab");
                }
                if (s_DefaultStabSound == null)
                {
                    s_DefaultStabSound = Resources.Load<AudioClip>("harpoon_stab");
                }
            }
            if (s_DefaultStabSound != null)
            {
                stabSound = s_DefaultStabSound;
            }
        }
    }

    public void Initialize(
        RiverBoat boat, 
        Vector3 launchPos, 
        float tiltAngleDeg, 
        float targetBottomY, 
        AudioClip shootClip = null, 
        AudioClip reelClip = null, 
        Material bubbleMat = null, 
        Texture2D bubbleTex = null,
        AudioClip stabClip = null)
    {
        EnsureComponents();

        linkedBoat = boat;
        launchOrigin = launchPos;
        floorY = targetBottomY;
        if (boat != null)
        {
            boundLeft = boat.WorldBgLeft;
            boundRight = boat.WorldBgRight;
        }
        descendElapsed = 0f;
        caughtFish = null;   // Reset pooled state
        caughtPlayer = null;
        caughtFishWorldRot = Quaternion.identity;

        // Ensure metal spear is visible when fired
        if (sr != null) sr.enabled = true;

        if (shootClip != null) shootSound = shootClip;
        if (reelClip != null) reelSound = reelClip;
        if (stabClip != null) stabSound = stabClip;

        transform.position = launchPos;

        launchDir = (Quaternion.Euler(0f, 0f, tiltAngleDeg) * Vector2.down).normalized;
        transform.rotation = Quaternion.Euler(0f, 0f, tiltAngleDeg);

        currentState = HarpoonState.Descending;
        bottomTimer = 0f;

        if (cableLine != null)
        {
            cableLine.enabled = true;
            cableLine.SetPosition(0, GetCurrentLauncherPos());
            cableLine.SetPosition(1, GetTetherWorldPos());
        }

        SetupBubbleTrail(bubbleMat, bubbleTex);
        if (bubbleTrail != null)
        {
            bubbleTrail.Play();
        }

        if (audioSource != null && shootSound != null && AudioSettingsManager.IsSfxEnabled)
        {
            audioSource.mute = false;
            audioSource.PlayOneShot(shootSound, 1.0f);
        }

        if (GameManager.instance != null)
        {
            GameManager.instance.CameraShake(0.18f, 3.8f, 1.8f);
        }
    }

    private void SetupBubbleTrail(Material bubbleMat, Texture2D bubbleTex)
    {
        if (bubbleTrail != null) return;

        GameObject pObj = new GameObject("HarpoonBubbles");
        pObj.transform.SetParent(transform, false);
        float tipY = -0.85f;
        if (sr != null && sr.sprite != null)
        {
            tipY = sr.sprite.bounds.min.y * 0.90f;
        }
        pObj.transform.localPosition = new Vector3(0f, tipY, 0f);

        bubbleTrail = pObj.AddComponent<ParticleSystem>();
        var renderer = pObj.GetComponent<ParticleSystemRenderer>();

        var main = bubbleTrail.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.0f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.gravityModifier = -0.1f;
        main.maxParticles = 60;

        var emission = bubbleTrail.emission;
        emission.rateOverTime = 30f;

        var shape = bubbleTrail.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.1f;

        if (bubbleMat != null)
        {
            renderer.material = bubbleMat;
        }
        else if (bubbleTex != null)
        {
            Material m = new Material(Shader.Find("Sprites/Default"));
            m.mainTexture = bubbleTex;
            renderer.material = m;
        }
        renderer.sortingOrder = 5;
    }

    private bool isEventSubscribed = false;

    private void Start()
    {
        if (!isEventSubscribed)
        {
            try
            {
                EventManager.StartListening<bool>("gamePaused", OnGamePaused);
                EventManager.StartListening("playerDeath", StopAudioForGameEnd);
                EventManager.StartListening("GameLoss", StopAudioForGameEnd);
                EventManager.StartListening("GameWin", StopAudioForGameEnd);
                isEventSubscribed = true;
            }
            catch { }
        }
    }

    private void OnEnable()
    {
        caughtFish = null;
        caughtPlayer = null;
        caughtFishWorldRot = Quaternion.identity;
        isPaused = false;
        descendElapsed = 0f;

        RestoreElevatedSortingOrders();

        // Reset harpoon visibility for next plunge
        if (sr != null) sr.enabled = true;
    }

    private void OnDisable()
    {
        if (cableLine != null) cableLine.enabled = false;
        if (bubbleTrail != null && bubbleTrail.isPlaying) bubbleTrail.Stop();
        if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
        if (stabAudioSource != null && stabAudioSource.isPlaying) stabAudioSource.Stop();

        RestoreElevatedSortingOrders();

        // Reset visibility so pooled object spawns correctly next time
        if (sr != null) sr.enabled = true;

        if (linkedBoat != null)
        {
            linkedBoat.OnHarpoonRetracted(this);
            linkedBoat = null;
        }

        if (caughtFish != null)
        {
            if (caughtFish.gameObject.activeInHierarchy) caughtFish.Die();
            caughtFish = null;
        }
        if (caughtPlayer != null)
        {
            if (caughtPlayer.IsAlive) caughtPlayer.OnReeledOutOfWater();
            caughtPlayer = null;
        }
    }

    private void OnDestroy()
    {
        if (isEventSubscribed)
        {
            try
            {
                EventManager.StopListening<bool>("gamePaused", OnGamePaused);
                EventManager.StopListening("playerDeath", StopAudioForGameEnd);
                EventManager.StopListening("GameLoss", StopAudioForGameEnd);
                EventManager.StopListening("GameWin", StopAudioForGameEnd);
            }
            catch { }
            isEventSubscribed = false;
        }
    }

    private void OnGamePaused(bool paused)
    {
        isPaused = paused;
        if (audioSource != null)
        {
            if (paused) audioSource.Pause();
            else audioSource.UnPause();
        }
        if (stabAudioSource != null)
        {
            if (paused) stabAudioSource.Pause();
            else stabAudioSource.UnPause();
        }
    }

    private void StopAudioForGameEnd()
    {
        if (audioSource != null) audioSource.Stop();
        if (stabAudioSource != null) stabAudioSource.Stop();
        if (bubbleTrail != null) bubbleTrail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private Vector3 GetCurrentLauncherPos()
    {
        if (linkedBoat != null)
        {
            return linkedBoat.GetHarpoonLauncherPosition();
        }
        return launchOrigin;
    }

    private Vector3 GetTetherWorldPos()
    {
        // When a fish/player is caught, the metal harpoon is buried inside its flesh.
        // The line connects directly into the center of the fish!
        if (caughtFish != null || caughtPlayer != null)
        {
            return transform.position;
        }
        // Top ring eyelet center of harpoon sprite while plunging
        return transform.TransformPoint(new Vector3(0f, 0.96f, 0f));
    }

    private void Update()
    {
        if (isPaused || (GameManager.instance != null && GameManager.Paused)) return;

        // Update cable positions continuously
        if (cableLine != null && cableLine.enabled)
        {
            cableLine.SetPosition(0, GetCurrentLauncherPos());
            cableLine.SetPosition(1, GetTetherWorldPos());
        }

        // Subtle, tense struggle tremble while reeled in by harpoon (tracked in world space without parenting scale distortion)
        if (caughtFish != null && caughtFish.gameObject.activeInHierarchy)
        {
            float trembleAngle = Mathf.Sin(Time.time * 22f) * 3.5f;
            Vector3 jitter = new Vector3(Mathf.Sin(Time.time * 24f) * 0.015f, Mathf.Cos(Time.time * 26f) * 0.015f, 0f);
            caughtFish.transform.position = transform.position + jitter;
            caughtFish.transform.rotation = caughtFishWorldRot * Quaternion.Euler(0f, 0f, trembleAngle);
        }

        switch (currentState)
        {
            case HarpoonState.Descending:
                UpdateDescending();
                break;
            case HarpoonState.ImpactAbsorb:
                UpdateImpactAbsorb();
                break;
            case HarpoonState.AtBottom:
                UpdateAtBottom();
                break;
            case HarpoonState.Retracting:
                UpdateRetracting();
                break;
        }
    }

    private void UpdateDescending()
    {
        descendElapsed += Time.deltaTime;
        Vector3 startPos = transform.position;
        Vector3 moveStep = (Vector3)(launchDir * descendSpeed * Time.deltaTime);
        Vector3 nextPos = startPos + moveStep;

        // Continuous line-of-fire trajectory sweep:
        // Even if the initial target dodged or swum away, any fish or player intersecting the line of fire gets hit!
        if (CheckTrajectoryHits(startPos, nextPos))
        {
            return;
        }

        transform.position = nextPos;

        Vector3 pos = transform.position;
        bool hitBoundary = false;

        // Vertical floor boundary
        if (pos.y <= floorY)
        {
            pos.y = floorY;
            hitBoundary = true;
        }

        // Horizontal left and right map boundaries
        // User Requirement: "it should not be able to shoot too far from the map boundaries"
        if (pos.x <= boundLeft)
        {
            pos.x = boundLeft;
            hitBoundary = true;
        }
        else if (pos.x >= boundRight)
        {
            pos.x = boundRight;
            hitBoundary = true;
        }

        // Safety max travel distance and timeout cutoff (prevents runaway projectiles at horizontal angles)
        if ((pos - launchOrigin).sqrMagnitude >= (maxTravelDistance * maxTravelDistance) || descendElapsed > 2.2f)
        {
            hitBoundary = true;
        }

        if (hitBoundary)
        {
            transform.position = pos;
            currentState = HarpoonState.AtBottom;
            bottomTimer = bottomPauseDuration;

            if (bubbleTrail != null && bubbleTrail.isPlaying)
            {
                bubbleTrail.Stop();
            }

            if (audioSource != null && hitFloorSound != null && AudioSettingsManager.IsSfxEnabled)
            {
                audioSource.PlayOneShot(hitFloorSound, 0.85f);
            }

            if (GameManager.instance != null)
            {
                GameManager.instance.CameraShake(0.16f, 3.2f, 1.6f);
            }
        }
    }

    private void UpdateAtBottom()
    {
        bottomTimer -= Time.deltaTime;
        if (bottomTimer <= 0f)
        {
            currentState = HarpoonState.Retracting;

            if (audioSource != null && reelSound != null && AudioSettingsManager.IsSfxEnabled)
            {
                audioSource.clip = reelSound;
                audioSource.loop = true;
                audioSource.Play();
            }
        }
    }

    private void UpdateRetracting()
    {
        Vector3 targetLauncher = GetCurrentLauncherPos();
        Vector3 tetherPos = GetTetherWorldPos();
        Vector3 toLauncher = targetLauncher - tetherPos;
        float dist = toLauncher.magnitude;

        if (dist < 0.55f || (dist < 1.0f && tetherPos.y >= targetLauncher.y))
        {
            if (caughtFish != null)
            {
                if (caughtFish.gameObject.activeInHierarchy)
                {
                    caughtFish.Die();
                }
                caughtFish = null;
            }
            if (caughtPlayer != null)
            {
                if (caughtPlayer.IsAlive)
                {
                    caughtPlayer.OnReeledOutOfWater();
                }
                caughtPlayer = null;
            }
            RestoreElevatedSortingOrders();
            currentState = HarpoonState.Completed;
            if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
            if (cableLine != null) cableLine.enabled = false;

            if (linkedBoat != null)
            {
                linkedBoat.OnHarpoonRetracted(this);
            }

            if (ObjectPoolManager.Instance != null)
            {
                ObjectPoolManager.Instance.Despawn(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
            return;
        }

        Vector3 moveStep = toLauncher.normalized * retractSpeed * Time.deltaTime;
        transform.position += moveStep;

        if (toLauncher.sqrMagnitude > 0.04f)
        {
            float targetAngle = Mathf.Atan2(toLauncher.y, toLauncher.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);
        }

        // Drag impaled victims along with the projectile in world space (without parenting, avoiding scale distortion)
        if (caughtPlayer != null && caughtPlayer.gameObject.activeInHierarchy)
        {
            caughtPlayer.transform.position = transform.position;
        }
        if (caughtFish != null && caughtFish.gameObject.activeInHierarchy)
        {
            caughtFish.transform.position = transform.position;
        }
    }

    /// <summary>
    /// Checks all targets (player and all AI fish) along the swept trajectory line between fromPos and toPos.
    /// Guarantees that even if the primary target dodged, any other fish directly within the line of fire gets hit!
    /// </summary>
    private bool CheckTrajectoryHits(Vector3 fromPos, Vector3 toPos)
    {
        if (currentState != HarpoonState.Descending) return false;
        if (caughtFish != null || caughtPlayer != null) return false;

        float bestT = float.MaxValue;
        bool hitIsPlayer = false;
        PlayerController hitPlayer = null;
        Fish bestHitFish = null;
        Vector3 hitPosition = Vector3.zero;

        // 1. Check Player along swept trajectory
        PlayerController pc = null;
        if (GridController.Instance != null && GridController.Instance.Player != null)
        {
            pc = GridController.Instance.Player.GetComponent<PlayerController>();
        }
        if (pc == null)
        {
            pc = FindFirstObjectByType<PlayerController>();
        }

        if (pc != null && pc.IsAlive && !pc.IsHooked)
        {
            Vector3 pPos = pc.transform.position;
            Collider2D pCol = pc.GetComponent<Collider2D>();
            float pRadius = (pCol != null) ? Mathf.Max(pCol.bounds.extents.x, pCol.bounds.extents.y) : 0.65f;
            float hitThreshold = 0.55f + pRadius;

            float distSqr = SqrDistanceToSegment(pPos, fromPos, toPos, out float t);
            if (distSqr <= hitThreshold * hitThreshold)
            {
                bestT = t;
                hitIsPlayer = true;
                hitPlayer = pc;
                hitPosition = Vector3.Lerp(fromPos, toPos, t);
            }
        }

        // 2. Check all active AI Fish along swept trajectory (any fish in the line of target!)
        if (Fish.AllFish != null && Fish.AllFish.Count > 0)
        {
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish f = Fish.AllFish[i];
                if (f == null || !f.gameObject.activeInHierarchy || f.IsDead || f.IsHooked) continue;
                // Harpoon ignores small Level 1 fish and Golden Fish
                if (f.Level < 2 || f.IsGoldenFish) continue;

                Vector3 fPos = f.transform.position;
                Collider2D fCol = f.GetComponent<Collider2D>();
                float fRadius = (fCol != null) ? Mathf.Max(fCol.bounds.extents.x, fCol.bounds.extents.y) : 0.55f;
                float hitThreshold = 0.52f + fRadius;

                float distSqr = SqrDistanceToSegment(fPos, fromPos, toPos, out float t);
                if (distSqr <= hitThreshold * hitThreshold)
                {
                    if (t < bestT)
                    {
                        bestT = t;
                        hitIsPlayer = false;
                        bestHitFish = f;
                        hitPosition = Vector3.Lerp(fromPos, toPos, t);
                    }
                }
            }
        }

        // 3. Fallback: Physics2D CircleCast along the step
        float stepDist = Vector3.Distance(fromPos, toPos);
        if (stepDist > 0.001f)
        {
            RaycastHit2D[] hits = Physics2D.CircleCastAll(fromPos, 0.45f, launchDir, stepDist);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i].collider;
                if (col == null) continue;

                if (col.GetComponentInParent<SharkHazard>() != null || 
                    col.GetComponentInParent<Hazard>() != null || 
                    col.GetComponentInParent<HarpoonHazard>() != null ||
                    col.GetComponentInParent<FishermanBoat>() != null || 
                    col.GetComponentInParent<RiverBoat>() != null) 
                {
                    continue;
                }

                PlayerController hitPc = col.GetComponentInParent<PlayerController>();
                if (hitPc != null && hitPc.IsAlive && !hitPc.IsHooked)
                {
                    float t = hits[i].fraction;
                    if (t < bestT)
                    {
                        bestT = t;
                        hitIsPlayer = true;
                        hitPlayer = hitPc;
                        hitPosition = hits[i].point;
                    }
                    continue;
                }

                Fish hitF = col.GetComponent<Fish>() ?? col.GetComponentInParent<Fish>();
                if (hitF != null && !hitF.IsDead && !hitF.IsHooked && hitF.gameObject.activeInHierarchy)
                {
                    // Harpoon ignores small Level 1 fish and Golden Fish
                    if (hitF.Level < 2 || hitF.IsGoldenFish) continue;

                    float t = hits[i].fraction;
                    if (t < bestT)
                    {
                        bestT = t;
                        hitIsPlayer = false;
                        bestHitFish = hitF;
                        hitPosition = hits[i].point;
                    }
                }
            }
        }

        // Apply hit to the first target encountered along the line
        if (hitIsPlayer && hitPlayer != null)
        {
            transform.position = hitPosition;
            ImpalePlayer(hitPlayer);
            return true;
        }
        else if (bestHitFish != null)
        {
            transform.position = hitPosition;
            ImpaleFish(bestHitFish);
            return true;
        }

        return false;
    }

    private float SqrDistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 0.0001f)
        {
            t = 0f;
            return (p - a).sqrMagnitude;
        }
        t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
        Vector2 proj = a + t * ab;
        return (p - proj).sqrMagnitude;
    }

    private void ImpalePlayer(PlayerController pc)
    {
        if (caughtPlayer != null || caughtFish != null || pc == null || !pc.IsAlive || pc.IsHooked) return;

        caughtPlayer = pc;
        ElevateFishSortingOrder(pc.gameObject);

        if (bubbleTrail != null && bubbleTrail.isPlaying) bubbleTrail.Stop();

        StartImpactAbsorb();
        PlayStabAudio(transform.position, isPlayer: true);
        pc.OnHarpoonImpaled(this);
    }

    private void ImpaleFish(Fish fish)
    {
        if (caughtFish != null || caughtPlayer != null || fish == null || fish.IsDead || fish.IsHooked || !fish.gameObject.activeInHierarchy) return;
        if (fish.Level < 2 || fish.IsGoldenFish) return;

        if (bubbleTrail != null && bubbleTrail.isPlaying) bubbleTrail.Stop();

        StartImpactAbsorb();
        PlayStabAudio(fish.transform.position, isPlayer: false);
        CatchFish(fish);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (currentState != HarpoonState.Descending && currentState != HarpoonState.AtBottom) return;

        if (other.GetComponentInParent<SharkHazard>() != null || 
            other.GetComponentInParent<Hazard>() != null || 
            other.GetComponentInParent<HarpoonHazard>() != null ||
            other.GetComponentInParent<FishermanBoat>() != null || 
            other.GetComponentInParent<RiverBoat>() != null) 
        {
            return;
        }

        // 1. Check Player
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc.IsAlive && !pc.IsHooked)
        {
            ImpalePlayer(pc);
            return;
        }

        // 2. Check AI Fish
        if (caughtFish != null || caughtPlayer != null) return;
        Fish fish = other.GetComponent<Fish>() ?? other.GetComponentInParent<Fish>();
        if (fish != null && !fish.IsDead && !fish.IsHooked && fish.gameObject.activeInHierarchy)
        {
            if (fish.Level < 2 || fish.IsGoldenFish) return;
            ImpaleFish(fish);
        }
    }

    /// <summary>
    /// When hitting a fish, the fish's mass absorbs the impact.
    /// Travel downward just a tiny fraction further (0.45 units) as momentum halts, then immediately reel in.
    /// </summary>
    private void StartImpactAbsorb()
    {
        currentState = HarpoonState.ImpactAbsorb;
        impactAbsorbTimer = 0f;
        impactStartPos = transform.position;
        Vector3 overshoot = (Vector3)(launchDir * 0.16f);
        impactTargetPos = transform.position + overshoot;
        if (impactTargetPos.y < floorY) impactTargetPos.y = floorY;
    }

    private void UpdateImpactAbsorb()
    {
        impactAbsorbTimer += Time.deltaTime;
        float t = Mathf.Clamp01(impactAbsorbTimer / impactAbsorbDuration);
        // Ease-out curve: initial carry-through rapidly decelerates to a stop
        float easeOut = 1f - (1f - t) * (1f - t);
        transform.position = Vector3.Lerp(impactStartPos, impactTargetPos, easeOut);

        if (caughtPlayer != null && caughtPlayer.gameObject.activeInHierarchy)
        {
            caughtPlayer.transform.position = transform.position;
        }
        if (caughtFish != null && caughtFish.gameObject.activeInHierarchy)
        {
            caughtFish.transform.position = transform.position;
        }

        if (t >= 1f)
        {
            // Momentum absorbed — immediately begin reeling in towards the boat!
            currentState = HarpoonState.Retracting;

            if (audioSource != null && reelSound != null && AudioSettingsManager.IsSfxEnabled)
            {
                audioSource.clip = reelSound;
                audioSource.loop = true;
                audioSource.Play();
            }
        }
    }

    /// <summary>
    /// Plays the harpoon stab sound effect with realistic distance attenuation and stereo panning.
    /// Centered & maximum punch when hitting the player; spatially attenuated relative to player for AI fish.
    /// </summary>
    private void PlayStabAudio(Vector3 victimPos, bool isPlayer)
    {
        if (stabSound == null || !AudioSettingsManager.IsSfxEnabled) return;

        float volume = 1.0f;
        float pan = 0f;
        float pitch = Random.Range(0.96f, 1.04f);

        Transform playerTransform = null;
        if (GridController.Instance != null && GridController.Instance.Player != null)
        {
            playerTransform = GridController.Instance.Player.transform;
        }
        else
        {
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) playerTransform = pc.transform;
        }

        if (isPlayer || playerTransform == null)
        {
            volume = 1.0f;
            pan = 0f;
            pitch = 1.0f;
            if (GameManager.instance != null)
            {
                GameManager.instance.CameraShake(0.22f, 5.5f, 2.2f);
            }
        }
        else
        {
            float dist = Vector2.Distance(victimPos, playerTransform.position);
            const float minDist = 4.0f;
            const float maxDist = 26.0f;

            if (dist <= minDist)
            {
                volume = 0.95f;
            }
            else if (dist >= maxDist)
            {
                volume = 0.06f;
            }
            else
            {
                float t = (dist - minDist) / (maxDist - minDist);
                volume = Mathf.Lerp(0.95f, 0.06f, t * t);
            }

            pan = Mathf.Clamp((victimPos.x - playerTransform.position.x) / 14.0f, -0.85f, 0.85f);

            if (dist < 14.0f && GameManager.instance != null)
            {
                float shakeIntensity = (1.0f - (dist / 14.0f)) * 2.0f;
                GameManager.instance.CameraShake(0.12f, shakeIntensity, 1.5f);
            }
        }

        if (stabAudioSource != null)
        {
            stabAudioSource.mute = false;
            stabAudioSource.panStereo = pan;
            stabAudioSource.pitch = pitch;
            stabAudioSource.PlayOneShot(stabSound, volume);
        }
        else if (audioSource != null)
        {
            audioSource.panStereo = pan;
            audioSource.PlayOneShot(stabSound, volume);
        }
    }

    /// <summary>
    /// Freezes the fish's AI and physics, parents it to the harpoon cable point so it
    /// rides along during ascent, and is silently despawned when fully reeled in.
    /// </summary>
    private void CatchFish(Fish fish)
    {
        caughtFish = fish;

        // Notify fish that it has been impaled by the harpoon to cleanly freeze autonomy and preserve exact scale
        fish.OnHarpoonImpaled(this);

        // Note: Do NOT parent the fish to the HarpoonHazard!
        // HarpoonHazard has a 1.40f scale and tilts/rotates, which causes child fish to inherit the 1.40x enlargement
        // and suffer transform shearing. Instead, track the fish in world space, identical to caughtPlayer.
        caughtFishWorldRot = fish.transform.rotation;
        fish.transform.position = transform.position;

        // Elevate fish sorting order so it renders in front of the boat hull and line
        ElevateFishSortingOrder(fish.gameObject);
    }

    /// <summary>
    /// Temporarily raises the sortingOrder of all SpriteRenderers on the caught fish
    /// so it renders cleanly in front of the boat hull (3), wake (4), and cable line (7),
    /// preventing the fish from slipping behind the boat hull while the cable stays in front.
    /// </summary>
    private void ElevateFishSortingOrder(GameObject fishObj)
    {
        if (fishObj == null) return;
        SpriteRenderer[] srs = fishObj.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < srs.Length; i++)
        {
            if (srs[i] != null && !elevatedRenderers.Contains(srs[i]))
            {
                elevatedRenderers.Add(srs[i]);
                originalSortingOrders.Add(srs[i].sortingOrder);
                // Ensure fish is in front of boat/cable without dropping behind foreground parallax (min 65)
                srs[i].sortingOrder = Mathf.Max(srs[i].sortingOrder, 65);
            }
        }
    }

    private void RestoreElevatedSortingOrders()
    {
        for (int i = 0; i < elevatedRenderers.Count; i++)
        {
            if (elevatedRenderers[i] != null && i < originalSortingOrders.Count)
            {
                elevatedRenderers[i].sortingOrder = originalSortingOrders[i];
            }
        }
        elevatedRenderers.Clear();
        originalSortingOrders.Clear();
    }
}
