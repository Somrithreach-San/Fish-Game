using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// Controls the Clam ocean creature and deadly trap.
/// - Opens through Closed and HalfOpen, then remains FullyOpened while a pearl is available.
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

    [Header("Clam Animation Frames")]
    [Tooltip("Sprite used when the clam is fully shut (idle closed state).")]
    [SerializeField] private Sprite closedSprite;

    [Tooltip("Frames played in order when the clam opens (with pearl inside). " +
             "Assign: opening_1, opening_2, opening_3, fully_open_has_pearl.")]
    [SerializeField] private Sprite[] openingFrames;

    [Tooltip("The held sprite while the clam is fully open and the pearl has been collected. " +
             "Assign: fully_open_no_pearl.")]
    [SerializeField] private Sprite fullyOpenedEmptySprite;

    [Tooltip("Frames played in order when the clam closes after the pearl was eaten. " +
             "Assign: closing_1_no_pearl, closing_2_no_pearl, closing_3_no_pearl.")]
    [SerializeField] private Sprite[] closingEmptyFrames;

    [Tooltip("Frames per second for transition animations.")]
    [SerializeField] private float animFps = 12f;

    [Header("Cycle Timers (Seconds)")]
    [Tooltip("Duration the clam remains fully closed")]
    [SerializeField] private float closedDuration = 4.0f;

    [Tooltip("Duration of the half-open opening warning state")]
    [SerializeField] private float halfOpenDuration = 1.0f;

    [Tooltip("Legacy setting. A clam with a pearl now stays open until that pearl is eaten.")]
    [SerializeField] private float fullyOpenedDuration = 3.5f;

    [Tooltip("Duration of the half-open closing transition (escape warning before snap shut)")]
    [SerializeField] private float closingHalfDuration = 0.35f;

    [Tooltip("Calculated delay after pearl is eaten before closing begins (escape grace period)")]
    [SerializeField] private float pearlEatenClosingDelay = 0.4f;

    [Tooltip("Duration of the snap shut impact")]
    [SerializeField] private float snapShutDuration = 0.15f;

    [Header("Pearl Settings")]
    [Tooltip("Whether this clam currently contains a pearl")]
    [SerializeField] private bool hasPearl = true;

    [Tooltip("XP awarded to the player when the pearl is eaten")]
    [SerializeField] private int pearlXp = 30;

    [Tooltip("Can the player eat the pearl during the HalfOpen warning state?")]
    [SerializeField] private bool canEatInHalfOpen = false;

    [Tooltip("Seconds the clam remains closed while generating a replacement pearl")]
    [SerializeField] private float pearlRespawnTime = 25.0f;

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

    [Tooltip("Audio clip played when clam starts opening")]
    [SerializeField] private AudioClip openClip;

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
    [SerializeField] private int bubbleSortingOrder = 110;

    [Header("Component References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private ClamPearl clamPearl;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform visualTransform;
    [SerializeField] private BoxCollider2D mouthTriggerCollider;

    // State tracking
    private ClamState currentState = ClamState.Closed;
    private Coroutine cycleCoroutine;
    private Coroutine respawnCoroutine;
    private Vector3 initialVisualPos;
    private Vector3 initialPearlLocalPos = new Vector3(0f, 1.37f, 0f);
    private bool hasCachedPearlPos = false;
    private float closedBaselineBottom = 0f;
    private bool isStarted = false;
    private bool isClosing = false;
    private bool endAudioEventsSubscribed = false;

    // Fish / Player inside tracking
    private PlayerController insidePlayer = null;
    private readonly HashSet<Fish> insideFish = new HashSet<Fish>();

    public ClamState CurrentState => currentState;
    public bool HasPearl => hasPearl;
    public bool IsClosing => isClosing;
    // The pearl is collectible only after the completed open state is reached.
    public bool CanEatPearl => hasPearl && currentState == ClamState.FullyOpened;

    private void Awake()
    {
        EnsureInitialized();
        EnsureComponents();
        EnsureBubbleSystems();
    }

    private void Start()
    {
        EnsureInitialized();
        EnsureComponents();
        EnsureBubbleSystems();
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

    private void EnsureInitialized()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (clamPearl == null) clamPearl = GetComponentInChildren<ClamPearl>();
        if (clamPearl != null && !hasCachedPearlPos)
        {
            initialPearlLocalPos = clamPearl.transform.localPosition;
            hasCachedPearlPos = true;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }
    }

    /// <summary>
    /// Ensures that the mouth chamber trigger collider and kinematic rigidbody are configured.
    /// </summary>
    public void EnsureComponents()
    {
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
        mouthTriggerCollider.offset = mouthTriggerOffset;
        mouthTriggerCollider.size = mouthTriggerSize;
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
                    hasPearl = true;
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

            // 2. Smooth Opening transition
            PlaySound(openClip);
            isClosing = false;
            currentState = ClamState.HalfOpen;
            UpdatePearlTrigger();
            yield return PlaySpriteFrames(openingFrames, halfOpenDuration);

            // 3. Fully Opened: keep the clam open for as long as the pearl remains.
            isClosing = false;
            SetState(ClamState.FullyOpened);
            while (hasPearl)
            {
                yield return null;
            }

            // 4. Closing transition (Half Open warning before snap shut)
            StartClosingTransition();
            yield return PlayClosingFramesIfEmpty();

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
        UpdatePearlTrigger();

        if (isClosing)
        {
            // Any AI fish caught inside when a fully-open clam begins closing dies.
            CrushInsideFish();
        }
    }

    /// <summary>
    /// Snaps the clam shut immediately with tactile timing, sound, squash juice, and lethal trap checks.
    /// </summary>
    private IEnumerator SnapShutRoutine()
    {
        PlaySound(snapShutClip);

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
        Vector2 worldCenter = transform.TransformPoint(mouthTriggerOffset);
        Vector2 worldSize = new Vector2(
            mouthTriggerSize.x * Mathf.Abs(transform.lossyScale.x),
            mouthTriggerSize.y * Mathf.Abs(transform.lossyScale.y)
        );

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

    private void KillPlayer(PlayerController player)
    {
        if (player == null || !player.IsAlive) return;

        Debug.Log("<color=red>[Clam] Player was crushed inside the clam!</color>");

        // Audio & bubble feedback
        PlaySound(snapShutClip);
        EmitCloseBubbles();

        // Trigger official player death sequence
        player.Death();
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
        Vector2 worldCenter = transform.TransformPoint(mouthTriggerOffset);
        Vector2 worldSize = new Vector2(
            mouthTriggerSize.x * Mathf.Abs(transform.lossyScale.x),
            mouthTriggerSize.y * Mathf.Abs(transform.lossyScale.y)
        );

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

        Debug.Log($"<color=orange>[Clam] AI fish {fish.name} was crushed by closing clam!</color>");

        fish.PlayEatEffect();
        fish.Die();
        insideFish.Remove(fish);
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

        Sprite targetSprite = GetSpriteForState(currentState);
        if (spriteRenderer != null && targetSprite != null)
        {
            spriteRenderer.sprite = targetSprite;
        }

        UpdatePearlTrigger();

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
                // Transition frames are played by PlaySpriteFrames; this is just a fallback.
                return closedSprite;

            case ClamState.FullyOpened:
                // Last frame of openingFrames is the fully-open-with-pearl sprite.
                // After pearl eaten we show fullyOpenedEmptySprite.
                if (!hasPearl && fullyOpenedEmptySprite != null) return fullyOpenedEmptySprite;
                if (openingFrames != null && openingFrames.Length > 0)
                    return openingFrames[openingFrames.Length - 1];
                return closedSprite;

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

    private IEnumerator PlayClosingFramesIfEmpty()
    {
        if (hasPearl || closingEmptyFrames == null || closingEmptyFrames.Length == 0)
        {
            yield return new WaitForSeconds(closingHalfDuration);
            yield break;
        }

        yield return PlaySpriteFrames(closingEmptyFrames, closingHalfDuration);
    }

    /// <summary>
    /// Updates the position and enabled state of the pearl trigger collider.
    /// </summary>
    private void UpdatePearlTrigger()
    {
        if (clamPearl == null) return;

        bool active = CanEatPearl;
        clamPearl.SetTriggerActive(active);

        if (active)
        {
            // Positioned inside the open mouth cavity above the bottom hinge
            clamPearl.transform.localPosition = initialPearlLocalPos;
        }
    }

    /// <summary>
    /// Called when the player fish swims into the pearl while the clam is open.
    /// AI fishes are not able to eat the pearl (guaranteed by ClamPearl component).
    /// </summary>
    public void OnPearlEaten(PlayerController player)
    {
        if (!CanEatPearl) return;

        hasPearl = false;

        // Disable pearl trigger AND hide the pearl sprite immediately
        if (clamPearl != null)
        {
            clamPearl.SetTriggerActive(false);
            // Hide the pearl visual instantly — no delay
            SpriteRenderer pearlRenderer = clamPearl.GetComponent<SpriteRenderer>();
            if (pearlRenderer != null) pearlRenderer.enabled = false;
            // Also deactivate the GameObject so it's fully gone
            clamPearl.gameObject.SetActive(false);
        }

        // Immediately show the "fully open, no pearl" shell sprite
        // so the player sees the empty clam before it starts closing.
        if (spriteRenderer != null && fullyOpenedEmptySprite != null)
        {
            spriteRenderer.sprite = fullyOpenedEmptySprite;
        }

        // Award player XP, score, bite animation, VFX, and floating text
        Vector3 pearlWorldPos = (clamPearl != null) ? clamPearl.transform.position : transform.position;
        if (player != null)
        {
            player.EatPearl(pearlXp, pearlWorldPos);
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
            yield return PlayClosingFramesIfEmpty();
        }
        yield return StartCoroutine(SnapShutRoutine());
        isClosing = false;
        SetState(ClamState.Closed);

        if (pearlRespawnTime > 0f)
        {
            yield return new WaitForSeconds(pearlRespawnTime);
        }

        hasPearl = true;

        // Re-enable the pearl object that was hidden when it was eaten
        if (clamPearl != null && !clamPearl.gameObject.activeSelf)
        {
            clamPearl.gameObject.SetActive(true);
            SpriteRenderer pearlRenderer = clamPearl.GetComponent<SpriteRenderer>();
            if (pearlRenderer != null) pearlRenderer.enabled = true;
            clamPearl.SetTriggerActive(false); // Trigger re-enabled by UpdatePearlTrigger when fully open
        }

        SetState(ClamState.Closed);
        cycleCoroutine = StartCoroutine(ClamCycleRoutine());
    }

    private IEnumerator SquashPunchRoutine()
    {
        if (visualTransform == null) yield break;

        // Capture the ACTUAL current scale — do NOT hardcode Vector3.one
        // because the prefab may have a non-1 scale set in the Inspector.
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
        Vector3 center = transform.TransformPoint(mouthTriggerOffset);
        Vector3 size = new Vector3(
            mouthTriggerSize.x * Mathf.Abs(transform.lossyScale.x),
            mouthTriggerSize.y * Mathf.Abs(transform.lossyScale.y),
            0.1f
        );
        Gizmos.DrawWireCube(center, size);
    }
}
