using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// Controls the Clam ocean creature and deadly trap.
/// - Opens through Closed and HalfOpen, then remains FullyOpened while a pearl is available.
/// - The clam shell uses clean empty shell sprites (never baked/attached pearl images).
/// - Pearls are rendered via the separate child ClamPearl / PearlRenderer object.
/// - Allows the player fish to eat the pearl inside when opened.
/// - When the pearl is eaten, stays open for a calculated delay (pearlEatenClosingDelay) before closing.
/// - Acts as a crushing hazard: any AI fish swimming inside while closing dies.
/// - If the player fish stays inside when the clam snaps shut, the player fish dies.
/// - AI fishes are not able to eat the pearl.
/// </summary>
public class Clam : MonoBehaviour
{
    public enum ClamState
    {
        Closed,
        HalfOpen,
        FullyOpened
    }

    public enum PearlType
    {
        None,
        Normal,
        Black
    }

    [Header("Clam Shell Animation Frames (Empty Shell Only)")]
    [Tooltip("Sprite used when the clam is fully shut (idle closed state).")]
    [SerializeField] private Sprite closedSprite;

    [Tooltip("Frames played in order when the clam opens.")]
    [SerializeField] private Sprite[] openingFrames;

    [Tooltip("The held sprite while the clam is fully open.")]
    [SerializeField] private Sprite fullyOpenedSprite;

    [Tooltip("Frames played in order when the clam closes.")]
    [SerializeField] private Sprite[] closingFrames;

    [Tooltip("Frames per second for transition animations.")]
    [SerializeField] private float animFps = 12f;

    [Header("Cycle Timers (Seconds)")]
    [Tooltip("Duration the clam remains fully closed")]
    [SerializeField] private float closedDuration = 4.0f;

    [Tooltip("Duration of the half-open opening warning state")]
    [SerializeField] private float halfOpenDuration = 1.0f;

    [Tooltip("Duration of the half-open closing transition (escape warning before snap shut)")]
    [SerializeField] private float closingHalfDuration = 0.35f;

    [Tooltip("Calculated delay after pearl is eaten before closing begins (escape grace period)")]
    [SerializeField] private float pearlEatenClosingDelay = 0.22f; // Snappy tactile reaction (calibrated from 0.4f)

    [Tooltip("Duration of the snap shut impact")]
    [SerializeField] private float snapShutDuration = 0.15f;

    [Header("Pearl Settings")]
    [Tooltip("Whether this clam currently contains a pearl")]
    [SerializeField] private bool hasPearl = true;

    [Tooltip("Type of pearl inside the clam")]
    [SerializeField] private PearlType pearlType = PearlType.Normal;

    [Tooltip("Sprite for normal white pearl")]
    [SerializeField] private Sprite normalPearlSprite;

    [Tooltip("Sprite for black pearl")]
    [SerializeField] private Sprite blackPearlSprite;

    [Tooltip("XP awarded to the player when normal white pearl is eaten")]
    [SerializeField] private int normalPearlXp = 30;

    [Tooltip("XP awarded to the player when black pearl is eaten")]
    [SerializeField] private int blackPearlXp = 0;

    [Tooltip("Chance of spawning a Black Pearl instead of a Normal Pearl (0.0 = never, 1.0 = always)")]
    [Range(0f, 1f)]
    [SerializeField] private float blackPearlChance = 2f / 3f;

    [Tooltip("Seconds the clam remains closed while generating a replacement pearl")]
    [SerializeField] private float pearlRespawnTime = 35.0f;

    [Tooltip("If true, clam stays closed when empty. Default false for continuous natural cycle.")]
    [SerializeField] private bool stayClosedWhenEmpty = false;

    [Header("Mouth Chamber Hazard Trigger")]
    [Tooltip("Local offset of the inside mouth chamber trigger")]
    [SerializeField] private Vector2 mouthTriggerOffset = new Vector2(0f, 1.6f);

    [Tooltip("Local size of the inside mouth chamber trigger")]
    [SerializeField] private Vector2 mouthTriggerSize = new Vector2(3.2f, 2.4f);

    [Header("Audio & Juice")]
    [Tooltip("Audio clip played when clam snaps shut")]
    [SerializeField] private AudioClip snapShutClip;

    [Header("Bubble VFX on Close")]
    [Tooltip("Material used for bubble particles (bubbleParticleMat)")]
    [SerializeField] private Material bubbleMaterial;

    [Tooltip("Particle system for left side shell seam")]
    [SerializeField] private ParticleSystem leftBubbleSystem;

    [Tooltip("Particle system for right side shell seam")]
    [SerializeField] private ParticleSystem rightBubbleSystem;

    [Tooltip("Base number of bubbles emitted per side on close")]
    [SerializeField] private int bubblesPerSide = 4;

    [Tooltip("Minimum particle size (scaled by clam hierarchy)")]
    [SerializeField] private float minBubbleSize = 0.34f;

    [Tooltip("Maximum particle size (scaled by clam hierarchy)")]
    [SerializeField] private float maxBubbleSize = 0.62f;

    [Tooltip("Sorting layer for clam bubbles")]
    [SerializeField] private string bubbleSortingLayer = "ParallaxForeground";

    [Tooltip("Sorting order for clam bubbles")]
    [SerializeField] private int bubbleSortingOrder = 57;

    [Header("Component References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer pearlRenderer;
    [SerializeField] private ClamPearl clamPearl;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform visualTransform;
    [SerializeField] private BoxCollider2D mouthTriggerCollider;

    // State tracking
    private ClamState currentState = ClamState.Closed;
    private Coroutine cycleCoroutine;
    private Coroutine respawnCoroutine;
    private readonly Vector3 initialPearlLocalPos = new Vector3(0f, 1.16f, 0f);
    private readonly Vector3 initialPearlLocalScale = new Vector3(0.85f, 0.85f, 1.0f);
    private bool isStarted = false;
    private bool isClosing = false;
    private bool snapSoundPlayed = false;
    private bool isPearlVisualRevealed = false;
    private bool endAudioEventsSubscribed = false;

    // Fish / Player inside tracking
    private PlayerController insidePlayer = null;
    private readonly HashSet<Fish> insideFish = new HashSet<Fish>();

    public static readonly System.Collections.Generic.List<Clam> AllClams = new System.Collections.Generic.List<Clam>();

    public ClamState CurrentState => currentState;
    public PearlType CurrentPearlType => pearlType;
    public bool HasPearl => hasPearl;
    public bool IsClosing => isClosing;
    // The pearl is collectible only after the completed open state is reached.
    public bool CanEatPearl => hasPearl && pearlType != PearlType.None && currentState == ClamState.FullyOpened;
    public SpriteRenderer PearlRenderer => pearlRenderer;
    public ClamPearl ClamPearl => clamPearl;

    private void OnEnable()
    {
        if (!AllClams.Contains(this))
        {
            AllClams.Add(this);
        }
    }

    private void Awake()
    {
        EnsureInitialized();
        EnsureComponents();
        EnsureBubbleSystems();
    }

    private void Start()
    {
        LevelConfig cfg = LevelManager.GetCurrentConfig();
        if (!cfg.enableClam)
        {
            gameObject.SetActive(false);
            return;
        }

        EnsureInitialized();
        EnsureComponents();
        EnsureBubbleSystems();
        if (hasPearl)
        {
            RollRandomPearl();
        }
        SetState(ClamState.Closed);
        isStarted = true;
        EventManager.StartListening("playerDeath", StopAudioForGameEnd);
        EventManager.StartListening("GameLoss", StopAudioForGameEnd);
        EventManager.StartListening("GameWin", StopAudioForGameEnd);
        endAudioEventsSubscribed = true;
        cycleCoroutine = StartCoroutine(ClamCycleRoutine());
    }

    private void OnDisable()
    {
        AllClams.Remove(this);
        if (cycleCoroutine != null)
        {
            StopCoroutine(cycleCoroutine);
            cycleCoroutine = null;
        }
        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
        insidePlayer = null;
        insideFish.Clear();
        crushingFish.Clear();
        isPlayerBeingCrushed = false;
    }

    private void OnDestroy()
    {
        if (!endAudioEventsSubscribed) return;
        EventManager.StopListening("playerDeath", StopAudioForGameEnd);
        EventManager.StopListening("GameLoss", StopAudioForGameEnd);
        EventManager.StopListening("GameWin", StopAudioForGameEnd);
        endAudioEventsSubscribed = false;
    }

    private void StopAudioForGameEnd()
    {
        if (audioSource != null) audioSource.Stop();
    }

    private void OnValidate()
    {
        EnsureClamSpritesLoaded();
        EnsurePearlSpritesLoaded();
        EnsureAudioLoaded();
        UpdatePearlVisual();
    }

    private void EnsureInitialized()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            // Clam sits on seafloor reef backdrop (50-55), behind AI fish (90), player fish (120), and hazards (95-130)
            spriteRenderer.sortingOrder = 56;
        }

        if (clamPearl == null) clamPearl = GetComponentInChildren<ClamPearl>();
        if (clamPearl != null)
        {
            clamPearl.transform.localPosition = initialPearlLocalPos;
            clamPearl.transform.localScale = initialPearlLocalScale;
            if (pearlRenderer == null)
            {
                pearlRenderer = clamPearl.GetComponent<SpriteRenderer>();
            }
        }

        if (pearlRenderer != null)
        {
            pearlRenderer.sortingOrder = 57;
            if (spriteRenderer != null)
            {
                pearlRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
            }
        }

        EnsureClamSpritesLoaded();
        EnsurePearlSpritesLoaded();
        EnsureAudioLoaded();
        UpdatePearlVisual();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        if (audioSource != null)
        {
            audioSource.spatialBlend = 1.0f; // 3D Spatial Audio for realistic underwater distance
            audioSource.minDistance = 6.0f;
            audioSource.maxDistance = 40.0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            AudioSettingsManager.RouteToSfx(audioSource);
        }
    }

    private static AudioClip s_CachedSnapShutClip = null;

    public void EnsureAudioLoaded()
    {
        if (snapShutClip == null)
        {
            if (s_CachedSnapShutClip == null)
            {
#if UNITY_EDITOR
                s_CachedSnapShutClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Clam_shut.mp3");
                if (s_CachedSnapShutClip == null)
                {
                    s_CachedSnapShutClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/clam_shut.mp3");
                }
#endif
                if (s_CachedSnapShutClip == null)
                {
                    s_CachedSnapShutClip = Resources.Load<AudioClip>("Clam_shut");
                }
                if (s_CachedSnapShutClip == null)
                {
                    s_CachedSnapShutClip = Resources.Load<AudioClip>("clam_shut");
                }
            }
            if (s_CachedSnapShutClip != null)
            {
                snapShutClip = s_CachedSnapShutClip;
            }
        }
    }

    public void EnsureClamSpritesLoaded()
    {
#if UNITY_EDITOR
        if (closedSprite == null)
            closedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closed.png");
        if (fullyOpenedSprite == null)
            fullyOpenedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_fully_open_no_pearl.png");

        if (openingFrames == null || openingFrames.Length == 0)
        {
            Sprite c3 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_3__no_pearl.png");
            if (c3 == null) c3 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_3_no_pearl.png");

            openingFrames = new Sprite[]
            {
                UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_1__no_pearl.png"),
                UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_2__no_pearl.png"),
                c3,
                UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_fully_open_no_pearl.png")
            };
        }

        if (closingFrames == null || closingFrames.Length == 0)
        {
            Sprite c3 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_3__no_pearl.png");
            if (c3 == null) c3 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_3_no_pearl.png");

            closingFrames = new Sprite[]
            {
                c3,
                UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_2__no_pearl.png"),
                UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/clam_closing_1__no_pearl.png")
            };
        }
#endif
        if (closedSprite == null) closedSprite = Resources.Load<Sprite>("clam_closed");
        if (fullyOpenedSprite == null) fullyOpenedSprite = Resources.Load<Sprite>("clam_fully_open_no_pearl");
    }

    public void EnsurePearlSpritesLoaded()
    {
#if UNITY_EDITOR
        if (normalPearlSprite == null)
            normalPearlSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/Pearl.png");
        if (blackPearlSprite == null)
            blackPearlSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/clam/Black Pearl.png");
#endif
        if (normalPearlSprite == null) normalPearlSprite = Resources.Load<Sprite>("Pearl");
        if (blackPearlSprite == null) blackPearlSprite = Resources.Load<Sprite>("Black Pearl");
    }

    public void SetPearlType(PearlType type)
    {
        pearlType = type;
        hasPearl = (type != PearlType.None);
        if (!hasPearl)
        {
            isPearlVisualRevealed = false;
        }
        if (clamPearl != null)
        {
            clamPearl.transform.localPosition = initialPearlLocalPos;
            clamPearl.transform.localScale = initialPearlLocalScale;
        }
        UpdatePearlVisual();
    }

    public void RollRandomPearl()
    {
        if (stayClosedWhenEmpty && pearlType == PearlType.None) return;
        PearlType chosen = (Random.value < blackPearlChance) ? PearlType.Black : PearlType.Normal;
        SetPearlType(chosen);
    }

    public void PreparePearlForSuction()
    {
        // Standalone pearl is already distinct and ready for suction
        if (pearlRenderer != null && hasPearl && pearlType != PearlType.None)
        {
            isPearlVisualRevealed = true;
            pearlRenderer.enabled = true;
        }
    }

    public void UpdatePearlVisual()
    {
        if (clamPearl == null) clamPearl = GetComponentInChildren<ClamPearl>();
        if (pearlRenderer == null && clamPearl != null)
        {
            pearlRenderer = clamPearl.GetComponent<SpriteRenderer>();
        }

        bool shouldShowPearl = hasPearl && pearlType != PearlType.None && isPearlVisualRevealed && currentState != ClamState.Closed;

        if (pearlRenderer != null)
        {
            if (shouldShowPearl)
            {
                Sprite spriteToUse = (pearlType == PearlType.Black) ? blackPearlSprite : normalPearlSprite;
                if (spriteToUse != null)
                {
                    pearlRenderer.sprite = spriteToUse;
                }
                pearlRenderer.enabled = true;
            }
            else
            {
                pearlRenderer.enabled = false;
            }
        }

        if (clamPearl != null)
        {
            clamPearl.SetTriggerActive(CanEatPearl);
        }
    }

    public Vector2 GetMouthWorldCenter()
    {
        if (spriteRenderer != null)
        {
            Bounds b = spriteRenderer.bounds;
            return new Vector2(b.center.x, b.min.y + b.size.y * 0.45f);
        }
        return transform.TransformPoint(mouthTriggerOffset);
    }

    public Vector2 GetMouthWorldSize()
    {
        if (spriteRenderer != null)
        {
            Bounds b = spriteRenderer.bounds;
            return new Vector2(b.size.x * 0.72f, b.size.y * 0.50f);
        }
        return new Vector2(
            mouthTriggerSize.x * Mathf.Abs(transform.lossyScale.x),
            mouthTriggerSize.y * Mathf.Abs(transform.lossyScale.y)
        );
    }

    /// <summary>
    /// Ensures that the mouth chamber trigger collider and kinematic rigidbody are configured.
    /// </summary>
    public void EnsureComponents()
    {
        EnsureInitialized();

        // 1. Kinematic Rigidbody2D on root so trigger callbacks reliably fire for moving fish
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.useFullKinematicContacts = true;

        // 2. BoxCollider2D trigger for the mouth chamber
        if (mouthTriggerCollider == null)
        {
            mouthTriggerCollider = GetComponent<BoxCollider2D>();
            if (mouthTriggerCollider == null)
            {
                mouthTriggerCollider = gameObject.AddComponent<BoxCollider2D>();
            }
        }
        mouthTriggerCollider.isTrigger = true;

        if (spriteRenderer != null)
        {
            Vector2 worldCenter = GetMouthWorldCenter();
            Vector2 worldSize = GetMouthWorldSize();
            mouthTriggerCollider.offset = transform.InverseTransformPoint(worldCenter);
            Vector2 localSize = transform.InverseTransformVector(worldSize);
            mouthTriggerCollider.size = new Vector2(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y));
        }
        else
        {
            mouthTriggerCollider.offset = mouthTriggerOffset;
            mouthTriggerCollider.size = mouthTriggerSize;
        }
    }

    /// <summary>
    /// Core state machine loop for natural, continuous opening and closing.
    /// Runs naturally on its own without requiring player interaction.
    /// </summary>
    private IEnumerator ClamCycleRoutine()
    {
        // Random slight phase offset on startup so multiple clams don't open simultaneously
        yield return new WaitForSeconds(Random.Range(0.2f, 1.5f));

        while (true)
        {
            // Keep the clam shut while a replacement pearl is being generated.
            if (!hasPearl && stayClosedWhenEmpty)
            {
                isClosing = false;
                SetState(ClamState.Closed);
                if (pearlRespawnTime > 0f)
                {
                    yield return new WaitForSeconds(pearlRespawnTime);
                    RollRandomPearl();
                    SetState(ClamState.Closed);
                }
                else
                {
                    yield return new WaitForSeconds(1.0f);
                }
                continue;
            }

            // 1. Closed state
            isClosing = false;
            SetState(ClamState.Closed);
            yield return new WaitForSeconds(closedDuration);

            // 2. Smooth Opening transition (Opening is silent per user request; sound only plays on closing)
            isClosing = false;
            currentState = ClamState.HalfOpen;
            isPearlVisualRevealed = false;
            UpdatePearlVisual();
            yield return PlayOpeningFramesRoutine(halfOpenDuration);

            // 3. Fully Opened: keep the clam open for as long as the pearl remains.
            isClosing = false;
            SetState(ClamState.FullyOpened);
            while (hasPearl)
            {
                yield return null;
            }

            // 4. Closing transition (Half Open warning before snap shut)
            StartClosingTransition();
            yield return PlayClosingFrames();

            // 5. Snap Shut
            yield return StartCoroutine(SnapShutRoutine());
        }
    }

    /// <summary>
    /// Begins the closing warning state. A clam can only crush fish if it had first
    /// reached its fully-open state; a partially opened shell is never lethal.
    /// </summary>
    private void StartClosingTransition()
    {
        bool wasFullyOpened = currentState == ClamState.FullyOpened;
        isClosing = wasFullyOpened;
        currentState = ClamState.HalfOpen;
        isPearlVisualRevealed = false;
        UpdatePearlVisual();

        if (isClosing)
        {
            // Any AI fish caught inside when a fully-open clam begins closing dies.
            CrushInsideFish();
        }
    }

    private void TriggerSnapSound()
    {
        if (snapSoundPlayed) return;
        snapSoundPlayed = true;
        PlaySound(snapShutClip);
    }

    /// <summary>
    /// Snaps the clam shut immediately with tactile timing, sound, squash juice, and lethal trap checks.
    /// </summary>
    private IEnumerator SnapShutRoutine()
    {
        TriggerSnapSound();
        snapSoundPlayed = false;

        // Snap to closed state
        SetState(ClamState.Closed);

        // Subtle squash punch for impact juice
        StartCoroutine(SquashPunchRoutine());

        // Spawn close bubbles at the seam
        EmitSnapBubbles();

        // Only a shell that was fully opened before closing can be lethal.
        if (isClosing)
        {
            CheckAndKillPlayerIfInside();
            CrushInsideFish();
        }

        isClosing = false;

        yield return new WaitForSeconds(snapShutDuration);
    }

    /// <summary>
    /// Checks if the player is currently inside the clam's mouth chamber and kills them if so.
    /// Uses both tracked trigger state and direct OverlapBox verification for 100% reliability.
    /// </summary>
    private void CheckAndKillPlayerIfInside()
    {
        // 1. Check tracked trigger player
        if (insidePlayer != null && insidePlayer.IsAlive)
        {
            KillPlayer(insidePlayer);
            return;
        }

        // 2. Physical OverlapBox check to eliminate any possibility of missed trigger exit timing
        Vector2 worldCenter = GetMouthWorldCenter();
        Vector2 worldSize = GetMouthWorldSize();

        Collider2D[] overlaps = Physics2D.OverlapBoxAll(worldCenter, worldSize, transform.eulerAngles.z);
        foreach (var col in overlaps)
        {
            if (col == null) continue;
            PlayerController pc = col.GetComponentInParent<PlayerController>();
            if (pc != null && pc.IsAlive)
            {
                KillPlayer(pc);
                return;
            }
        }
    }

    private readonly HashSet<Fish> crushingFish = new HashSet<Fish>();
    private bool isPlayerBeingCrushed = false;

    private void KillPlayer(PlayerController player)
    {
        if (player == null || !player.IsAlive || isPlayerBeingCrushed) return;
        if (PlayerAbilitySystem.IsPlayerInvulnerable) return;

        isPlayerBeingCrushed = true;
        insidePlayer = null;

        StartCoroutine(ShrinkAndCrushPlayerRoutine(player));
    }

    private IEnumerator ShrinkAndCrushPlayerRoutine(PlayerController player)
    {
        if (player == null) yield break;

        Debug.Log("<color=red>[Clam] Player was caught and crushed inside closing clam!</color>");

        // 1. Lock player movement & disable colliders
        player.TerminateMovement();
        Collider2D[] allCols = player.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = false;
        }

        // 2. Audio & bubble feedback
        PlaySound(snapShutClip);
        EmitCloseBubbles();

        // 3. Smoothly shrink player down into the clam center as it shuts
        Vector3 initialScale = player.transform.localScale;
        Vector3 startPos = player.transform.position;
        Vector3 targetPos = GetMouthWorldCenter();

        float duration = Mathf.Max(0.28f, snapShutDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (player == null) break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            player.transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, ease);
            player.transform.position = Vector3.Lerp(startPos, targetPos, ease * 0.75f);

            yield return null;
        }

        if (player != null)
        {
            player.transform.localScale = Vector3.zero;
            Sprite clamSp = GetComponentInChildren<SpriteRenderer>()?.sprite;
            player.Death(clamSp);
        }

        isPlayerBeingCrushed = false;
    }

    /// <summary>
    /// Crushes all AI fish currently inside the clam mouth chamber.
    /// </summary>
    private void CrushInsideFish()
    {
        // 1. Kill tracked fish
        if (insideFish.Count > 0)
        {
            List<Fish> toKill = new List<Fish>(insideFish);
            foreach (var f in toKill)
            {
                KillFish(f);
            }
            insideFish.Clear();
        }

        // 2. OverlapBox verification for any newly entered fish
        Vector2 worldCenter = GetMouthWorldCenter();
        Vector2 worldSize = GetMouthWorldSize();

        Collider2D[] overlaps = Physics2D.OverlapBoxAll(worldCenter, worldSize, transform.eulerAngles.z);
        foreach (var col in overlaps)
        {
            if (col == null) continue;
            Fish f = col.GetComponent<Fish>() ?? col.GetComponentInParent<Fish>();
            if (f != null && !f.IsDead && f.gameObject.activeInHierarchy)
            {
                KillFish(f);
            }
        }
    }

    private void KillFish(Fish fish)
    {
        if (fish == null || fish.IsDead || !fish.gameObject.activeInHierarchy) return;
        if (crushingFish.Contains(fish)) return;

        crushingFish.Add(fish);
        insideFish.Remove(fish);

        StartCoroutine(ShrinkAndCrushFishRoutine(fish));
    }

    private IEnumerator ShrinkAndCrushFishRoutine(Fish fish)
    {
        if (fish == null) yield break;

        Debug.Log($"<color=orange>[Clam] AI fish {fish.name} is being smoothly crushed by closing clam!</color>");

        // 1. Immediately disable AI, physics velocity and colliders so it stays trapped inside
        fish.IsDead = true; // Prevents being eaten by other predators during shrinking
        var ai = fish.GetComponent<FishAI>();
        if (ai != null) ai.enabled = false;
        var mv = fish.GetComponent<FishMovement>();
        if (mv != null) mv.enabled = false;

        Rigidbody2D rb = fish.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        Collider2D[] allCols = fish.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < allCols.Length; i++)
        {
            if (allCols[i] != null) allCols[i].enabled = false;
        }

        // 2. Smoothly shrink the fish scale down to 0 while gently drawing it into the clam center
        Vector3 initialScale = fish.transform.localScale;
        Vector3 startPos = fish.transform.position;
        Vector3 targetPos = GetMouthWorldCenter();

        float duration = Mathf.Max(0.26f, snapShutDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (fish == null || !fish.gameObject.activeInHierarchy) break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            fish.transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, ease);
            fish.transform.position = Vector3.Lerp(startPos, targetPos, ease * 0.75f);

            yield return null;
        }

        if (fish != null)
        {
            fish.transform.localScale = Vector3.zero;
            fish.PlayEatEffect();
            fish.Die();
        }

        crushingFish.Remove(fish);
    }

    #region Trigger Detection

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;

        // Player check
        PlayerController pc = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            insidePlayer = pc;
            return;
        }

        // AI Fish check
        Fish fish = other.GetComponent<Fish>() ?? other.GetComponentInParent<Fish>();
        if (fish != null && !fish.IsDead)
        {
            // Any AI fish that swims inside while it's closing dies immediately!
            if (isClosing)
            {
                KillFish(fish);
            }
            else
            {
                insideFish.Add(fish);
            }
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other == null) return;

        // Ensure tracked player is valid
        if (insidePlayer == null)
        {
            PlayerController pc = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (pc != null) insidePlayer = pc;
        }

        // If closing, any AI fish inside dies immediately!
        if (isClosing)
        {
            Fish fish = other.GetComponent<Fish>() ?? other.GetComponentInParent<Fish>();
            if (fish != null && !fish.IsDead)
            {
                KillFish(fish);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null) return;

        // Player exit
        PlayerController pc = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
        if (pc != null && insidePlayer == pc)
        {
            insidePlayer = null;
            return;
        }

        // AI Fish exit
        Fish fish = other.GetComponent<Fish>() ?? other.GetComponentInParent<Fish>();
        if (fish != null)
        {
            insideFish.Remove(fish);
        }
    }

    #endregion

    /// <summary>
    /// Changes the clam state, swaps the sprite, and toggles pearl trigger.
    /// </summary>
    public void SetState(ClamState newState)
    {
        EnsureInitialized();
        ClamState previousState = currentState;
        currentState = newState;

        if (newState == ClamState.Closed)
        {
            isPearlVisualRevealed = false;
        }
        else if (newState == ClamState.FullyOpened)
        {
            isPearlVisualRevealed = true;
        }

        Sprite targetSprite = GetSpriteForState(currentState);
        if (spriteRenderer != null && targetSprite != null)
        {
            spriteRenderer.sprite = targetSprite;
        }

        UpdatePearlVisual();

        if (newState == ClamState.Closed && clamPearl != null)
        {
            clamPearl.transform.localPosition = initialPearlLocalPos;
            clamPearl.transform.localScale = initialPearlLocalScale;
        }

        // Emit small bubbles from each side of the clam when closing
        if (isStarted && previousState != ClamState.Closed && newState == ClamState.Closed)
        {
            EmitCloseBubbles();
        }
    }

    private Sprite GetSpriteForState(ClamState state)
    {
        switch (state)
        {
            case ClamState.Closed:
                return closedSprite;

            case ClamState.HalfOpen:
                return closedSprite;

            case ClamState.FullyOpened:
                return fullyOpenedSprite != null ? fullyOpenedSprite : closedSprite;

            default:
                return closedSprite;
        }
    }

    /// <summary>
    /// Plays sprite frames at animFps rate. If frames is null or empty, just waits for duration.
    /// </summary>
    private IEnumerator PlaySpriteFrames(Sprite[] frames, float duration)
    {
        if (frames == null || frames.Length == 0 || spriteRenderer == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        float frameDuration = (animFps > 0f) ? (1f / animFps) : (duration / frames.Length);
        foreach (Sprite frame in frames)
        {
            if (frame == null) continue;
            spriteRenderer.sprite = frame;
            yield return new WaitForSeconds(frameDuration);
        }
    }

    private IEnumerator PlayOpeningFramesRoutine(float duration)
    {
        if (openingFrames == null || openingFrames.Length == 0 || spriteRenderer == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        float frameDuration = (animFps > 0f) ? (1f / animFps) : (duration / openingFrames.Length);
        for (int i = 0; i < openingFrames.Length; i++)
        {
            Sprite frame = openingFrames[i];
            if (frame != null)
            {
                spriteRenderer.sprite = frame;
            }

            // Only reveal pearl on the last frames when the shell is almost fully open
            bool almostOpen = (i >= openingFrames.Length - 2);
            if (isPearlVisualRevealed != almostOpen)
            {
                isPearlVisualRevealed = almostOpen;
                UpdatePearlVisual();
            }

            yield return new WaitForSeconds(frameDuration);
        }
    }

    private IEnumerator PlayClosingFrames()
    {
        TriggerSnapSound();
        if (closingFrames == null || closingFrames.Length == 0)
        {
            yield return new WaitForSeconds(closingHalfDuration);
            yield break;
        }

        yield return PlaySpriteFrames(closingFrames, closingHalfDuration);
    }

    /// <summary>
    /// Called when the player fish swims into the pearl while the clam is open.
    /// AI fishes are not able to eat the pearl (guaranteed by ClamPearl component).
    /// </summary>
    public void OnPearlEaten(PlayerController player)
    {
        if (!CanEatPearl) return;

        bool isBlack = (pearlType == PearlType.Black);
        int earnedXp = isBlack ? blackPearlXp : normalPearlXp;

        hasPearl = false;
        isPearlVisualRevealed = false;

        // Hide pearl visual and trigger immediately
        UpdatePearlVisual();
        if (clamPearl != null)
        {
            clamPearl.transform.localPosition = initialPearlLocalPos;
            clamPearl.transform.localScale = initialPearlLocalScale;
        }

        // Award player XP, score, bite animation, VFX, and floating text
        Vector3 pearlWorldPos = (clamPearl != null) ? clamPearl.transform.position : transform.position;
        if (player != null)
        {
            player.EatPearl(earnedXp, pearlWorldPos, isBlack);
        }

        // Stop the natural cycle and close immediately. The clam remains shut while
        // the replacement pearl is generated.
        if (cycleCoroutine != null)
        {
            StopCoroutine(cycleCoroutine);
        }
        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
        }
        cycleCoroutine = StartCoroutine(PearlEatenRecoveryRoutine());
    }

    /// <summary>
    /// Closes after the pearl is eaten, stays shut while a replacement pearl is generated,
    /// then restores the pearl before resuming the natural cycle.
    /// </summary>
    private IEnumerator PearlEatenRecoveryRoutine()
    {
        isClosing = true;

        if (pearlEatenClosingDelay > 0f)
        {
            yield return new WaitForSeconds(pearlEatenClosingDelay);
        }

        if (currentState == ClamState.FullyOpened)
        {
            StartClosingTransition();
            yield return PlayClosingFrames();
        }
        yield return StartCoroutine(SnapShutRoutine());
        isClosing = false;
        SetState(ClamState.Closed);

        if (pearlRespawnTime > 0f)
        {
            yield return new WaitForSeconds(pearlRespawnTime);
        }

        RollRandomPearl();
        UpdatePearlVisual();

        SetState(ClamState.Closed);
        cycleCoroutine = StartCoroutine(ClamCycleRoutine());
    }

    private IEnumerator SquashPunchRoutine()
    {
        if (visualTransform == null) yield break;

        Vector3 originalScale = visualTransform.localScale;
        Vector3 squashed = new Vector3(originalScale.x * 1.05f, originalScale.y * 0.95f, originalScale.z);

        float elapsed = 0f;
        float duration = 0.16f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float curve = Mathf.Sin(t * Mathf.PI);
            visualTransform.localScale = Vector3.Lerp(originalScale, squashed, curve);
            yield return null;
        }

        visualTransform.localScale = originalScale;
    }

    private void EmitSnapBubbles()
    {
        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            pc.PlayEatEffect();
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (GameManager.instance != null && GameManager.instance.IsGameOver) return;
        if (clip == null)
        {
            EnsureAudioLoaded();
            clip = snapShutClip;
        }
        if (clip != null && audioSource != null && AudioSettingsManager.IsSfxEnabled)
        {
            audioSource.PlayOneShot(clip, 1.0f);
        }
    }

    /// <summary>
    /// Emits small bubble particles from each side as the clam shuts.
    /// </summary>
    public void EmitCloseBubbles()
    {
        EnsureBubbleSystems();

        int leftCount = Mathf.Max(1, bubblesPerSide + Random.Range(-1, 2));
        int rightCount = Mathf.Max(1, bubblesPerSide + Random.Range(-1, 2));

        if (leftBubbleSystem != null) leftBubbleSystem.Emit(leftCount);
        if (rightBubbleSystem != null) rightBubbleSystem.Emit(rightCount);
    }

    /// <summary>
    /// Ensures that the left and right bubble particle systems exist and are fully configured.
    /// </summary>
    public void EnsureBubbleSystems()
    {
        if (bubbleMaterial == null)
        {
#if UNITY_EDITOR
            bubbleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
#endif
        }

        Transform parentTransform = visualTransform != null ? visualTransform : transform;

        if (leftBubbleSystem == null)
        {
            Transform existing = parentTransform.Find("Bubbles_Left");
            if (existing != null) leftBubbleSystem = existing.GetComponent<ParticleSystem>();
            if (leftBubbleSystem == null)
            {
                GameObject go = new GameObject("Bubbles_Left");
                go.transform.SetParent(parentTransform, false);
                leftBubbleSystem = go.AddComponent<ParticleSystem>();
            }
        }

        if (rightBubbleSystem == null)
        {
            Transform existing = parentTransform.Find("Bubbles_Right");
            if (existing != null) rightBubbleSystem = existing.GetComponent<ParticleSystem>();
            if (rightBubbleSystem == null)
            {
                GameObject go = new GameObject("Bubbles_Right");
                go.transform.SetParent(parentTransform, false);
                rightBubbleSystem = go.AddComponent<ParticleSystem>();
            }
        }

        // Emitter positions adjusted for BottomCenter grounded pivot
        if (leftBubbleSystem != null)
        {
            leftBubbleSystem.transform.localPosition = new Vector3(-1.85f, 0.25f, -0.05f);
            ConfigureBubbleParticleSystem(leftBubbleSystem, true);
        }
        if (rightBubbleSystem != null)
        {
            rightBubbleSystem.transform.localPosition = new Vector3(1.85f, 0.25f, -0.05f);
            ConfigureBubbleParticleSystem(rightBubbleSystem, false);
        }
    }

    public void ConfigureBubbleParticleSystem(ParticleSystem ps, bool isLeft)
    {
        if (ps == null) return;

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(minBubbleSize, maxBubbleSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.038f;
        main.maxParticles = 36;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = isLeft ? new ParticleSystem.MinMaxCurve(-0.55f, -0.15f) : new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
        vel.y = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.95f, 0.98f, 1.0f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0.00f),
                new GradientAlphaKey(0.95f, 0.08f),
                new GradientAlphaKey(0.85f, 0.75f),
                new GradientAlphaKey(0.0f, 1.00f)
            }
        );
        col.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerName = bubbleSortingLayer;
        renderer.sortingOrder = bubbleSortingOrder;
        if (bubbleMaterial != null)
        {
            renderer.material = bubbleMaterial;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isClosing ? Color.red : (CanEatPearl ? Color.green : Color.yellow);
        Vector3 center = GetMouthWorldCenter();
        Vector2 size2D = GetMouthWorldSize();
        Vector3 size = new Vector3(size2D.x, size2D.y, 0.1f);
        Gizmos.DrawWireCube(center, size);
    }
}
