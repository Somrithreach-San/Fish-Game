using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SuctionVFX creates the authentic Feeding Frenzy Inhale suction animation.
/// Spawns in-game bubble particles in a wide cone in front of the fish
/// and pulls them inward along a ">" funnel directly into the player's open mouth.
/// </summary>
public class SuctionVFX : MonoBehaviour
{
    private ParticleSystem suctionBubbles;
    private ParticleSystemRenderer bubbleRenderer;
    private ParticleSystem suctionStreaks;
    private ParticleSystemRenderer streakRenderer;
    private PlayerController player;
    private static Material cachedBubbleMat;

    public void Initialize(PlayerController playerController)
    {
        player = playerController;
        transform.SetParent(null);
        BuildSuctionVFX();
    }

    private void BuildSuctionVFX()
    {
        Material inGameMat = ResolveInGameBubbleMaterial();

        // 1. Primary Inhale Suction Bubbles (Outer Swirling Bubble Stream)
        Transform existingBubbles = transform.Find("InGameSuctionBubbles");
        GameObject bubblesObj = (existingBubbles != null) ? existingBubbles.gameObject : new GameObject("InGameSuctionBubbles");
        bubblesObj.transform.SetParent(transform, false);
        bubblesObj.transform.localPosition = Vector3.zero;
        bubblesObj.transform.localRotation = Quaternion.identity;
        bubblesObj.transform.localScale = Vector3.one;

        suctionBubbles = bubblesObj.GetComponent<ParticleSystem>();
        if (suctionBubbles == null) suctionBubbles = bubblesObj.AddComponent<ParticleSystem>();

        bubbleRenderer = bubblesObj.GetComponent<ParticleSystemRenderer>();
        if (bubbleRenderer == null) bubbleRenderer = bubblesObj.AddComponent<ParticleSystemRenderer>();

        if (inGameMat != null) bubbleRenderer.material = inGameMat;
        bubbleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        bubbleRenderer.sortingOrder = 125; // Render directly in front of player mouth sprite (120)

        var main = suctionBubbles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f; // Velocity is driven by VelocityOverLifetime pulling inwards into mouth
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.40f, 0.52f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.maxParticles = 80;
        main.gravityModifier = 0f;

        var emission = suctionBubbles.emission;
        emission.enabled = true;
        emission.rateOverTime = 42;

        var shape = suctionBubbles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.ConeVolume;
        shape.position = new Vector3(1.2f, 0f, 0f); // Offset forward so particles spawn well in front
        shape.rotation = new Vector3(0f, 90f, 0f); // Directed forward (+X)
        shape.angle = 22f;
        shape.radius = 0.20f;
        shape.length = 4.8f;

        // Inward suction velocity: pulls particles from open water inward towards the mouth gap
        var vol = suctionBubbles.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = new ParticleSystem.MinMaxCurve(-6.0f, -8.5f); // Smooth inward suction flow
        vol.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.radial = new ParticleSystem.MinMaxCurve(-1.8f, -2.5f); // Converges stream into funnel apex
        vol.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

        var noise = suctionBubbles.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 1.6f;
        noise.scrollSpeed = 1.0f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var sol = suctionBubbles.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0.0f, 1.0f); // Full size in open water
        sizeCurve.AddKey(0.55f, 0.85f);
        sizeCurve.AddKey(0.85f, 0.20f);
        sizeCurve.AddKey(1.0f, 0.0f); // Shrinks to 0 before reaching mouth
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var col = suctionBubbles.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.9f, 0.98f, 1.0f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.20f, 0f),
                new GradientAlphaKey(0.95f, 0.25f),
                new GradientAlphaKey(0.85f, 0.75f),
                new GradientAlphaKey(0f, 0.92f) // Clean fadeout
            }
        );
        col.color = grad;

        // 2. Dense Core Suction Micro-Bubbles (100% genuine bubble billboards rushing into mouth)
        Transform existingStreaks = transform.Find("InGameSuctionStreaks");
        GameObject streaksObj = (existingStreaks != null) ? existingStreaks.gameObject : new GameObject("InGameSuctionStreaks");
        streaksObj.transform.SetParent(transform, false);
        streaksObj.transform.localPosition = Vector3.zero;
        streaksObj.transform.localRotation = Quaternion.identity;
        streaksObj.transform.localScale = Vector3.one;

        suctionStreaks = streaksObj.GetComponent<ParticleSystem>();
        if (suctionStreaks == null) suctionStreaks = streaksObj.AddComponent<ParticleSystem>();

        streakRenderer = streaksObj.GetComponent<ParticleSystemRenderer>();
        if (streakRenderer == null) streakRenderer = streaksObj.AddComponent<ParticleSystemRenderer>();

        if (inGameMat != null) streakRenderer.material = inGameMat;
        streakRenderer.renderMode = ParticleSystemRenderMode.Billboard; // Pure bubble billboards!
        streakRenderer.sortingOrder = 126;

        var sMain = suctionStreaks.main;
        sMain.loop = true;
        sMain.playOnAwake = false;
        sMain.simulationSpace = ParticleSystemSimulationSpace.Local;
        sMain.startSpeed = 0f;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.38f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); // Dense micro-bubbles
        sMain.maxParticles = 70;
        sMain.gravityModifier = 0f;

        var sEmission = suctionStreaks.emission;
        sEmission.enabled = true;
        sEmission.rateOverTime = 55;

        var sShape = suctionStreaks.shape;
        sShape.enabled = true;
        sShape.shapeType = ParticleSystemShapeType.ConeVolume;
        sShape.position = new Vector3(1.0f, 0f, 0f); // Offset forward
        sShape.rotation = new Vector3(0f, 90f, 0f); // Directed forward (+X)
        sShape.angle = 12f; // Narrow focused core
        sShape.radius = 0.10f;
        sShape.length = 4.5f;

        var sVol = suctionStreaks.velocityOverLifetime;
        sVol.enabled = true;
        sVol.space = ParticleSystemSimulationSpace.Local;
        sVol.x = new ParticleSystem.MinMaxCurve(-7.5f, -10.5f); // High-speed stream into funnel
        sVol.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        sVol.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        sVol.radial = new ParticleSystem.MinMaxCurve(-1.8f, -2.4f);
        sVol.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        sVol.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        sVol.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

        var sNoise = suctionStreaks.noise;
        sNoise.enabled = true;
        sNoise.strength = 0.08f;
        sNoise.frequency = 2.0f;
        sNoise.scrollSpeed = 1.0f;
        sNoise.damping = true;
        sNoise.quality = ParticleSystemNoiseQuality.Low;

        var sSol = suctionStreaks.sizeOverLifetime;
        sSol.enabled = true;
        sSol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var sCol = suctionStreaks.colorOverLifetime;
        sCol.enabled = true;
        Gradient sGrad = new Gradient();
        sGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.85f, 0.95f, 1.0f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.20f, 0f),
                new GradientAlphaKey(0.90f, 0.25f),
                new GradientAlphaKey(0.85f, 0.75f),
                new GradientAlphaKey(0f, 0.90f) // Clean fadeout
            }
        );
        sCol.color = sGrad;

        suctionBubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        suctionStreaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private Material ResolveInGameBubbleMaterial()
    {
        if (cachedBubbleMat != null) return cachedBubbleMat;

        // 1. Try Player's EatEffect material
        if (player != null && player.EatEffect != null)
        {
            var r = player.EatEffect.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sharedMaterial != null)
            {
                cachedBubbleMat = r.sharedMaterial;
                return cachedBubbleMat;
            }
        }

        // 2. Try Player's SpeedEffect material
        if (player != null && player.SpeedEffect != null)
        {
            var r = player.SpeedEffect.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sharedMaterial != null)
            {
                cachedBubbleMat = r.sharedMaterial;
                return cachedBubbleMat;
            }
        }

        // 3. Try Player's assigned BubbleMaterial
        if (player != null && player.BubbleMaterial != null)
        {
            cachedBubbleMat = player.BubbleMaterial;
            return cachedBubbleMat;
        }

#if UNITY_EDITOR
        // 4. Load asset directly from Graphics folder in Editor
        Material edMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
        if (edMat != null)
        {
            cachedBubbleMat = edMat;
            return cachedBubbleMat;
        }
#endif

        // 5. Search in all loaded materials
        Material[] allMats = Resources.FindObjectsOfTypeAll<Material>();
        foreach (Material m in allMats)
        {
            if (m != null && m.name.Contains("bubbleParticleMat"))
            {
                cachedBubbleMat = m;
                return cachedBubbleMat;
            }
        }

        // 6. Look for bubble texture and build material
        Texture2D bTex = (player != null) ? player.BubbleTexture : null;
        if (bTex == null)
        {
#if UNITY_EDITOR
            bTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Graphics/bubble.png");
#endif
        }
        if (bTex == null)
        {
            Texture2D[] allTex = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (Texture2D t in allTex)
            {
                if (t != null && t.name.ToLower().Contains("bubble"))
                {
                    bTex = t;
                    break;
                }
            }
        }

        Shader shader = Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Mobile/Particles/Alpha Blended")
                     ?? Shader.Find("Sprites/Default");

        if (shader != null)
        {
            Material mat = new Material(shader);
            if (bTex != null) mat.mainTexture = bTex;
            mat.renderQueue = 3000;
            cachedBubbleMat = mat;
            return cachedBubbleMat;
        }

        return null;
    }

    public void StartSuction()
    {
        if (suctionBubbles == null || suctionStreaks == null)
        {
            BuildSuctionVFX();
        }

        if (suctionBubbles != null)
        {
            if (bubbleRenderer != null && (bubbleRenderer.sharedMaterial == null || bubbleRenderer.material == null))
            {
                Material mat = ResolveInGameBubbleMaterial();
                if (mat != null) bubbleRenderer.material = mat;
            }

            suctionBubbles.Clear();
            suctionBubbles.Play();
        }

        if (suctionStreaks != null)
        {
            if (streakRenderer != null && (streakRenderer.sharedMaterial == null || streakRenderer.material == null))
            {
                Material mat = ResolveInGameBubbleMaterial();
                if (mat != null) streakRenderer.material = mat;
            }

            suctionStreaks.Clear();
            suctionStreaks.Play();
        }
    }

    public void StopSuction()
    {
        if (suctionBubbles != null)
        {
            suctionBubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (suctionStreaks != null)
        {
            suctionStreaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void LateUpdate()
    {
        if (player == null)
        {
            if (GameManager.instance?.playerGameObject != null)
            {
                player = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            }
        }

        if (player != null && player.gameObject.activeInHierarchy && player.IsAlive)
        {
            float dir = (player.transform.localScale.x >= 0f) ? 1f : -1f;
            Vector3 mouthPos = player.GetMouthPosition();

            Transform gfx = player.transform.Find("PlayerGraphics");
            float tiltZ = 0f;
            if (gfx != null)
            {
                tiltZ = gfx.localEulerAngles.z;
                if (tiltZ > 180f) tiltZ -= 360f;
            }

            // Facing Right: Euler(0, 0, tiltZ)
            // Facing Left: Euler(0, 180, -tiltZ) rotates 180 deg around Y cleanly facing left with positive scale!
            Quaternion rot = (dir >= 0f) ? Quaternion.Euler(0f, 0f, tiltZ) : Quaternion.Euler(0f, 180f, -tiltZ);
            transform.rotation = rot;

            float baseScale = player.CurrentBaseScale;
            transform.localScale = new Vector3(baseScale, baseScale, 1f);

            // Forward gap offset in front of mouth lips with prominent visible breathing room
            float mouthGap = 2.6f * baseScale;
            Vector3 forwardGap = rot * new Vector3(mouthGap, 0f, 0f);
            transform.position = mouthPos + forwardGap;
        }
        else
        {
            StopSuction();
        }
    }

    private void OnDisable()
    {
        StopSuction();
    }
}
