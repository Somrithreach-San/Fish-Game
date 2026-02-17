using UnityEngine;
using Rhinotap.Toolkit;

public class StartupValidator : MonoBehaviour
{
    [SerializeField] private bool checkResources = true;
    [SerializeField] private bool checkMobileUi = true;
    [SerializeField] private bool checkPoolManager = true;
    [SerializeField] private bool checkEventManager = true;
    [SerializeField] private bool checkGameManager = true;
    [SerializeField] private bool enablePerformanceMonitor = true;

    private void Awake()
    {
        if (checkGameManager && GameManager.instance == null)
        {
            Debug.LogError("StartupValidator: GameManager instance is missing");
        }

        if (checkEventManager && !EventManager.HasInstance)
        {
            Debug.LogError("StartupValidator: EventManager instance is missing");
        }

        if (checkPoolManager && ObjectPoolManager.Instance == null)
        {
            Debug.LogWarning("StartupValidator: ObjectPoolManager not found, creating one");
            var obj = new GameObject("ObjectPoolManager");
            obj.AddComponent<ObjectPoolManager>();
        }

        if (checkResources)
        {
            var pauseIcon = Resources.Load<Sprite>("pause_icon");
            if (pauseIcon == null)
            {
                Debug.LogWarning("StartupValidator: pause_icon not found in Resources");
            }
        }

        if (checkMobileUi)
        {
            var mobileCanvas = Resources.Load<GameObject>("MobileInputCanvas");
            if (Application.isMobilePlatform && mobileCanvas == null)
            {
                Debug.LogWarning("StartupValidator: MobileInputCanvas not found in Resources for mobile");
            }
        }
        
        if (enablePerformanceMonitor && PerformanceMonitor.Instance == null)
        {
            var perfObj = new GameObject("PerformanceMonitor");
            perfObj.AddComponent<PerformanceMonitor>();
        }
    }
}
