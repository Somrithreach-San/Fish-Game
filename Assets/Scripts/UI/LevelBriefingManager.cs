using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Rhinotap
{
    /// <summary>
    /// Manages the level briefing modal shown before gameplay begins.
    /// Displays a designer-crafted PNG modal per level, pausing time until
    /// the player presses Continue.
    ///
    /// Asset naming convention (place in Assets/Resources/LevelBriefs/):
    ///   Background  — Ocean : Ocean_Level_Selection_BG.png
    ///   Background  — Lake  : Lake_Level_Selection_BG.png
    ///   Ocean briefs (N=1..8) : Coral_Coast_Level_N_Brief_Modal.png
    ///   Lake  briefs (N=1..5) : Lost_Lake_Level_N_Brief_Modal.png
    ///   Continue button       : Continue_Button.png
    ///
    /// Usage (called from GameManager.Start):
    ///   LevelBriefingManager.Show(levelNumber, isLakeLevel, onContinue);
    /// </summary>
    public class LevelBriefingManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────
        public static LevelBriefingManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Briefing state (read by PlayerSpawn / GridController) ────────
        /// <summary>True while the briefing modal is visible. Use to gate any
        /// system that must not start until Continue is pressed.</summary>
        public static bool IsBriefingActive { get; private set; }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// When true, skips displaying the briefing modal on the next level start (e.g. on Restart).
        /// Automatically resets to false once consumed.
        /// </summary>
        public static bool SkipNextBriefing { get; set; } = false;

        /// <summary>
        /// Shows the briefing modal for the given level. Freezes time until
        /// the player clicks Continue, then fires onContinue.
        /// If no briefing PNG exists for this level, onContinue fires immediately.
        /// </summary>
        public static void Show(int levelNumber, bool isLakeLevel, Action onContinue)
        {
            if (SkipNextBriefing)
            {
                SkipNextBriefing = false;
                IsBriefingActive = false;
                onContinue?.Invoke();
                return;
            }

            if (Instance == null)
            {
                var go = new GameObject("LevelBriefingManager");
                Instance = go.AddComponent<LevelBriefingManager>();
            }
            IsBriefingActive = true;
            Instance.StartCoroutine(Instance.ShowBriefingRoutine(levelNumber, isLakeLevel, onContinue));
        }

        // ── Internals ────────────────────────────────────────────────────

        private Canvas     briefCanvas;
        private CanvasGroup briefCanvasGroup;
        private GameObject modalRoot;

        private static Sprite LoadSpriteSafe(string path, string assetDbPath = null)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(assetDbPath))
            {
                Sprite editorSp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetDbPath);
                if (editorSp != null) return editorSp;
                var allObjs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetDbPath);
                if (allObjs != null && allObjs.Length > 0)
                {
                    Sprite best = null;
                    float maxArea = 0f;
                    foreach (var obj in allObjs)
                    {
                        if (obj is Sprite s)
                        {
                            float area = s.rect.width * s.rect.height;
                            if (area > maxArea) { maxArea = area; best = s; }
                        }
                    }
                    if (best != null) return best;
                }
            }
#endif
            if (!string.IsNullOrEmpty(path))
            {
                Sprite sp = Resources.Load<Sprite>(path);
                if (sp != null) return sp;

                Sprite[] all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0)
                {
                    Sprite best = null;
                    float maxArea = 0f;
                    foreach (var s in all)
                    {
                        if (s != null)
                        {
                            float area = s.rect.width * s.rect.height;
                            if (area > maxArea) { maxArea = area; best = s; }
                        }
                    }
                    if (best != null) return best;
                }

                // Fallback: load as Texture2D and create sprite dynamically
                Texture2D tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }

#if UNITY_EDITOR
            // Disk fallback: read raw PNG directly if asset database hasn't imported it yet
            if (!string.IsNullOrEmpty(assetDbPath))
            {
                try
                {
                    string fullPath = assetDbPath.StartsWith("Assets") 
                        ? System.IO.Path.Combine(Application.dataPath, assetDbPath.Substring("Assets/".Length))
                        : assetDbPath;
                    if (System.IO.File.Exists(fullPath))
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes))
                        {
                            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                        }
                    }
                }
                catch { }
            }
#endif

            return null;
        }

        private IEnumerator ShowBriefingRoutine(int levelNumber, bool isLakeLevel, Action onContinue)
        {
            // 1. Load modal sprite for this level (try multiple prefixes and paths)
            string prefix = isLakeLevel ? "Lost_Lake" : "Coral_Coast";
            string altPrefix = isLakeLevel ? "Lake" : "Ocean";

            string[] possiblePaths = new string[]
            {
                $"LevelBriefs/{prefix}_Level_{levelNumber}_Brief_Modal",
                $"LevelBriefs/{prefix}_Level {levelNumber}_Brief_Modal",
                $"LevelBriefs/{prefix}_Level_{levelNumber}",
                $"LevelBriefs/{altPrefix}_Level_{levelNumber}_Brief_Modal",
                $"LevelBriefs/Level_{levelNumber}_Brief_Modal"
            };

            string[] possibleEditorPaths = new string[]
            {
                $"Assets/Resources/LevelBriefs/{prefix}_Level_{levelNumber}_Brief_Modal.png",
                $"Assets/Graphics/GUI Components/{prefix}_Level_{levelNumber}_Brief_Modal.png",
                $"Assets/Resources/LevelBriefs/{prefix}_Level {levelNumber}_Brief_Modal.png",
                $"Assets/Graphics/GUI Components/{prefix}_Level {levelNumber}_Brief_Modal.png",
                $"Assets/Resources/LevelBriefs/{altPrefix}_Level_{levelNumber}_Brief_Modal.png",
                $"Assets/Graphics/GUI Components/{altPrefix}_Level_{levelNumber}_Brief_Modal.png",
                $"Assets/Resources/LevelBriefs/Level_{levelNumber}_Brief_Modal.png",
                $"Assets/Graphics/GUI Components/Level_{levelNumber}_Brief_Modal.png"
            };

            Sprite modalSprite = null;
            for (int i = 0; i < possiblePaths.Length; i++)
            {
                string rPath = possiblePaths[i];
                string ePath = (i < possibleEditorPaths.Length) ? possibleEditorPaths[i] : null;
                modalSprite = LoadSpriteSafe(rPath, ePath);
                if (modalSprite != null) break;
            }

            if (modalSprite == null)
            {
                for (int i = 0; i < possibleEditorPaths.Length; i++)
                {
                    modalSprite = LoadSpriteSafe(null, possibleEditorPaths[i]);
                    if (modalSprite != null) break;
                }
            }

            if (modalSprite == null)
            {
                // No briefing art for this level — start immediately
                IsBriefingActive = false;
                onContinue?.Invoke();
                yield break;
            }

            // 2. Load supporting assets
            Sprite continueSprite = LoadSpriteSafe("LevelBriefs/Continue_Button", "Assets/Graphics/GUI Components/Continue_Button.png")
                                 ?? LoadSpriteSafe("Continue_Button", "Assets/Resources/LevelBriefs/Continue_Button.png");
            string bgPath         = isLakeLevel ? "LevelBriefs/Lake_Level_Selection_BG"
                                                : "LevelBriefs/Ocean_Level_Selection_BG";
            string editorBgPath   = isLakeLevel ? "Assets/Resources/LevelBriefs/Lake_Level_Selection_BG.png"
                                                : "Assets/Resources/LevelBriefs/Ocean_Level_Selection_BG.png";
            Sprite bgSprite       = LoadSpriteSafe(bgPath, editorBgPath) 
                                 ?? LoadSpriteSafe(isLakeLevel ? "Lake_Level_Selection_BG" : "Ocean_Level_Selection_BG");

            // 3. Freeze game time (without triggering the full pause/event flow).
            //    Silence only the BGM — ambient/water loop keeps playing.
            float prevTimeScale = Time.timeScale;
            Time.timeScale      = 0f;
            if (GameManager.instance != null)
                GameManager.instance.SilenceMusicForBriefing();

            // 4. Build UI (button is created inside, we wire it here before fade)
            BuildBriefingUI(bgSprite, modalSprite, continueSprite);

            // 5. Wire Continue button BEFORE fade so it's ready from frame 1
            bool continuePressed = false;
            var  continueBtn     = briefCanvas.GetComponentInChildren<Button>();
            if (continueBtn != null)
            {
                continueBtn.onClick.AddListener(() => {
                    if (continuePressed) return;
                    continuePressed = true;
                    PlayButtonClickSound();
                    if (continueBtn != null) continueBtn.interactable = false;
                });
            }

            // 5b. Ready state (instantly visible behind loading curtain)
            if (briefCanvasGroup != null) briefCanvasGroup.alpha = 1f;

            while (!continuePressed)
            {
                yield return null;
            }

            // 7. Fade out
            yield return StartCoroutine(FadeCanvasGroup(briefCanvasGroup, 1f, 0f, 0.25f));

            // 8. Restore time
            Time.timeScale = 1f;

            // 9. Destroy UI
            if (briefCanvas != null) Destroy(briefCanvas.gameObject);
            briefCanvas      = null;
            briefCanvasGroup = null;
            modalRoot        = null;

            // 10. Start gameplay
            IsBriefingActive = false;
            onContinue?.Invoke();
        }

        private void PlayButtonClickSound()
        {
            if (GameManager.instance != null && GameManager.instance.ButtonSoundEffect != null)
            {
                SFXPool.Play2D(GameManager.instance.ButtonSoundEffect, 1.0f);
            }
        }

        private void OnDisable()
        {
            IsBriefingActive = false;
        }

        private void OnDestroy()
        {
            IsBriefingActive = false;
        }

        private void BuildBriefingUI(Sprite bgSprite, Sprite modalSprite, Sprite continueSprite)
        {
            // ── Canvas ───────────────────────────────────────────────────
            var canvasGo = new GameObject("BriefingCanvas");
            briefCanvas = canvasGo.AddComponent<Canvas>();
            briefCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            briefCanvas.sortingOrder = 32000; // Below LoadingScreen (32767), above everything else

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode       = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            briefCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
            briefCanvasGroup.alpha         = 1f; // Ready immediately behind loading screen
            briefCanvasGroup.blocksRaycasts = true;
            briefCanvasGroup.interactable   = true;

            // Ensure EventSystem exists
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Scale factor — same formula as GuiManager
            var canvasRect = briefCanvas.GetComponent<RectTransform>();
            float scaleFactor = canvasRect != null
                ? Mathf.Clamp(canvasRect.rect.height / 1080f, 0.5f, 2.0f)
                : 1f;

            // ── Layer 1: Full-screen scenario background ─────────────────
            var bgGo = new GameObject("ScenarioBG");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            if (bgSprite != null)
            {
                bgImg.sprite = bgSprite;
                bgImg.preserveAspect = false;
            }
            else
            {
                bgImg.color = new Color(0.04f, 0.12f, 0.22f, 1f); // deep-ocean fallback
            }
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin        = Vector2.zero;
            bgRt.anchorMax        = Vector2.one;
            bgRt.sizeDelta        = Vector2.zero;
            bgRt.anchoredPosition = Vector2.zero;

            // ── Layer 2: Semi-transparent dark overlay ────────────────────
            var overlayGo = new GameObject("DarkOverlay");
            overlayGo.transform.SetParent(canvasGo.transform, false);
            var overlayImg = overlayGo.AddComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.45f);
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin        = Vector2.zero;
            overlayRt.anchorMax        = Vector2.one;
            overlayRt.sizeDelta        = Vector2.zero;
            overlayRt.anchoredPosition = Vector2.zero;

            // ── Layer 3: Brief modal card — enlarged to 880h for prominent presence ─
            modalRoot = new GameObject("BriefingModal");
            modalRoot.transform.SetParent(canvasGo.transform, false);

            var modalRt = modalRoot.AddComponent<RectTransform>();
            modalRt.anchorMin        = new Vector2(0.5f, 0.5f);
            modalRt.anchorMax        = new Vector2(0.5f, 0.5f);
            modalRt.pivot            = new Vector2(0.5f, 0.5f);
            
            float modalH = 700f * scaleFactor;
            float modalW = 1050f * scaleFactor;
            if (modalSprite != null && modalSprite.rect.height > 0)
            {
                modalW = modalH * (modalSprite.rect.width / modalSprite.rect.height);
            }
            modalRt.sizeDelta        = new Vector2(modalW, modalH);
            modalRt.anchoredPosition = Vector2.zero;

            var modalImg = modalRoot.AddComponent<Image>();
            modalImg.sprite        = modalSprite;
            modalImg.preserveAspect = true;

            // ── Layer 4: Continue button — centered at bottom of card ─────
            var btnGo = new GameObject("ContinueBtn");
            btnGo.transform.SetParent(modalRoot.transform, false);

            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0.5f);
            btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.pivot     = new Vector2(0.5f, 0.5f);

            // Size: match the Continue_Button sprite aspect at 245×138 reference height
            float btnH = 138f * scaleFactor;
            float btnW = 245f * scaleFactor;
            if (continueSprite != null)
                btnW = btnH * (continueSprite.rect.width / continueSprite.rect.height);

            btnRt.sizeDelta        = new Vector2(btnW, btnH);
            btnRt.anchoredPosition = new Vector2(0f, -250f * scaleFactor);

            var btnImg = btnGo.AddComponent<Image>();
            if (continueSprite != null)
            {
                btnImg.sprite        = continueSprite;
                btnImg.preserveAspect = false;
            }
            else
            {
                btnImg.color = new Color(0.2f, 0.7f, 0.3f, 1f);
            }

            var btn = btnGo.AddComponent<Button>();
            // No hover / press visual effect — image stays exactly as designed
            btn.transition = Selectable.Transition.None;
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
        {
            if (cg == null) yield break;
            float elapsed = 0f;
            cg.alpha = from;
            while (elapsed < duration)
            {
                elapsed  += Time.unscaledDeltaTime;
                cg.alpha  = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            cg.alpha = to;
        }
    }
}
