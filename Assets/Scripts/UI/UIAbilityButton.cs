using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UIAbilityButton displays the radial energy gauge, ready glow animation,
/// 5s active countdown ring, and handles mouse/touch activation for the player's ability.
/// </summary>
public class UIAbilityButton : MonoBehaviour, IPointerDownHandler
{
    public static UIAbilityButton Instance;

    [Header("UI Elements")]
    [SerializeField] private Image bgImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image activeRingImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private Text keyHintText;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private float pulseTimer = 0f;
    private static Sprite s_CachedCircleSprite;

    private static Sprite GetCircleSprite()
    {
        if (s_CachedCircleSprite != null) return s_CachedCircleSprite;
        s_CachedCircleSprite = Resources.Load<Sprite>("circle512");
        if (s_CachedCircleSprite == null) s_CachedCircleSprite = Resources.Load<Sprite>("Knob");
        if (s_CachedCircleSprite == null)
        {
            Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color[] colors = new Color[64 * 64];
            float r = 31f;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    colors[y * 64 + x] = (dist <= r) ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(colors);
            tex.Apply();
            s_CachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        }
        return s_CachedCircleSprite;
    }

    private void Awake()
    {
        Instance = this;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void Start()
    {
        BuildUIIfNeeded();
    }

    public void BuildUIIfNeeded()
    {
        Sprite circle = GetCircleSprite();

        if (bgImage == null)
        {
            bgImage = GetComponent<Image>();
            if (bgImage == null) bgImage = gameObject.AddComponent<Image>();
            bgImage.sprite = circle;
            bgImage.color = new Color(0.05f, 0.15f, 0.25f, 0.75f);
        }

        // 1. Fill Image (Radial 360)
        if (fillImage == null)
        {
            Transform fT = transform.Find("FillImage");
            if (fT == null)
            {
                GameObject fObj = new GameObject("FillImage");
                fObj.transform.SetParent(transform, false);
                RectTransform fRt = fObj.AddComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero;
                fRt.anchorMax = Vector2.one;
                fRt.sizeDelta = new Vector2(-8, -8);
                fillImage = fObj.AddComponent<Image>();
                fillImage.sprite = circle;
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Radial360;
                fillImage.fillOrigin = (int)Image.Origin360.Top;
                fillImage.fillClockwise = true;
                fillImage.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            }
            else
            {
                fillImage = fT.GetComponent<Image>();
            }
        }

        // 2. Center Icon (Lightning / Vortex Whirl Icon)
        if (iconImage == null)
        {
            Transform iT = transform.Find("IconImage");
            if (iT == null)
            {
                GameObject iObj = new GameObject("IconImage");
                iObj.transform.SetParent(transform, false);
                RectTransform iRt = iObj.AddComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0.5f, 0.5f);
                iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.sizeDelta = new Vector2(36, 36);
                iconImage = iObj.AddComponent<Image>();
                iconImage.sprite = circle;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage = iT.GetComponent<Image>();
            }
        }

        // 3. Key Hint Text (Desktop "E")
        if (keyHintText == null)
        {
            Transform tT = transform.Find("KeyHint");
            if (tT == null)
            {
                GameObject tObj = new GameObject("KeyHint");
                tObj.transform.SetParent(transform, false);
                RectTransform tRt = tObj.AddComponent<RectTransform>();
                tRt.anchorMin = new Vector2(0.5f, 0.5f);
                tRt.anchorMax = new Vector2(0.5f, 0.5f);
                tRt.sizeDelta = new Vector2(50, 30);
                tRt.anchoredPosition = new Vector2(0, 0);
                keyHintText = tObj.AddComponent<Text>();
                keyHintText.text = "E";
                keyHintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                keyHintText.alignment = TextAnchor.MiddleCenter;
                keyHintText.fontSize = 20;
                keyHintText.fontStyle = FontStyle.Bold;
                keyHintText.color = Color.white;
                Shadow shadow = tObj.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.8f);
                shadow.effectDistance = new Vector2(1, -1);
            }
            else
            {
                keyHintText = tT.GetComponent<Text>();
            }
        }
    }

    private void Update()
    {
        bool isGameOver = (GameManager.instance != null && GameManager.instance.IsGameOver) || LevelManager.IsLevelCompleted;
        if (isGameOver)
        {
            transform.localScale = Vector3.one;
            return;
        }

        if (PlayerAbilitySystem.Instance == null) return;

        PlayerAbilitySystem pas = PlayerAbilitySystem.Instance;

        bool isMobile = Application.isMobilePlatform || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;

        if (pas.IsAbilityActive)
        {
            // Active Ability state: Radial ring drains over 5s with glowing yellow/cyan border
            if (fillImage != null)
            {
                fillImage.fillAmount = pas.ActiveTimerRatio;
                fillImage.color = new Color(1f, 0.9f, 0.2f, 0.95f);
            }

            if (keyHintText != null)
            {
                keyHintText.enabled = true;
                keyHintText.text = $"{pas.ActiveTimeRemaining:F1}s";
                keyHintText.color = new Color(1f, 0.95f, 0.3f, 1f);
                keyHintText.fontSize = isMobile ? 26 : 18;
            }

            // Pulsing scale during active ability
            float scale = 1.0f + Mathf.Sin(Time.time * 8f) * 0.06f;
            transform.localScale = Vector3.one * scale;
        }
        else
        {
            // Charging state: radial progress 0.0 to 1.0
            float energy = pas.Energy;
            if (fillImage != null)
            {
                fillImage.fillAmount = energy;
                fillImage.color = (energy >= 1.0f) ? new Color(0.2f, 1f, 0.45f, 1f) : new Color(0.2f, 0.8f, 1f, 0.8f);
            }

            if (pas.IsAbilityReady)
            {
                // Pulsing glow when 100% full & ready
                pulseTimer += Time.deltaTime * 5f;
                float scale = 1.0f + Mathf.Sin(pulseTimer) * 0.09f;
                transform.localScale = Vector3.one * scale;

                if (keyHintText != null)
                {
                    keyHintText.enabled = true;
                    keyHintText.text = isMobile ? "READY!" : "RMB";
                    keyHintText.color = new Color(0.2f, 1f, 0.45f, 1f);
                    keyHintText.fontSize = isMobile ? 24 : 19;
                }

                if (bgImage != null)
                {
                    bgImage.color = new Color(0.08f, 0.45f, 0.25f, 0.9f);
                }
            }
            else
            {
                transform.localScale = Vector3.one;

                if (keyHintText != null)
                {
                    keyHintText.enabled = true;
                    int pct = Mathf.RoundToInt(energy * 100f);
                    keyHintText.text = (pct > 0) ? $"{pct}%" : (isMobile ? "" : "E");
                    keyHintText.color = new Color(0.85f, 0.95f, 1f, 0.9f);
                    keyHintText.fontSize = isMobile ? 24 : 16;
                }

                if (bgImage != null)
                {
                    bgImage.color = new Color(0.05f, 0.15f, 0.25f, 0.75f);
                }
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (PlayerAbilitySystem.Instance != null)
        {
            PlayerAbilitySystem.Instance.ActivateAbility();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
