using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// Cuttlefish creature for Ocean levels.
/// Scans for larger predators in its forward cone.
/// If threatened from the front, sprays an ink cloud and jet-boosts away in reverse.
/// Can be snuck up upon and eaten from behind, above, or below!
/// </summary>
[RequireComponent(typeof(Fish))]
public class Cuttlefish : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("Maximum distance in front of the cuttlefish where threats are detected (~4.5 units)")]
    [SerializeField] private float visionRange = 4.5f;
    [Tooltip("Forward cone alignment threshold (0.45 = ~63° focused cone in front of tentacles)")]
    [SerializeField] private float visionDotThreshold = 0.45f;
    [SerializeField] private float inkCooldownDuration = 8.0f; // Cooldown before spraying ink again
    [SerializeField] private float jetEscapeSpeed = 10.5f;

    [Header("Effects & Audio")]
    [SerializeField] private AudioClip inkSpraySound;
    [SerializeField] private GameObject inkCloudPrefab;

    private Fish fish;
    private Rigidbody2D rb;
    private FishAI ai;
    private float inkCooldownTimer = 0f;
    private bool isJetting = false;
    private float jetTimer = 0f;
    private Vector2 jetDirection = Vector2.zero;

    private Material bubbleMat;
    private Texture2D bubbleTex;

    private void Awake()
    {
        fish = GetComponent<Fish>();
        rb = GetComponent<Rigidbody2D>();
        ai = GetComponent<FishAI>();

        LoadDefaultAudio();
    }

    private void LoadDefaultAudio()
    {
#if UNITY_EDITOR
        if (inkSpraySound == null)
        {
            inkSpraySound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Cuttle_Fish_Shooting_Ink.mp3") ??
                            UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Blitz_Swoosh.mp3");
        }
#endif
        if (inkSpraySound == null)
        {
            inkSpraySound = Resources.Load<AudioClip>("Cuttle_Fish_Shooting_Ink") ?? Resources.Load<AudioClip>("Blitz_Swoosh");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        LoadDefaultAudio();
    }
#endif

    public void Initialize(Material mat, Texture2D tex)
    {
        bubbleMat = mat;
        bubbleTex = tex;
    }

    private PlayerController cachedPlayer;

    private PlayerController GetPlayer()
    {
        if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy) return cachedPlayer;
        if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
        {
            cachedPlayer = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            if (cachedPlayer != null) return cachedPlayer;
        }
        cachedPlayer = Object.FindFirstObjectByType<PlayerController>();
        return cachedPlayer;
    }

    private void OnEnable()
    {
        inkCooldownTimer = 0f; // Immediately vigilant upon spawn
        isJetting = false;
        jetTimer = 0f;
    }

    private void Update()
    {
        if (fish != null && fish.IsDead) return;

        if (inkCooldownTimer > 0f)
        {
            inkCooldownTimer -= Time.deltaTime;
        }

        if (isJetting)
        {
            jetTimer -= Time.deltaTime;
            if (jetTimer <= 0f)
            {
                isJetting = false;
                if (ai != null) ai.enabled = true;
            }
            else if (rb != null)
            {
                rb.linearVelocity = jetDirection * jetEscapeSpeed * (jetTimer / 0.85f);
            }
            return;
        }

        if (inkCooldownTimer <= 0f)
        {
            CheckForThreatsInFront();
        }
    }

    /// <summary>
    /// Checks if any predator or player is approaching inside the cuttlefish's forward field of view.
    /// Strict front-cone only: It NEVER shoots ink if the threat is behind it (dot <= 0).
    /// </summary>
    private void CheckForThreatsInFront()
    {
        if (inkCooldownTimer > 0f || isJetting) return;

        float facing = (fish != null) ? (fish.IsFacingRight ? 1f : -1f) : Mathf.Sign(transform.localScale.x);
        Vector2 forward = new Vector2(facing, 0f);
        Vector2 myPos = transform.position;

        // 1. Check Player (Always defends against player approaching in front cone, regardless of player level)
        PlayerController player = GetPlayer();
        if (player != null && player.IsAlive)
        {
            Vector2 toPlayer = (Vector2)player.transform.position - myPos;
            float dist = toPlayer.magnitude;

            if (dist <= visionRange && dist > 0.05f)
            {
                Vector2 dirToPlayer = toPlayer / dist;
                float dot = Vector2.Dot(forward, dirToPlayer);

                // STRICT FORWARD CONE:
                // dot > visionDotThreshold (0.15f) means threat is within ~81° in front of the head/tentacles.
                // If dot <= 0, the threat is BEHIND the cuttlefish and CANNOT be seen!
                if (dot > visionDotThreshold)
                {
                    TriggerInkAndJetEscape(dirToPlayer, targetPlayer: player);
                    return;
                }
            }
        }

        // 2. Check Larger AI Fish Predators in front (Level >= 2 only, ignores Level 1 schooling fish)
        if (Fish.AllFish != null)
        {
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish other = Fish.AllFish[i];
                if (other == null || other == fish || other.IsDead || other.IsCuttlefish) continue;
                if (other.Level < 2 || other.IsGoldenFish) continue;

                Vector2 toOther = (Vector2)other.transform.position - myPos;
                float dist = toOther.magnitude;

                if (dist <= visionRange && dist > 0.05f)
                {
                    Vector2 dirToOther = toOther / dist;
                    float dot = Vector2.Dot(forward, dirToOther);

                    if (dot > visionDotThreshold)
                    {
                        TriggerInkAndJetEscape(dirToOther, targetFish: other);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sprays blinding ink into the water and propels backward at high speed.
    /// Only triggers if the threat is in front of the cuttlefish.
    /// </summary>
    public void TriggerInkAndJetEscape(Vector2 threatDir, PlayerController targetPlayer = null, Fish targetFish = null)
    {
        if (inkCooldownTimer > 0f || isJetting) return; // Prevent firing during cooldown or active jet

        float facing = (fish != null) ? (fish.IsFacingRight ? 1f : -1f) : Mathf.Sign(transform.localScale.x);
        Vector2 forward = new Vector2(facing, 0f);

        // Never shoot ink if threat is behind the cuttlefish!
        if (threatDir.sqrMagnitude > 0.001f)
        {
            float dot = Vector2.Dot(forward, threatDir.normalized);
            if (dot <= 0f)
            {
                // Threat is behind, do not spray ink
                return;
            }
        }

        inkCooldownTimer = inkCooldownDuration;

        Vector3 siphonPos = transform.position + new Vector3(facing * 0.45f, -0.05f, 0f);

        // Spawn Ink Cloud targeting exclusively the threat it was shot at
        GameObject cloudObj = new GameObject("CuttlefishInkCloud");
        cloudObj.transform.position = siphonPos;
        InkCloud cloud = cloudObj.AddComponent<InkCloud>();
        Vector2 sprayDir = (threatDir.sqrMagnitude > 0.001f) ? threatDir.normalized : new Vector2(facing, 0f);
        cloud.Initialize(bubbleMat, bubbleTex, inkSpraySound, sprayDir, targetPlayer, targetFish);

        // Jet escape in reverse away from threat
        if (threatDir.sqrMagnitude < 0.001f) threatDir = new Vector2(facing, 0f);
        jetDirection = new Vector2(-Mathf.Sign(threatDir.x != 0f ? threatDir.x : facing), Random.Range(-0.35f, 0.35f)).normalized;
        isJetting = true;
        jetTimer = 0.85f;

        if (ai != null) ai.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = jetDirection * jetEscapeSpeed;
        }

        StartCoroutine(JetSquidAnimation());
    }

    private IEnumerator JetSquidAnimation()
    {
        Transform gfx = (fish != null && fish.GfxTransform != null) ? fish.GfxTransform : transform;
        Vector3 baseScale = gfx.localScale;
        float elapsed = 0f;
        float duration = 0.40f;

        // Stretch back during jet burst
        while (elapsed < duration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration * 0.5f);
            gfx.localScale = new Vector3(baseScale.x * Mathf.Lerp(1f, 1.25f, t), baseScale.y * Mathf.Lerp(1f, 0.75f, t), baseScale.z);
            yield return null;
        }

        // Return to normal
        elapsed = 0f;
        while (elapsed < duration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration * 0.5f);
            gfx.localScale = new Vector3(baseScale.x * Mathf.Lerp(1.25f, 1f, t), baseScale.y * Mathf.Lerp(0.75f, 1f, t), baseScale.z);
            yield return null;
        }
        gfx.localScale = baseScale;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.5f);
        float facing = (fish != null) ? (fish.IsFacingRight ? 1f : -1f) : Mathf.Sign(transform.localScale.x);
        Vector3 origin = transform.position;
        Vector3 fwd = new Vector3(facing, 0f, 0f);

        float halfAngleDeg = Mathf.Acos(Mathf.Clamp01(visionDotThreshold)) * Mathf.Rad2Deg;
        Vector3 leftRay = Quaternion.Euler(0, 0, halfAngleDeg) * fwd * visionRange;
        Vector3 rightRay = Quaternion.Euler(0, 0, -halfAngleDeg) * fwd * visionRange;

        Gizmos.DrawLine(origin, origin + leftRay);
        Gizmos.DrawLine(origin, origin + rightRay);
        Gizmos.DrawLine(origin + leftRay, origin + fwd * visionRange);
        Gizmos.DrawLine(origin + rightRay, origin + fwd * visionRange);
    }
#endif
}
