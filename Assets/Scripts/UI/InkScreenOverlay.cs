using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the polished, high-fidelity squid ink screen blinding effect.
/// Features multi-layered organic splatters, wet glossy liquid rim highlights,
/// dynamic impact scale-punch, viscous gravity dripping, and an ocean wash-out dissolve.
/// </summary>
public class InkScreenOverlay : MonoBehaviour
{
    private static InkScreenOverlay _instance;
    public static InkScreenOverlay Instance => _instance;
    public static bool IsBlindingActive => _instance != null && _instance.activeBlindingCoroutine != null && _instance.canvasGroup != null && _instance.canvasGroup.alpha > 0.01f;
    public static float BlindingAlpha => (_instance != null && _instance.canvasGroup != null && _instance.activeBlindingCoroutine != null) ? _instance.canvasGroup.alpha : 0f;

    private struct SplatDef
    {
        public Vector2 pos;
        public float radius;
        public int arms;
        public float intensity;
        public float noiseScale;
        public SplatDef(Vector2 p, float r, int a, float i, float ns)
        {
            pos = p; radius = r; arms = a; intensity = i; noiseScale = ns;
        }
    }

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RawImage inkOverlayImage;
    private RectTransform imageRectTransform;
    private Material inkMaterial;
    private Texture2D proceduralInkTexture;
    private Coroutine activeBlindingCoroutine;

    private static readonly int PropDissolve = Shader.PropertyToID("_Dissolve");
    private static readonly int PropDripOffset = Shader.PropertyToID("_DripOffset");
    private static readonly int PropAlpha = Shader.PropertyToID("_Alpha");

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        SetupCanvasAndVisuals();
    }

    private void SetupCanvasAndVisuals()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450; // In front of standard HUD elements but behind modal dialogs

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        // Visual Image Container
        GameObject imageObj = new GameObject("InkBlindingLayer");
        imageObj.transform.SetParent(transform, false);

        imageRectTransform = imageObj.AddComponent<RectTransform>();
        imageRectTransform.anchorMin = Vector2.zero;
        imageRectTransform.anchorMax = Vector2.one;
        imageRectTransform.sizeDelta = Vector2.zero;
        imageRectTransform.pivot = new Vector2(0.5f, 0.5f);

        inkOverlayImage = imageObj.AddComponent<RawImage>();

        // Material with custom liquid ink shader
        Shader inkShader = Shader.Find("Custom/ScreenInkOverlay");
        if (inkShader != null)
        {
            inkMaterial = new Material(inkShader);
            inkOverlayImage.material = inkMaterial;
        }

        GeneratePolishedInkTexture();
        inkOverlayImage.texture = proceduralInkTexture;
    }

    /// <summary>
    /// Procedurally bakes a rich, multi-layered organic squid ink texture (512x512).
    /// Stores:
    ///   R: Base ink thickness / opacity density
    ///   G: Wet liquid rim & specular bevel highlight
    ///   B: Micro-splatters & satellite droplet clusters
    ///   A: Full silhouette coverage
    /// </summary>
    private void GeneratePolishedInkTexture()
    {
        int width = 512;
        int height = 512;
        proceduralInkTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        proceduralInkTexture.name = "ProceduralSquidInkMap";
        proceduralInkTexture.wrapMode = TextureWrapMode.Clamp;
        proceduralInkTexture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        Vector2 center = new Vector2(0.5f, 0.5f);

        SplatDef[] splats = new SplatDef[]
        {
            // Perimeter framing blotches
            new SplatDef(new Vector2(0.14f, 0.16f), 0.22f, 5, 0.96f, 9.0f),
            new SplatDef(new Vector2(0.86f, 0.20f), 0.24f, 6, 0.95f, 8.5f),
            new SplatDef(new Vector2(0.18f, 0.84f), 0.25f, 6, 0.97f, 8.0f),
            new SplatDef(new Vector2(0.84f, 0.82f), 0.23f, 5, 0.94f, 9.5f),
            // Edge bleeders
            new SplatDef(new Vector2(0.04f, 0.50f), 0.19f, 4, 0.92f, 10.0f),
            new SplatDef(new Vector2(0.96f, 0.52f), 0.20f, 4, 0.93f, 10.0f),
            new SplatDef(new Vector2(0.50f, 0.95f), 0.18f, 5, 0.91f, 9.0f),
            new SplatDef(new Vector2(0.52f, 0.05f), 0.17f, 4, 0.90f, 9.0f),
            // Floating mid-field splashes (offset from exact center to maintain clear peephole visibility)
            new SplatDef(new Vector2(0.32f, 0.38f), 0.14f, 4, 0.88f, 12.0f),
            new SplatDef(new Vector2(0.68f, 0.64f), 0.15f, 5, 0.89f, 11.5f),
            new SplatDef(new Vector2(0.70f, 0.34f), 0.12f, 3, 0.85f, 13.0f),
            new SplatDef(new Vector2(0.28f, 0.66f), 0.13f, 4, 0.86f, 12.5f)
        };

        // Satellite droplet cluster locations
        Vector2[] droplets = new Vector2[]
        {
            new Vector2(0.36f, 0.22f), new Vector2(0.42f, 0.18f), new Vector2(0.64f, 0.22f),
            new Vector2(0.78f, 0.44f), new Vector2(0.22f, 0.45f), new Vector2(0.38f, 0.78f),
            new Vector2(0.62f, 0.82f), new Vector2(0.74f, 0.72f), new Vector2(0.26f, 0.28f),
            new Vector2(0.76f, 0.26f), new Vector2(0.55f, 0.38f), new Vector2(0.44f, 0.62f)
        };
        float[] dropletRadii = new float[]
        {
            0.035f, 0.022f, 0.028f, 0.024f, 0.030f, 0.026f, 0.032f, 0.025f, 0.018f, 0.020f, 0.024f, 0.022f
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / width;
                float v = (float)y / height;
                Vector2 uv = new Vector2(u, v);

                // 1. Perimeter Vignette (Dark corners and edges)
                float distFromCenter = Vector2.Distance(uv, center) * 1.414f; // 0 at center, ~1 at corner
                float edgeVignette = Mathf.Clamp01(Mathf.Pow(distFromCenter, 2.6f) * 0.90f);

                // 2. Starburst Ink Splats with tentacles
                float splatSum = 0f;
                float rimGlow = 0f;

                for (int s = 0; s < splats.Length; s++)
                {
                    SplatDef splat = splats[s];
                    Vector2 delta = uv - splat.pos;
                    float dist = delta.magnitude;

                    if (dist < splat.radius * 1.5f)
                    {
                        float angle = Mathf.Atan2(delta.y, delta.x);
                        // Tentacle spikes around the perimeter
                        float armWave = Mathf.Sin(angle * splat.arms) * 0.22f;
                        float noise = (Mathf.PerlinNoise(u * splat.noiseScale + s * 7.1f, v * splat.noiseScale + s * 7.1f) - 0.5f) * 0.32f;
                        float effectiveRadius = splat.radius * (1.0f + armWave + noise);

                        if (dist < effectiveRadius)
                        {
                            float normDist = dist / effectiveRadius;
                            float coreFalloff = Mathf.SmoothStep(1.0f, 0.0f, normDist);
                            splatSum = Mathf.Max(splatSum, coreFalloff * splat.intensity);

                            // Rim highlight around the boundary of the splat
                            float rim = Mathf.Exp(-Mathf.Pow((normDist - 0.85f) * 6.0f, 2.0f));
                            rimGlow = Mathf.Max(rimGlow, rim * 0.75f);
                        }
                    }
                }

                // 3. Discrete Satellite Droplets
                float dropletSum = 0f;
                for (int d = 0; d < droplets.Length; d++)
                {
                    float dDist = Vector2.Distance(uv, droplets[d]);
                    float r = dropletRadii[d];
                    if (dDist < r)
                    {
                        float dFalloff = Mathf.SmoothStep(1.0f, 0.0f, dDist / r);
                        dropletSum = Mathf.Max(dropletSum, dFalloff * 0.92f);

                        // Droplet wet rim
                        float dRim = Mathf.Exp(-Mathf.Pow((dDist / r - 0.75f) * 5.0f, 2.0f));
                        rimGlow = Mathf.Max(rimGlow, dRim * 0.80f);
                    }
                }

                // 4. Fine Organic Noise / Fluid Fibers
                float fineNoise = Mathf.PerlinNoise(u * 24.0f + 5.0f, v * 24.0f + 5.0f);
                float fineTexture = (fineNoise > 0.65f) ? ((fineNoise - 0.65f) / 0.35f) * 0.25f : 0f;

                // Total Ink Density
                float totalDensity = Mathf.Clamp01(edgeVignette + splatSum + dropletSum + fineTexture);

                // Preserve a soft, clear peephole near the center (dist < 0.26)
                if (distFromCenter < 0.38f)
                {
                    float peepholeMask = Mathf.SmoothStep(0.0f, 1.0f, (distFromCenter - 0.12f) / 0.26f);
                    totalDensity *= Mathf.Clamp(peepholeMask, 0.18f, 1.0f);
                }

                // Channel Encoding:
                // R: Base ink thickness
                // G: Specular wet rim
                // B: Droplets
                // A: Overall silhouette
                pixels[y * width + x] = new Color(
                    totalDensity,
                    Mathf.Clamp01(rimGlow * totalDensity),
                    Mathf.Clamp01(dropletSum),
                    Mathf.Clamp01(totalDensity * 1.1f)
                );
            }
        }

        proceduralInkTexture.SetPixels(pixels);
        proceduralInkTexture.Apply();
    }

    /// <summary>
    /// Triggers the blinding screen effect for the specified duration.
    /// </summary>
    public static void TriggerBlinding(float duration = 4.0f)
    {
        if (_instance == null)
        {
            GameObject overlayObj = new GameObject("InkScreenOverlay_AutoCreated");
            _instance = overlayObj.AddComponent<InkScreenOverlay>();
        }

        _instance.StartBlindingRoutine(duration);
    }

    private void StartBlindingRoutine(float duration)
    {
        if (activeBlindingCoroutine != null)
        {
            StopCoroutine(activeBlindingCoroutine);
        }
        activeBlindingCoroutine = StartCoroutine(BlindingRoutine(duration));
    }

    private IEnumerator BlindingRoutine(float duration)
    {
        if (canvasGroup == null) yield break;

        // 1. Physical Impact Punch & Micro Camera Shake
        if (GameManager.instance != null)
        {
            GameManager.instance.CameraShake(0.22f, 3.2f, 1.5f);
        }

        // Initialize shader parameters
        if (inkMaterial != null)
        {
            inkMaterial.SetFloat(PropDissolve, 0f);
            inkMaterial.SetFloat(PropDripOffset, 0f);
            inkMaterial.SetFloat(PropAlpha, 1.0f);
        }

        // Phase 1: Splat Impact Punch (0.16s snappy pop)
        float elapsed = 0f;
        float punchDuration = 0.16f;
        canvasGroup.alpha = 1.0f;

        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / punchDuration;

            // Elastic scale pop: Expands rapidly to 1.10x then settles back to 1.0x
            float scale = Mathf.Lerp(1.10f, 1.0f, Mathf.Sin(t * Mathf.PI * 0.5f));
            if (imageRectTransform != null)
            {
                imageRectTransform.localScale = new Vector3(scale, scale, 1.0f);
            }

            if (inkMaterial != null)
            {
                inkMaterial.SetFloat(PropAlpha, Mathf.Clamp01(t * 1.5f));
            }

            yield return null;
        }

        if (imageRectTransform != null)
        {
            imageRectTransform.localScale = Vector3.one;
        }

        // Phase 2: Viscous Dripping & Sustain
        float sustainDuration = Mathf.Max(0.8f, duration - 1.6f);
        elapsed = 0f;

        while (elapsed < sustainDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sustainDuration;

            // Subtle fluid gravity drip: ink slides downward slightly on the screen
            if (inkMaterial != null)
            {
                float drip = Mathf.SmoothStep(0f, 0.022f, t);
                inkMaterial.SetFloat(PropDripOffset, drip);
                inkMaterial.SetFloat(PropAlpha, 1.0f);
            }

            yield return null;
        }

        // Phase 3: Oceanic Washout Dissolve (1.3s fluid wash-away)
        float washDuration = 1.35f;
        elapsed = 0f;

        while (elapsed < washDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / washDuration;

            // Organically dissolve ink from thin edges to thick cores
            if (inkMaterial != null)
            {
                float dissolve = Mathf.SmoothStep(0f, 1.0f, t);
                inkMaterial.SetFloat(PropDissolve, dissolve);
                inkMaterial.SetFloat(PropAlpha, Mathf.Lerp(1.0f, 0.0f, Mathf.Pow(t, 1.5f)));
            }
            canvasGroup.alpha = Mathf.Lerp(1.0f, 0f, t * t);

            yield return null;
        }

        canvasGroup.alpha = 0f;
        if (inkMaterial != null)
        {
            inkMaterial.SetFloat(PropAlpha, 0f);
            inkMaterial.SetFloat(PropDissolve, 1.0f);
            inkMaterial.SetFloat(PropDripOffset, 0f);
        }

        activeBlindingCoroutine = null;
    }

    private void OnDestroy()
    {
        if (proceduralInkTexture != null)
        {
            Destroy(proceduralInkTexture);
            proceduralInkTexture = null;
        }
        if (inkMaterial != null)
        {
            Destroy(inkMaterial);
            inkMaterial = null;
        }
    }
}
