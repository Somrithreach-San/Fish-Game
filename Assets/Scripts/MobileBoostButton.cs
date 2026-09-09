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

    private Sprite GetBubbleSprite()
    {
        if (buttonShape != null) return buttonShape;
        Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite s in allSprites)
        {
            if (s.name.Contains("Bubble_Button")) return s;
        }
        return null;
    }

    private void Awake()
    {
        Instance = this;
        originalScale = transform.localScale;
    }
    
    private void Start()
    {
        // Apply Glass Bubble Styling
        Image img = GetComponent<Image>();
        if (img != null)
        {
            Sprite bubble = GetBubbleSprite();
            if (bubble != null)
            {
                img.sprite = bubble;
                img.type = Image.Type.Simple;
            }
            img.color = Color.white;
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

        // Scale and perfectly center the icon inside the glass bubble button
        if (transform.childCount > 0)
        {
            RectTransform iconRt = transform.GetChild(0).GetComponent<RectTransform>();
            if (iconRt != null)
            {
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                iconRt.pivot = new Vector2(0.5f, 0.5f);
                iconRt.anchoredPosition = Vector2.zero; // Perfectly center in bubble
                
                // Reduced icon size (32% of button size: ~80px for a 250px button)
                float iconSize = targetSize * 0.32f;
                iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        lastPressedFrame = Time.frameCount;
        transform.localScale = originalScale * 0.92f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = originalScale;
    }
    
    private void OnDisable()
    {
        lastPressedFrame = -1;
        transform.localScale = originalScale;
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
