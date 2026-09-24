using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// Lingering dark ink cloud emitted by the Cuttlefish when startled by a predator in front of it.
/// Shoots forward through the water towards the threat, then floats upward and slowly dissolves.
/// Obscures player visibility or disorients AI fishes only when they visually and physically touch the ink cloud.
/// </summary>
public class InkCloud : MonoBehaviour
{
    private ParticleSystem inkParticles;
    private float lifeTimer = 0f;
    private const float CLOUD_LIFETIME = 5.5f;

    private Vector2 sprayDirection = Vector2.right;
    private PlayerController targetPlayer;
    private Fish targetFish;

    private bool playerInked = false;
    private HashSet<Fish> inkedFishSet = new HashSet<Fish>();

    private const float FORWARD_SHOT_DISTANCE = 3.2f;
    private const float FORWARD_SHOT_DURATION = 0.55f;

    public void Initialize(Material bubbleMat, Texture2D bubbleTex, AudioClip inkAudio = null, Vector2 sprayDir = default, PlayerController player = null, Fish fish = null)
    {
        lifeTimer = 0f;
        targetPlayer = player;
        targetFish = fish;

        if (sprayDir.sqrMagnitude > 0.001f)
        {
            sprayDirection = sprayDir.normalized;
        }
        else
        {
            sprayDirection = Vector2.right;
        }

        transform.rotation = Quaternion.identity;

        SetupInkParticles(bubbleMat, bubbleTex);

        if (inkParticles != null)
        {
            inkParticles.Play();
        }

        if (inkAudio != null && AudioSettingsManager.IsSfxEnabled)
        {
            AudioSource audio = gameObject.AddComponent<AudioSource>();
            audio.spatialBlend = 1.0f;
            audio.minDistance = 5f;
            audio.maxDistance = 35f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.volume = 0.45f;
            AudioSettingsManager.RouteToSfx(audio);
            audio.PlayOneShot(inkAudio, 0.45f);
        }
    }

    private void SetupInkParticles(Material mat, Texture2D tex)
    {
        if (inkParticles != null) return;

        inkParticles = GetComponent<ParticleSystem>();
        if (inkParticles == null)
        {
            inkParticles = gameObject.AddComponent<ParticleSystem>();
        }

        inkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = inkParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 4.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.25f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.06f, 0.04f, 0.10f, 0.88f),
            new Color(0.12f, 0.08f, 0.18f, 0.75f)
        );
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = -0.055f;
        main.maxParticles = 55;

        var emission = inkParticles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 38)
        });

        var shape = inkParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.32f;

        var sizeOverLifetime = inkParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.6f);
        sizeCurve.AddKey(0.35f, 1.0f);
        sizeCurve.AddKey(1f, 1.25f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = inkParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(new Color(0.07f, 0.05f, 0.12f), 0f), 
                new GradientColorKey(new Color(0.04f, 0.03f, 0.08f), 1f) 
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(0f, 0f), 
                new GradientAlphaKey(0.85f, 0.08f), 
                new GradientAlphaKey(0.75f, 0.40f), 
                new GradientAlphaKey(0.35f, 0.70f), 
                new GradientAlphaKey(0f, 1f) 
            }
        );
        colorOverLifetime.color = grad;

        var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
        if (mat != null) renderer.material = mat;
        else
        {
            Material m = new Material(Shader.Find("Sprites/Default"));
            if (tex != null) m.mainTexture = tex;
            renderer.material = m;
        }
        renderer.sortingLayerName = "ParallaxForeground";
        renderer.sortingOrder = 92;
    }

    private void Update()
    {
        if (GameManager.instance != null && GameManager.Paused) return;

        lifeTimer += Time.deltaTime;

        if (lifeTimer >= CLOUD_LIFETIME)
        {
            Destroy(gameObject);
            return;
        }

        // Forward shot physics: glides smoothly forward in the spray direction
        if (lifeTimer < FORWARD_SHOT_DURATION)
        {
            float t = lifeTimer / FORWARD_SHOT_DURATION;
            float speed = Mathf.Lerp(FORWARD_SHOT_DISTANCE / (FORWARD_SHOT_DURATION * 0.5f), 0f, t);
            transform.position += (Vector3)(sprayDirection * speed * Time.deltaTime);
        }

        // Active ink cloud contact detection while cloud is dense (first 3.5s)
        if (lifeTimer <= 3.5f)
        {
            UpdateInkContactDetection();
        }
    }

    private void UpdateInkContactDetection()
    {
        Vector2 currentCloudCenter = transform.position;
        float currentCloudRadius = Mathf.Lerp(0.55f, 1.55f, Mathf.Clamp01(lifeTimer / 1.2f));

        // 1. Check Player
        if (!playerInked)
        {
            PlayerController pc = targetPlayer;
            if (pc == null && GameManager.instance != null && GameManager.instance.playerGameObject != null)
            {
                pc = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            }

            if (pc != null && pc.IsAlive && !pc.IsHooked)
            {
                if (IsTouchingCloud(pc.transform.position, pc.GetComponent<Collider2D>(), currentCloudCenter, currentCloudRadius))
                {
                    playerInked = true;
                    InkScreenOverlay.TriggerBlinding(5.0f);
                    pc.ApplyInkDisorientation(5.0f);
                }
            }
        }

        // 2. Check Target AI Fish
        if (targetFish != null && !targetFish.IsDead && !targetFish.IsCuttlefish && !inkedFishSet.Contains(targetFish))
        {
            if (IsTouchingCloud(targetFish.transform.position, targetFish.GetComponent<Collider2D>(), currentCloudCenter, currentCloudRadius))
            {
                inkedFishSet.Add(targetFish);
                targetFish.ApplyInkDisorientation(5.0f);
            }
        }

        // 3. Check any other AI predator swimming through the dense cloud
        if (Fish.AllFish != null)
        {
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish f = Fish.AllFish[i];
                if (f == null || f.IsDead || f.IsCuttlefish || inkedFishSet.Contains(f)) continue;

                if (IsTouchingCloud(f.transform.position, f.GetComponent<Collider2D>(), currentCloudCenter, currentCloudRadius))
                {
                    inkedFishSet.Add(f);
                    f.ApplyInkDisorientation(5.0f);
                }
            }
        }
    }

    private bool IsTouchingCloud(Vector3 targetPos, Collider2D col, Vector2 cloudCenter, float cloudRadius)
    {
        if (col != null)
        {
            Vector2 closestPoint = col.ClosestPoint(cloudCenter);
            return Vector2.Distance(closestPoint, cloudCenter) <= cloudRadius;
        }
        return Vector2.Distance(targetPos, cloudCenter) <= cloudRadius + 0.35f;
    }
}
