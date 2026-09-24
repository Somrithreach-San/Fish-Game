using UnityEngine;

namespace Rhinotap
{
    /// <summary>
    /// Controls ambient ocean background bubble particles drifting horizontally from Right to Left.
    /// Configures the particle system to spawn bubbles along the right ocean boundary and float them smoothly
    /// across the screen to the left (-X direction) with gentle organic bobbing and soft edge fading.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class OceanBackgroundBubbles : MonoBehaviour
    {
        private static OceanBackgroundBubbles _instance;
        public static OceanBackgroundBubbles Instance => _instance;

        [Header("Movement & Velocity (Right to Left)")]
        [Tooltip("Horizontal drift speed towards the left (min, max in units/sec)")]
        [SerializeField] private Vector2 horizontalSpeed = new Vector2(1.5f, 2.6f);

        [Tooltip("Vertical bobbing / slight drift range (min, max in units/sec)")]
        [SerializeField] private Vector2 verticalDrift = new Vector2(-0.25f, 0.25f);

        [Header("Spawn & Dimensions")]
        [Tooltip("Emission rate (bubbles per second)")]
        [SerializeField] private float emissionRate = 3.5f;

        [Tooltip("Lifetime in seconds to ensure bubbles cross entire screen (min, max)")]
        [SerializeField] private Vector2 lifetimeRange = new Vector2(16.0f, 24.0f);

        [Tooltip("Bubble size range (min, max)")]
        [SerializeField] private Vector2 sizeRange = new Vector2(0.08f, 0.22f);

        [Tooltip("Vertical spawn span across the ocean height")]
        [SerializeField] private float spawnHeight = 22.0f;

        [Tooltip("Horizontal spawn band width on the right")]
        [SerializeField] private float spawnWidth = 2.5f;

        [Tooltip("Right-side X offset for the spawn area relative to world")]
        [SerializeField] private float spawnXOffset = 21.0f;

        [Tooltip("Center Y offset for spawn area")]
        [SerializeField] private float spawnYCenter = -2.0f;

        [Header("Visuals & Sorting")]
        [SerializeField] private Material bubbleMaterial;
        [SerializeField] private string sortingLayerName = "ParallaxBackground";
        [SerializeField] private int sortingOrder = 100;

        [Header("Simulation")]
        [SerializeField] private bool enablePrewarm = true;
        [SerializeField] private bool enableNoise = true;

        private ParticleSystem ps;
        private ParticleSystemRenderer psRenderer;

        private void Awake()
        {
            _instance = this;
            ps = GetComponent<ParticleSystem>();
            psRenderer = GetComponent<ParticleSystemRenderer>();

            ConfigureParticleSystem();
        }

        private void Start()
        {
            UpdateLevelVisibility();
        }

        private void OnEnable()
        {
            UpdateLevelVisibility();
        }

        public static void Refresh()
        {
            if (_instance != null)
            {
                _instance.UpdateLevelVisibility();
            }
        }

        public void UpdateLevelVisibility()
        {
            bool isLake = LevelManager.IsCurrentLakeLevel;
            if (ps != null)
            {
                if (isLake)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    gameObject.SetActive(false);
                }
                else
                {
                    gameObject.SetActive(true);
                    if (!ps.isPlaying)
                    {
                        ps.Play();
                    }
                }
            }
        }

        [ContextMenu("Apply Horizontal Bubble Configuration")]
        public void ConfigureParticleSystem()
        {
            if (ps == null) ps = GetComponent<ParticleSystem>();
            if (psRenderer == null) psRenderer = GetComponent<ParticleSystemRenderer>();
            if (ps == null) return;

            if (bubbleMaterial == null)
            {
#if UNITY_EDITOR
                bubbleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/bubbleParticleMat.mat");
#endif
            }

            if (psRenderer != null)
            {
                psRenderer.sortingLayerName = sortingLayerName;
                psRenderer.sortingOrder = sortingOrder;
                if (bubbleMaterial != null) psRenderer.material = bubbleMaterial;
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                psRenderer.alignment = ParticleSystemRenderSpace.View;
                psRenderer.minParticleSize = 0f;
                psRenderer.maxParticleSize = 0.5f;
            }

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = enablePrewarm;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.maxParticles = 120;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(spawnWidth, spawnHeight, 1.0f);
            Vector3 worldSpawnCenter = new Vector3(spawnXOffset, spawnYCenter, 0f);
            shape.position = transform.InverseTransformPoint(worldSpawnCenter);
            shape.rotation = Vector3.zero;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-horizontalSpeed.y, -horizontalSpeed.x);
            vel.y = new ParticleSystem.MinMaxCurve(verticalDrift.x, verticalDrift.y);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.08f),
                    new GradientAlphaKey(0.85f, 0.88f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.75f);
            sizeCurve.AddKey(0.15f, 1.0f);
            sizeCurve.AddKey(1f, 1.15f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = enableNoise;
            if (enableNoise)
            {
                noise.strength = new ParticleSystem.MinMaxCurve(0.18f);
                noise.frequency = 0.15f;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.2f);
                noise.damping = true;
                noise.quality = ParticleSystemNoiseQuality.Medium;
            }
        }
    }
}
