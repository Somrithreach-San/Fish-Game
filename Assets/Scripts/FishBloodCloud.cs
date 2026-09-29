using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Organic underwater liquid blood cloud effect spawned when a fish is eaten/bitten.
/// Features a truly translucent, liquid watercolor appearance (never solid/thick spots),
/// a continuous live-streaming trailing drag wake behind moving predators (Hungry Shark style),
/// and realistic fluid dispersion/buoyancy in water.
/// </summary>
public class FishBloodCloud : MonoBehaviour
{
    private ParticleSystem bloodParticles;
    private ParticleSystemRenderer bloodRenderer;
    private float lifeTimer = 0f;
    private const float CLOUD_LIFETIME = 5.2f;

    private static Texture2D s_CachedLiquidBloodTex;
    private static Material s_CachedLiquidBloodMat;

    // ─────────────────────────────────────────────────────────────
    //  SPAWN & POOLING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a soft liquid blood cloud with a natural trailing drag wake.
    /// Tracks the live eater's mouth as it moves through water to form a continuous streaming blood trail.
    /// </summary>
    public static FishBloodCloud Spawn(Vector3 worldPos, float scale = 1.0f, Vector2 swimVelocity = default, Transform trackingEater = null)
    {
        worldPos.z = 0f;

        FishBloodCloudPool pool = FishBloodCloudPool.GetOrCreate();
        FishBloodCloud cloud = pool.GetFromPool();

        cloud.transform.position = worldPos;
        cloud.gameObject.SetActive(true);
        cloud.Initialize(scale, swimVelocity, trackingEater, worldPos);
        return cloud;
    }

    private void ReturnToPool()
    {
        StopAllCoroutines();

        if (bloodParticles != null)
            bloodParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        FishBloodCloudPool.GetOrCreate().ReturnToPool(this);
    }

    // ─────────────────────────────────────────────────────────────
    //  MATERIAL & PROCEDURAL LIQUID TEXTURE (True Liquid Translucency)
    // ─────────────────────────────────────────────────────────────

    private static Material GetOrCreateLiquidBloodMaterial()
    {
        if (s_CachedLiquidBloodMat != null && s_CachedLiquidBloodTex != null) return s_CachedLiquidBloodMat;

        s_CachedLiquidBloodTex = GenerateLiquidBloodTexture();

        Shader shader = Shader.Find("Sprites/Default");
        s_CachedLiquidBloodMat = new Material(shader);
        s_CachedLiquidBloodMat.name = "LiquidBlood_ParticleMaterial";
        s_CachedLiquidBloodMat.mainTexture = s_CachedLiquidBloodTex;
        return s_CachedLiquidBloodMat;
    }

    /// <summary>
    /// Procedurally bakes a true translucent liquid blood blot texture.
    /// Uses airy watercolor exponential falloff, multi-octave fluid turbulence, and wispy filaments
    /// so individual puffs are soft and watery, preventing opaque/solid dark spots when overlapping in water.
    /// </summary>
    private static Texture2D GenerateLiquidBloodTexture()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "LiquidBloodParticleMap";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(0.5f, 0.5f);

        Vector2[] satellites = new Vector2[]
        {
            new Vector2(0.24f, 0.30f), new Vector2(0.76f, 0.26f), new Vector2(0.72f, 0.74f),
            new Vector2(0.25f, 0.70f), new Vector2(0.48f, 0.86f), new Vector2(0.84f, 0.48f),
            new Vector2(0.16f, 0.50f), new Vector2(0.52f, 0.14f)
        };
        float[] satRadii = new float[] { 0.055f, 0.060f, 0.052f, 0.056f, 0.048f, 0.052f, 0.045f, 0.050f };

        // Translucent liquid watercolor red profile (vibrant crimson core with soft translucent rose wash)
        Color coreColor = new Color(0.88f, 0.08f, 0.12f, 0.36f);
        Color midColor  = new Color(0.92f, 0.16f, 0.20f, 0.24f);
        Color rimColor  = new Color(0.96f, 0.30f, 0.32f, 0.08f);

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

                // Multi-frequency organic fluid arms and wispy turbulence
                float armWave = Mathf.Sin(angle * 5f) * 0.12f + Mathf.Cos(angle * 3f + 1.4f) * 0.09f + Mathf.Sin(angle * 8f - 0.8f) * 0.05f;
                float perlinNoise = (Mathf.PerlinNoise(u * 8.5f + 2.1f, v * 8.5f + 2.1f) - 0.5f) * 0.28f;
                float fineTurbulence = (Mathf.PerlinNoise(u * 18f + 5.7f, v * 18f + 5.7f) - 0.5f) * 0.14f;
                float microWisps = (Mathf.PerlinNoise(u * 32f + 9.3f, v * 32f + 9.3f) - 0.5f) * 0.08f;
                float maxRadius = 0.44f * (1.0f + armWave + perlinNoise + fineTurbulence + microWisps);

                float alpha = 0f;
                float norm = 1f;

                if (dist < maxRadius)
                {
                    norm = dist / maxRadius;
                    // Airy watercolor falloff (smooth cubic with exponential softness)
                    float coreFalloff = Mathf.Clamp01(1.0f - norm);
                    // Soft liquid transparency with delicate pigment - balanced visibility
                    alpha = Mathf.Pow(coreFalloff, 1.8f) * 0.36f;
                }

                for (int s = 0; s < satellites.Length; s++)
                {
                    float dSat = Vector2.Distance(uv, satellites[s]);
                    if (dSat < satRadii[s])
                    {
                        float sNorm = dSat / satRadii[s];
                        float sAlpha = Mathf.Pow(Mathf.Clamp01(1.0f - sNorm), 1.8f) * 0.18f;
                        if (sAlpha > alpha)
                        {
                            alpha = sAlpha;
                            norm = Mathf.Min(norm, sNorm);
                        }
                    }
                }

                Color baseTone = norm < 0.45f ? Color.Lerp(coreColor, midColor, norm / 0.45f) : Color.Lerp(midColor, rimColor, (norm - 0.45f) / 0.55f);
                Color finalColor = baseTone;
                finalColor.a = Mathf.Clamp01(alpha);
                pixels[y * size + x] = finalColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ─────────────────────────────────────────────────────────────
    //  INITIALIZATION & CONFIGURATION
    // ─────────────────────────────────────────────────────────────

    public void Initialize(float scale, Vector2 swimVelocity, Transform trackingEater, Vector3 biteOrigin)
    {
        lifeTimer = 0f;
        scale = Mathf.Clamp(scale, 0.35f, 2.5f);
        transform.position = biteOrigin;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        SetupParticleSystem();
        bloodParticles.Clear(true);
        StopAllCoroutines();
        StartCoroutine(StreamBloodTrailRoutine(scale, swimVelocity, trackingEater, biteOrigin));
    }

    public void PrewarmParticleSystem()
    {
        SetupParticleSystem();
    }

    private void SetupParticleSystem()
    {
        if (bloodParticles != null) return;

        bloodParticles = GetComponent<ParticleSystem>();
        if (bloodParticles == null)
        {
            bloodParticles = gameObject.AddComponent<ParticleSystem>();
        }

        bloodParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = bloodParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1.0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4.2f, 5.0f);
        main.startSpeed = 0f;
        main.startSize = 1.0f;
        main.startColor = Color.white;
        // CRITICAL: World Space simulation so blood puffs stay suspended in the ocean where they were shed as predator swims forward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.maxParticles = 64;

        var emission = bloodParticles.emission;
        emission.enabled = false;

        var shape = bloodParticles.shape;
        shape.enabled = false;

        // Fluid swelling: puffs billow outward and diffuse smoothly into water
        var sizeOverLifetime = bloodParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.50f);
        sizeCurve.AddKey(0.12f, 1.00f);
        sizeCurve.AddKey(0.45f, 1.80f);
        sizeCurve.AddKey(1f, 2.65f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Fluid swirl and vortex churning in water
        var rotationOverLifetime = bloodParticles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

        // Water drag: decelerates initial dispersion burst into a drifting suspension
        var limitVelocity = bloodParticles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.dampen = 0.40f;

        // Upward buoyant force on individual wisps in world space (natural aqueous drift)
        var forceOverLifetime = bloodParticles.forceOverLifetime;
        forceOverLifetime.enabled = true;
        forceOverLifetime.space = ParticleSystemSimulationSpace.World;
        forceOverLifetime.y = new ParticleSystem.MinMaxCurve(0.06f, 0.20f);

        // Color and Dissolve: bursts in liquid watercolor red, holds translucent shape, dissolves into water
        var colorOverLifetime = bloodParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1.0f, 0.95f, 0.96f), 0.40f),
                new GradientColorKey(new Color(0.96f, 0.72f, 0.76f), 0.75f),
                new GradientColorKey(new Color(0.90f, 0.55f, 0.60f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.48f, 0.05f),
                new GradientAlphaKey(0.40f, 0.30f),
                new GradientAlphaKey(0.22f, 0.65f),
                new GradientAlphaKey(0.07f, 0.90f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = grad;

        bloodRenderer = gameObject.GetComponent<ParticleSystemRenderer>();
        bloodRenderer.material = GetOrCreateLiquidBloodMaterial();
        bloodRenderer.sortingLayerName = "ParallaxForeground";
        bloodRenderer.sortingOrder = 200; // Always render directly on top of all fishes & player
        bloodRenderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // ─────────────────────────────────────────────────────────────
    //  STREAMING TRAIL COROUTINE (Hungry Shark Drag Wake)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Actively follows the predator's mouth as it lunges forward through the water,
    /// depositing soft liquid watercolor blood puffs along the swimming path in World Space.
    /// Creates the iconic long, undulating trailing blood wake connecting the mouth to the bite origin.
    /// </summary>
    private IEnumerator StreamBloodTrailRoutine(float scale, Vector2 swimVelocity, Transform trackingEater, Vector3 biteOrigin)
    {
        // 1. Initial Impact Plume at bite origin
        EmitBiteImpactPlume(biteOrigin, scale);

        // 2. Stream trailing wake behind the moving predator
        float trailDuration = 0.45f;
        float elapsed = 0f;
        float emitInterval = 0.022f;
        float nextEmitTime = 0f;
        int puffIndex = 0;

        Vector3 lastEmitPos = biteOrigin;
        Vector2 estVelocity = swimVelocity;
        if (estVelocity.sqrMagnitude < 0.1f && trackingEater != null)
        {
            float facing = Mathf.Sign(trackingEater.localScale.x);
            estVelocity = new Vector2(facing * 4.5f, 0f);
        }

        while (elapsed < trailDuration)
        {
            elapsed += Time.deltaTime;

            if (elapsed >= nextEmitTime)
            {
                nextEmitTime += emitInterval;
                puffIndex++;

                // Determine current live mouth position
                Vector3 currentMouthPos;
                if (trackingEater != null && trackingEater.gameObject.activeInHierarchy)
                {
                    currentMouthPos = GetEaterMouthWorldPos(trackingEater);
                }
                else
                {
                    currentMouthPos = biteOrigin + (Vector3)(estVelocity * elapsed);
                }
                currentMouthPos.z = 0f;

                // Dragging blood starts at the SAME size as the on-impact plume (~1.25f) and smoothly gets smaller and smaller
                float progress = Mathf.Clamp01(elapsed / trailDuration);
                float sizeMultiplier = Mathf.Lerp(1.25f, 0.25f, Mathf.Pow(progress, 0.85f));
                float puffSize = sizeMultiplier * scale * Random.Range(0.95f, 1.05f);

                // Undulating organic wave offset perpendicular to movement
                Vector2 moveDir = (currentMouthPos - lastEmitPos);
                if (moveDir.sqrMagnitude < 0.001f) moveDir = estVelocity;
                Vector2 perp = new Vector2(-moveDir.y, moveDir.x).normalized;
                float waveOffset = Mathf.Sin(puffIndex * 1.8f) * (0.08f * scale);
                Vector3 spawnPos = currentMouthPos + (Vector3)(perp * waveOffset);

                // Dispersion velocity: blood stays in water with gentle outward expansion
                Vector2 dispersion = perp * (Mathf.Sin(puffIndex * 2.3f) * 0.10f * scale);
                dispersion -= moveDir.normalized * (Random.Range(0.04f, 0.12f) * scale);

                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
                ep.position = spawnPos;
                ep.startSize = puffSize;
                ep.startLifetime = Random.Range(4.2f, 5.0f);
                ep.startColor = Color.white;
                ep.rotation = Random.Range(0f, 360f);
                ep.velocity = new Vector3(dispersion.x, dispersion.y, 0f);

                bloodParticles.Emit(ep, 1);
                lastEmitPos = currentMouthPos;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Emits the blooming liquid cloud cluster and satellite wisps at the initial bite location.
    /// </summary>
    private void EmitBiteImpactPlume(Vector3 origin, float scale)
    {
        // Core billowing cloud lobes
        Vector2[] billowOffsets = new Vector2[]
        {
            new Vector2( 0.00f,  0.00f),
            new Vector2( 0.15f,  0.18f),
            new Vector2(-0.14f, -0.16f),
            new Vector2( 0.20f, -0.15f),
            new Vector2(-0.18f,  0.16f),
            new Vector2( 0.02f,  0.26f),
            new Vector2(-0.02f, -0.24f)
        };
        float[] billowSizes = new float[] { 1.35f, 1.15f, 1.10f, 1.20f, 1.15f, 1.05f, 1.00f };

        for (int i = 0; i < billowOffsets.Length; i++)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = origin + (Vector3)(billowOffsets[i] * scale);
            ep.startSize = billowSizes[i] * scale;
            ep.startLifetime = Random.Range(4.2f, 5.0f);
            ep.startColor = Color.white;
            ep.rotation = Random.Range(0f, 360f);

            Vector2 radial = billowOffsets[i].normalized * Random.Range(0.12f, 0.28f) * scale;
            ep.velocity = new Vector3(radial.x, radial.y, 0f);

            bloodParticles.Emit(ep, 1);
        }

        // Satellite micro-wisps around impact
        for (int s = 0; s < 4; s++)
        {
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(0.35f, 0.65f) * scale;
            Vector3 wispPos = origin + new Vector3(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist, 0f);

            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = wispPos;
            ep.startSize = Random.Range(0.35f, 0.45f) * scale;
            ep.startLifetime = Random.Range(3.8f, 4.6f);
            ep.startColor = Color.white;
            ep.rotation = Random.Range(0f, 360f);
            ep.velocity = new Vector3(Mathf.Cos(ang) * 0.25f * scale, Mathf.Sin(ang) * 0.25f * scale, 0f);

            bloodParticles.Emit(ep, 1);
        }
    }

    /// <summary>
    /// Helper to sample the live mouth position of any predator (Player, AI Fish, Shark, etc.).
    /// </summary>
    private static Vector3 GetEaterMouthWorldPos(Transform eater)
    {
        if (eater == null) return Vector3.zero;

        // 1. PlayerController
        PlayerController pc = eater.GetComponent<PlayerController>();
        if (pc != null) return pc.GetMouthPosition();

        // 2. Fish
        Fish fish = eater.GetComponent<Fish>();
        if (fish != null) return fish.GetMouthPosition();

        // 3. Generic / SharkHazard
        float facing = Mathf.Sign(eater.localScale.x);
        SpriteRenderer sr = eater.GetComponentInChildren<SpriteRenderer>();
        float forwardExt = (sr != null && sr.bounds.size.x > 0.1f) ? sr.bounds.extents.x * 0.85f : 1.2f * Mathf.Abs(eater.localScale.x);
        return eater.position + new Vector3(facing * forwardExt, 0f, 0f);
    }

    // ─────────────────────────────────────────────────────────────
    //  UPDATE — Lifecycle Management
    // ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (GameManager.instance != null && GameManager.Paused) return;

        lifeTimer += Time.deltaTime;

        if (lifeTimer >= CLOUD_LIFETIME)
        {
            ReturnToPool();
        }
    }
}


