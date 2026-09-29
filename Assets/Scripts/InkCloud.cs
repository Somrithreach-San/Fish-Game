using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.Toolkit;

/// <summary>
/// Lingering dark ink cloud emitted by the Cuttlefish when startled by a threat in front of it.
/// Squirts out in a focused straight-line siphon tail and blooms into a billowing mushroom cloud,
/// matching the exact cephalopod ink reference silhouette using our custom liquid ink particles.
/// Obscures player visibility or disorients AI fishes that touch the ink trail or plume.
/// </summary>
public class InkCloud : MonoBehaviour
{
    private ParticleSystem inkParticles;
    private float lifeTimer = 0f;
    private const float CLOUD_LIFETIME = 3.8f;

    private Vector2 sprayDirection = Vector2.right;
    private PlayerController targetPlayer;
    private Fish targetFish;

    private bool playerInked = false;
    private HashSet<Fish> inkedFishSet = new HashSet<Fish>();

    private float contactCheckTimer = 0f;
    private const float CONTACT_CHECK_INTERVAL = 0.08f;

    private static Texture2D s_CachedLiquidInkTex;
    private static Material s_CachedLiquidInkMat;

    private struct InkPuffDef
    {
        public float x;      // Forward distance along jet stream
        public float y;      // Perpendicular wave offset
        public float size;   // Diameter of puff
        public float delay;  // Siphon emergence timing (seconds)
        public InkPuffDef(float x, float y, float size, float delay)
        {
            this.x = x; this.y = y; this.size = size; this.delay = delay;
        }
    }

    // Precise particle puff layout matching the reference shape:
    // Narrow squiggly straight tail at the siphon (X=0.0 to 1.6) -> billowing mushroom head (X=1.9 to 2.8)
    private static readonly InkPuffDef[] s_PuffLayout = new InkPuffDef[]
    {
        // 1. Tapering Siphon Jet Stream (Narrow squiggly line from siphon)
        new InkPuffDef(0.08f,  0.00f, 0.16f, 0.00f), // Siphon tip needle
        new InkPuffDef(0.20f,  0.02f, 0.22f, 0.01f),
        new InkPuffDef(0.35f, -0.03f, 0.28f, 0.02f),
        new InkPuffDef(0.52f,  0.04f, 0.35f, 0.03f),
        new InkPuffDef(0.72f, -0.05f, 0.44f, 0.05f),
        new InkPuffDef(0.95f,  0.05f, 0.54f, 0.07f),
        new InkPuffDef(1.20f, -0.04f, 0.65f, 0.09f),
        new InkPuffDef(1.45f,  0.06f, 0.78f, 0.11f),
        new InkPuffDef(1.70f, -0.02f, 0.90f, 0.13f),

        // 2. Billowing Mushroom Cloud Head (Dense overlapping cluster at impact front)
        new InkPuffDef(1.95f,  0.30f, 0.98f, 0.14f), // Upper rear billow
        new InkPuffDef(1.98f, -0.28f, 0.94f, 0.14f), // Lower rear billow
        new InkPuffDef(2.20f,  0.00f, 1.35f, 0.15f), // Core dense center mass
        new InkPuffDef(2.26f,  0.42f, 1.12f, 0.16f), // Top crest lobe
        new InkPuffDef(2.28f, -0.38f, 1.08f, 0.16f), // Bottom crest lobe
        new InkPuffDef(2.55f,  0.22f, 1.25f, 0.18f), // Forward-upper impact plume
        new InkPuffDef(2.58f, -0.20f, 1.22f, 0.18f), // Forward-lower impact plume
        new InkPuffDef(2.80f,  0.02f, 1.30f, 0.20f), // Front leading head billow

        // 3. Satellite micro-splashes around the mushroom crown
        new InkPuffDef(2.45f,  0.66f, 0.32f, 0.18f), // Top splash
        new InkPuffDef(2.48f, -0.62f, 0.30f, 0.18f), // Bottom splash
        new InkPuffDef(2.96f,  0.22f, 0.34f, 0.21f), // Forward splash
        new InkPuffDef(2.94f, -0.20f, 0.32f, 0.21f)  // Forward splash
    };

    private static Material GetOrCreateLiquidInkMaterial()
    {
        if (s_CachedLiquidInkMat != null) return s_CachedLiquidInkMat;

        if (s_CachedLiquidInkTex == null)
        {
            s_CachedLiquidInkTex = GenerateLiquidInkTexture();
        }

        Shader shader = Shader.Find("Sprites/Default");
        s_CachedLiquidInkMat = new Material(shader);
        s_CachedLiquidInkMat.name = "LiquidSquidInk_ParticleMaterial";
        s_CachedLiquidInkMat.mainTexture = s_CachedLiquidInkTex;
        return s_CachedLiquidInkMat;
    }

    /// <summary>
    /// Procedurally bakes a rich, organic liquid ink blot texture with dark viscous core,
    /// dynamic fluid arms, wet indigo-violet sheen, and satellite micro-droplets matching the screen obscure.
    /// Uses soft, watery falloff to blend naturally without hard solid edges.
    /// </summary>
    private static Texture2D GenerateLiquidInkTexture()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "LiquidInkParticleMap";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(0.5f, 0.5f);

        Vector2[] satellites = new Vector2[]
        {
            new Vector2(0.25f, 0.32f), new Vector2(0.74f, 0.28f), new Vector2(0.70f, 0.72f),
            new Vector2(0.26f, 0.68f), new Vector2(0.48f, 0.84f), new Vector2(0.82f, 0.48f),
            new Vector2(0.18f, 0.50f), new Vector2(0.52f, 0.16f)
        };
        float[] satRadii = new float[] { 0.045f, 0.052f, 0.042f, 0.046f, 0.038f, 0.042f, 0.036f, 0.040f };

        // Liquid cephalopod ink color profile (dark viscous core with wet violet sheen fringe)
        Color coreColor = new Color(0.04f, 0.025f, 0.07f, 0.82f);
        Color rimColor = new Color(0.24f, 0.16f, 0.36f, 0.35f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;
                Vector2 uv = new Vector2(u, v);
                Vector2 delta = uv - center;
                float dist = delta.magnitude;
                float angle = Mathf.Atan2(delta.y, delta.x);

                // Multi-frequency organic fluid arms and viscous blob boundary
                float armWave = Mathf.Sin(angle * 5f) * 0.11f + Mathf.Cos(angle * 3f + 1.4f) * 0.08f;
                float perlinNoise = (Mathf.PerlinNoise(u * 9.5f + 2.1f, v * 9.5f + 2.1f) - 0.5f) * 0.26f;
                float fineTurbulence = (Mathf.PerlinNoise(u * 22f + 5.7f, v * 22f + 5.7f) - 0.5f) * 0.10f;
                float maxRadius = 0.42f * (1.0f + armWave + perlinNoise + fineTurbulence);

                float alpha = 0f;
                float norm = 1f;

                if (dist < maxRadius)
                {
                    norm = dist / maxRadius;
                    float coreFalloff = Mathf.SmoothStep(1.0f, 0.0f, norm);
                    // Organic aqueous falloff so overlapping puffs create depth rather than a solid cutout
                    alpha = Mathf.Pow(coreFalloff, 1.35f) * 0.85f;
                }

                for (int s = 0; s < satellites.Length; s++)
                {
                    float dSat = Vector2.Distance(uv, satellites[s]);
                    if (dSat < satRadii[s])
                    {
                        float sNorm = dSat / satRadii[s];
                        float sAlpha = Mathf.SmoothStep(1.0f, 0.0f, sNorm) * 0.55f;
                        if (sAlpha > alpha)
                        {
                            alpha = sAlpha;
                            norm = Mathf.Min(norm, sNorm);
                        }
                    }
                }

                Color finalColor = Color.Lerp(coreColor, rimColor, Mathf.Clamp01(norm * 0.85f));
                finalColor.a = Mathf.Clamp01(alpha);
                pixels[y * size + x] = finalColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

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

        // Orient in the 2D spray direction:
        // Local +X = along sprayDirection (siphon nozzle -> threat)
        // Local +Y = perpendicular
        // Local +Z = 0 (strictly 2D plane)
        float angleDeg = Mathf.Atan2(sprayDirection.y, sprayDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        transform.localScale = Vector3.one;

        SetupParticleSystem();
        StartCoroutine(SquirtShapeRoutine());

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

    private void SetupParticleSystem()
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
        main.duration = 1.0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 3.6f);
        main.startSpeed = 0f; // Siphon positions and dispersion velocity set via EmitParams
        main.startSize = 1.0f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.maxParticles = 32;

        var emission = inkParticles.emission;
        emission.enabled = false; // Programmatic emission in exact silhouette

        var shape = inkParticles.shape;
        shape.enabled = false;

        // Fluid swelling: puffs billow outward and diffuse as ink suspends in water
        var sizeOverLifetime = inkParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.70f);
        sizeCurve.AddKey(0.18f, 1.00f);
        sizeCurve.AddKey(0.55f, 1.45f);
        sizeCurve.AddKey(1f, 1.85f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Fluid swirl and vortex churning in water
        var rotationOverLifetime = inkParticles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.40f, 0.40f);

        // Water drag: decelerates initial dispersion burst into a drifting suspension
        var limitVelocity = inkParticles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.dampen = 0.45f;

        // Upward buoyant force on individual ink wisps in world space
        var forceOverLifetime = inkParticles.forceOverLifetime;
        forceOverLifetime.enabled = true;
        forceOverLifetime.space = ParticleSystemSimulationSpace.World;
        forceOverLifetime.y = new ParticleSystem.MinMaxCurve(0.10f, 0.25f);

        // Color and Dissolve: squirts in translucent, holds shape, then slowly floats upward and fades away
        var colorOverLifetime = inkParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(Color.white, 0f), 
                new GradientColorKey(new Color(0.92f, 0.88f, 0.96f), 0.40f),
                new GradientColorKey(new Color(0.68f, 0.58f, 0.80f), 1f) 
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(0f, 0f), 
                new GradientAlphaKey(0.78f, 0.05f), 
                new GradientAlphaKey(0.66f, 0.30f), 
                new GradientAlphaKey(0.42f, 0.60f), 
                new GradientAlphaKey(0.18f, 0.85f), 
                new GradientAlphaKey(0f, 1f) 
            }
        );
        colorOverLifetime.color = grad;

        var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetOrCreateLiquidInkMaterial();
        renderer.sortingLayerName = "ParallaxForeground";
        renderer.sortingOrder = 92;
    }

    /// <summary>
    /// Squirts out the liquid ink puffs in a straight line from the siphon, blooming into the
    /// mushroom head to form the exact reference shape. As it settles, particles organically disperse
    /// radially and float slowly upwards like real ink in water.
    /// </summary>
    private IEnumerator SquirtShapeRoutine()
    {
        float timer = 0f;
        int emittedCount = 0;

        while (emittedCount < s_PuffLayout.Length)
        {
            timer += Time.deltaTime;

            while (emittedCount < s_PuffLayout.Length && timer >= s_PuffLayout[emittedCount].delay)
            {
                var puff = s_PuffLayout[emittedCount];
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
                ep.position = new Vector3(puff.x, puff.y, 0f);
                ep.startSize = puff.size;
                ep.startLifetime = Random.Range(3.0f, 3.6f);
                ep.startColor = Color.white;
                ep.rotation = Random.Range(0f, 360f); // Random organic orientation

                // Dispersion velocity: breaks rigidity so ink billows, stretches, and disperses in water
                Vector3 dispersionVel;
                if (puff.x < 1.80f)
                {
                    // Tail: stretches forward and gently widens
                    dispersionVel = new Vector3(Random.Range(0.08f, 0.20f), puff.y * 0.45f + Random.Range(-0.06f, 0.06f), 0f);
                }
                else
                {
                    // Mushroom head: expands radially outward from the billow core
                    Vector2 radial = (new Vector2(puff.x, puff.y) - new Vector2(2.15f, 0f)).normalized;
                    float speed = Random.Range(0.20f, 0.42f);
                    dispersionVel = new Vector3(radial.x * speed, radial.y * speed, 0f);
                }
                ep.velocity = dispersionVel;

                inkParticles.Emit(ep, 1);
                emittedCount++;
            }

            yield return null;
        }
    }

    private void Update()
    {
        if (GameManager.instance != null && GameManager.Paused) return;

        lifeTimer += Time.deltaTime;

        // Buoyant upward drift in ocean water: ink gently rises upwards as it diffuses and fades
        float progress = Mathf.Clamp01(lifeTimer / CLOUD_LIFETIME);
        float upwardSpeed = Mathf.Lerp(0.08f, 0.38f, progress);
        transform.position += Vector3.up * (upwardSpeed * Time.deltaTime);

        if (lifeTimer >= CLOUD_LIFETIME)
        {
            Destroy(gameObject);
            return;
        }

        // Active ink cloud contact detection while cloud is dense and lingering (first 3.2s)
        if (lifeTimer <= 3.2f)
        {
            contactCheckTimer += Time.deltaTime;
            if (contactCheckTimer >= CONTACT_CHECK_INTERVAL)
            {
                contactCheckTimer = 0f;
                UpdateInkContactDetection();
            }
        }
    }

    private void UpdateInkContactDetection()
    {
        // Continuous conical plume: siphon origin expanding outwards to billowing mushroom head
        Vector2 siphonOrigin = (Vector2)transform.position;
        Vector2 headCenter = siphonOrigin + sprayDirection * 2.45f;
        float headRadius = 1.65f;
        float tailRadius = 0.90f;

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
                Collider2D col = pc.GetComponent<Collider2D>();
                if (IsTouchingSegment(pc.transform.position, col, siphonOrigin, headCenter, headRadius, tailRadius))
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
            Collider2D col = targetFish.GetComponent<Collider2D>();
            if (IsTouchingSegment(targetFish.transform.position, col, siphonOrigin, headCenter, headRadius, tailRadius))
            {
                inkedFishSet.Add(targetFish);
                targetFish.ApplyInkDisorientation(5.0f);
            }
        }

        // 3. Check any other AI fish/predator swimming through the ink
        if (Fish.AllFish != null)
        {
            for (int i = 0; i < Fish.AllFish.Count; i++)
            {
                Fish f = Fish.AllFish[i];
                if (f == null || f.IsDead || f.IsCuttlefish || inkedFishSet.Contains(f)) continue;

                Collider2D col = f.GetComponent<Collider2D>();
                if (IsTouchingSegment(f.transform.position, col, siphonOrigin, headCenter, headRadius, tailRadius))
                {
                    inkedFishSet.Add(f);
                    f.ApplyInkDisorientation(5.0f);
                }
            }
        }
    }

    private bool IsTouchingSegment(Vector3 targetPos, Collider2D col, Vector2 siphonOrigin, Vector2 headCenter, float headRadius, float tailRadius)
    {
        Vector2 seg = headCenter - siphonOrigin;
        float segLenSqr = seg.sqrMagnitude;
        if (segLenSqr > 0.001f)
        {
            // Point on collider
            Vector2 checkPos = (col != null) ? col.ClosestPoint(headCenter) : (Vector2)targetPos;
            float t = Mathf.Clamp01(Vector2.Dot(checkPos - siphonOrigin, seg) / segLenSqr);
            Vector2 closestPoint = siphonOrigin + seg * t;
            float allowedRadius = Mathf.Lerp(tailRadius, headRadius, t);
            if (Vector2.Distance(checkPos, closestPoint) <= allowedRadius) return true;

            // Direct position check as well
            Vector2 directPos = (Vector2)targetPos;
            float tDir = Mathf.Clamp01(Vector2.Dot(directPos - siphonOrigin, seg) / segLenSqr);
            Vector2 closestDirect = siphonOrigin + seg * tDir;
            float allowedDirect = Mathf.Lerp(tailRadius, headRadius, tDir);
            if (Vector2.Distance(directPos, closestDirect) <= allowedDirect + 0.25f) return true;
        }

        return false;
    }
}
