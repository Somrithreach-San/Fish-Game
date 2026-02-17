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

    private void Start()
    {
        EventManager.StartListening("GameStart", SpawnPlayer);
        
    }

    public void SpawnPlayer()
    {
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
            // trigger spawn so other systems (camera) can follow immediately
            EventManager.Trigger<GameObject>("PlayerSpawn", player);
            PlaySpawnSound(player);
            PlayBubble(player);
            
            // Enable speed particles during drop
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.PlaySpeedEffect();

            // animate drop
            StartCoroutine(DropToPosition(player, targetPos, dropDuration));
        }
        else
        {
            // reuse existing player: enable, reposition above screen, then drop
            player.SetActive(true);
            player.transform.position = spawnPos;
            EventManager.Trigger<GameObject>("PlayerSpawn", player);
            PlaySpawnSound(player);
            PlayBubble(player);

            // Enable speed particles during drop
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.PlaySpeedEffect();

            StartCoroutine(DropToPosition(player, targetPos, dropDuration));
        }
    }

    private void PlaySpawnSound(GameObject p)
    {
        if (spawnSound == null || p == null) return;
        AudioSource src = p.GetComponent<AudioSource>();
        if (src == null)
        {
            src = p.AddComponent<AudioSource>();
            src.playOnAwake = false;
        }
        src.PlayOneShot(spawnSound, 1.0f);
    }

    private void PlayBubble(GameObject p)
    {
        if (bubbleClips == null || bubbleClips.Length == 0 || p == null) return;
        AudioSource src = p.GetComponent<AudioSource>();
        if (src == null)
        {
            src = p.AddComponent<AudioSource>();
            src.playOnAwake = false;
        }
        
        AudioClip clip = bubbleClips[Random.Range(0, bubbleClips.Length)];
        if (clip != null)
        {
            src.PlayOneShot(clip, bubbleVolume);
        }
    }

    private System.Collections.IEnumerator DropToPosition(GameObject obj, Vector3 target, float duration)
    {
        if (obj == null) yield break;
        float elapsed = 0f;
        Vector3 start = obj.transform.position;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // smoothstep easing for nicer drop
            float ease = Mathf.SmoothStep(0f, 1f, t);
            obj.transform.position = Vector3.Lerp(start, target, ease);
            yield return null;
        }
        obj.transform.position = target;
        
        // Stop speed particles after drop
        PlayerController pc = obj.GetComponent<PlayerController>();
        if (pc != null) pc.StopSpeedEffect();
    }

}
