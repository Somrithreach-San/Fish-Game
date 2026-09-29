using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MobileAbilityButton manages the mobile UI ability button for both Ocean ("Vortex Vacuum")
/// and River ("Apex Blitz") special abilities.
/// 
/// - Button size: Sized slightly smaller than the Speed Boost button (~80% of boost size).
/// - Position: Positioned next to the Speed Boost button (to its left), 40% lower in height.
/// - Disabled State: Greyed out and darkened when ability is not yet ready.
/// - Ready State: Solid crisp white icon with glowing border and subtle pulse animation.
/// - Active State: Depleting radial energy ring during the 5s special ability duration.
/// </summary>
public class MobileAbilityButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public static MobileAbilityButton Instance;

    [Header("Sprites")]
    [SerializeField] public Sprite vortexSprite;
    [SerializeField] public Sprite blitzSprite;
    [SerializeField] public Sprite buttonShape;

    [Header("UI References")]
    [SerializeField] private Image buttonImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image iconImage;

    private RectTransform rectTransform;
    private Vector3 baseScale = Vector3.one;
    private float pulseTimer = 0f;
    private bool isPressed = false;

    // Disabled / Inactive styling (Matching Joystick and Speed Boost unactive colors)
    private static readonly Color DisabledBgColor = new Color(1f, 1f, 1f, 0.25f);
    private static readonly Color DisabledIconColor = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color DisabledFillColor = new Color(1f, 1f, 1f, 0.40f);

    // Ready styling
    private static readonly Color ReadyBgColor = new Color(0.08f, 0.40f, 0.55f, 0.45f);
    private static readonly Color ReadyIconColor = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color ReadyFillColor = new Color(0.2f, 1f, 0.5f, 0.90f);

    // Active (5s frenzy) styling
    private static readonly Color ActiveBgColor = new Color(0.6f, 0.45f, 0.05f, 0.50f);
    private static readonly Color ActiveIconColor = new Color(1f, 1f, 1f, 1.0f);
    private static readonly Color ActiveFillColor = new Color(1f, 0.9f, 0.2f, 0.95f);

    private float currentFillAmount = 0f;
    [SerializeField] private float fillSmoothSpeed = 8.0f;

    private Sprite plainCircleSprite;

    private Sprite GetPlainCircleSprite()
    {
        if (plainCircleSprite != null) return plainCircleSprite;
        if (buttonShape != null && !buttonShape.name.Contains("Bubble_Button"))
        {
            plainCircleSprite = buttonShape;
            return plainCircleSprite;
        }

        plainCircleSprite = Resources.Load<Sprite>("circle512");
        if (plainCircleSprite == null) plainCircleSprite = Resources.Load<Sprite>("Knob");
        if (plainCircleSprite == null) plainCircleSprite = Resources.Load<Sprite>("JoystickCircle");
        return plainCircleSprite;
    }

    private void Awake()
    {
        Instance = this;
        rectTransform = GetComponent<RectTransform>();
        if (buttonImage == null) buttonImage = GetComponent<Image>();
    }

    private void Start()
    {
        LoadSprites();
        BuildUIHierarchyIfNeeded();
        SyncLayoutWithBoostButton();
    }

    public void LoadSprites()
    {
        // Load Ocean Vortex Icon
        #if UNITY_EDITOR
        vortexSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Vortex_Ability.png");
        #endif
        if (vortexSprite == null)
        {
            vortexSprite = Resources.Load<Sprite>("Vortex_Ability");
        }

        // Load River Blitz Icon
        #if UNITY_EDITOR
        blitzSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Blitz_Ability.png");
        #endif
        if (blitzSprite == null)
        {
            blitzSprite = Resources.Load<Sprite>("Blitz_Ability");
        }
    }

    public void BuildUIHierarchyIfNeeded()
    {
        Sprite circle = GetPlainCircleSprite();

        // 1. Background Image
        if (buttonImage == null) buttonImage = GetComponent<Image>();
        if (buttonImage == null) buttonImage = gameObject.AddComponent<Image>();
        buttonImage.sprite = circle;
        buttonImage.type = Image.Type.Simple;
        buttonImage.color = DisabledBgColor;

        // 2. Radial Fill Ring (Energy progress & active countdown)
        if (fillImage == null)
        {
            Transform fT = transform.Find("FillRing");
            if (fT == null)
            {
                GameObject fObj = new GameObject("FillRing");
                fObj.transform.SetParent(transform, false);
                RectTransform fRt = fObj.AddComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero;
                fRt.anchorMax = Vector2.one;
                fRt.sizeDelta = Vector2.zero;
                fRt.anchoredPosition = Vector2.zero;

                fillImage = fObj.AddComponent<Image>();
                fillImage.sprite = circle;
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Radial360;
                fillImage.fillOrigin = (int)Image.Origin360.Top;
                fillImage.fillClockwise = true;
                fillImage.color = DisabledFillColor;
                fillImage.raycastTarget = false;
            }
            else
            {
                fillImage = fT.GetComponent<Image>();
            }
        }

        // 3. Center Icon Image (Vortex_Ability / Blitz_Ability)
        if (iconImage == null)
        {
            Transform iT = transform.Find("AbilityIcon");
            if (iT == null)
            {
                GameObject iObj = new GameObject("AbilityIcon");
                iObj.transform.SetParent(transform, false);
                RectTransform iRt = iObj.AddComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0.5f, 0.5f);
                iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.pivot = new Vector2(0.5f, 0.5f);
                iRt.anchoredPosition = Vector2.zero;

                iconImage = iObj.AddComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.color = DisabledIconColor;
            }
            else
            {
                iconImage = iT.GetComponent<Image>();
            }
        }
    }

    /// <summary>
    /// Position and size the ability button relative to MobileBoostButton:
    /// - Size: Sized smaller than the Speed Boost button (~68% of boost size: ~150px).
    /// - Position: To the RIGHT side of the Speed Boost button and BELOW the Speed Boost button (staggered down-right).
    /// </summary>
    public void SyncLayoutWithBoostButton()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null) return;

        // Base boost dimensions
        float boostSize = 220f;
        Vector2 boostPos = new Vector2(-280f, -70f);

        if (MobileBoostButton.Instance != null)
        {
            RectTransform boostRt = MobileBoostButton.Instance.GetComponent<RectTransform>();
            if (boostRt != null)
            {
                boostSize = boostRt.sizeDelta.x > 0 ? boostRt.sizeDelta.x : 220f;
                boostPos = boostRt.anchoredPosition;
            }
        }

        // 1. Button Size: A bit smaller than Speed Boost (~68%: e.g. 150px for 220px boost)
        float abilityButtonSize = boostSize * 0.68f;
        rectTransform.anchorMin = new Vector2(1f, 0.5f);
        rectTransform.anchorMax = new Vector2(1f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(abilityButtonSize, abilityButtonSize);

        // 2. Position: On the RIGHT side of Speed Boost and BELOW Speed Boost (pushed down with it)
        float abilityX = boostPos.x + 170f; // e.g. -280 + 170 = -110px (to the right of Speed Boost)
        float abilityY = boostPos.y - 130f; // e.g. -70 - 130 = -200px (below Speed Boost)

        rectTransform.anchoredPosition = new Vector2(abilityX, abilityY);

        // 3. Center and scale the Ability Icon inside the button (~52% of button size)
        if (iconImage != null)
        {
            RectTransform iconRt = iconImage.rectTransform;
            float iconSize = abilityButtonSize * 0.52f; // e.g. ~78px
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            iconRt.anchoredPosition = Vector2.zero;
        }
    }

    private void Update()
    {
        // Update active sprite for Ocean vs River
        UpdateAbilityIconSprite();

        // Update UI state (Disabled / Ready / Active)
        UpdateVisualState();
    }

    private void UpdateAbilityIconSprite()
    {
        if (iconImage == null) return;

        bool isRiverLevel = LevelManager.IsCurrentLakeLevel;
        Sprite targetSprite = isRiverLevel ? blitzSprite : vortexSprite;

        if (targetSprite == null)
        {
            LoadSprites();
            targetSprite = isRiverLevel ? blitzSprite : vortexSprite;
        }

        if (iconImage.sprite != targetSprite && targetSprite != null)
        {
            iconImage.sprite = targetSprite;
        }
    }

    private void UpdateVisualState()
    {
        PlayerAbilitySystem pas = PlayerAbilitySystem.Instance;
        bool hasPlayer = pas != null;
        bool isReady = hasPlayer && pas.IsAbilityReady;
        bool isActive = hasPlayer && pas.IsAbilityActive;

        float targetFill = 0f;
        Color targetFillColor = DisabledFillColor;

        if (isActive)
        {
            // --- ACTIVE STATE (5s duration) ---
            if (buttonImage != null) buttonImage.color = ActiveBgColor;
            if (iconImage != null) iconImage.color = ActiveIconColor;

            targetFill = pas.ActiveTimerRatio;
            targetFillColor = ActiveFillColor;

            // Pulsing scale during active ability
            if (!isPressed)
            {
                float pulse = 1.0f + Mathf.Sin(Time.time * 8f) * 0.05f;
                transform.localScale = baseScale * pulse;
            }
        }
        else if (isReady)
        {
            // --- READY STATE (100% full energy / x10 streak) ---
            if (buttonImage != null) buttonImage.color = ReadyBgColor;
            if (iconImage != null) iconImage.color = ReadyIconColor;

            targetFill = 1.0f;
            targetFillColor = ReadyFillColor;

            // Pulsing scale when ready
            pulseTimer += Time.deltaTime * 5f;
            if (!isPressed)
            {
                float pulse = 1.0f + Mathf.Sin(pulseTimer) * 0.06f;
                transform.localScale = baseScale * pulse;
            }
        }
        else
        {
            // --- DISABLED / GREYED OUT STATE (Not ready / Charging) ---
            pulseTimer = 0f;
            if (!isPressed)
            {
                transform.localScale = baseScale;
            }

            if (buttonImage != null) buttonImage.color = DisabledBgColor;
            if (iconImage != null) iconImage.color = DisabledIconColor;

            targetFill = hasPlayer ? pas.Energy : 0f;
            targetFillColor = DisabledFillColor;
        }

        // Smooth liquid interpolation for fill gauge (handles both smooth increase & smooth decay)
        if (fillImage != null)
        {
            currentFillAmount = Mathf.MoveTowards(currentFillAmount, targetFill, Time.deltaTime * fillSmoothSpeed);

            fillImage.enabled = (currentFillAmount > 0.005f);
            fillImage.fillAmount = currentFillAmount;
            fillImage.color = targetFillColor;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;

        if (PlayerAbilitySystem.Instance != null && PlayerAbilitySystem.Instance.IsAbilityReady)
        {
            PlayerAbilitySystem.Instance.ActivateAbility();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    private void OnDisable()
    {
        isPressed = false;
        transform.localScale = baseScale;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
