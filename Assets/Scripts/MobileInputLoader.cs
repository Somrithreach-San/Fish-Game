using UnityEngine;
using Rhinotap.Toolkit;

public class MobileInputLoader : MonoBehaviour
{
    public GameObject mobileInputPrefab;
    
    [Tooltip("If true, the joystick will be shown in the Editor even if not in Simulator mode.")]
    public bool simulateMobileInEditor = false;

    private GameObject instantiatedControls;

    void Start()
    {
        // Listen for Pause Event
        EventManager.StartListening<bool>("gamePaused", OnGamePaused);

        bool isMobile = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // In WebGL, Application.isMobilePlatform evaluates browser userAgent for mobile devices.
        // Never check Touchscreen.current on desktop browsers as touch-enabled laptops report it non-null.
        isMobile = Application.isMobilePlatform;
#elif UNITY_EDITOR
        if (simulateMobileInEditor) isMobile = true;
#else
        isMobile = Application.isMobilePlatform || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
#endif
        
        if (isMobile)
        {
            // Ensure EventSystem exists (Critical for Mobile UI Input)
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Fallback: Load from Resources if prefab is missing
            if (mobileInputPrefab == null)
            {
                mobileInputPrefab = Resources.Load<GameObject>("MobileInputCanvas");
            }
            
            if (mobileInputPrefab != null && MobileJoystick.Instance == null)
            {
                instantiatedControls = Instantiate(mobileInputPrefab);
                EnsureMobileAbilityButton(instantiatedControls);
            }
            else if (mobileInputPrefab == null)
            {
                Debug.LogError("MobileInputLoader: MobileInputCanvas prefab is missing! Please run Tools > Setup Mobile Input.");
            }
        }
        else
        {
            // FORCE CLEANUP: Ensure no mobile controls exist on Desktop
            if (MobileJoystick.Instance != null)
            {
                // Safety Check: Only destroy root if it's the dedicated MobileInputCanvas
                // Otherwise, just destroy the joystick object itself to avoid deleting Main UI
                if (MobileJoystick.Instance.transform.root.name.Contains("MobileInputCanvas"))
                {
                    Destroy(MobileJoystick.Instance.transform.root.gameObject);
                }
                else
                {
                    Destroy(MobileJoystick.Instance.gameObject);
                }
                Debug.Log("MobileInputLoader: Removed Mobile Joystick for Desktop platform.");
            }

            // Also check for Boost Button separately just in case
            if (MobileBoostButton.Instance != null)
            {
                if (MobileBoostButton.Instance.transform.root.name.Contains("MobileInputCanvas"))
                {
                    Destroy(MobileBoostButton.Instance.transform.root.gameObject);
                }
                else
                {
                    Destroy(MobileBoostButton.Instance.gameObject);
                }
            }

            // Also check for Ability Button separately
            if (MobileAbilityButton.Instance != null)
            {
                if (MobileAbilityButton.Instance.transform.root.name.Contains("MobileInputCanvas"))
                {
                    Destroy(MobileAbilityButton.Instance.transform.root.gameObject);
                }
                else
                {
                    Destroy(MobileAbilityButton.Instance.gameObject);
                }
            }
        }
    }

    private void EnsureMobileAbilityButton(GameObject canvasObj)
    {
        if (canvasObj == null) return;
        if (canvasObj.GetComponentInChildren<MobileAbilityButton>(true) != null) return;

        // Create AbilityButton under canvas
        GameObject abilityBtnObj = new GameObject("AbilityButton");
        abilityBtnObj.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = abilityBtnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(150f, 150f);
        rt.anchoredPosition = new Vector2(-110f, -200f);

        abilityBtnObj.AddComponent<CanvasRenderer>();
        MobileAbilityButton abilityBtn = abilityBtnObj.AddComponent<MobileAbilityButton>();
        abilityBtn.BuildUIHierarchyIfNeeded();
        abilityBtn.SyncLayoutWithBoostButton();
    }

    private void OnDestroy()
    {
        // Safe check to prevent errors when quitting the application
        if (EventManager.HasInstance)
        {
            EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        }
    }
    
    private void OnDisable()
    {
        // Handle pooled or temporary disable scenarios
        if (EventManager.HasInstance)
        {
            EventManager.StopListening<bool>("gamePaused", OnGamePaused);
        }
    }

    private void OnGamePaused(bool isPaused)
    {
        if (instantiatedControls != null)
        {
            instantiatedControls.SetActive(!isPaused);
        }
    }
}
