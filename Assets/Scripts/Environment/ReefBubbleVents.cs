using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Rhinotap
{
    /// <summary>
    /// Spawns and manages gentle, heartbeat-like bubble pulses from the 3 tube/straw
    /// openings of the coral reef element. Each straw vent has its own independent,
    /// randomized rhythm so they never fire at the same time.
    /// Bubbles float naturally upwards through the water and pop/destroy at the water surface.
    /// </summary>
    public class ReefBubbleVents : MonoBehaviour
    {
        [System.Serializable]
        public class VentPoint
        {
            public string name = "Straw Vent";
            public Vector3 localOffset;
            [Tooltip("Continuous bubble emission rate per second (0 for pure pulse mode)")]
            public float emissionRate = 0f;
            [Tooltip("Minimum wait time between pulses (seconds)")]
            public float minInterval = 2.5f;
            [Tooltip("Maximum wait time between pulses (seconds)")]
            public float maxInterval = 5.0f;
            [Tooltip("Minimum bubbles emitted in a pulse")]
            public int minBurstCount = 1;
            [Tooltip("Maximum bubbles emitted in a pulse")]
            public int maxBurstCount = 2;
            [Tooltip("Chance (0 to 1) for a secondary 'heartbeat' pulse 0.25s later")]
            [Range(0f, 1f)] public float doublePulseChance = 0.30f;
            [Tooltip("Minimum bubble size")]
            public float minSize = 0.045f;
            [Tooltip("Maximum bubble size")]
            public float maxSize = 0.080f;
            [Tooltip("Upward rise speed range (min, max)")]
            public Vector2 riseSpeed = new Vector2(0.55f, 0.95f);
        }

        [Header("Material & Visuals")]
        [SerializeField] private Material bubbleMaterial;
        [SerializeField] private string sortingLayerName = "ParallaxForeground";
        [SerializeField] private int sortingOrder = 55;

        [Header("Water Boundary")]
        [Tooltip("Top of the map / water surface where bubbles pop and destroy")]
        [SerializeField] private float waterSurfaceY = 15.0f;

        [Header("Straw Vent Mapping")]
        [SerializeField]
        private List<VentPoint> vents = new List<VentPoint>()
        {
            new VentPoint
            {
                name = "Tube_Left",
                localOffset = new Vector3(-0.34f, 0.405f, 0f),
                emissionRate = 0f,
                minInterval = 2.5f,
                maxInterval = 5.0f,
                minBurstCount = 1,
                maxBurstCount = 2,
                doublePulseChance = 0.30f,
                minSize = 0.045f,
                maxSize = 0.080f,
                riseSpeed = new Vector2(0.55f, 0.95f)
            },
            new VentPoint
            {
                name = "Tube_CenterTall",
                localOffset = new Vector3(0.00f, 0.805f, 0f),
                emissionRate = 0f,
                minInterval = 2.0f,
                maxInterval = 4.2f,
                minBurstCount = 1,
                maxBurstCount = 3,
                doublePulseChance = 0.35f,
                minSize = 0.050f,
                maxSize = 0.090f,
                riseSpeed = new Vector2(0.65f, 1.05f)
            },
            new VentPoint
            {
                name = "Tube_Right",
                localOffset = new Vector3(0.36f, 0.405f, 0f),
                emissionRate = 0f,
                minInterval = 2.8f,
                maxInterval = 5.5f,
                minBurstCount = 1,
                maxBurstCount = 2,
                doublePulseChance = 0.30f,
                minSize = 0.045f,
                maxSize = 0.080f,
                riseSpeed = new Vector2(0.55f, 0.90f)
            }
        };

        private class ActiveVent
        {
            public VentPoint config;
            public ParticleSystem ps;
            public Coroutine coroutine;
        }

        private List<ActiveVent> activeVents = new List<ActiveVent>();
        private ParticleSystem.Particle[] particleBuffer;
        private const int MAX_BUFFER_SIZE = 512;

        private void Awake()
        {
            EnsureMaterial();
            particleBuffer = new ParticleSystem.Particle[MAX_BUFFER_SIZE];
            CreateVentParticleSystems();
        }

        private void Start()
        {
            if (activeVents.Count == 0)
            {
                CreateVentParticleSystems();
            }
            StartAllHeartbeats();
        }

        private void OnEnable()
        {
            StartAllHeartbeats();
        }

        private void OnDisable()
        {
            StopAllHeartbeats();
        }

        private void EnsureMaterial()
        {
            if (bubbleMaterial == null)
            {
#if UNITY_EDITOR
                bubbleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
#endif
                if (bubbleMaterial == null)
                {
                    bubbleMaterial = Resources.Load<Material>("bubbleParticleMat");
                }
            }
        }

        public void CreateVentParticleSystems()
        {
            StopAllHeartbeats();
            activeVents.Clear();
            EnsureMaterial();

            for (int i = 0; i < vents.Count; i++)
            {
                var vent = vents[i];
                string childName = $"Vent_{vent.name}";
                Transform existingChild = transform.Find(childName);
                GameObject ventObj;
                if (existingChild != null)
                {
                    ventObj = existingChild.gameObject;
                }
                else
                {
                    ventObj = new GameObject(childName);
                    ventObj.transform.SetParent(transform, false);
                }

                ventObj.transform.localPosition = vent.localOffset;
                ventObj.transform.localRotation = Quaternion.Inverse(transform.localRotation); // Cancel parent tilt -> Straight UP in world space

                var ps = ventObj.GetComponent<ParticleSystem>();
                if (ps == null) ps = ventObj.AddComponent<ParticleSystem>();
                ConfigureParticleSystem(ps, vent);

                var av = new ActiveVent
                {
                    config = vent,
                    ps = ps
                };
                activeVents.Add(av);
            }

            if (isActiveAndEnabled && Application.isPlaying)
            {
                StartAllHeartbeats();
            }
        }

        private void ConfigureParticleSystem(ParticleSystem ps, VentPoint vent)
        {
            float emitterWorldY = transform.TransformPoint(vent.localOffset).y;
            float travelDistance = Mathf.Max(5.0f, waterSurfaceY - emitterWorldY);
            float maxLifetime = travelDistance / Mathf.Max(0.3f, vent.riseSpeed.x) + 1.0f;

            // 1. Main Module (manual burst pulses, upright particles, gentle natural speed)
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = new ParticleSystem.MinMaxCurve(vent.riseSpeed.x, vent.riseSpeed.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(maxLifetime * 0.95f, maxLifetime * 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(vent.minSize, vent.maxSize);
            main.startRotation = 0f; // Straight billboard, no sprite rotation tilt
            main.maxParticles = 80;
            main.gravityModifier = -0.01f; // Gentle upward buoyancy

            // 2. Emission Module (Pulsing burst mode driven by VentHeartbeatRoutine)
            var emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = 0f;

            // 3. Shape Module: narrow cone pointing straight UP (+Y in world space)
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4.0f; // tight straight mouth
            shape.radius = 0.04f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // UP (+Y)

            // 4. Velocity over Lifetime (Subtle gentle drift)
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.10f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // 4b. Noise Module (Natural underwater gentle sway)
            var noise = ps.noise;
            noise.enabled = true;
            noise.frequency = 0.20f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            noise.scrollSpeed = 0.3f;
            noise.damping = true;

            // 5. Size over Lifetime: Disabled so bubbles remain at constant size throughout ascent
            var sol = ps.sizeOverLifetime;
            sol.enabled = false;

            // 6. Color over Lifetime
            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(new Color(0.95f, 0.98f, 1.0f), 1.0f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.0f, 0.00f),
                    new GradientAlphaKey(0.95f, 0.04f),
                    new GradientAlphaKey(0.90f, 0.88f),
                    new GradientAlphaKey(0.0f, 1.00f) // Dissolve
                }
            );
            col.color = grad;

            // 7. Renderer Module
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            if (bubbleMaterial != null)
            {
                renderer.sharedMaterial = bubbleMaterial;
            }

            if (Application.isPlaying && !ps.isPlaying)
            {
                ps.Play();
            }
        }

        private void StartAllHeartbeats()
        {
            if (!Application.isPlaying) return;

            // Ensure activeVents is populated
            if (activeVents.Count == 0)
            {
                CreateVentParticleSystems();
            }

            // Assign a randomized initial offset to each vent so they NEVER start simultaneously
            float initialStagger = 0.2f;
            for (int i = 0; i < activeVents.Count; i++)
            {
                var av = activeVents[i];
                if (av.ps != null && !av.ps.isPlaying)
                {
                    av.ps.Play();
                }
                if (av.coroutine != null) StopCoroutine(av.coroutine);
                float startDelay = initialStagger + Random.Range(0.2f, 1.2f);
                initialStagger += 1.2f;
                av.coroutine = StartCoroutine(VentHeartbeatRoutine(av, startDelay));
            }
        }

        private void StopAllHeartbeats()
        {
            for (int i = 0; i < activeVents.Count; i++)
            {
                if (activeVents[i].coroutine != null)
                {
                    StopCoroutine(activeVents[i].coroutine);
                    activeVents[i].coroutine = null;
                }
            }
        }

        /// <summary>
        /// Coroutine for an individual vent simulating an organic, intermittent heartbeat pulse.
        /// </summary>
        private IEnumerator VentHeartbeatRoutine(ActiveVent av, float initialDelay)
        {
            if (av.ps != null && !av.ps.isPlaying)
            {
                av.ps.Play();
            }

            yield return new WaitForSeconds(initialDelay);

            while (true)
            {
                if (av.ps != null)
                {
                    if (!av.ps.isPlaying) av.ps.Play();

                    // Primary heartbeat pulse: emit a small cluster of 2-4 bubbles
                    int burstCount = Random.Range(av.config.minBurstCount, av.config.maxBurstCount + 1);
                    av.ps.Emit(burstCount);

                    // Secondary trailing pulse (lub-dub heartbeat effect)
                    if (Random.value < av.config.doublePulseChance)
                    {
                        yield return new WaitForSeconds(Random.Range(0.20f, 0.35f));
                        if (av.ps != null)
                        {
                            if (!av.ps.isPlaying) av.ps.Play();
                            av.ps.Emit(Random.Range(1, 3));
                        }
                    }
                }

                // Wait for the next heartbeat interval (randomized per vent)
                float cooldown = Random.Range(av.config.minInterval, av.config.maxInterval);
                yield return new WaitForSeconds(cooldown);
            }
        }

        private void LateUpdate()
        {
            if (particleBuffer == null || particleBuffer.Length < MAX_BUFFER_SIZE)
            {
                particleBuffer = new ParticleSystem.Particle[MAX_BUFFER_SIZE];
            }

            for (int s = 0; s < activeVents.Count; s++)
            {
                var ps = activeVents[s].ps;
                if (ps == null) continue;

                int alive = ps.GetParticles(particleBuffer);
                bool modified = false;

                for (int i = 0; i < alive; i++)
                {
                    if (particleBuffer[i].position.y >= waterSurfaceY)
                    {
                        // Kill the particle instantly upon touching the water surface
                        particleBuffer[i].remainingLifetime = 0f;
                        modified = true;
                    }
                }

                if (modified)
                {
                    ps.SetParticles(particleBuffer, alive);
                }
            }
        }

        /// <summary>
        /// Helper for editor simulation: trigger an individual staggered heartbeat on all vents
        /// </summary>
        public void SimulateEditorHeartbeat(float timeAhead = 3.0f)
        {
            // Emit staggered bursts across the 3 vents
            float[] delays = new float[] { 0.4f, 1.8f, 3.2f };
            for (int i = 0; i < activeVents.Count; i++)
            {
                var av = activeVents[i];
                if (av.ps == null) continue;
                av.ps.Clear();
                int count = Random.Range(av.config.minBurstCount, av.config.maxBurstCount + 1);
                av.ps.Emit(count);
                av.ps.Simulate(timeAhead - delays[i % delays.Length], true, false);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            foreach (var vent in vents)
            {
                Vector3 worldPos = transform.TransformPoint(vent.localOffset);
                Gizmos.DrawWireSphere(worldPos, 0.1f);
                Gizmos.DrawLine(worldPos, worldPos + Vector3.up * 1.5f);
            }
        }
#endif
    }
}
