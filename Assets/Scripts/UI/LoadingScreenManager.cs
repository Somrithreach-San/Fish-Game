using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Rhinotap
{
    /// <summary>
    /// Universal loading screen manager.
    /// Displays a sleek dark ocean overlay with the animated Loading_Asset spinning in the bottom-right corner.
    /// Operates across scene loads via DontDestroyOnLoad.
    /// </summary>
    public class LoadingScreenManager : MonoBehaviour
    {
        private static LoadingScreenManager s_Instance;
        public static LoadingScreenManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = FindFirstObjectByType<LoadingScreenManager>();
                    if (s_Instance == null)
                    {
                        GameObject obj = new GameObject("[LoadingScreenManager]");
                        s_Instance = obj.AddComponent<LoadingScreenManager>();
                        if (Application.isPlaying) DontDestroyOnLoad(obj);
                    }
                }
                s_Instance.InitializeUI();
                return s_Instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            var inst = Instance;
        }

        [Header("Visual Elements")]
        [SerializeField] private Canvas loadingCanvas;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform loadingIconRect;
        [SerializeField] private Image loadingIconImage;

        [Header("Settings")]
        [SerializeField] private float rotationSpeed = 240f;
        [SerializeField] private float fadeDuration = 0.25f;
        [SerializeField] private float defaultMinLoadTime = 0.6f;

        public static bool IsLoading { get; private set; }

        private Coroutine activeLoadRoutine;
        private Sprite loadingSprite;

        private void Awake()
        {
            if (s_Instance == null)
            {
                s_Instance = this;
                if (Application.isPlaying) DontDestroyOnLoad(gameObject);
                InitializeUI();
            }
            else if (s_Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            IsLoading = false;
        }

        private void Update()
        {
            if (canvasGroup != null && canvasGroup.alpha > 0.001f && loadingIconRect != null)
            {
                // Spin clockwise / smoothly in bottom-right corner
                loadingIconRect.Rotate(0f, 0f, -rotationSpeed * Time.unscaledDeltaTime);
            }
        }

        private void InitializeUI()
        {
            if (loadingCanvas != null) return;

            // 1. Root Canvas
            loadingCanvas = gameObject.GetComponent<Canvas>();
            if (loadingCanvas == null) loadingCanvas = gameObject.AddComponent<Canvas>();
            loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            loadingCanvas.sortingOrder = 32767; // Topmost UI layer

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            // 2. Full-Screen Dark Ocean Curtain Overlay (prevents in-game screen from flashing/flickering during load)
            GameObject bgObj = new GameObject("LoadingBG");
            bgObj.transform.SetParent(transform, false);
            RectTransform bgRt = bgObj.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            bgRt.anchoredPosition = Vector2.zero;

            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.10f, 0.18f, 1f); // Deep ocean dark curtain
            bgImg.raycastTarget = false;

            // 3. Loading_Asset Icon directly on Canvas (Bottom-Right corner)
            GameObject iconObj = new GameObject("LoadingIcon");
            iconObj.transform.SetParent(transform, false);
            loadingIconRect = iconObj.AddComponent<RectTransform>();
            loadingIconRect.anchorMin = new Vector2(1f, 0f);
            loadingIconRect.anchorMax = new Vector2(1f, 0f);
            loadingIconRect.pivot = new Vector2(0.5f, 0.5f);
            loadingIconRect.anchoredPosition = new Vector2(-75f, 75f);
            loadingIconRect.sizeDelta = new Vector2(90f, 90f);

            loadingIconImage = iconObj.AddComponent<Image>();
            RefreshLoadingSprite();
            loadingIconImage.raycastTarget = false;
        }

        private void RefreshLoadingSprite()
        {
#if UNITY_EDITOR
            loadingSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Loading_Asset.png")
                         ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Loading_Asset.png");
#endif
            if (loadingSprite == null)
            {
                loadingSprite = Resources.Load<Sprite>("Loading_Asset");
            }
            if (loadingIconImage != null && loadingSprite != null)
            {
                loadingIconImage.sprite = loadingSprite;
                loadingIconImage.preserveAspect = true;
            }
        }

        /// <summary>
        /// Asynchronously loads a target scene while displaying the bottom-right Loading_Asset.
        /// </summary>
        public static void LoadScene(string sceneName, float minDuration = -1f, Action onComplete = null)
        {
            if (IsLoading) return;
            Instance.StartSceneLoad(sceneName, minDuration, onComplete);
        }

        public void StartSceneLoad(string sceneName, float minDuration = -1f, Action onComplete = null)
        {
            if (IsLoading) return;
            IsLoading = true;
            RefreshLoadingSprite();
            if (minDuration < 0f) minDuration = defaultMinLoadTime;
            if (activeLoadRoutine != null)
            {
                StopCoroutine(activeLoadRoutine);
            }
            activeLoadRoutine = StartCoroutine(LoadSceneRoutine(sceneName, minDuration, onComplete));
        }

        private IEnumerator LoadSceneRoutine(string sceneName, float minDuration, Action onComplete)
        {
            // Freeze gameplay time scale during scene load transition so outgoing game objects never continue running
            Time.timeScale = 0f;

            // 1. Fade In Loading Screen & Immediately block all UI clicks
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 1f;

            // 2. Performance Optimization: Boost background loading priority for faster asset processing
            ThreadPriority prevPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;

            // 3. Begin Scene Loading
            float startTime = Time.unscaledTime;
            AsyncOperation asyncOp = null;

            try
            {
                asyncOp = SceneManager.LoadSceneAsync(sceneName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LoadingScreenManager] Failed to load scene '{sceneName}': {ex.Message}");
            }

            if (asyncOp != null)
            {
                asyncOp.allowSceneActivation = false;

                // Wait until loaded (progress reaches 0.9f) AND min duration has passed
                while (asyncOp.progress < 0.9f || (Time.unscaledTime - startTime) < minDuration)
                {
                    yield return null;
                }

                // Activate new scene
                asyncOp.allowSceneActivation = true;
                while (!asyncOp.isDone)
                {
                    yield return null;
                }
            }
            else
            {
                // Fallback direct load
                SceneManager.LoadScene(sceneName);
                yield return new WaitForSecondsRealtime(minDuration);
            }

            // 4. Clean Memory & Purge Unused Assets behind the loading overlay
            // This prevents mid-game GC frame spikes and stuttering
            AsyncOperation unloadOp = Resources.UnloadUnusedAssets();
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }
            GC.Collect();

            // Pre-warm shaders to prevent first-render micro-stutters
            Shader.WarmupAllShaders();

            // Restore background loading priority for optimal gameplay frame stability
            Application.backgroundLoadingPriority = prevPriority;

            // Short breath for newly loaded scene to settle initial physics/colliders
            yield return new WaitForSecondsRealtime(0.10f);

            // Restore normal gameplay time scale for the newly loaded scene (unless a briefing modal just paused it!)
            if (!Rhinotap.LevelBriefingManager.IsBriefingActive)
            {
                Time.timeScale = 1f;
            }

            onComplete?.Invoke();

            // 5. Fade Out Loading Screen
            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(1f - (elapsed / fadeDuration));
                yield return null;
            }
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            activeLoadRoutine = null;
            IsLoading = false;
        }

        /// <summary>
        /// Shows the loading overlay manually.
        /// </summary>
        public static void Show()
        {
            if (Instance == null) return;
            Instance.canvasGroup.blocksRaycasts = true;
            Instance.canvasGroup.interactable = true;
            Instance.canvasGroup.alpha = 1f;
        }

        /// <summary>
        /// Hides the loading overlay manually.
        /// </summary>
        public static void Hide()
        {
            if (Instance == null) return;
            Instance.canvasGroup.alpha = 0f;
            Instance.canvasGroup.blocksRaycasts = false;
            Instance.canvasGroup.interactable = false;
            IsLoading = false;
        }
    }
}
