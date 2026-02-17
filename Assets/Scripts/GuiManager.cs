using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Rhinotap.Toolkit;
using System.Runtime.InteropServices;

public class GuiManager : Singleton<GuiManager>
{
    #if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern void RequestFullScreen();
    #endif

    [SerializeField]
    private Image XpBar;

    [Header("Double XP Status")]
    [SerializeField]
    private Sprite doubleXpSprite; // Drag the sprite here
    private GameObject doubleXpIconObj; 

    [Header("Infection Status")]
    private Sprite infectedStatusSprite;
    private GameObject infectedIconObj;

    [Header("Growth Icons")]
    [SerializeField]
    private Image[] growthIcons;
    [SerializeField]
    private Color completedColor = Color.white;
    [SerializeField]
    private Color currentColor = new Color(1f, 1f, 1f, 1f); // Full white
    [SerializeField]
    private Color lockedColor = Color.black; // Solid black for silhouette effect
    [SerializeField]
    private Sprite warningIconSprite; // Sprite for the warning icon on locked fishes



    [SerializeField]
    private GameObject pauseBtn;
    [SerializeField]
    private GameObject resumeBtn;
    [SerializeField]
    private GameObject restartBtn; // New Restart Button
    [SerializeField]
    private GameObject menuBtn;    // New Main Menu Button
    [SerializeField]
    private GameObject pausedBg;

    [SerializeField]
    private GameObject ScoreScreen;
    [Header("Game Over Messages")]
    [SerializeField]
    [TextArea]
private string victoryMessage = "GbGrsaTr Gñk)anrYcCIvitkñúgvKÁenH";
    [SerializeField]
    [TextArea]
    private string defeatMessage = "BüayammþgeTot";
    
    [SerializeField]
    private Font messageFont;
    [SerializeField]
    private TMP_FontAsset messageFontTmp; // TMP Support

    [SerializeField]
    private Text ScoreText;
    private Text messageText;
    
    // Floating Text
    [Header("Floating Text")]
    [SerializeField]
    private GameObject floatingTextPrefab; 
    [SerializeField] 
    private int poolSize = 20;
    private Queue<GameObject> floatingTextPool = new Queue<GameObject>();

    private float targetXpFill = 0f;
    
    // UI Audio
    private AudioSource uiAudioSource;

    // Start is called before the first frame update
    void Start()
    {
        // 0. Critical: Ensure EventSystem exists (Required for UI clicks)
        EnsureEventSystem();
        
        // Setup UI Audio
        uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.ignoreListenerPause = true; // Ensure UI sounds play when game is paused!

        // Fix: Ensure AudioListener volume is set (sometimes starts at 0 on mobile until interaction)
        // We will handle the actual "Unmute" in Update() on first tap.

        // Simple fallback for ACTIVE objects only (Cheap, fixes dark screen if unassigned)
        if (pausedBg == null) pausedBg = GameObject.Find("PausedBG");
        if (ScoreScreen == null) ScoreScreen = GameObject.Find("ScoreScreen");

        // Fallback for Buttons if not assigned
        if (pauseBtn == null) pauseBtn = FindUIObjectByName("PauseButton", "PauseBtn", "BtnPause", "Pause");
        
        // FORCE RECREATE PAUSE MENU BUTTONS (User Request)
        // Destroy existing buttons to ensure fresh procedural generation with correct settings
        if (resumeBtn != null) { Destroy(resumeBtn); resumeBtn = null; }
        if (restartBtn != null) { Destroy(restartBtn); restartBtn = null; }
        if (menuBtn != null) { Destroy(menuBtn); menuBtn = null; }

        // Also clean up any lingering objects in the scene that might conflict (Active or Inactive)
        // We only target children of PausedBG if it exists, to avoid destroying unrelated UI
        if (pausedBg != null)
        {
            foreach (Transform child in pausedBg.transform)
            {
                if (child.name.Contains("Resume") || child.name.Contains("Restart") || child.name.Contains("Menu"))
                {
                    Destroy(child.gameObject);
                }
            }
        }
        else 
        {
            // If PausedBG isn't assigned, try to find it first
            pausedBg = GameObject.Find("PausedBG");
            if (pausedBg != null)
            {
                foreach (Transform child in pausedBg.transform)
                {
                    if (child.name.Contains("Resume") || child.name.Contains("Restart") || child.name.Contains("Menu"))
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        // Ensure buttons exist (Restore if lost)
        CreateMissingButtons();

        // Setup Buttons (Listeners + Hover Effects)
        SetupButton(resumeBtn, () => GameManager.instance.PlayPause()); // Resume just toggles pause
        SetupButton(restartBtn, RestartGame);
        SetupButton(menuBtn, GoToMainMenu);
        
        // Pause Button and Fullscreen Button are handled by SetupTopRightControls() later
        // We defer creation to ensure everything else is ready
        /*
        if (pauseBtn != null)
        {
             // Legacy setup removed
        }
        */

        // Ensure overlays are hidden at start (Fix for Black Screen)
        if (pausedBg != null) pausedBg.SetActive(false);
        if (ScoreScreen != null) ScoreScreen.SetActive(false);

        // Apply Font to ScoreText Template (if available) to fix "Default English" look
        if (ScoreText != null && messageFont != null)
        {
            ScoreText.font = messageFont;
        }

        InitializeFloatingTextPool();

        // Setup Message Text (Clone ScoreText)
        if (messageText == null && ScoreText != null)
        {
            GameObject msgObj = Instantiate(ScoreText.gameObject, ScoreText.transform.parent);
            msgObj.name = "MessageText";
            messageText = msgObj.GetComponent<Text>();
            
            if (messageFont != null)
            {
                messageText.font = messageFont;
            }

            messageText.text = "";
            // Optimize for long text
            messageText.resizeTextForBestFit = true;
            messageText.resizeTextMinSize = 10;
            messageText.resizeTextMaxSize = 60;
            messageText.alignment = TextAnchor.MiddleCenter;
            
            // Center the message text in the screen (Fill Parent)
            RectTransform rt = messageText.GetComponent<RectTransform>();
            if (rt != null)
            {
                 rt.anchorMin = Vector2.zero;
                 rt.anchorMax = Vector2.one;
                 rt.sizeDelta = Vector2.zero; 
                 rt.anchoredPosition = Vector2.zero;
            }

            msgObj.SetActive(false);
        }

        EventManager.StartListening("GameWin", () => {
             ShowGameMessage(victoryMessage);
             // Ensure cursor is visible for UI interaction
             Cursor.visible = true;
             Cursor.lockState = CursorLockMode.None;
        });

        EventManager.StartListening("GameLoss", () => {
             ShowGameMessage(defeatMessage);
             // Ensure cursor is visible for UI interaction
             Cursor.visible = true;
             Cursor.lockState = CursorLockMode.None;
        });

        EventManager.StartListening("GameStart", () => {
            SetXp(0, 1);
            HideScore();
            UpdateGrowthIcons(1); // Reset icons to level 1
        });
        

        EventManager.StartListening<bool>("gamePaused", (isPaused) => {
            TogglePauseBtn(isPaused);
        });


        EventManager.StartListening<int>("GameOver", (score) => {
            ShowScore(score);
        });

        EventManager.StartListening<int>("onLevelUp", (level) => {
            UpdateGrowthIcons(level);
        });

        // Ensure layout is fixed at start
        UpdateGrowthIcons(1); // Initial state
        
        // Ensure XP bar starts empty
        if (XpBar != null)
        {
             if (XpBar.type != Image.Type.Filled)
             {
                 XpBar.type = Image.Type.Filled;
                 XpBar.fillMethod = Image.FillMethod.Horizontal;
             }
             XpBar.fillAmount = 0f;
        }

        // Ensure UI overlays are hidden at start (Fix Black Screen)
        if (pausedBg != null) pausedBg.SetActive(false);
        if (ScoreScreen != null) ScoreScreen.SetActive(false);
        if (pauseBtn != null) pauseBtn.SetActive(true);
        if (resumeBtn != null) resumeBtn.SetActive(false);

        // Fix: Ensure Main UI Canvas is above Shark Warning Canvas (Order 999)
        if (pausedBg != null)
        {
            Canvas rootCanvas = pausedBg.GetComponentInParent<Canvas>();
            if (rootCanvas != null)
            {
                // Ensure we are active to set this? No, component access is fine.
                // We want the Pause Menu to cover the Warning Icon.
                rootCanvas.sortingOrder = 2000; 
            }
        }

        // Final Layout Fix: Run this LAST to ensure all buttons are created and ready
        FixPauseLayout();
        
        // Fix: Ensure Pause Button and Fullscreen Button are created and positioned correctly
        SetupTopRightControls();

        // FORCE AUDIO ON START (User Request)
        // Attempt to brute-force audio enabling immediately
        ForceEnableAudio();
    }

    private void ForceEnableAudio()
    {
         // 1. Play silent sound to unlock audio engine
         if (uiAudioSource != null && uiAudioSource.clip != null)
         {
             uiAudioSource.PlayOneShot(uiAudioSource.clip); // Plays null or whatever, just triggers context
         }
         
         // 2. Ensure AudioListener is active/unpaused
         AudioListener.pause = false; 
         
         // 3. Force volume update (sometimes starts muted)
         float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
         AudioListener.volume = savedVolume;
    }

    [Header("Top Right Controls")]
    public Sprite pauseSprite;
    private static Sprite _cachedPauseIcon;
    
    [Header("Custom Assets (Performance Boost)")]
    [SerializeField] public Font customFont; // For lmns1
    [SerializeField] public Sprite buttonShape; // For the circle background

    private void SetupTopRightControls()
    {
        // 1. Get Reference to Main Canvas (Parent of Controls)
        Canvas mainCanvas = null;
        if (pausedBg != null) mainCanvas = pausedBg.GetComponentInParent<Canvas>();
        if (mainCanvas == null) mainCanvas = FindFirstObjectByType<Canvas>();
        
        if (mainCanvas == null) return; 

        // 2. Handle Pause Button
        Sprite existingPauseSprite = null;
        if (pauseBtn != null)
        {
            Image img = pauseBtn.GetComponent<Image>();
            if (img != null) existingPauseSprite = img.sprite;
            
            // Destroy existing button to replace with our clean programmatic one
            Destroy(pauseBtn);
        }
        
        // Use field if assigned (Fastest), otherwise fallback to Resources (Slower)
        Sprite finalPauseSprite = pauseSprite;
        if (finalPauseSprite == null)
        {
            if (_cachedPauseIcon == null) _cachedPauseIcon = Resources.Load<Sprite>("pause_icon");
            finalPauseSprite = _cachedPauseIcon;
        }
        if (finalPauseSprite == null) finalPauseSprite = existingPauseSprite;
        
        // Create Pause Button (Right-most)
        // User request: Same size as Fullscreen (40x40)
        // Adjusted padding to match header (-50 from right), aligned Y (-30)
        pauseBtn = CreateControlButton("PauseButton", finalPauseSprite, new Vector2(-50, -30), new Vector2(40, 40), () => {
             PlayButtonSound();
             GameManager.instance.PlayPause();
        });
        
        if (pauseBtn != null)
        {
             pauseBtn.transform.SetParent(mainCanvas.transform, false);
             pauseBtn.transform.SetAsLastSibling();
        }

        // 3. Handle Fullscreen Button (Left of Pause Button)
        /* DISABLED PER USER REQUEST
        if (fullscreenSprite == null) fullscreenSprite = Resources.Load<Sprite>("fullscreen_icon");
        
        // Position: -50 (Pause Pos) - 30 (Pause Size) - 10 (Gap) = -90
        // Y aligned with Pause (-30)
        fullscreenBtn = CreateControlButton("FullscreenButton", fullscreenSprite, new Vector2(-90, -30), new Vector2(40, 40), () => GoFullScreen());
        if (fullscreenBtn != null)
        {
             fullscreenBtn.transform.SetParent(mainCanvas.transform, false);
             fullscreenBtn.transform.SetAsLastSibling();
        }
        */
    }

    private GameObject CreateControlButton(string name, Sprite sprite, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = new GameObject(name);
        
        // RectTransform
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1); // Top-Right
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        rt.localScale = Vector3.one;
        
        // Image
        Image img = btnObj.AddComponent<Image>();
        if (sprite != null) img.sprite = sprite;
        img.color = Color.white;
        img.raycastTarget = true;
        
        // Button
        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(() => PlayButtonSound());
        btn.onClick.AddListener(action);
        
        // Layout Element (Ignore Layout)
        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // Ensure Canvas/Raycaster for Mobile Tap Reliability
        Canvas c = btnObj.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 2001; // Max Priority
        
        btnObj.AddComponent<GraphicRaycaster>();
        
        // Fix: Add transparent "Hit Area" padding for easier mobile tapping
        // 40x40 is small for fingers. We add a child that is 60x60 but transparent.
        GameObject hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(btnObj.transform, false);
        
        RectTransform rtHit = hitArea.AddComponent<RectTransform>();
        rtHit.anchorMin = new Vector2(0.5f, 0.5f);
        rtHit.anchorMax = new Vector2(0.5f, 0.5f);
        rtHit.sizeDelta = new Vector2(60, 60); // 150% padding
        
        Image imgHit = hitArea.AddComponent<Image>();
        imgHit.color = new Color(0, 0, 0, 0); // Transparent
        imgHit.raycastTarget = true;
        
        // Forward click to parent button
        Button btnHit = hitArea.AddComponent<Button>();
        btnHit.onClick.AddListener(() => btn.onClick.Invoke());

        return btnObj;
    }

    public void GoFullScreen() 
    { 
        #if UNITY_WEBGL && !UNITY_EDITOR
        RequestFullScreen();
        #else
        // Toggle Fullscreen Mode
        // Improved logic for Mobile/WebGL compatibility
        if (!Screen.fullScreen)
        {
            // Force FullScreenWindow mode which is often required for mobile/web
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = true;
        }
        else
        {
            Screen.fullScreen = false;
        }
        #endif
    }

    private Image doubleXpProgressImg;
    private Image infectedProgressImg;
    private Sprite ringSprite;

    // Generate a Ring Sprite at runtime for transparent background support
    private Sprite CreateRingSprite(int resolution, float thicknessRatio)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear; // Smooth edges
        texture.wrapMode = TextureWrapMode.Clamp;
        
        Color[] colors = new Color[resolution * resolution];
        float center = resolution / 2f;
        float outerRadius = resolution / 2f;
        float innerRadius = outerRadius * (1f - thicknessRatio);
        
        float outerRSquared = outerRadius * outerRadius;
        float innerRSquared = innerRadius * innerRadius;
        
        // Anti-aliasing width
        float aaWidth = 1.5f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float distSquared = dx * dx + dy * dy;
                float dist = Mathf.Sqrt(distSquared);
                
                // Logic:
                // 1. Outside Outer Radius -> Transparent
                // 2. Inside Inner Radius -> Transparent
                // 3. Between -> White
                // + Anti-aliasing at both edges

                float alpha = 0f;

                if (dist > outerRadius + aaWidth || dist < innerRadius - aaWidth)
                {
                    alpha = 0f;
                }
                else if (dist >= innerRadius && dist <= outerRadius)
                {
                    alpha = 1f;
                }
                else if (dist > outerRadius)
                {
                    // Outer Edge Fade
                    alpha = Mathf.InverseLerp(outerRadius + aaWidth, outerRadius, dist);
                }
                else if (dist < innerRadius)
                {
                    // Inner Edge Fade
                    alpha = Mathf.InverseLerp(innerRadius - aaWidth, innerRadius, dist);
                }

                colors[y * resolution + x] = new Color(1, 1, 1, alpha);
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f));
    }

    private void SetupStatusIcon(ref GameObject containerObj, ref Image progressImg, string name, Sprite iconSprite)
    {
        // 1. Container
        if (containerObj == null && XpBar != null)
        {
             // Try to find existing container
             Transform t = XpBar.transform.parent.Find(name);
             if (t != null) 
             {
                 // DESTROY EXISTING TO ENSURE FRESH STATE
                 Destroy(t.gameObject); 
                 containerObj = null;
             }
             
             if (containerObj == null)
             {
                 containerObj = new GameObject(name);
                 containerObj.transform.SetParent(XpBar.transform.parent, false);
             }
        }

        if (containerObj != null)
        {
            // Ensure Container has NO Image
            Image containerImg = containerObj.GetComponent<Image>();
            if (containerImg != null) Destroy(containerImg);

            // Container Layout
            RectTransform rt = containerObj.GetComponent<RectTransform>();
            if (rt == null) rt = containerObj.AddComponent<RectTransform>();
            
            // Anchor to RIGHT of parent
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0, 0.5f); // Pivot Left
            rt.anchoredPosition = Vector2.zero;

            // Layout Element (Reserve Space)
            LayoutElement le = containerObj.GetComponent<LayoutElement>();
            if (le == null) le = containerObj.AddComponent<LayoutElement>();
            
            le.ignoreLayout = false; 
            le.minWidth = 12;
            le.minHeight = 12;
            le.preferredWidth = 12;
            le.preferredHeight = 12;
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;

            // Ensure scale is correct
            containerObj.transform.localScale = Vector3.one;

            // Cleanup old "Ring" or "Icon" children if they exist from previous style
            Transform ringT = containerObj.transform.Find("Ring");
            if (ringT != null) Destroy(ringT.gameObject);
            Transform oldIconT = containerObj.transform.Find("Icon");
            if (oldIconT != null) Destroy(oldIconT.gameObject);

            // 2. Background Icon (Ghost/Silhouette)
            Transform bgT = containerObj.transform.Find("Background");
            GameObject bgObj;
            if (bgT == null)
            {
                bgObj = new GameObject("Background");
                bgObj.transform.SetParent(containerObj.transform, false);
            }
            else bgObj = bgT.gameObject;

            Image bgImg = bgObj.GetComponent<Image>();
            if (bgImg == null) bgImg = bgObj.AddComponent<Image>();
            bgImg.sprite = iconSprite;
            bgImg.preserveAspect = true;
            bgImg.color = new Color(0, 0, 0, 0.3f); // Subtle dark silhouette
            bgImg.raycastTarget = false;

            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero; // Stretch to fill container

            // 3. Foreground Icon (Filled Progress)
            Transform fgT = containerObj.transform.Find("Foreground");
            GameObject fgObj;
            if (fgT == null)
            {
                fgObj = new GameObject("Foreground");
                fgObj.transform.SetParent(containerObj.transform, false);
            }
            else fgObj = fgT.gameObject;

            progressImg = fgObj.GetComponent<Image>();
            if (progressImg == null) progressImg = fgObj.AddComponent<Image>();
            progressImg.sprite = iconSprite;
            progressImg.preserveAspect = true;
            progressImg.type = Image.Type.Filled;
            progressImg.fillMethod = Image.FillMethod.Vertical;
            progressImg.fillOrigin = (int)Image.OriginVertical.Bottom;
            progressImg.color = Color.white;
            progressImg.raycastTarget = false;

            RectTransform fgRt = fgObj.GetComponent<RectTransform>();
            fgRt.anchorMin = Vector2.zero;
            fgRt.anchorMax = Vector2.one;
            fgRt.sizeDelta = Vector2.zero; // Stretch to fill container
            
            // Ensure Foreground is on top
            fgObj.transform.SetAsLastSibling();
        }
    }

    public void SetDoubleXpStatus(bool active)
    {
        // Force reset reference if active to ensure regeneration
        if (active && doubleXpIconObj != null) 
        {
            Destroy(doubleXpIconObj);
            doubleXpIconObj = null;
        }

        if (active)
        {
            SetupStatusIcon(ref doubleXpIconObj, ref doubleXpProgressImg, "DoubleXpIcon", doubleXpSprite);
            if (doubleXpIconObj != null) doubleXpIconObj.SetActive(true);
        }
        else
        {
            if (doubleXpIconObj != null) doubleXpIconObj.SetActive(false);
        }
        
        // Update Layout
        UpdateStatusIconsLayout();
    }
    
    public void UpdateDoubleXpProgress(float percent)
    {
        if (doubleXpProgressImg != null)
        {
            doubleXpProgressImg.fillAmount = percent;
        }
    }

    // Removed Coroutine (AnimateDoubleXpIcon)

    public void SetInfectedStatus(bool active)
    {
        if (infectedStatusSprite == null) infectedStatusSprite = Resources.Load<Sprite>("infected_icon");
        
        // Force reset reference if active to ensure regeneration
        if (active && infectedIconObj != null) 
        {
            Destroy(infectedIconObj);
            infectedIconObj = null;
        }

        if (active)
        {
            SetupStatusIcon(ref infectedIconObj, ref infectedProgressImg, "InfectedIcon", infectedStatusSprite);
            if (infectedIconObj != null) infectedIconObj.SetActive(true);
        }
        else
        {
             if (infectedIconObj != null) infectedIconObj.SetActive(false);
        }

        // Update Layout
        UpdateStatusIconsLayout();
    }
    
    public void UpdateInfectedProgress(float percent)
    {
        if (infectedProgressImg != null)
        {
            infectedProgressImg.fillAmount = percent;
        }
    }

    // Removed Coroutine (AnimateInfectedIcon)

    private void UpdateStatusIconsLayout()
    {
        if (XpBar == null) return;

        RectTransform barParent = XpBar.transform.parent as RectTransform;
        if (barParent == null) return;

        // 0. CLEANUP: Remove any LayoutGroup from the Parent (It fights our manual control)
        HorizontalLayoutGroup hlg = barParent.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) Destroy(hlg);
        
        ContentSizeFitter csf = barParent.GetComponent<ContentSizeFitter>();
        if (csf != null) Destroy(csf);

        // 1. Identify active icons
        List<GameObject> activeIcons = new List<GameObject>();
        if (doubleXpIconObj != null && doubleXpIconObj.activeSelf) activeIcons.Add(doubleXpIconObj);
        if (infectedIconObj != null && infectedIconObj.activeSelf) activeIcons.Add(infectedIconObj);

        // 2. Determine Size dynamically based on Bar Height
        // This ensures "similar size to the bar"
        float parentHeight = barParent.rect.height;
        float iconSize = (parentHeight > 0) ? parentHeight : 12f; // Fallback to 12 if height invalid
        
        // Constants
        const float GAP = 8f; // Gap between Bar and First Icon
        const float ICON_SPACING = 5f; // Gap between Icons

        // 3. RESET XP BAR & PARENT (Decouple them from Indicators)
        // Reset XP Bar to fill the parent normally
        XpBar.rectTransform.anchorMin = Vector2.zero;
        XpBar.rectTransform.anchorMax = Vector2.one;
        XpBar.rectTransform.offsetMin = Vector2.zero;
        XpBar.rectTransform.offsetMax = Vector2.zero;

        // Stop forcing LayoutElement properties on Parent
        LayoutElement le = barParent.GetComponent<LayoutElement>();
        if (le != null)
        {
             le.ignoreLayout = false;
             le.minWidth = -1;
             le.preferredWidth = -1;
             le.flexibleWidth = -1;
        }

        // 4. Position Icons OUTSIDE the Bar (To the Right)
        float currentX = GAP; 

        foreach (var icon in activeIcons)
        {
             RectTransform rt = icon.GetComponent<RectTransform>();
             
             // Set Size to match Bar Height
             rt.sizeDelta = new Vector2(iconSize, iconSize);

             // Anchor: Right Center
             rt.anchorMin = new Vector2(1, 0.5f);
             rt.anchorMax = new Vector2(1, 0.5f);
             rt.pivot = new Vector2(0, 0.5f); // Pivot Left
             
             // Position: Positive X is "Outside" to the right
             rt.anchoredPosition = new Vector2(currentX, 0);
             
             // Ensure scale is correct
             rt.localScale = Vector3.one;
             
             currentX += iconSize + ICON_SPACING;
        }

        // 5. Force layout rebuild (for parent container mostly)
        LayoutRebuilder.ForceRebuildLayoutImmediate(barParent);
    }

    private void Update()
    {
        // Smooth Fill XP Bar
        if (XpBar != null)
        {
            XpBar.fillAmount = Mathf.Lerp(XpBar.fillAmount, targetXpFill, Time.deltaTime * 5f);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (messageFontTmp == null)
        {
             // Try to find default TMP font
             messageFontTmp = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }
#endif

    private void SetupButton(GameObject btnObj, UnityEngine.Events.UnityAction action)
    {
        if (btnObj == null) return;
        Button btn = btnObj.GetComponent<Button>();
        if (btn == null) return;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => PlayButtonSound()); // Add standard click sound
        btn.onClick.AddListener(action);

        if (btnObj.GetComponent<ButtonHoverEffect>() == null)
            btnObj.AddComponent<ButtonHoverEffect>();
    }

    public void PlayButtonSound()
    {
        if (GameManager.instance != null && GameManager.instance.ButtonSoundEffect != null)
        {
            if (uiAudioSource == null) 
            {
                 uiAudioSource = gameObject.AddComponent<AudioSource>();
                 uiAudioSource.ignoreListenerPause = true;
            }
            uiAudioSource.PlayOneShot(GameManager.instance.ButtonSoundEffect);
        }
    }

    private void CreateMissingButtons()
    {
        // 1. Ensure Background/Parent exists
        if (pausedBg == null)
        {
            pausedBg = new GameObject("PausedBG");
            Canvas canvas = pausedBg.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // Above everything
            
            // Fix: Use ScaleWithScreenSize for Mobile compatibility
            CanvasScaler scaler = pausedBg.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f; // Balance between width/height
            
            pausedBg.AddComponent<GraphicRaycaster>();
            
            // Add semi-transparent background image
            Image img = pausedBg.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.20f); // Darker background (User Request: 65%)
        }
        else
        {
             // Ensure existing background is also darkened
             Image img = pausedBg.GetComponent<Image>();
             if (img != null)
             {
                 img.color = new Color(0, 0, 0, 0.20f); // Darker background (User Request: 65%)
             }
             
             // Ensure it has a Canvas for proper sorting (Overlay on top of everything)
             Canvas c = pausedBg.GetComponent<Canvas>();
             if (c == null) c = pausedBg.AddComponent<Canvas>();
             c.overrideSorting = true;
             c.sortingOrder = 2000;
             
             if (pausedBg.GetComponent<GraphicRaycaster>() == null) pausedBg.AddComponent<GraphicRaycaster>();
        }

        // Ensure the background fills the screen completely (User Request: "cover whole screen perfectly")
        RectTransform rtBg = pausedBg.GetComponent<RectTransform>();
        if (rtBg != null)
        {
            // Reset anchors to stretch
            rtBg.anchorMin = Vector2.zero;
            rtBg.anchorMax = Vector2.one;
            rtBg.pivot = new Vector2(0.5f, 0.5f);
            
            // Reset offsets to extend slightly beyond screen (User Request: "bigger than current size abit")
            rtBg.offsetMin = new Vector2(-10, -10); // Left/Bottom
            rtBg.offsetMax = new Vector2(10, 10); // Right/Top
            
            rtBg.localScale = Vector3.one; // Ensure scale is 1
        }

        // 2. Create Buttons if missing
        if (resumeBtn == null) resumeBtn = CreateButton("ResumeBtn", pausedBg.transform);
        if (restartBtn == null) restartBtn = CreateButton("RestartBtn", pausedBg.transform);
        if (menuBtn == null) menuBtn = CreateButton("MenuBtn", pausedBg.transform);
        
        // 3. FORCE ORDER: Ensure Buttons are strictly ON TOP of the background
        // Unity UI draws children in order. Last child = Topmost.
        if (resumeBtn != null) resumeBtn.transform.SetAsLastSibling();
        if (restartBtn != null) restartBtn.transform.SetAsLastSibling();
        if (menuBtn != null) menuBtn.transform.SetAsLastSibling();
    }

    private GameObject CreateButton(string name, Transform parent)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        // Add Image
        btnObj.AddComponent<Image>();
        
        // Add Button
        Button btn = btnObj.AddComponent<Button>();
        
        // Add Text Child
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        Text t = textObj.AddComponent<Text>();
        t.alignment = TextAnchor.MiddleCenter;
        
        // Fill Parent
        RectTransform rtText = textObj.GetComponent<RectTransform>();
        rtText.anchorMin = Vector2.zero;
        rtText.anchorMax = Vector2.one;
        rtText.sizeDelta = Vector2.zero;
        
        return btnObj;
    }

    private void FixPauseLayout()
    {
        // 1. Calculate Scaling Factor
        // Main Menu Reference: 1920x1080
        // Main Menu Button Size: 180x180 -> Reduced to 150x150 per request
        // Ratio: 150 / 1080 = 0.1388f
        
        float scaleFactor = 1.0f;
        Canvas canvas = pausedBg.GetComponent<Canvas>();
        if (canvas == null) canvas = pausedBg.GetComponentInParent<Canvas>();
        
        if (canvas != null)
        {
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                // Dynamic Scale based on Height relative to 1080p reference
                scaleFactor = canvasRect.rect.height / 1080f;
            }
        }
        
        // Ensure scale doesn't get too crazy (Clamp between 0.5x and 2.0x)
        scaleFactor = Mathf.Clamp(scaleFactor, 0.5f, 2.0f);

        // 2. Define Style (Match Main Menu but Scaled)
        // Reduced base size from 180 to 150 ("tiny bit smaller")
        float baseSize = 150f;
        Vector2 buttonSize = new Vector2(baseSize * scaleFactor, baseSize * scaleFactor);
        
        // Main Menu Style: White with 50% opacity
        Color buttonColor = new Color(1f, 1f, 1f, 0.5f); 

        // Text Size Calculation: Increased from 50 to 60 per user request
        int fontSize = Mathf.RoundToInt(60f * scaleFactor);

        // 3. Map Buttons to Positions (Scaled & Centered)
        // Centering Logic:
        // Resume: Top (100)
        // Menu/Restart: Bottom (-100)

        // Update Text to Khmer (User Request)
        // Resume -> "bnþ"
        CustomizeButton(resumeBtn, "bnþ", buttonColor, buttonSize, fontSize);
        PositionButton(resumeBtn, new Vector2(0, 100f * scaleFactor));

        // Menu -> "muWnuy"
        CustomizeButton(menuBtn, "muWnuy", buttonColor, buttonSize, fontSize);
        PositionButton(menuBtn, new Vector2(150f * scaleFactor, -100f * scaleFactor));

        // Restart -> "safµI"
        CustomizeButton(restartBtn, "safµI", buttonColor, buttonSize, fontSize);
        PositionButton(restartBtn, new Vector2(-150f * scaleFactor, -100f * scaleFactor));
    }

    private void CustomizeButton(GameObject btnObj, string label, Color color, Vector2 size, int fontSize = 24)
    {
        if (btnObj == null) return;
        
        // 1. Image Style
        Image img = btnObj.GetComponent<Image>();
        if (img != null)
        {
            // Use assigned shape if available (Performance Boost)
            if (buttonShape != null)
            {
                 img.sprite = buttonShape;
                 img.name = "CustomShape";
            }
            // Otherwise generate procedural circle
            else if (img.sprite == null || img.sprite.name != "ProceduralCircle")
            {
                 img.sprite = CreateCircleSprite(256, 2);
                 img.sprite.name = "ProceduralCircle";
            }
            img.color = color;
            img.type = Image.Type.Simple;
            img.raycastTarget = true; // Ensure clickable!
        }
        
        // 2. Size & Anchors
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f); // Center
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }
        
        // 3. Text Style (Black, Bold, No Best Fit - Match Main Menu)
        Text txt = btnObj.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.text = label;
            txt.color = Color.black;
            txt.resizeTextForBestFit = false; // Main Menu uses fixed size
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold; // Main Menu is Bold
            txt.alignment = TextAnchor.MiddleCenter;
            txt.alignByGeometry = true; // Improve centering for fonts with offsets
            
            // Fix: Use Limon Font for Khmer Text (User Request)
            Font standardFont = customFont;
            if (standardFont == null) standardFont = Resources.Load<Font>("lmns1");
            
            // Fallback: LegacyRuntime or Arial
            if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            
            // Fallback: Find ANY font if specific ones fail
            if (standardFont == null)
            {
                 Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                 foreach (Font f in fonts) {
                     if (f != null && f.name.Length > 0) {
                         standardFont = f;
                         // Prefer Arial if found
                         if (f.name.Contains("Arial")) break;
                     }
                 }
            }

            if (standardFont != null) 
            {
                txt.font = standardFont;
            }
            
            // Ensure Text fills the button
            RectTransform rtText = txt.GetComponent<RectTransform>();
            if (rtText != null)
            {
                rtText.pivot = new Vector2(0.5f, 0.5f);
                rtText.anchorMin = Vector2.zero;
                rtText.anchorMax = Vector2.one;
                rtText.sizeDelta = Vector2.zero;
                rtText.anchoredPosition = Vector2.zero;
            }
        }
        
        // TMP Support
        TextMeshProUGUI tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = label;
            tmp.color = Color.black;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            
            // Reset to default font asset for plain English
            tmp.font = TMP_Settings.defaultFontAsset;
        }
    }

    private void PositionButton(GameObject btnObj, Vector2 pos)
    {
        if (btnObj != null)
        {
            RectTransform rt = btnObj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = pos;
            }
        }
    }

    private Sprite CreateCircleSprite(int resolution, int antiAliasing)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear; // Smooth scaling

        Color[] colors = new Color[resolution * resolution];
        float center = resolution / 2f;
        float radius = resolution / 2f;
        float rSquared = radius * radius;
        
        // Anti-aliasing edge width (approx 2 pixels)
        float aaWidth = 2f * antiAliasing; 

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float distSquared = dx * dx + dy * dy;

                if (distSquared <= rSquared - (radius * aaWidth))
                {
                    // Inner Circle (Full Alpha)
                    colors[y * resolution + x] = Color.white;
                }
                else if (distSquared <= rSquared + (radius * aaWidth))
                {
                    // Edge (Anti-aliasing)
                    float dist = Mathf.Sqrt(distSquared);
                    float alpha = Mathf.InverseLerp(radius + aaWidth/2f, radius - aaWidth/2f, dist);
                    colors[y * resolution + x] = new Color(1, 1, 1, alpha);
                }
                else
                {
                    // Outside (Transparent)
                    colors[y * resolution + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f));
    }

    private Sprite CreatePauseIconSprite(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point; // Sharp edges
        Color[] colors = new Color[resolution * resolution];
        
        // Clear to transparent
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        int barWidth = resolution / 3; // Two bars, total width ~2/3
        int gap = resolution / 6;
        int height = (int)(resolution * 0.7f);
        int startY = (resolution - height) / 2;
        int startX1 = (resolution / 2) - gap - barWidth; // Left Bar
        int startX2 = (resolution / 2) + gap; // Right Bar

        for (int y = startY; y < startY + height; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Left Bar
                if (x >= startX1 && x < startX1 + barWidth)
                {
                    colors[y * resolution + x] = Color.white;
                }
                // Right Bar
                else if (x >= startX2 && x < startX2 + barWidth)
                {
                    colors[y * resolution + x] = Color.white;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f));
    }

    private Sprite CreateFullscreenIconSprite(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        Color[] colors = new Color[resolution * resolution];

        // Clear
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        int thickness = resolution / 8;
        int padding = resolution / 6;
        int size = resolution - (padding * 2);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Check if inside the box area
                bool inBox = (x >= padding && x < resolution - padding) && (y >= padding && y < resolution - padding);
                
                if (inBox)
                {
                    // Corners logic
                    bool left = x < padding + thickness;
                    bool right = x >= resolution - padding - thickness;
                    bool bottom = y < padding + thickness;
                    bool top = y >= resolution - padding - thickness;

                    // Draw corners only (Top-Left, Top-Right, Bottom-Left, Bottom-Right)
                    if ((left || right) && (top || bottom))
                    {
                        colors[y * resolution + x] = Color.white;
                    }
                    // Connect horizontal/vertical lines slightly?
                    // Let's just draw a hollow box for simplicity, looks like maximize
                    else if (left || right || bottom || top)
                    {
                         // Full Box
                         colors[y * resolution + x] = Color.white;
                    }
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f));
    }



    public void SetXp(int currentXP, int maxXp, int currentLevel = 1, int maxLevels = 1)
    {
        if (XpBar == null) return;
        
        // Calculate progress within current level (0 to 1)
        float levelProgress = (float)currentXP / (float)maxXp;

        if (growthIcons != null && growthIcons.Length > 0)
        {
            // Determine Start Position
            float startPos = 0f;
            int startIdx = currentLevel - 1;
            
            // FIX: For Level 1, start at 0 (empty bar) instead of the first icon position
            if (currentLevel == 1)
            {
                startPos = 0f;
            }
            else if (startIdx >= 0 && startIdx < growthIcons.Length)
            {
                startPos = GetNormalizedPosition(growthIcons[startIdx].rectTransform);
            }
            else if (startIdx >= growthIcons.Length)
            {
                // If we are past the last icon, start from the last icon's position
                startPos = GetNormalizedPosition(growthIcons[growthIcons.Length - 1].rectTransform);
            }

            // Determine End Position
            float endPos = 1f; // Default to full bar if no next icon
            int endIdx = currentLevel;

            if (endIdx >= 0 && endIdx < growthIcons.Length)
            {
                endPos = GetNormalizedPosition(growthIcons[endIdx].rectTransform);
            }

            // Interpolate
            float finalFill = Mathf.Lerp(startPos, endPos, levelProgress);
            
            // Set Target for Smooth Animation
            targetXpFill = finalFill;
        }
        else
        {
            // Fallback to simple math if icons are missing
            float segmentSize = 1f / (float)maxLevels;
            float totalProgress = ((currentLevel - 1) * segmentSize) + (levelProgress * segmentSize);
            targetXpFill = totalProgress;
        }
    }

    private void InitializeFloatingTextPool()
    {
        // Fix: Reparent to Main Canvas to avoid layout distortion/squashing from HUD panels
        Transform parent = null;
        Canvas mainCanvas = null;
        if (pausedBg != null) mainCanvas = pausedBg.GetComponentInParent<Canvas>();
        if (mainCanvas == null) mainCanvas = FindFirstObjectByType<Canvas>();
        
        if (mainCanvas != null) parent = mainCanvas.transform;
        else if (XpBar != null) parent = XpBar.transform.parent;
        else parent = transform;

        GameObject template = (ScoreText != null) ? ScoreText.gameObject : null;
        if (template == null && floatingTextPrefab != null) template = floatingTextPrefab;

        if (template == null) return;

        floatingTextPool.Clear(); // Ensure pool is clean before init
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(template, parent);
            obj.name = "FloatingTextPool_" + i;
            obj.SetActive(false);
            
            // Fix: Ensure Scale is 1,1,1 (Square)
            obj.transform.localScale = Vector3.one;

            // Ensure Font is applied (Legacy Text)
            if (messageFont != null)
            {
                Text t = obj.GetComponent<Text>();
                if (t != null) 
                {
                    t.font = messageFont;
                    // Fix: Ensure overflow settings prevent squashing
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }

            // Ensure Font is applied (TMP)
            TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                 if (messageFontTmp != null) tmp.font = messageFontTmp;
                 else if (tmp.font == null) 
                 {
                     // Fallback 1: Default Settings
                     tmp.font = TMP_Settings.defaultFontAsset;
                     
                     // Fallback 2: Load explicit resource if default is missing
                     if (tmp.font == null)
                     {
                         tmp.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                     }
                 }
            }
            
            floatingTextPool.Enqueue(obj);
        }
    }

    private GameObject GetFloatingTextFromPool()
    {
        while (floatingTextPool.Count > 0)
        {
            GameObject obj = floatingTextPool.Dequeue();
            if (obj != null)
            {
                obj.SetActive(true);
                // Re-apply font in case it was lost/changed
                if (messageFont != null)
                {
                     Text t = obj.GetComponent<Text>();
                     if (t != null) t.font = messageFont;
                }

                // Re-apply font (TMP)
                TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                     if (messageFontTmp != null) tmp.font = messageFontTmp;
                     else if (tmp.font == null) 
                     {
                         tmp.font = TMP_Settings.defaultFontAsset;
                         if (tmp.font == null) tmp.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                     }
                }

                return obj;
            }
        }

        // If pool is empty, fallback
        Transform parent = null;
        Canvas mainCanvas = null;
        if (pausedBg != null) mainCanvas = pausedBg.GetComponentInParent<Canvas>();
        if (mainCanvas == null) mainCanvas = FindFirstObjectByType<Canvas>();
        
        if (mainCanvas != null) parent = mainCanvas.transform;
        else if (XpBar != null) parent = XpBar.transform.parent;
        else if (ScoreText != null) parent = ScoreText.transform.parent;
        else parent = transform;
        
        GameObject template = (ScoreText != null) ? ScoreText.gameObject : null;
        if (template == null && floatingTextPrefab != null) template = floatingTextPrefab;
        
        if (template != null)
        {
            GameObject objFallback = Instantiate(template, parent);
            objFallback.name = "FloatingXP_Fallback";
            objFallback.SetActive(true);

            // Assign Font (TMP)
            TextMeshProUGUI tmp = objFallback.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                 if (messageFontTmp != null) tmp.font = messageFontTmp;
                 else if (tmp.font == null) 
                 {
                     tmp.font = TMP_Settings.defaultFontAsset;
                     if (tmp.font == null) tmp.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                 }
            }
            
            // Add Outline - Removed
            /*
            if (objFallback.GetComponent<Outline>() == null)
            {
                 Outline outline = objFallback.AddComponent<Outline>();
                 outline.effectColor = Color.black;
                 outline.effectDistance = new Vector2(2, -2);
            }
            */
            
            return objFallback;
        }
        return null;
    }

    private void ReturnFloatingTextToPool(GameObject obj)
    {
        if (obj == null) return;
        obj.SetActive(false);
        floatingTextPool.Enqueue(obj);
    }

    public void ShowFloatingText(Vector3 worldPos, string text, Color color)
    {
        GameObject obj = GetFloatingTextFromPool();
        if (obj == null) 
        {
            return;
        }

        RectTransform rt = obj.GetComponent<RectTransform>();
        Text txt = obj.GetComponent<Text>();
        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();

        // Ensure it's last sibling to be on top
        obj.transform.SetAsLastSibling();
        // Reset Scale (Will be animated)
        obj.transform.localScale = Vector3.one;

        if (txt != null)
        {
            txt.resizeTextForBestFit = false; 
            txt.text = text;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            // High quality trick: Large font size, scaled down object
            txt.fontSize = 64; // Increased from 56 to 64
            txt.fontStyle = FontStyle.Bold; 
            
            // Remove Shadow if it exists
            Shadow shadow = obj.GetComponent<Shadow>();
            if (shadow != null) Destroy(shadow);
        }
        
        if (tmp != null)
        {
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 80; // Increased from 72 to 80
            tmp.fontStyle = FontStyles.Bold;
        }

        // Position
        if (Camera.main != null)
        {
            // Spawn slightly above the eaten fish (Offset Y)
            Vector3 offsetPos = worldPos + Vector3.up * 0.8f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(offsetPos);
            if (rt != null) 
            {
                rt.position = screenPos;
                // AUTO-FIX: Increase width to prevent text wrapping/shrinking (User Request)
                // Was 400, increasing to 600 to accommodate longer text
                rt.sizeDelta = new Vector2(600, 100);  
            }
        }
        
        // Start Animation
        StartCoroutine(AnimateFloatingText(obj, rt, txt, tmp));
    }

    private IEnumerator AnimateFloatingText(GameObject obj, RectTransform rt, Text txt, TextMeshProUGUI tmp)
    {
        float duration = 0.8f; 
        float elapsed = 0f;
        
        Vector3 startPos = (rt != null) ? rt.position : Vector3.zero;
        
        // Drift: Up and slightly random X
        float driftX = UnityEngine.Random.Range(-30f, 30f); 
        Vector3 endPos = startPos + Vector3.up * 100f + Vector3.right * driftX;

        // Scale Logic: Start tiny, target scale 0.3 (for high quality small text)
        // AUTO-FIX: Increased from 0.45 to 0.6 per user request
        Vector3 targetScale = Vector3.one * 0.6f; 
        if(rt != null) rt.localScale = Vector3.zero; 

        Color startColor = Color.white;
        if (txt != null) startColor = txt.color;
        if (tmp != null) startColor = tmp.color;
        
        Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (elapsed < duration)
        {
            if (obj == null) yield break;

            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // 1. Pop In (EaseOutBack - Cleaner, no double bounce)
            if (rt != null)
            {
                float scaleDuration = 0.3f;
                if (t < scaleDuration)
                {
                    float st = t / scaleDuration;
                    // Standard EaseOutBack
                    float c1 = 1.70158f;
                    float c3 = c1 + 1f;
                    float ease = 1f + c3 * Mathf.Pow(st - 1f, 3f) + c1 * Mathf.Pow(st - 1f, 2f);
                    
                    rt.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, ease);
                }
                else
                {
                    rt.localScale = targetScale;
                }
            }

            // 2. Position (Linear is smoother for floating)
            if (rt != null)
            {
                rt.position = Vector3.Lerp(startPos, endPos, t);
            }

            // 3. Fade Out (Last 50% for smoother exit)
            float fadeStart = 0.5f;
            Color currentColor = startColor;
            if (t > fadeStart)
            {
                float ft = (t - fadeStart) / (1f - fadeStart);
                currentColor = Color.Lerp(startColor, endColor, ft);
            }
            
            if (txt != null) txt.color = currentColor;
            if (tmp != null) tmp.color = currentColor;
            
            yield return null;
        }

        ReturnFloatingTextToPool(obj);
    }

    // Helper for "Pop" effect
    private float EvaluateEaseOutBack(float x) 
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1;
        return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
    }

    private float GetNormalizedPosition(RectTransform target)
    {
        if (XpBar == null || target == null) return 0f;
        
        RectTransform barRect = XpBar.rectTransform;
        Vector3[] corners = new Vector3[4];
        barRect.GetWorldCorners(corners);
        
        float startX = corners[0].x;
        float totalWidth = corners[2].x - corners[0].x;
        
        if (totalWidth <= 0) return 0f;

        float targetX = target.position.x;
        float normalized = (targetX - startX) / totalWidth;
        
        return Mathf.Clamp01(normalized);
    }

    private void UpdateGrowthIcons(int currentLevel)
    {
        if (growthIcons == null || growthIcons.Length == 0) return;

        for (int i = 0; i < growthIcons.Length; i++)
        {
            if (growthIcons[i] == null) continue;

            int iconLevel = i + 1;

            // --- Warning Icon Cleanup ---
            // We check for and destroy any existing "WarningIcon" objects to clean up the scene.
            Transform warningTrans = growthIcons[i].transform.Find("WarningIcon");
            if (warningTrans != null)
            {
                Destroy(warningTrans.gameObject);
            }
            // ---------------------------

            if (iconLevel < currentLevel)
            {
                // Past Levels: Completed Color (White)
                growthIcons[i].color = completedColor;
                growthIcons[i].transform.localScale = Vector3.one;
            }
            else if (iconLevel == currentLevel)
            {
                // Current Level: Highlighted (White) + Scaled Up (1.2x)
                growthIcons[i].color = currentColor;
                growthIcons[i].transform.localScale = Vector3.one * 1.2f;
            }
            else
            {
                // Future Levels: Locked (Solid Black Silhouette)
                growthIcons[i].color = lockedColor;
                growthIcons[i].transform.localScale = Vector3.one;
            }
        }
    }

    // Removed duplicate CreateMissingButtons
    private void TogglePauseBtn(bool isPaused)
    {
        if( pauseBtn == null || resumeBtn == null)
        {
            // Debug.Log("Missing pause/resume btns");
            return;
        }

        if(isPaused)
        {
            pauseBtn.SetActive(false);
            
            resumeBtn.SetActive(true);
            if(restartBtn != null) restartBtn.SetActive(true);
            if(menuBtn != null) menuBtn.SetActive(true);
            if(pausedBg != null) pausedBg.SetActive(true);

            // Re-attach listeners + sound to ensure they work after enabling
            SetupButton(resumeBtn, () => GameManager.instance.PlayPause());
            SetupButton(restartBtn, RestartGame);
            SetupButton(menuBtn, GoToMainMenu);

            // Ensure buttons are ON TOP of any other elements in the background
            if (resumeBtn != null) resumeBtn.transform.SetAsLastSibling();
            if (restartBtn != null) restartBtn.transform.SetAsLastSibling();
            if (menuBtn != null) menuBtn.transform.SetAsLastSibling();

            // Ensure layout is correct
            FixPauseLayout();
        }else
        {
            pauseBtn.SetActive(true);
            
            resumeBtn.SetActive(false);
            if(restartBtn != null) restartBtn.SetActive(false);
            if(menuBtn != null) menuBtn.SetActive(false);
            if(pausedBg != null) pausedBg.SetActive(false);
        }
    }

    public void RestartGame()
    {
        StartCoroutine(RestartGameRoutine());
    }

    private IEnumerator RestartGameRoutine()
    {
        // Delay to allow button click sound to play
        yield return new WaitForSecondsRealtime(0.25f);
        
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void GoToMainMenu()
    {
        StartCoroutine(GoToMainMenuRoutine());
    }

    private IEnumerator GoToMainMenuRoutine()
    {
        // Delay to allow button click sound to play
        yield return new WaitForSecondsRealtime(0.25f);
        
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    private void ShowScore(int score = 0)
    {
        if (ScoreScreen == null || ScoreText == null) return;
        ScoreScreen.SetActive(true);
        ScoreText.gameObject.SetActive(false);
        
        // Fix: Removed score display logic per user request
        /*
        if (messageText != null)
        {
            if (messageText.gameObject.activeSelf && !messageText.text.Contains("Score:"))
            {
                messageText.text += "\nScore: " + score.ToString();
            }
            else
            {
                messageText.text = "Score: " + score.ToString();
            }
            messageText.gameObject.SetActive(true);
        }
        */
    }

    private void ShowGameMessage(string message)
    {
        if (ScoreScreen == null) return;
        ScoreScreen.SetActive(true);
        if (ScoreText != null) ScoreText.gameObject.SetActive(false);
        
        if (messageText != null)
        {
            messageText.text = message;
            messageText.gameObject.SetActive(true);
        }
    }

    private void HideScore()
    {
        if (ScoreScreen == null || ScoreText == null) return;
        ScoreScreen.SetActive(false);
        ScoreText.text = "0";
        if (messageText != null) messageText.gameObject.SetActive(false);
    }

    // Helper to find UI objects even if inactive
    private GameObject FindUIObjectByName(params string[] names)
    {
        foreach (string name in names)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null) return obj;
        }
        
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (Canvas c in canvases)
        {
            if (c.gameObject.scene.rootCount == 0) continue; 
            foreach (string name in names)
            {
                Transform t = FindDeepChild(c.transform, name);
                if (t != null) return t.gameObject;
            }
        }
        return null;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            // Debug.Log("GuiManager created missing EventSystem.");
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}



