using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MobileJoystick : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
{
    public static MobileJoystick Instance;
    
    [Header("Settings")]
    public float handleRange = 1f;
    public bool hideOnDesktop = true;

    [Header("References")]
    public RectTransform background;
    public RectTransform handle;

    [Header("Visuals")]
    [SerializeField] public Sprite buttonShape;
    [SerializeField] public Sprite handleShape;

    public Vector2 InputDirection { get; private set; }

    private Sprite GetPlainCircleSprite()
    {
        // Use the plain circle asset (not the glass Bubble_Button)
        // Try 512px first for best quality, then fallbacks
        Sprite s = Resources.Load<Sprite>("circle512");
        if (s != null) return s;

        // Try to find in all loaded assets
        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite sp in all)
        {
            if (sp.name == "circle512" || sp.name == "circle256" || sp.name == "circle128") return sp;
        }
        return null;
    }

    private Sprite GetHandleSprite()
    {
        if (handleShape != null) return handleShape;
        Sprite[] sprites = Resources.LoadAll<Sprite>("JoystickCircle");
        if (sprites != null && sprites.Length > 0) return sprites[0];
        Sprite single = Resources.Load<Sprite>("JoystickCircle");
        if (single != null) return single;
        Sprite knob = Resources.Load<Sprite>("Knob");
        if (knob != null) return knob;
        Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite s in allSprites)
        {
            if (s.name.Contains("JoystickCircle") || s.name.Contains("Knob")) return s;
        }
        return null;
    }

    private Image handleImage;
    private readonly Color handleIdleColor = new Color(1f, 1f, 1f, 0.55f);
    private readonly Color handleActiveColor = new Color(1f, 1f, 1f, 1.0f);

    private void Awake()
    {
        Instance = this;
        
        if (background == null) background = GetComponent<RectTransform>();
        if (handle == null && transform.childCount > 0) handle = transform.GetChild(0).GetComponent<RectTransform>();

        // 1. Outer Joystick Ring: Plain circle with soft transparency (NOT glass)
        if (background != null)
        {
            Image bgImg = background.GetComponent<Image>();
            if (bgImg != null)
            {
                Sprite circle = GetPlainCircleSprite();
                if (circle != null) bgImg.sprite = circle;
                // Soft white with low opacity - classic transparent joystick look
                bgImg.color = new Color(1f, 1f, 1f, 0.25f);
                bgImg.type = Image.Type.Simple;
            }
        }

        // 2. Inner Handle: Slightly more visible white circle (goes full white when used)
        if (handle != null)
        {
            handleImage = handle.GetComponent<Image>();
            if (handleImage != null)
            {
                Sprite circle = GetHandleSprite();
                if (circle != null) handleImage.sprite = circle;
                handleImage.color = handleIdleColor;
            }
        }
        
        // Determine if we should show or hide
        bool shouldShow = false;

        // Fix: Removed Input.touchSupported to prevent joystick from appearing on Desktop devices with touch screens
#if UNITY_WEBGL && !UNITY_EDITOR
        if (Application.isMobilePlatform)
        {
            shouldShow = true;
        }
#else
        if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld)
        {
            shouldShow = true;
        }
#endif
        else
        {
            // Desktop / Editor Logic
            #if UNITY_EDITOR
            // Check if Loader is forcing simulation
            MobileInputLoader loader = Object.FindFirstObjectByType<MobileInputLoader>();
            if (loader != null)
            {
                // If a Loader exists, we assume it manages our existence (or we are debugging).
                // This ensures visibility in Simulator where Input.touchSupported can be flaky.
                shouldShow = true;
            }
            #endif
        }

        if (hideOnDesktop && !shouldShow)
        {
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(true);
            
            // AUTO-FIX: Enforce standard mobile sizing & lowered position aligned with Speed Boost
            if (background != null)
            {
                background.anchorMin = new Vector2(0f, 0.5f);
                background.anchorMax = new Vector2(0f, 0.5f);
                background.pivot = new Vector2(0.5f, 0.5f);
                background.sizeDelta = new Vector2(240f, 240f);
                // Lowered comfortably on the left side (aligned with Speed Boost button)
                background.anchoredPosition = new Vector2(220f, -70f); 
                
                if (handle != null)
                {
                    handle.anchorMin = new Vector2(0.5f, 0.5f);
                    handle.anchorMax = new Vector2(0.5f, 0.5f);
                    handle.pivot = new Vector2(0.5f, 0.5f);
                    handle.anchoredPosition = Vector2.zero;
                    handle.sizeDelta = new Vector2(100f, 100f); // Increased inner joystick circle
                }
                
                // Improve responsiveness: Reduce travel distance
                handleRange = 0.5f; 
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 position = Vector2.zero;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventData.pressEventCamera, out position))
        {
            position.x = (position.x / background.sizeDelta.x);
            position.y = (position.y / background.sizeDelta.y);

            InputDirection = new Vector2(position.x * 2, position.y * 2);
            InputDirection = (InputDirection.magnitude > 1.0f) ? InputDirection.normalized : InputDirection;

            // Move Handle
            if (handle != null)
            {
                handle.anchoredPosition = new Vector2(
                    InputDirection.x * (background.sizeDelta.x / 2) * handleRange,
                    InputDirection.y * (background.sizeDelta.y / 2) * handleRange
                );
            }

            // Make handle full white while active/dragged
            if (handleImage != null)
            {
                handleImage.color = handleActiveColor;
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (handleImage != null)
        {
            handleImage.color = handleActiveColor;
        }
        OnDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetJoystick();
    }

    private void OnDisable()
    {
        ResetJoystick();
    }

    private void ResetJoystick()
    {
        InputDirection = Vector2.zero;
        if (handle != null)
            handle.anchoredPosition = Vector2.zero;
        if (handleImage != null)
            handleImage.color = handleIdleColor;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
