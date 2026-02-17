using UnityEngine;
using UnityEngine.EventSystems;

public class MobileBoostButton : MonoBehaviour, IPointerDownHandler
{
    public static MobileBoostButton Instance;
    
    private int lastPressedFrame = -1;
    public bool WasPressedThisFrame => lastPressedFrame == Time.frameCount;
    
    private void Start()
    {
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
            
            // Also scale the icon if it exists (Child 0 usually)
            if (transform.childCount > 0)
            {
                RectTransform iconRt = transform.GetChild(0).GetComponent<RectTransform>();
                if (iconRt != null)
                {
                     // Ensure icon fits nicely (approx 45% of button size)
                     float iconSize = targetSize * 0.45f;
                     iconRt.sizeDelta = new Vector2(iconSize, iconSize);
                }
            }
             Debug.Log($"MobileBoostButton: Auto-synced size to {targetSize}px.");
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        lastPressedFrame = Time.frameCount;
    }
    
    private void OnDisable()
    {
        lastPressedFrame = -1;
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
