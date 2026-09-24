using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Rhinotap.Toolkit;

public class PlayerSpawn : MonoBehaviour
{
    [Header("Current Player Prefab")]
    [SerializeField]
    private GameObject PlayerPrefab;

    [Header("Spawn Drop Settings")]
    [Tooltip("How far above the top of the viewport to spawn (in world units). This value is added to the world Y coordinate at the top of the camera view.")]
    [SerializeField]
    private float spawnYOffset = 10f; // Increased from 2.5f for "Feeding Frenzy" style drop

    [Tooltip("Duration of the drop animation in seconds")]
    [SerializeField]
    private float dropDuration = 2.5f; // Increased from 1.2f for slower, smoother drop

    [Header("Audio")]
    [Tooltip("Sound to play when player spawns (e.g. Splash or Intro)")]
    [SerializeField]
    private AudioClip spawnSound;

    [Tooltip("Bubble sounds to play when player drops in")]
    [SerializeField]
    private AudioClip[] bubbleClips;
    [Range(0f,1f)]
    [SerializeField]
    private float bubbleVolume = 0.6f;


    private GameObject player;
    private bool hasSpawned = false;
    private Coroutine activeDropCoroutine = null;

    private void Awake()
    {
        EventManager.StartListening("GameStart", OnGameStartEvent);
        EventManager.StartListening("playerDeath", OnPlayerDeathEvent);
    }

    private void OnDestroy()
    {
        EventManager.StopListening("GameStart", OnGameStartEvent);
        EventManager.StopListening("playerDeath", OnPlayerDeathEvent);
    }

    private void OnPlayerDeathEvent()
    {
        hasSpawned = false;
        if (activeDropCoroutine != null)
        {
            StopCoroutine(activeDropCoroutine);
            activeDropCoroutine = null;
        }
    }

    private void OnGameStartEvent()
    {
        if (hasSpawned) return;
        SpawnPlayer();
    }

    private void Start()
    {
        // Fallback: only spawn if GameStart event was not already triggered
        if (!hasSpawned && player == null)
        {
            SpawnPlayer();
        }
    }

    public void SpawnPlayer()
    {
        // Prevent duplicate execution if player is already spawned and active
        if (hasSpawned && player != null && player.activeInHierarchy)
        {
            return;
        }

        hasSpawned = true;

        if (activeDropCoroutine != null)
        {
            StopCoroutine(activeDropCoroutine);
            activeDropCoroutine = null;
        }

        // Prevent duplicate player instances
        PlayerController[] existingPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        if (existingPlayers != null && existingPlayers.Length > 0)
        {
            player = existingPlayers[0].gameObject;
            // Clean up any extra player fish if somehow duplicated
            for (int i = 1; i < existingPlayers.Length; i++)
            {
                if (existingPlayers[i] != null)
                {
                    Destroy(existingPlayers[i].gameObject);
                }
            }
        }

        // Determine spawn target (center of the viewport)
        Camera cam = Camera.main;
        Vector3 targetPos = transform.position;
        if (cam != null)
        {
            float camZ = Mathf.Abs(cam.transform.position.z);
            Vector3 centerViewport = new Vector3(0.5f, 0.5f, camZ);
            targetPos = cam.ViewportToWorldPoint(centerViewport);
            targetPos.z = transform.position.z;
        }
        Vector3 spawnPos = targetPos;
        if (cam != null)
        {
            float camZ = Mathf.Abs(cam.transform.position.z);
            // compute world position at the top of the viewport (y == 1)
            Vector3 topWorld = cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, camZ));
            // move further up by spawnYOffset (in world units) so the player spawns off-screen
            spawnPos = new Vector3(targetPos.x, topWorld.y + spawnYOffset, targetPos.z);
        }

        if (player == null)
        {
            player = Instantiate(PlayerPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // reuse existing player: enable, reposition above screen, then drop
            player.SetActive(true);
            Transform gfx = player.transform.Find("PlayerGraphics");
            if (gfx != null) gfx.gameObject.SetActive(true);
            player.transform.position = spawnPos;
        }

        // trigger spawn so other systems (camera) can follow immediately
        EventManager.Trigger<GameObject>("PlayerSpawn", player);
        if (GameManager.instance != null)
        {
            GameManager.instance.SetCameraFollow(player.transform);
        }
        PlaySpawnSound(player);
        PlayBubble(player);
        
        // Enable speed particles during drop
        PlayerController pc = player.GetComponent<PlayerController>();
        if (pc != null) pc.PlaySpeedEffect();

        // animate drop
        activeDropCoroutine = StartCoroutine(DropToPosition(player, targetPos, dropDuration));
    }

    private void PlaySpawnSound(GameObject p)
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
        if (spawnSound == null || p == null) return;
        AudioSource src = p.GetComponent<AudioSource>();
        if (src == null)
        {
            src = p.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
        }
        AudioSettingsManager.RouteToSfx(src);
        src.PlayOneShot(spawnSound, 1.0f);
    }

    private void PlayBubble(GameObject p)
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
        if (bubbleClips == null || bubbleClips.Length == 0 || p == null) return;
        AudioSource src = p.GetComponent<AudioSource>();
        if (src == null)
        {
            src = p.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
        }
        AudioSettingsManager.RouteToSfx(src);
        
        AudioClip clip = bubbleClips[Random.Range(0, bubbleClips.Length)];
        if (clip != null)
        {
            src.PlayOneShot(clip, bubbleVolume);
        }
    }

    private System.Collections.IEnumerator DropToPosition(GameObject obj, Vector3 target, float duration)
    {
        if (obj == null) yield break;
        PlayerController pc = obj.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.IsDropping = true;
        }

        float elapsed = 0f;
        Vector3 start = obj.transform.position;
        while (elapsed < duration)
        {
            if (obj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // smoothstep easing for nicer drop
            float ease = Mathf.SmoothStep(0f, 1f, t);
            obj.transform.position = Vector3.Lerp(start, target, ease);
            yield return null;
        }
        if (obj != null)
        {
            obj.transform.position = target;
            
            // Stop speed particles and unlock control after drop
            if (pc != null)
            {
                pc.SetTargetPosition(target);
                pc.IsDropping = false;
                pc.StopSpeedEffect();
            }
        }
        activeDropCoroutine = null;
    }

}
