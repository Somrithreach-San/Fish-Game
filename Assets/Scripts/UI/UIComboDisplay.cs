using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIComboDisplay presents the Eating Combo Streak banner (x2, x3, x4, x5 MAX FRENZY!)
/// and decay timer bar right under the player's XP bar.
/// </summary>
public class UIComboDisplay : MonoBehaviour
{
    public static UIComboDisplay Instance;

    [Header("UI References")]
    [SerializeField] private Text comboText;
    [SerializeField] private Image timerBar;
    [SerializeField] private Image bgPanel;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private int lastStreak = 1;
    private float popTimer = 0f;

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
        if (bgPanel == null)
        {
            bgPanel = GetComponent<Image>();
            if (bgPanel == null) bgPanel = gameObject.AddComponent<Image>();
            bgPanel.color = new Color(0f, 0f, 0f, 0.45f);
        }

        if (comboText == null)
        {
            Transform tT = transform.Find("ComboText");
            if (tT == null)
            {
                GameObject tObj = new GameObject("ComboText");
                tObj.transform.SetParent(transform, false);
                RectTransform tRt = tObj.AddComponent<RectTransform>();
                tRt.anchorMin = new Vector2(0f, 0.35f);
                tRt.anchorMax = new Vector2(1f, 1f);
                tRt.sizeDelta = Vector2.zero;
                tRt.anchoredPosition = Vector2.zero;
                comboText = tObj.AddComponent<Text>();
                Font khmerFont = Resources.Load<Font>("KhmerUI") ?? Resources.Load<Font>("lmns1");
                comboText.font = khmerFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                comboText.alignment = TextAnchor.MiddleCenter;
                comboText.fontSize = 22;
                comboText.fontStyle = FontStyle.Bold;
                comboText.color = new Color(1f, 0.85f, 0.2f, 1f);

                Shadow shadow = tObj.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.9f);
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
            }
            else
            {
                comboText = tT.GetComponent<Text>();
            }
        }

        if (timerBar == null)
        {
            Transform bT = transform.Find("TimerBar");
            if (bT == null)
            {
                GameObject bObj = new GameObject("TimerBar");
                bObj.transform.SetParent(transform, false);
                RectTransform bRt = bObj.AddComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.05f, 0.08f);
                bRt.anchorMax = new Vector2(0.95f, 0.30f);
                bRt.sizeDelta = Vector2.zero;
                bRt.anchoredPosition = Vector2.zero;
                timerBar = bObj.AddComponent<Image>();
                timerBar.type = Image.Type.Filled;
                timerBar.fillMethod = Image.FillMethod.Horizontal;
                timerBar.fillOrigin = (int)Image.OriginHorizontal.Left;
                timerBar.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            }
            else
            {
                timerBar = bT.GetComponent<Image>();
            }
        }
    }

    private void Update()
    {
        bool isGameOver = (GameManager.instance != null && GameManager.instance.IsGameOver) || LevelManager.IsLevelCompleted;
        if (isGameOver)
        {
            transform.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            return;
        }

        if (PlayerAbilitySystem.Instance == null)
        {
            transform.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            return;
        }

        PlayerAbilitySystem pas = PlayerAbilitySystem.Instance;
        int streak = pas.ComboStreak;

        if (streak > 1)
        {
            // Pop effect when streak increases
            if (streak > lastStreak)
            {
                popTimer = 0.25f;
            }
            lastStreak = streak;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1.0f, Time.deltaTime * 8f);
            }

            // Update text & colors
            if (comboText != null)
            {
                string khmerNum = KhmerNumberUtils.ToKhmerNumber(streak);
                if (streak >= 10)
                {
                    comboText.text = $"x{khmerNum} MAX FRENZY!";
                    comboText.color = new Color(1f, 0.3f, 0.2f, 1f); // Vibrant fiery red/orange
                }
                else
                {
                    comboText.text = $"x{khmerNum} COMBO!";
                    if (streak >= 7)
                    {
                        comboText.color = new Color(1f, 0.5f, 0.1f, 1f); // Vibrant deep orange
                    }
                    else if (streak >= 4)
                    {
                        comboText.color = new Color(1f, 0.75f, 0.1f, 1f); // Golden amber
                    }
                    else
                    {
                        comboText.color = new Color(1f, 0.9f, 0.3f, 1f); // Bright yellow
                    }
                }
            }

            // Update decay timer fill
            if (timerBar != null)
            {
                timerBar.fillAmount = pas.StreakTimerRatio;
                timerBar.color = (streak >= 10) ? new Color(1f, 0.4f, 0.2f, 1f) : new Color(0.2f, 0.9f, 1f, 1f);
            }

            // Pop bounce animation & continuous pulse at x10
            if (streak >= 10)
            {
                float pulse = 1.0f + Mathf.Sin(Time.unscaledTime * 7.0f) * 0.14f;
                transform.localScale = Vector3.one * pulse;
            }
            else if (popTimer > 0f)
            {
                popTimer -= Time.deltaTime;
                float scale = 1.0f + Mathf.Sin(popTimer * 12f) * 0.15f;
                transform.localScale = Vector3.one * scale;
            }
            else
            {
                transform.localScale = Vector3.one;
            }
        }
        else
        {
            lastStreak = 1;
            transform.localScale = Vector3.one;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, Time.deltaTime * 6f);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
