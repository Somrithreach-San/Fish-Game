using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MobileBoostButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public static MobileBoostButton Instance;
    
    [Header("Visuals")]
    [SerializeField] public Sprite buttonShape;

    private int lastPressedFrame = -1;
    public bool WasPressedThisFrame => lastPressedFrame == Time.frameCount;

    private Vector3 originalScale = Vector3.one;
    private Image buttonImage;
    private Image iconImage;
    private bool isPressed = false;

    private readonly Color defaultColor = new Color(1f, 1f, 1f, 0.25f);
    private readonly Color pressedColor = new Color(1f, 1f, 1f, 0.50f);

    private readonly Color iconIdleColor = new Color(1f, 1f, 1f, 0.55f);
    private readonly Color iconActiveColor = new Color(1f, 1f, 1f, 1.0f);

    private Sprite GetPlainCircleSprite()
    {
        if (buttonShape != null && !buttonShape.name.Contains("Bubble_Button")) return buttonShape;
        Sprite s = Resources.Load<Sprite>("circle512");
        if (s != null) return s;

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite sp in all)
        {
            if (sp.name == "circle512" || sp.name == "circle256" || sp.name == "circle128") return sp;
        }
        return null;
    }

    private void Awake()
    {
        Instance = this;
        originalScale = transform.localScale;
        buttonImage = GetComponent<Image>();
    }
    
    private void Start()
    {
        // Apply Joystick-like styling (clean translucent circle instead of glossy glass bubble)
        if (buttonImage == null) buttonImage = GetComponent<Image>();
        if (buttonImage != null)
        {
            Sprite circle = GetPlainCircleSprite();
            if (circle != null)
            {
                buttonImage.sprite = circle;
                buttonImage.type = Image.Type.Simple;
            }
            buttonImage.color = defaultColor;
        }

        // Sizing & Positioning: Lowered and aligned vertically with MobileJoystick
        float targetSize = 220f;

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(targetSize, targetSize);
            // Positioned to the left of the Ability button, aligned horizontally/vertically with Joystick
            rt.anchoredPosition = new Vector2(-280f, -70f);
            Debug.Log($"MobileBoostButton: Set size to {targetSize}px at {rt.anchoredPosition}.");
        }

        if (MobileAbilityButton.Instance != null)
        {
            MobileAbilityButton.Instance.SyncLayoutWithBoostButton();
        }

        // Scale and perfectly center the icon inside the joystick-style button
        if (transform.childCount > 0)
        {
            RectTransform iconRt = transform.GetChild(0).GetComponent<RectTransform>();
            if (iconRt != null)
            {
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                iconRt.pivot = new Vector2(0.5f, 0.5f);
                iconRt.anchoredPosition = Vector2.zero; // Perfectly center in button
                
                // Sized prominently for clear visibility on mobile (62% of button size: ~136px for a 220px button)
                float iconSize = targetSize * 0.62f;
                iconRt.sizeDelta = new Vector2(iconSize, iconSize);

                iconImage = iconRt.GetComponent<Image>();
                if (iconImage != null)
                {
                    iconImage.preserveAspect = true;
                    iconImage.color = iconIdleColor;
                }
            }
        }
    }

    private void Update()
    {
        bool isBoosting = isPressed;
        if (!isBoosting && GameManager.instance?.playerGameObject != null)
        {
            PlayerController pc = GameManager.instance.playerGameObject.GetComponent<PlayerController>();
            if (pc != null && pc.IsBoosting)
            {
                isBoosting = true;
            }
        }

        if (iconImage != null)
        {
            iconImage.color = isBoosting ? iconActiveColor : iconIdleColor;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        lastPressedFrame = Time.frameCount;
        if (iconImage != null)
        {
            iconImage.color = iconActiveColor;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }
    
    private void OnDisable()
    {
        isPressed = false;
        lastPressedFrame = -1;
        if (buttonImage != null)
        {
            buttonImage.color = defaultColor;
        }
        if (iconImage != null)
        {
            iconImage.color = iconIdleColor;
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
