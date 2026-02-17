using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Vector3 originalScale;
    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float transitionSpeed = 15f;
    private bool isHovered = false;
    private bool initialized = false;

    private void Awake()
    {
        if (!initialized)
        {
            originalScale = transform.localScale;
            initialized = true;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        // Optional: Play sound here if we had a reference to an AudioManager
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    private void Update()
    {
        // Smooth Scale Logic
        Vector3 target = isHovered ? originalScale * hoverScale : originalScale;
        
        if (Vector3.Distance(transform.localScale, target) > 0.001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, target, Time.unscaledDeltaTime * transitionSpeed);
        }
    }
    
    private void OnDisable()
    {
        // Reset on disable to prevent getting stuck in scaled state
        if (initialized)
        {
            transform.localScale = originalScale;
        }
        isHovered = false;
    }
}
