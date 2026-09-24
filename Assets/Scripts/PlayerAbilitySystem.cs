using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Rhinotap.Toolkit;

/// <summary>
/// PlayerAbilitySystem handles the Eating-Streak Frenzy gauge and the unique 5-second
/// special abilities for both Ocean ("Vortex Vacuum") and River ("Apex Blitz") player fish.
/// </summary>
public class PlayerAbilitySystem : MonoBehaviour
{
    public static PlayerAbilitySystem Instance { get; private set; }

    /// <summary>
    /// Invulnerability safeguard flag active during special abilities (5s duration).
    /// </summary>
    public static bool IsPlayerInvulnerable => Instance != null && Instance.isAbilityActive;
    public static bool IsWorldFrozen => false;

    [Header("Combo & Frenzy Settings")]
    [Tooltip("Ability active duration in seconds")]
    [SerializeField] private float abilityDuration = 5.0f;
    [Tooltip("Streak decay timers for tiers 1 to 9 (seconds) - tuned forgivingly")]
    [SerializeField] private float tier1Duration = 4.5f;
    [SerializeField] private float tier2Duration = 4.2f;
    [SerializeField] private float tier3Duration = 3.9f;
    [SerializeField] private float tier4Duration = 3.6f;
    [SerializeField] private float tier5Duration = 3.4f;
    [SerializeField] private float tier6Duration = 3.2f;
    [SerializeField] private float tier7Duration = 3.0f;
    [SerializeField] private float tier8Duration = 2.9f;
    [SerializeField] private float tier9Duration = 2.8f;

    [Header("Ability State")]
    [SerializeField] private int comboStreak = 0;
    [SerializeField] private float streakTimer = 0f;
    [SerializeField] private float currentEnergy = 0f; // 0.0 to 1.0 (100% = Ready)
    [SerializeField] private bool isAbilityActive = false;
    [SerializeField] private float activeTimer = 0f;

    [Header("Audio")]
    [SerializeField] private AudioClip vortexSound;
    [SerializeField] private AudioClip blitzSound;
    [SerializeField] private AudioClip abilityReadySound;

    private PlayerController playerController;
    private AudioSource audioSource;
    private Coroutine activeAbilityRoutine;
    private bool wasReadyLastFrame = false;
    private SuctionVFX suctionVFX;

    private void EnsureSuctionVFX()
    {
        if (suctionVFX == null)
        {
            GameObject vfxObj = new GameObject("SuctionVFX_Root");
            suctionVFX = vfxObj.AddComponent<SuctionVFX>();
            suctionVFX.Initialize(playerController);
        }
    }

    // Public properties for UI and game systems
    public int ComboStreak => comboStreak;
    public float StreakTimerRatio
    {
        get
        {
            if (comboStreak >= 10) return 1.0f;
            float maxDur = GetStreakDurationForTier(comboStreak);
            return maxDur > 0f ? Mathf.Clamp01(streakTimer / maxDur) : 0f;
        }
    }
    public float Energy => (comboStreak >= 10) ? 1.0f : Mathf.Clamp01(currentEnergy);
    public bool IsAbilityReady => (comboStreak >= 10 || currentEnergy >= 1.0f) && !isAbilityActive;
    public bool IsAbilityActive => isAbilityActive;
    public float ActiveTimerRatio => abilityDuration > 0f ? Mathf.Clamp01(activeTimer / abilityDuration) : 0f;
    public float ActiveTimeRemaining => Mathf.Max(0f, activeTimer);

    private float GetStreakDurationForTier(int tier)
    {
        switch (tier)
        {
            case 1: return tier1Duration;
            case 2: return tier2Duration;
            case 3: return tier3Duration;
            case 4: return tier4Duration;
            case 5: return tier5Duration;
            case 6: return tier6Duration;
            case 7: return tier7Duration;
            case 8: return tier8Duration;
            case 9: return tier9Duration;
            default: return (tier >= 10) ? tier9Duration : tier1Duration;
        }
    }

    /// <summary>
    /// XP / Score Multiplier based on current Eating Combo Streak:
    /// 1x Streak = 1.0x
    /// 2x Streak = 1.2x
    /// 3x Streak = 1.4x
    /// 4x Streak = 1.6x
    /// 5x Streak = 1.8x
    /// 6x Streak = 2.0x
    /// 7x Streak = 2.2x
    /// 8x Streak = 2.4x
    /// 9x Streak = 2.6x
    /// 10x Streak = 3.0x (MAX FRENZY / ABILITY READY)
    /// </summary>
    public float CurrentMultiplier
    {
        get
        {
            switch (comboStreak)
            {
                case 2: return 1.2f;
                case 3: return 1.4f;
                case 4: return 1.6f;
                case 5: return 1.8f;
                case 6: return 2.0f;
                case 7: return 2.2f;
                case 8: return 2.4f;
                case 9: return 2.6f;
                case 10: return 3.0f;
                default: return (comboStreak > 10) ? 3.0f : 1.0f;
            }
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        playerController = GetComponent<PlayerController>();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        AudioSettingsManager.RouteToSfx(audioSource);
    }

    private void Start()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
            if (playerController == null && GameManager.instance?.playerGameObject != null)
            {
                playerController = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            }
        }
    }

    private void Update()
    {
        if (playerController == null)
        {
            if (GameManager.instance?.playerGameObject != null)
            {
                playerController = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            }
            if (playerController == null) return;
        }

        if (LevelManager.IsLevelCompleted || !playerController.IsAlive || playerController.IsHooked)
        {
            if (isAbilityActive)
            {
                EndAbility();
            }
            return;
        }

        // 1. Ability Active Countdown
        if (isAbilityActive)
        {
            activeTimer -= Time.deltaTime;
            if (activeTimer <= 0f)
            {
                EndAbility();
            }
        }
        else
        {
            // 2. Combo Streak Decay
            // When x10 is reached, the UI and 100% full circle stay PERSISTENT (no decay) until ability is used!
            if (comboStreak >= 10)
            {
                if (!wasReadyLastFrame)
                {
                    PlayAbilityReadySound();
                    wasReadyLastFrame = true;
                }
                if (GuiManager.instance != null)
                {
                    GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
                }
            }
            else if (streakTimer > 0f && comboStreak >= 1)
            {
                streakTimer -= Time.deltaTime;
                if (GuiManager.instance != null)
                {
                    GuiManager.instance.UpdateComboMultiplierProgress(StreakTimerRatio);
                }

                if (streakTimer <= 0f)
                {
                    comboStreak = 0;
                    currentEnergy = 0f;
                    wasReadyLastFrame = false;
                    if (GuiManager.instance != null)
                    {
                        GuiManager.instance.SetComboMultiplierStatus(false);
                    }
                }
            }

            // 3. Keyboard / Mouse Shortcuts to Activate Ability (RMB, E, or Q)
            CheckKeyboardActivation();
        }
    }

    private void CheckKeyboardActivation()
    {
        if (!IsAbilityReady) return;

        bool activatePressed = false;

        // Right Mouse Button
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            activatePressed = true;
        }

        // Keyboard E or Q
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.qKey.wasPressedThisFrame)
            {
                activatePressed = true;
            }
        }

        // Gamepad: Right Trigger (RT), X Button, or Right Shoulder (RB)
        if (Gamepad.current != null)
        {
            if (Gamepad.current.xButton.wasPressedThisFrame || 
                Gamepad.current.rightTrigger.wasPressedThisFrame || 
                Gamepad.current.rightShoulder.wasPressedThisFrame)
            {
                activatePressed = true;
            }
        }

        if (activatePressed)
        {
            ActivateAbility();
        }
    }

    /// <summary>
    /// Called when the player eats a fish. Steps up combo streak, resets decay timer, and fills energy.
    /// </summary>
    public void OnFishEaten(Fish fish)
    {
        // Guard: Do not build combo streak or energy while ability is active
        if (fish == null || isAbilityActive) return;

        // If x10 is already reached, keep x10 persistent and full until ability is used!
        if (comboStreak >= 10)
        {
            comboStreak = 10;
            currentEnergy = 1.0f;
            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetComboMultiplierStatus(true, 10, 1.0f);
                GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
            }
            return;
        }

        // Step up combo streak towards x10
        comboStreak = Mathf.Clamp(comboStreak + 1, 1, 10);

        if (comboStreak >= 10)
        {
            // Reached x10 Max Frenzy & Ability Ready!
            currentEnergy = 1.0f;
            streakTimer = 0f;
            PlayAbilityReadySound();
            wasReadyLastFrame = true;

            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetComboMultiplierStatus(true, 10, 1.0f);
                GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
            }
        }
        else
        {
            // Active countdown tier
            wasReadyLastFrame = false;
            streakTimer = GetStreakDurationForTier(comboStreak);
            currentEnergy = Mathf.Clamp01(comboStreak / 10.0f);

            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetComboMultiplierStatus(true, comboStreak, 1.0f);
            }
        }
    }

    /// <summary>
    /// Awards x5 combo streak boost when eating a Black Pearl.
    /// </summary>
    public void TriggerBlackPearlBonus()
    {
        if (isAbilityActive) return;

        // Black pearl grants x5 streak boost (setting to at least 5, or boosting existing streak by 5 up to max 10)
        comboStreak = Mathf.Clamp(Mathf.Max(comboStreak + 5, 5), 5, 10);
        currentEnergy = Mathf.Clamp01(comboStreak / 10.0f);

        if (comboStreak >= 10)
        {
            currentEnergy = 1.0f;
            streakTimer = 0f;
            PlayAbilityReadySound();
            wasReadyLastFrame = true;

            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetComboMultiplierStatus(true, 10, 1.0f);
                GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
            }
        }
        else
        {
            wasReadyLastFrame = false;
            streakTimer = GetStreakDurationForTier(comboStreak);
            if (GuiManager.instance != null)
            {
                GuiManager.instance.SetComboMultiplierStatus(true, comboStreak, 1.0f);
                GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
            }
        }
    }

    /// <summary>
    /// Backwards compatibility alias for Black Pearl.
    /// </summary>
    public void TriggerInstantMaxFrenzy()
    {
        TriggerBlackPearlBonus();
    }

    /// <summary>
    /// Immediately depletes streak count and combo gauge back to 0.
    /// Called when the player accidentally eats a sick fish.
    /// </summary>
    public void ResetStreak()
    {
        comboStreak = 0;
        streakTimer = 0f;
        currentEnergy = 0f;
        wasReadyLastFrame = false;

        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetComboMultiplierStatus(false);
            GuiManager.instance.UpdateComboMultiplierProgress(0f);
        }
    }

    /// <summary>
    /// Activates the special ability if energy is full.
    /// </summary>
    public bool ActivateAbility()
    {
        if (!IsAbilityReady || isAbilityActive || playerController == null || !playerController.IsAlive || playerController.IsHooked || LevelManager.IsLevelCompleted)
        {
            return false;
        }

        isAbilityActive = true;
        activeTimer = abilityDuration;
        currentEnergy = 0f;
        comboStreak = 0; // Reset streak so it starts fresh after ability!
        streakTimer = 0f;
        wasReadyLastFrame = false;

        // Hide combo multiplier status during ability
        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetComboMultiplierStatus(false);
        }

        // Camera shake impact
        if (GameManager.instance != null)
        {
            GameManager.instance.CameraShake(0.35f, 6.0f, 2.0f);
        }

        bool isRiverLevel = LevelManager.IsCurrentLakeLevel;

        if (activeAbilityRoutine != null)
        {
            StopCoroutine(activeAbilityRoutine);
        }

        if (isRiverLevel)
        {
            activeAbilityRoutine = StartCoroutine(RiverBlitzRoutine());
        }
        else
        {
            activeAbilityRoutine = StartCoroutine(OceanVortexRoutine());
        }

        return true;
    }

    /// <summary>
    /// Ocean Fish Ability: "Inhale" (Feeding Frenzy 2 style - Boris / Goliath)
    /// Inhales fish in a forward cone in front of the player's mouth.
    /// Player can steer and move freely while sucking in schools of small prey.
    /// </summary>
    private IEnumerator OceanVortexRoutine()
    {
        if (playerController == null) yield break;

        // Sound effect (Plays Vortex_Suction.wav for duration of ability)
        AudioClip vClip = GetVortexSound();
        if (audioSource != null && vClip != null && AudioSettingsManager.IsSfxEnabled)
        {
            audioSource.clip = vClip;
            audioSource.loop = false;
            audioSource.volume = 0.95f;
            audioSource.Play();
        }

        // Keep player mouth wide open during Inhale
        playerController.SetMouthOpen(true);

        // Start Suction VFX animation in front of mouth
        EnsureSuctionVFX();
        if (suctionVFX != null)
        {
            suctionVFX.StartSuction();
        }

        // Inhale suction cone loop
        while (activeTimer > 0f && isAbilityActive && playerController.IsAlive && !playerController.IsHooked && !LevelManager.IsLevelCompleted)
        {
            playerController.SetMouthOpen(true); // Always keep mouth open while suction is active
            Vector3 mouthPos = playerController.GetMouthPosition();
            // Forward direction determined by player localScale.x (positive = facing right, negative = facing left)
            Vector2 forwardDir = (playerController.transform.localScale.x >= 0f) ? Vector2.right : Vector2.left;

            if (Fish.AllFish != null)
            {
                for (int i = 0; i < Fish.AllFish.Count; i++)
                {
                    Fish targetFish = Fish.AllFish[i];
                    if (targetFish == null || !targetFish.gameObject.activeInHierarchy || targetFish.IsDead) continue;
                    if (targetFish.Level > playerController.Level || targetFish.IsHooked) continue;

                    Vector3 fishPos = targetFish.transform.position;
                    Vector2 toFish = (Vector2)fishPos - (Vector2)mouthPos;
                    float distToMouth = toFish.magnitude;

                    // Cone check: Inhale range 10.0 units, forward cone angle ~55 degrees (dot product >= 0.55)
                    if (distToMouth <= 10.0f)
                    {
                        Vector2 dirToFish = toFish.normalized;
                        float dot = Vector2.Dot(forwardDir, dirToFish);

                        // If fish is inside the forward cone in front of mouth
                        if (dot >= 0.50f)
                        {
                            // Inhale suction speed increases as fish gets closer
                            float pullSpeed = Mathf.Lerp(16f, 8f, distToMouth / 10.0f);
                            Vector3 newPos = Vector3.MoveTowards(fishPos, mouthPos, pullSpeed * Time.deltaTime);
                            targetFish.transform.position = newPos;

                            // Check if reached mouth
                            if (distToMouth < 0.75f)
                            {
                                playerController.EatFromAbility(targetFish);
                            }
                        }
                    }
                }
            }

            // Pearl Suction: Vacuum in pearls from open clams
            if (Clam.AllClams != null)
            {
                for (int c = 0; c < Clam.AllClams.Count; c++)
                {
                    Clam targetClam = Clam.AllClams[c];
                    if (targetClam == null || !targetClam.gameObject.activeInHierarchy || !targetClam.CanEatPearl || targetClam.ClamPearl == null) continue;

                    Vector3 pearlPos = targetClam.ClamPearl.transform.position;
                    Vector2 toPearl = (Vector2)pearlPos - (Vector2)mouthPos;
                    float distToMouth = toPearl.magnitude;

                    // Cone check: Inhale range 10.0 units, forward cone angle ~55 degrees (dot product >= 0.50)
                    if (distToMouth <= 10.0f)
                    {
                        Vector2 dirToPearl = toPearl.normalized;
                        float dot = Vector2.Dot(forwardDir, dirToPearl);

                        if (dot >= 0.50f)
                        {
                            targetClam.PreparePearlForSuction();
                            float pullSpeed = Mathf.Lerp(16f, 8f, distToMouth / 10.0f);
                            Vector3 newPos = Vector3.MoveTowards(pearlPos, mouthPos, pullSpeed * Time.deltaTime);
                            targetClam.ClamPearl.transform.position = newPos;

                            // Check if reached mouth
                            if (distToMouth < 0.75f)
                            {
                                targetClam.OnPearlEaten(playerController);
                            }
                        }
                    }
                }
            }

            yield return null;
        }

        EndAbility();
    }

    /// <summary>
    /// River Fish Ability: "Feeding Fury / Apex Blitz"
    /// Upon activation, all edible fish on-screen are immobilized.
    /// The player quickly dashes through the screen, consumes all edible targets in rapid succession,
    /// and then dashes smoothly back to the original spot where the ability was activated!
    /// </summary>
    private IEnumerator RiverBlitzRoutine()
    {
        if (playerController == null) yield break;

        // 1. Save original spot to return to after the frenzy dash
        Vector3 originalSpot = playerController.transform.position;

        AudioClip blitzClip = GetBlitzSound();

        Camera cam = Camera.main;

        // 2. Gather and temporarily immobilize all visible on-screen edible targets
        List<Fish> targetList = GetAllVisibleEdibleFish(cam);

        // Immobilize targets so they stay frozen in place for the blitz dash
        for (int i = 0; i < targetList.Count; i++)
        {
            if (targetList[i] != null && targetList[i].GetComponent<Rigidbody2D>() != null)
            {
                targetList[i].GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            }
        }

        float dashSpeed = 32.0f;

        // 3. Fast zig-zag blitz dash through each target
        while (isAbilityActive && playerController.IsAlive && !playerController.IsHooked && !LevelManager.IsLevelCompleted)
        {
            // Clean dead, inactive, or null references from target list
            targetList.RemoveAll(f => f == null || !f.gameObject.activeInHierarchy || f.IsDead);

            if (targetList.Count == 0)
            {
                break; // All targets consumed or none visible!
            }

            // Pick nearest target from current position for optimal smooth pathing
            Fish nextTarget = GetNearestTarget(targetList, playerController.transform.position);
            targetList.Remove(nextTarget);

            if (nextTarget == null || !nextTarget.gameObject.activeInHierarchy || nextTarget.IsDead)
            {
                yield return null;
                continue;
            }

            // Play swoosh sound on each dash toward a new fish position
            if (blitzClip != null && AudioSettingsManager.IsSfxEnabled)
            {
                float pitch = Random.Range(0.96f, 1.08f);
                SFXPool.Play2D(blitzClip, 0.85f, pitch);
            }

            // Dash towards target with a 0.4s maximum safety timeout per target
            float targetTimer = 0.4f;
            while (nextTarget != null && nextTarget.gameObject.activeInHierarchy && !nextTarget.IsDead && isAbilityActive && !playerController.IsHooked && targetTimer > 0f)
            {
                targetTimer -= Time.deltaTime;
                Vector3 targetPos = nextTarget.transform.position;
                Vector3 currentPos = playerController.transform.position;
                float dist = Vector2.Distance(currentPos, targetPos);

                // Face direction of travel
                if (Mathf.Abs(targetPos.x - currentPos.x) > 0.05f)
                {
                    playerController.SetFacingDirection(targetPos.x > currentPos.x);
                }

                playerController.transform.position = Vector3.MoveTowards(currentPos, targetPos, dashSpeed * Time.deltaTime);

                if (dist < 0.75f)
                {
                    playerController.EatFromAbility(nextTarget);
                    playerController.PlayEatEffect();
                    if (GameManager.instance != null)
                    {
                        GameManager.instance.CameraShake(0.08f, 2.5f, 1.0f);
                    }
                    break;
                }

                yield return null;
            }

            yield return null;
        }

        // 4. Return to the original spot where the ability was activated (with 0.6s safety timeout)
        if (blitzClip != null && AudioSettingsManager.IsSfxEnabled)
        {
            SFXPool.Play2D(blitzClip, 0.85f, 1.05f);
        }

        float returnTimer = 0.6f;
        while (isAbilityActive && playerController.IsAlive && !playerController.IsHooked && !LevelManager.IsLevelCompleted && returnTimer > 0f)
        {
            returnTimer -= Time.deltaTime;
            Vector3 currentPos = playerController.transform.position;
            float distToOrigin = Vector2.Distance(currentPos, originalSpot);

            if (distToOrigin <= 0.35f)
            {
                playerController.transform.position = originalSpot;
                break;
            }

            if (Mathf.Abs(originalSpot.x - currentPos.x) > 0.05f)
            {
                playerController.SetFacingDirection(originalSpot.x > currentPos.x);
            }

            playerController.transform.position = Vector3.MoveTowards(currentPos, originalSpot, 32.0f * Time.deltaTime);
            yield return null;
        }

        // Brief delay before restoring control
        yield return new WaitForSeconds(0.1f);

        EndAbility();
    }

    private List<Fish> GetAllVisibleEdibleFish(Camera cam)
    {
        List<Fish> visibleList = new List<Fish>();
        if (Fish.AllFish == null || playerController == null) return visibleList;

        for (int i = 0; i < Fish.AllFish.Count; i++)
        {
            Fish f = Fish.AllFish[i];
            if (f == null || !f.gameObject.activeInHierarchy || f.IsDead) continue;
            if (f.Level > playerController.Level || f.IsSickFish || f.IsHooked) continue;

            if (cam != null)
            {
                Vector3 viewPos = cam.WorldToViewportPoint(f.transform.position);
                if (viewPos.x < 0.05f || viewPos.x > 0.95f || viewPos.y < 0.05f || viewPos.y > 0.95f)
                {
                    continue; // Skip off-screen fish
                }
            }

            visibleList.Add(f);
        }

        return visibleList;
    }

    private Fish GetNearestTarget(List<Fish> list, Vector3 fromPos)
    {
        Fish best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            Fish f = list[i];
            if (f == null || !f.gameObject.activeInHierarchy || f.IsDead) continue;
            float d = Vector2.Distance(fromPos, f.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = f;
            }
        }
        return best;
    }

    /// <summary>
    /// Ends the active ability and restores normal game physics & mouth sprites.
    /// </summary>
    public void EndAbility()
    {
        if (!isAbilityActive) return;

        isAbilityActive = false;
        activeTimer = 0f;
        comboStreak = 0;
        streakTimer = 0f;
        currentEnergy = 0f;
        wasReadyLastFrame = false;

        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetComboMultiplierStatus(false);
        }

        if (activeAbilityRoutine != null)
        {
            StopCoroutine(activeAbilityRoutine);
            activeAbilityRoutine = null;
        }

        if (playerController != null)
        {
            playerController.SetMouthOpen(false);
        }

        if (suctionVFX != null)
        {
            suctionVFX.StopSuction();
        }

        if (audioSource != null && (audioSource.isPlaying || audioSource.loop) && audioSource.clip != null && (audioSource.clip.name.Contains("Vortex") || audioSource.clip.name.Contains("vortex")))
        {
            audioSource.Stop();
            audioSource.loop = false;
        }
    }

    private static AudioClip s_CachedVortexClip;
    public AudioClip GetVortexSound()
    {
        if (vortexSound != null) return vortexSound;
        if (s_CachedVortexClip != null) return s_CachedVortexClip;

#if UNITY_EDITOR
        s_CachedVortexClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Vortex_Suction.mp3");
        if (s_CachedVortexClip != null) return s_CachedVortexClip;
#endif

        s_CachedVortexClip = Resources.Load<AudioClip>("Vortex_Suction");
        if (s_CachedVortexClip != null) return s_CachedVortexClip;

        AudioClip[] all = Resources.FindObjectsOfTypeAll<AudioClip>();
        foreach (var c in all)
        {
            if (c != null && (c.name.Contains("Vortex") || c.name.Contains("vortex")))
            {
                s_CachedVortexClip = c;
                return s_CachedVortexClip;
            }
        }
        return null;
    }

    private static AudioClip s_CachedBlitzClip;
    public AudioClip GetBlitzSound()
    {
        if (blitzSound != null) return blitzSound;
        if (s_CachedBlitzClip != null) return s_CachedBlitzClip;

#if UNITY_EDITOR
        s_CachedBlitzClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/speedStart.ogg");
        if (s_CachedBlitzClip != null) return s_CachedBlitzClip;
#endif

        s_CachedBlitzClip = Resources.Load<AudioClip>("speedStart");
        if (s_CachedBlitzClip != null) return s_CachedBlitzClip;

        AudioClip[] all = Resources.FindObjectsOfTypeAll<AudioClip>();
        foreach (var c in all)
        {
            if (c != null && (c.name.Contains("speedStart") || c.name.Contains("SpeedStart")))
            {
                s_CachedBlitzClip = c;
                return s_CachedBlitzClip;
            }
        }
        return null;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null && AudioSettingsManager.IsSfxEnabled)
        {
            audioSource.PlayOneShot(clip, 1.0f);
        }
    }

    private static AudioClip s_CachedAbilityReadyClip;
    public AudioClip GetAbilityReadySound()
    {
        if (abilityReadySound != null) return abilityReadySound;
        if (s_CachedAbilityReadyClip != null) return s_CachedAbilityReadyClip;

#if UNITY_EDITOR
        s_CachedAbilityReadyClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Deadly_Frenzy.mp3");
        if (s_CachedAbilityReadyClip != null) return s_CachedAbilityReadyClip;
#endif
        s_CachedAbilityReadyClip = Resources.Load<AudioClip>("Deadly_Frenzy");
        if (s_CachedAbilityReadyClip != null) return s_CachedAbilityReadyClip;
        s_CachedAbilityReadyClip = Resources.Load<AudioClip>("Max_Streak_Frenzy");
        if (s_CachedAbilityReadyClip != null) return s_CachedAbilityReadyClip;
        s_CachedAbilityReadyClip = Resources.Load<AudioClip>("powerup_ready");
        return s_CachedAbilityReadyClip;
    }

    private void PlayAbilityReadySound()
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;

        AudioClip clip = GetAbilityReadySound();

        if (clip != null)
        {
            // Play 2D direct unspatialized sound (connected to user/UI/camera, unaffected by player distance)
            if (GuiManager.instance != null)
            {
                GuiManager.instance.PlayUiSound(clip, 1.0f);
            }
            else if (audioSource != null)
            {
                audioSource.spatialBlend = 0f;
                audioSource.PlayOneShot(clip, 1.0f);
            }
        }
    }

    #region Cheat / Testing Helpers

    public void CheatSetMaxStreak()
    {
        comboStreak = 10;
        currentEnergy = 1.0f;
        streakTimer = 0f;
        PlayAbilityReadySound();
        if (GuiManager.instance != null)
        {
            GuiManager.instance.SetComboMultiplierStatus(true, 10, 1.0f);
            GuiManager.instance.UpdateComboMultiplierProgress(1.0f);
        }
    }

    public void CheatTriggerAbility()
    {
        ActivateAbility();
    }

    #endregion

    private void OnDestroy()
    {
        if (suctionVFX != null)
        {
            Destroy(suctionVFX.gameObject);
        }
        if (Instance == this) Instance = null;
    }
}
