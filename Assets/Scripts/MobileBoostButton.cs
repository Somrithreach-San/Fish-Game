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
    private readonly Color defaultColor = new Color(1f, 1f, 1f, 0.25f);
    private readonly Color pressedColor = new Color(1f, 1f, 1f, 0.50f);

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

        // AUTO-FIX: Sync size with MobileJoystick if available, otherwise default to 250
        float targetSize = 250f;
        
        if (MobileJoystick.Instance != null && MobileJoystick.Instance.background != null)
        {
             targetSize = MobileJoystick.Instance.background.sizeDelta.x;
        }

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null && (Mathf.Abs(rt.sizeDelta.x - targetSize) > 1))
        {
            rt.sizeDelta = new Vector2(targetSize, targetSize);
            rt.anchoredPosition = new Vector2(-200, 380); // Improved position (Aligned with Joystick)
            Debug.Log($"MobileBoostButton: Auto-synced size to {targetSize}px.");
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
                
                // Sized proportionally like the joystick handle (40% of button size: 100px for a 250px button)
                float iconSize = targetSize * 0.40f;
                iconRt.sizeDelta = new Vector2(iconSize, iconSize);

                Image iconImg = iconRt.GetComponent<Image>();
                if (iconImg != null)
                {
                    iconImg.color = new Color(1f, 1f, 1f, 0.85f);
                }
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        lastPressedFrame = Time.frameCount;
        transform.localScale = originalScale * 0.92f;
        if (buttonImage != null)
        {
            buttonImage.color = pressedColor;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = originalScale;
        if (buttonImage != null)
        {
            buttonImage.color = defaultColor;
        }
    }
    
    private void OnDisable()
    {
        lastPressedFrame = -1;
        transform.localScale = originalScale;
        if (buttonImage != null)
        {
            buttonImage.color = defaultColor;
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
