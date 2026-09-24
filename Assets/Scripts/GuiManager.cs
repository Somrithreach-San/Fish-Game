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

    [Header("Combo Multiplier Status")]
    [Tooltip("Drag your custom Multiplier / Frenzy icon sprite here (e.g. 2x/3x/5x boost icon)")]
    [SerializeField]
    private Sprite comboMultiplierSprite;
    private GameObject comboMultiplierIconObj;

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

    [Header("XP Progress Bar (Volume Slider Style)")]
    [SerializeField]
    private Sprite volumeSliderTrackSprite;
    [SerializeField]
    private Sprite volumeSliderFillSprite;
    private static Sprite _cachedVolumeSliderTrack;
    private static Sprite _cachedVolumeSliderFill;

    private RectTransform xpFillMaskRt;
    private Image xpFillImage;
    private Image xpTrackImage;
    private float targetFillPct = 0f;
    private float currentFillPct = 0f;
    public float sectionWidth = 95f;
    public float barHeight = 48f;
    public float totalBarWidth = 420f;
    private int currentSegmentCount = -1;
    private GameObject segmentedBarRoot;
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
    [Header("Game Over Messages & Modals")]
    [SerializeField]
    private Sprite gameOverModalSprite;
    [SerializeField]
    private Sprite tryAgainButtonSprite;
    [SerializeField]
    private Sprite backToMenuButtonSprite;
    [SerializeField]
    private Sprite pausedModalSprite;
    [SerializeField]
    private Sprite resumeButtonSprite;
    [SerializeField]
    private Sprite restartButtonSprite;
    [SerializeField]
    private Sprite levelCompletionModalSprite;
    [SerializeField]
    private Sprite shortContinueButtonSprite;
    [SerializeField]
    private Sprite shortRestartButtonSprite;
    private static Sprite _cachedGameOverModalSprite;
    private static Sprite _cachedTryAgainButtonSprite;
    private static Sprite _cachedBackToMenuButtonSprite;
    private static Sprite _cachedPausedModalSprite;
    private static Sprite _cachedResumeButtonSprite;
    private static Sprite _cachedRestartButtonSprite;
    private static Sprite _cachedLevelCompletionModalSprite;
    private static Sprite _cachedShortContinueSprite;
    private static Sprite _cachedShortRestartSprite;
    private static Sprite _cachedTimeLabelSprite;
    private static Sprite _cachedColonSprite;
    private static Sprite _cachedPlusSprite;
    private static Sprite _cachedMinusSprite;
    private static Sprite _cachedXpLabelSprite;
    private static Sprite _cachedXpRedLabelSprite;
    private static Sprite _cachedDoubleXpSprite;
    private static Sprite _cachedMultiplierSignSprite;
    private static Sprite[] _cachedDigitSprites = new Sprite[10];
    private static Sprite[] _cachedRedDigitSprites = new Sprite[10];
    private bool isSceneTransitionInProgress = false;
    
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
    public AudioSource UiAudioSource => uiAudioSource;

    public void PlayUiSound(AudioClip clip, float volume = 1.0f)
    {
        if (clip != null && AudioSettingsManager.IsSfxEnabled)
        {
            if (uiAudioSource == null)
            {
                uiAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                uiAudioSource.playOnAwake = false;
                uiAudioSource.ignoreListenerPause = true;
                AudioSettingsManager.RouteToSfx(uiAudioSource);
            }
            uiAudioSource.PlayOneShot(clip, volume);
        }
    }

    private void Awake()
    {
        if (pausedBg == null)
        {
            pausedBg = GameObject.Find("pausedBg") ?? GameObject.Find("PausedBG") ?? GameObject.Find("PauseBG");
        }
        if (pausedBg != null)
        {
            pausedBg.SetActive(false);
        }
        if (ScoreScreen == null)
        {
            ScoreScreen = GameObject.Find("ScoreScreen");
        }
        if (ScoreScreen != null)
        {
            ScoreScreen.SetActive(false);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        // 0. Ensure CanvasScaler is configured consistently with MainMenu (1920x1080 reference)
        EnsureCanvasScaler();

        // 0. Critical: Ensure EventSystem exists (Required for UI clicks)
        EnsureEventSystem();
        
        // Setup UI Audio
        uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.ignoreListenerPause = true; // Ensure UI sounds play when game is paused!

        // Fix: Ensure AudioListener volume is set (sometimes starts at 0 on mobile until interaction)
        // We will handle the actual "Unmute" in Update() on first tap.

        // Ensure buttons and pause modal exist (Restore if lost)
        CreateMissingButtons();

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

        EventManager.StartListening("GameWin", OnEventGameWin);
        EventManager.StartListening("GameLoss", OnEventGameLoss);
        EventManager.StartListening("GameStart", OnEventGameStart);
        EventManager.StartListening<bool>("gamePaused", OnEventGamePaused);
        EventManager.StartListening<int>("GameOver", OnEventGameOver);
        EventManager.StartListening<int>("onLevelUp", OnEventLevelUp);

        // Auto-heal XpBar and GrowthIcons if references were accidentally lost
        if (XpBar == null)
        {
            GameObject pbGo = GameObject.Find("ProgressBar1");
            if (pbGo != null)
            {
                Transform f = pbGo.transform.Find("XpBar");
                if (f != null) XpBar = f.GetComponent<Image>();
                else XpBar = pbGo.GetComponentInChildren<Image>();
            }
        }
        EnsureGrowthIconsAssigned();

        // Ensure XP bar starts empty and ProgressBar1 is uniformly scaled
        if (XpBar != null)
        {
             if (XpBar.type != Image.Type.Filled)
             {
                 XpBar.type = Image.Type.Filled;
                 XpBar.fillMethod = Image.FillMethod.Horizontal;
             }
             XpBar.fillAmount = 0f;

             // Runtime enforcement: Ensure root ProgressBar1 is uniformly scaled 1:1 and properly sized
             Transform pbTr = XpBar.transform.parent;
             if (pbTr != null)
             {
                 RectTransform pbRt = pbTr.GetComponent<RectTransform>();
                 if (pbRt != null)
                 {
                     pbRt.localScale = Vector3.one;
                     pbRt.anchoredPosition = new Vector2(67f, -116f);
                 }
             }
        }

        // Initialize dynamic segmented XP bar and icons for current stage
        EnsureModularSpritesLoaded();
        RebuildSegmentedBar(LevelManager.GetCurrentConfig().targetPlayerLevel);
        UpdateGrowthIcons(1); // Initial state
        SnapSegmentFills();

        // Fix: Ensure Main UI Canvas is above Shark Warning Canvas (Order 999)
        Canvas rootCanvas = GetRootCanvas();
        if (rootCanvas != null)
        {
            rootCanvas.sortingOrder = 2000; 
        }

        // Final Layout Fix: Run this to ensure all pause buttons are sized and ready
        FixPauseLayout();
        if (pausedBg != null) pausedBg.SetActive(false);
        
        // Fix: Ensure Pause Button is created and positioned correctly
        SetupTopRightControls();

        // Initialize Ability Button and Combo Streak UI
        SetupAbilityUI();

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
    public Sprite unpauseSprite;
    private static Sprite _cachedPauseIcon;
    private static Sprite _cachedUnpauseIcon;
    
    [Header("Custom Assets (Performance Boost)")]
    [SerializeField] public Font customFont; // For lmns1
    [SerializeField] public Sprite buttonShape; // For the bubble background

    private Sprite GetBubbleSprite()
    {
        if (buttonShape != null) return buttonShape;
        Sprite[] sprites = Resources.LoadAll<Sprite>("Bubble_Button");
        if (sprites != null && sprites.Length > 0) return sprites[0];
        Sprite single = Resources.Load<Sprite>("Bubble_Button");
        if (single != null) return single;
        Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite s in allSprites)
        {
            if (s.name.Contains("Bubble_Button")) return s;
        }
        return Resources.Load<Sprite>("Knob");
    }

    private Sprite GetPauseButtonSprite()
    {
        if (pauseSprite != null && (pauseSprite.name.Equals("Pause") || pauseSprite.name.Equals("Pause_0")))
        {
            return pauseSprite;
        }
        return LoadBestSprite("Assets/Graphics/GUI Components/Pause.png", "Pause", ref _cachedPauseIcon);
    }

    private Sprite GetUnpauseButtonSprite()
    {
        if (unpauseSprite != null && (unpauseSprite.name.Equals("Unpaused") || unpauseSprite.name.Equals("Unpaused_0")))
        {
            return unpauseSprite;
        }
        return LoadBestSprite("Assets/Graphics/GUI Components/Unpaused.png", "Unpaused", ref _cachedUnpauseIcon);
    }

    public Canvas GetRootCanvas()
    {
        Canvas c = GetComponent<Canvas>();
        if (c != null) return c;
        c = GetComponentInParent<Canvas>();
        if (c != null) return c;
        if (pausedBg != null && pausedBg.transform.parent != null)
        {
            c = pausedBg.transform.parent.GetComponentInParent<Canvas>();
            if (c != null) return c;
        }
        Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas canvas in allCanvases)
        {
            if (canvas.isRootCanvas && (pausedBg == null || canvas.gameObject != pausedBg))
                return canvas;
        }
        return FindFirstObjectByType<Canvas>();
    }

    public void EnsureCanvasScaler()
    {
        Canvas c = GetRootCanvas();
        if (c != null)
        {
            CanvasScaler scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = c.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    public float GetCanvasScale()
    {
        Canvas c = GetRootCanvas();
        if (c != null)
        {
            RectTransform rt = c.GetComponent<RectTransform>();
            if (rt != null && rt.rect.height > 0)
            {
                return Mathf.Clamp(rt.rect.height / 1080f, 0.55f, 1.2f);
            }
            if (rt != null && rt.rect.width > 0)
            {
                return Mathf.Clamp(rt.rect.width / 1920f, 0.55f, 1.2f);
            }
        }
        return 1.0f;
    }

    private void SetupTopRightControls()
    {
        // 1. Get Reference to Main Canvas (Parent of Controls)
        EnsureCanvasScaler();
        Canvas mainCanvas = GetRootCanvas();
        if (mainCanvas == null) return; 

        // 2. Handle Pause Button
        if (pauseBtn != null)
        {
            Destroy(pauseBtn);
            pauseBtn = null;
        }
        
        // Create Pause Button matching Settings close button (180x180, anchored top-right at (-50, -50))
        pauseBtn = CreatePauseBubbleButton(mainCanvas, () => {
             PlayButtonSound();
             GameManager.instance.PlayPause();
        });
        
        if (pauseBtn != null)
        {
             pauseBtn.SetActive(true);
             pauseBtn.transform.SetAsLastSibling();
        }

        // Ensure any old HackWinButton is removed
        GameObject oldHackBtn = GameObject.Find("HackWinButton");
        if (oldHackBtn != null) Destroy(oldHackBtn);
    }

    private GameObject CreatePauseBubbleButton(Canvas parentCanvas, UnityEngine.Events.UnityAction action)
    {
        // Remove any old PauseButton or btnPause in parentCanvas
        Transform oldBtn = parentCanvas.transform.Find("PauseButton");
        if (oldBtn != null) Destroy(oldBtn.gameObject);
        Transform oldBtn2 = parentCanvas.transform.Find("btnPause");
        if (oldBtn2 != null) Destroy(oldBtn2.gameObject);

        GameObject btnObj = new GameObject("PauseButton");
        btnObj.layer = parentCanvas.gameObject.layer;
        btnObj.transform.SetParent(parentCanvas.transform, false);

        // RectTransform: In-game HUD Pause Button (exact same dimensions and anchoring as Close_Button in Settings/Level pages)
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(130f, 130f);
        rt.anchoredPosition = new Vector2(-45f, -45f);
        rt.localScale = Vector3.one;

        Sprite pauseButtonSprite = GetPauseButtonSprite();

        // Button Image
        Image bgImg = btnObj.AddComponent<Image>();
        if (pauseButtonSprite != null)
        {
            bgImg.sprite = pauseButtonSprite;
            bgImg.preserveAspect = true;
        }
        else
        {
            Sprite bubble = GetBubbleSprite();
            if (bubble != null)
            {
                bgImg.sprite = bubble;
                bgImg.type = Image.Type.Simple;
            }
        }
        bgImg.color = Color.white;
        bgImg.raycastTarget = true;

        // Button component
        Button btn = btnObj.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => PlayButtonSound());
        btn.onClick.AddListener(action);

        // Fallback: Only create child text if no pause button sprite was found
        if (pauseButtonSprite == null)
        {
            GameObject iconObj = new GameObject("PauseIcon");
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform rtIcon = iconObj.AddComponent<RectTransform>();
            rtIcon.anchorMin = new Vector2(0.5f, 0.5f);
            rtIcon.anchorMax = new Vector2(0.5f, 0.5f);
            rtIcon.pivot = new Vector2(0.5f, 0.5f);
            rtIcon.anchoredPosition = Vector2.zero;
            rtIcon.sizeDelta = new Vector2(18f, 22f);

            Text t = iconObj.AddComponent<Text>();
            Font standardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (standardFont == null) standardFont = Font.CreateDynamicFontFromOSFont("Arial", 20);
            if (standardFont != null) t.font = standardFont;
            t.text = "❚❚";
            t.fontSize = 16;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
        }

        return btnObj;
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
        btn.transition = Selectable.Transition.None;
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

    public void SetupAbilityUI()
    {
        Canvas mainCanvas = GetRootCanvas();
        if (mainCanvas == null) return;

        bool isMobile = Application.isMobilePlatform || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;

        // Remove second UI bar under XP bar per Option 2 (Arcade Floating Text Only)
        Transform pbTr = (XpBar != null) ? XpBar.transform.parent : null;
        if (pbTr == null)
        {
            GameObject pbGo = GameObject.Find("ProgressBar1");
            if (pbGo != null) pbTr = pbGo.transform;
        }

        if (pbTr != null)
        {
            Transform existingCombo = pbTr.Find("UIComboDisplay");
            if (existingCombo != null)
            {
                Destroy(existingCombo.gameObject);
            }
        }

        // Remove Skill Button if present
        Transform existingBtn = mainCanvas.transform.Find("UIAbilityButton");
        if (existingBtn != null)
        {
            Destroy(existingBtn.gameObject);
        }
        if (UIAbilityButton.Instance != null)
        {
            Destroy(UIAbilityButton.Instance.gameObject);
        }
    }

    private Image doubleXpProgressImg;
    private Image infectedProgressImg;
    private Image comboMultiplierProgressImg;
    private Text comboMultiplierText;
    private int currentComboMultiplier = 0;
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
            if (doubleXpSprite == null) doubleXpSprite = Resources.Load<Sprite>("x2 xp fish") ?? Resources.Load<Sprite>("fish/x2 xp fish");
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

    public void SetComboMultiplierStatus(bool active, int multiplier = 1, float percent = 1f)
    {
        currentComboMultiplier = active ? multiplier : 0;

        if (active && multiplier >= 1 && XpBar != null)
        {
            Transform pbTr = XpBar.transform.parent;
            if (pbTr == null)
            {
                GameObject pbGo = GameObject.Find("ProgressBar1");
                if (pbGo != null) pbTr = pbGo.transform;
            }

            if (pbTr != null)
            {
                if (comboMultiplierIconObj == null)
                {
                    Transform existing = pbTr.Find("ComboMultiplierCircleContainer");
                    if (existing != null)
                    {
                        Destroy(existing.gameObject);
                    }

                    comboMultiplierIconObj = new GameObject("ComboMultiplierCircleContainer");
                    comboMultiplierIconObj.transform.SetParent(pbTr, false);

                    RectTransform rt = comboMultiplierIconObj.GetComponent<RectTransform>();
                    if (rt == null) rt = comboMultiplierIconObj.AddComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(62f, 62f);

                    Sprite circleRing = CreateRingSprite(128, 0.20f);

                    // 1. Foreground Radial Fill Ring (Pure white duration countdown)
                    GameObject fgObj = new GameObject("DurationFillRing");
                    fgObj.transform.SetParent(comboMultiplierIconObj.transform, false);
                    RectTransform fgRt = fgObj.AddComponent<RectTransform>();
                    fgRt.anchorMin = Vector2.zero;
                    fgRt.anchorMax = Vector2.one;
                    fgRt.sizeDelta = Vector2.zero;
                    comboMultiplierProgressImg = fgObj.AddComponent<Image>();
                    comboMultiplierProgressImg.sprite = circleRing;
                    comboMultiplierProgressImg.type = Image.Type.Filled;
                    comboMultiplierProgressImg.fillMethod = Image.FillMethod.Radial360;
                    comboMultiplierProgressImg.fillOrigin = (int)Image.Origin360.Top;
                    comboMultiplierProgressImg.fillClockwise = true;
                    comboMultiplierProgressImg.color = Color.white;
                    comboMultiplierProgressImg.fillAmount = percent;
                    comboMultiplierProgressImg.raycastTarget = false;
                }

                if (comboMultiplierIconObj != null)
                {
                    comboMultiplierIconObj.SetActive(true);
                    comboMultiplierIconObj.transform.SetAsLastSibling();

                    UpdateComboMultiplierDigits(multiplier);

                    if (comboMultiplierProgressImg != null)
                    {
                        comboMultiplierProgressImg.fillAmount = percent;
                    }
                }
            }
        }
        else
        {
            if (comboMultiplierIconObj != null)
            {
                comboMultiplierIconObj.transform.localScale = Vector3.one;
                comboMultiplierIconObj.SetActive(false);
            }
        }

        UpdateStatusIconsLayout();
    }

    private void UpdateComboMultiplierDigits(int multiplier)
    {
        if (comboMultiplierIconObj == null) return;

        Transform rowTr = comboMultiplierIconObj.transform.Find("MultiplierRow");
        GameObject rowObj;
        if (rowTr == null)
        {
            rowObj = new GameObject("MultiplierRow");
            rowObj.transform.SetParent(comboMultiplierIconObj.transform, false);
        }
        else
        {
            rowObj = rowTr.gameObject;
            for (int i = rowObj.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(rowObj.transform.GetChild(i).gameObject);
            }
        }

        RectTransform rowRt = rowObj.GetComponent<RectTransform>();
        if (rowRt == null) rowRt = rowObj.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = Vector2.zero;
        rowRt.localScale = Vector3.one;

        HorizontalLayoutGroup hlg = rowObj.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 1.2f;

        float digitHeight = 17f;
        float xHeight = 10.5f; // Smaller multiplier sign than numbers per user request
        float totalW = 0f;

        // 1. 'x' Multiplier Sign (from Texts/x.png)
        Sprite xSp = GetMultiplierSignSprite();
        if (xSp != null)
        {
            GameObject xObj = new GameObject("Sign_X");
            xObj.transform.SetParent(rowObj.transform, false);
            RectTransform xRt = xObj.AddComponent<RectTransform>();
            float xAspect = (xSp.rect.height > 0) ? (xSp.rect.width / xSp.rect.height) : 0.85f;
            float xW = xHeight * xAspect;
            xRt.sizeDelta = new Vector2(xW, xHeight);

            Image xImg = xObj.AddComponent<Image>();
            xImg.sprite = xSp;
            xImg.preserveAspect = true;
            xImg.raycastTarget = false;
            xImg.color = Color.white;
            totalW += xW + hlg.spacing;
        }

        // 2. Multiplier Number Digits (from Texts/0.png..9.png)
        string numStr = multiplier.ToString();
        for (int i = 0; i < numStr.Length; i++)
        {
            int d = numStr[i] - '0';
            Sprite dSp = GetDigitSprite(d);
            if (dSp != null)
            {
                GameObject dObj = new GameObject("Digit_" + i);
                dObj.transform.SetParent(rowObj.transform, false);
                RectTransform dRt = dObj.AddComponent<RectTransform>();
                float dAspect = (dSp.rect.height > 0) ? (dSp.rect.width / dSp.rect.height) : 0.71f;
                float dW = digitHeight * dAspect;
                dRt.sizeDelta = new Vector2(dW, digitHeight);

                Image dImg = dObj.AddComponent<Image>();
                dImg.sprite = dSp;
                dImg.preserveAspect = true;
                dImg.raycastTarget = false;
                dImg.color = Color.white;
                totalW += dW + hlg.spacing;
            }
        }

        rowRt.sizeDelta = new Vector2(totalW, digitHeight);
    }

    public void UpdateComboMultiplierProgress(float percent)
    {
        if (comboMultiplierProgressImg != null)
        {
            comboMultiplierProgressImg.fillAmount = Mathf.Clamp01(percent);
        }
    }

    private void UpdateStatusIconsLayout()
    {
        Transform pbTr = (XpBar != null) ? XpBar.transform.parent : null;
        if (pbTr == null)
        {
            GameObject pbGo = GameObject.Find("ProgressBar1");
            if (pbGo != null) pbTr = pbGo.transform;
        }
        if (pbTr == null) return;

        RectTransform barParent = pbTr as RectTransform;
        if (barParent == null) return;

        // 0. CLEANUP: Remove any LayoutGroup from the Parent
        HorizontalLayoutGroup hlg = barParent.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) Destroy(hlg);
        
        ContentSizeFitter csf = barParent.GetComponent<ContentSizeFitter>();
        if (csf != null) Destroy(csf);

        // 1. Identify active icons in display priority order:
        // Combo Streaks 1st (always displays), Double XP 2nd, Infected Status 3rd
        List<GameObject> activeIcons = new List<GameObject>();
        if (comboMultiplierIconObj != null && comboMultiplierIconObj.activeSelf) activeIcons.Add(comboMultiplierIconObj);
        if (doubleXpIconObj != null && doubleXpIconObj.activeSelf) activeIcons.Add(doubleXpIconObj);
        if (infectedIconObj != null && infectedIconObj.activeSelf) activeIcons.Add(infectedIconObj);

        // 2. Constants
        float iconSize = 38f;
        float multiplierSize = 62f;
        const float GAP = 14f;
        const float ICON_SPACING = 16f; // Comfortable visual gap between streaks and x2 XP boost icons

        // 3. Position Icons OUTSIDE the Bar (To the Right of totalBarWidth)
        float currentX = GAP; 

        foreach (var icon in activeIcons)
        {
             RectTransform rt = icon.GetComponent<RectTransform>();
             if (rt == null) continue;

             bool isMultiplier = (icon == comboMultiplierIconObj);
             float itemWidth = isMultiplier ? multiplierSize : iconSize;
             float itemHeight = isMultiplier ? multiplierSize : iconSize;

             rt.sizeDelta = new Vector2(itemWidth, itemHeight);
             rt.anchorMin = new Vector2(1f, 0.5f);
             rt.anchorMax = new Vector2(1f, 0.5f);
             rt.pivot = new Vector2(0.5f, 0.5f);
             rt.anchoredPosition = new Vector2(currentX + itemWidth * 0.5f, 0f);
             if (!isMultiplier)
             {
                 rt.localScale = Vector3.one;
             }
             icon.transform.SetAsLastSibling();
             
             currentX += itemWidth + ICON_SPACING;
        }
    }

    private void Update()
    {
        // Smooth Fill Unified XP Bar
        if (xpFillImage != null)
        {
            if (!xpFillImage.gameObject.activeSelf)
            {
                xpFillImage.gameObject.SetActive(true);
            }

            float dt = (Time.timeScale > 0f) ? Time.deltaTime : Time.unscaledDeltaTime;
            currentFillPct = Mathf.Lerp(currentFillPct, targetFillPct, dt * 8f);
            if (Mathf.Abs(currentFillPct - targetFillPct) < 0.0005f)
            {
                currentFillPct = targetFillPct;
            }
            xpFillImage.fillAmount = currentFillPct;
        }

        // Streak UI Pulsing Animation (Scale up and down continuously when reaching x10)
        if (comboMultiplierIconObj != null)
        {
            bool isGameOver = (GameManager.instance != null && GameManager.instance.IsGameOver) || LevelManager.IsLevelCompleted;
            if (isGameOver)
            {
                comboMultiplierIconObj.transform.localScale = Vector3.one;
                if (comboMultiplierIconObj.activeSelf)
                {
                    comboMultiplierIconObj.SetActive(false);
                }
            }
            else if (comboMultiplierIconObj.activeSelf)
            {
                bool isReadyAtTen = (currentComboMultiplier >= 10);
                if (!isReadyAtTen && PlayerAbilitySystem.Instance != null)
                {
                    isReadyAtTen = (PlayerAbilitySystem.Instance.ComboStreak >= 10 || PlayerAbilitySystem.Instance.IsAbilityReady);
                }

                if (isReadyAtTen)
                {
                    float pulse = 1.0f + Mathf.Sin(Time.unscaledTime * 7.0f) * 0.14f;
                    comboMultiplierIconObj.transform.localScale = new Vector3(pulse, pulse, 1f);
                }
                else
                {
                    comboMultiplierIconObj.transform.localScale = Vector3.one;
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        bool isInvalidPause = pauseSprite == null;
        if (pauseSprite != null)
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(pauseSprite);
            if (!string.IsNullOrEmpty(path) && (path.Contains("3rd Party") || !path.Contains("GUI Components")))
            {
                isInvalidPause = true;
            }
        }

        if (isInvalidPause)
        {
            pauseSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Pause.png");
            if (pauseSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Pause.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { pauseSprite = s; break; }
                    }
                }
            }
        }

        bool isInvalidUnpause = unpauseSprite == null;
        if (unpauseSprite != null)
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(unpauseSprite);
            if (!string.IsNullOrEmpty(path) && (path.Contains("3rd Party") || !path.Contains("GUI Components")))
            {
                isInvalidUnpause = true;
            }
        }

        if (isInvalidUnpause)
        {
            unpauseSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Unpaused.png");
            if (unpauseSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Unpaused.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { unpauseSprite = s; break; }
                    }
                }
            }
        }

        if (messageFontTmp == null)
        {
             // Try to find default TMP font
             messageFontTmp = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        if (volumeSliderTrackSprite == null)
        {
            volumeSliderTrackSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Track.png");
        }
        if (volumeSliderFillSprite == null)
        {
            volumeSliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png");
            if (volumeSliderFillSprite == null)
                volumeSliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Fill.png");
        }

        if (segmentedBarRoot != null && currentSegmentCount > 0)
        {
            RefreshSegmentLayout();
        }
    }
#endif

    private void SetupButton(GameObject btnObj, UnityEngine.Events.UnityAction action)
    {
        if (btnObj == null) return;
        Button btn = btnObj.GetComponent<Button>();
        if (btn == null) return;

        btn.transition = Selectable.Transition.None;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => PlayButtonSound()); // Add standard click sound
        btn.onClick.AddListener(action);

        ButtonHoverEffect bhe = btnObj.GetComponent<ButtonHoverEffect>();
        if (bhe != null) Destroy(bhe);
    }

    public void PlayButtonSound()
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
        if (!AudioSettingsManager.CanPlayButtonSound()) return;

        if (GameManager.instance != null && GameManager.instance.ButtonSoundEffect != null)
        {
            if (uiAudioSource == null) 
            {
                 uiAudioSource = gameObject.AddComponent<AudioSource>();
                 uiAudioSource.ignoreListenerPause = true;
                 uiAudioSource.spatialBlend = 0f;
                 AudioSettingsManager.RouteToSfx(uiAudioSource);
            }
            uiAudioSource.PlayOneShot(GameManager.instance.ButtonSoundEffect, 1.0f);
        }
    }

    private GameObject EnsurePausedModal()
    {
        if (pausedBg == null) return null;

        Transform modalTr = pausedBg.transform.Find("PausedModal");
        GameObject modalObj = null;

        if (modalTr != null)
        {
            try
            {
                modalObj = modalTr.gameObject;
            }
            catch
            {
                modalObj = null;
                modalTr = null;
            }
        }

        if (modalObj == null)
        {
            modalObj = new GameObject("PausedModal");
            modalObj.layer = pausedBg.layer;
            modalObj.transform.SetParent(pausedBg.transform, false);
            modalTr = modalObj.transform;
        }

        if (modalObj == null || modalTr == null) return null;

        modalObj.SetActive(true);

        RectTransform rtModal = modalObj.GetComponent<RectTransform>();
        if (rtModal == null) rtModal = modalObj.AddComponent<RectTransform>();
        rtModal.anchorMin = new Vector2(0.5f, 0.5f);
        rtModal.anchorMax = new Vector2(0.5f, 0.5f);
        rtModal.pivot = new Vector2(0.5f, 0.5f);
        rtModal.anchoredPosition = new Vector2(0f, 0f);
        // Paused_Modal matches Main Menu modal dimensions (640x788)
        rtModal.sizeDelta = new Vector2(640f, 788f);
        rtModal.localScale = Vector3.one;

        Image modalImg = modalObj.GetComponent<Image>();
        if (modalImg == null) modalImg = modalObj.AddComponent<Image>();
        modalImg.sprite = GetPausedModalSprite();
        modalImg.color = Color.white;
        modalImg.type = Image.Type.Simple;
        modalImg.preserveAspect = true;
        modalImg.raycastTarget = false;

        try
        {
            if (modalTr != null && modalTr.parent != null)
            {
                modalTr.SetSiblingIndex(0); // Position behind the buttons
            }
        }
        catch { }

        return modalObj;
    }

    private void CreateMissingButtons()
    {
        // 1. Ensure Background/Parent exists
        if (pausedBg == null)
        {
            pausedBg = GameObject.Find("pausedBg") ?? GameObject.Find("PausedBG") ?? GameObject.Find("PauseBG");
        }

        Canvas mainCanvas = GetRootCanvas();

        if (pausedBg == null)
        {
            pausedBg = new GameObject("pausedBg");
            if (mainCanvas != null)
            {
                pausedBg.transform.SetParent(mainCanvas.transform, false);
            }
            
            Image img = pausedBg.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.45f);
        }
        else
        {
            Canvas nestedCanvas = pausedBg.GetComponent<Canvas>();
            if (nestedCanvas != null) Destroy(nestedCanvas);
            GraphicRaycaster gr = pausedBg.GetComponent<GraphicRaycaster>();
            if (gr != null) Destroy(gr);

            Image img = pausedBg.GetComponent<Image>();
            if (img == null) img = pausedBg.AddComponent<Image>();
            img.sprite = null;
            img.color = new Color(0, 0, 0, 0.45f);
            img.raycastTarget = true;
        }

        if (mainCanvas != null)
        {
            pausedBg.layer = mainCanvas.gameObject.layer;
        }
        else
        {
            pausedBg.layer = 5;
        }

        RectTransform rtBg = pausedBg.GetComponent<RectTransform>();
        if (rtBg != null)
        {
            rtBg.anchorMin = Vector2.zero;
            rtBg.anchorMax = Vector2.one;
            rtBg.pivot = new Vector2(0.5f, 0.5f);
            rtBg.offsetMin = Vector2.zero;
            rtBg.offsetMax = Vector2.zero;
            rtBg.localScale = Vector3.one;
        }

        // 2. Ensure Paused Modal graphic is present
        EnsurePausedModal();

        // 3. Create Buttons if missing
        if (resumeBtn == null) resumeBtn = CreateButton("ResumeBtn", pausedBg.transform);
        if (restartBtn == null) restartBtn = CreateButton("RestartBtn", pausedBg.transform);
        if (menuBtn == null) menuBtn = CreateButton("MenuBtn", pausedBg.transform);
        
        // 4. Position and format buttons
        FixPauseLayout();

        // 5. Setup button click actions
        SetupButton(resumeBtn, () => GameManager.instance.PlayPause());
        SetupButton(restartBtn, RestartGame);
        SetupButton(menuBtn, GoToMainMenu);
    }

    private GameObject CreateButton(string name, Transform parent)
    {
        if (parent == null) return null;

        Transform existing = parent.Find(name);
        GameObject btnObj = null;

        if (existing != null)
        {
            try
            {
                btnObj = existing.gameObject;
            }
            catch
            {
                btnObj = null;
            }
        }

        if (btnObj == null)
        {
            btnObj = new GameObject(name);
            btnObj.layer = parent.gameObject.layer;
            btnObj.transform.SetParent(parent, false);
            
            btnObj.AddComponent<Image>();
            btnObj.AddComponent<Button>();
        }
        
        return btnObj;
    }

    private void FixPauseLayout()
    {
        if (pausedBg == null) return;

        // 1. Ensure Paused Modal graphic is created and styled
        EnsurePausedModal();

        // Standard Main Menu button dimensions: 280 x 158
        Vector2 buttonSize = new Vector2(280f, 158f);

        // 2. Format Resume Button (Top) - matching Main Menu Play Button (0, 95)
        if (resumeBtn != null)
        {
            try
            {
                resumeBtn.SetActive(true);
                SetupPauseButtonGraphic(resumeBtn, GetResumeButtonSprite(), buttonSize, new Vector2(0f, 95f));
                resumeBtn.transform.SetAsLastSibling();
            }
            catch
            {
                resumeBtn = CreateButton("ResumeBtn", pausedBg.transform);
                if (resumeBtn != null)
                {
                    SetupPauseButtonGraphic(resumeBtn, GetResumeButtonSprite(), buttonSize, new Vector2(0f, 95f));
                    SetupButton(resumeBtn, () => GameManager.instance.PlayPause());
                    resumeBtn.transform.SetAsLastSibling();
                }
            }
        }

        // 3. Format Restart Button (Middle) - matching Main Menu Settings Button (0, -25)
        if (restartBtn != null)
        {
            try
            {
                restartBtn.SetActive(true);
                SetupPauseButtonGraphic(restartBtn, GetRestartButtonSprite(), buttonSize, new Vector2(0f, -25f));
                restartBtn.transform.SetAsLastSibling();
            }
            catch
            {
                restartBtn = CreateButton("RestartBtn", pausedBg.transform);
                if (restartBtn != null)
                {
                    SetupPauseButtonGraphic(restartBtn, GetRestartButtonSprite(), buttonSize, new Vector2(0f, -25f));
                    SetupButton(restartBtn, RestartGame);
                    restartBtn.transform.SetAsLastSibling();
                }
            }
        }

        // 4. Format Back To Menu Button (Bottom) - matching Main Menu Quit Button (0, -145)
        if (menuBtn != null)
        {
            try
            {
                menuBtn.SetActive(true);
                SetupPauseButtonGraphic(menuBtn, GetBackToMenuButtonSprite(), buttonSize, new Vector2(0f, -145f));
                menuBtn.transform.SetAsLastSibling();
            }
            catch
            {
                menuBtn = CreateButton("MenuBtn", pausedBg.transform);
                if (menuBtn != null)
                {
                    SetupPauseButtonGraphic(menuBtn, GetBackToMenuButtonSprite(), buttonSize, new Vector2(0f, -145f));
                    SetupButton(menuBtn, GoToMainMenu);
                    menuBtn.transform.SetAsLastSibling();
                }
            }
        }
    }

    private void SetupPauseButtonGraphic(GameObject btnObj, Sprite sprite, Vector2 size, Vector2 pos)
    {
        if (btnObj == null) return;

        try
        {
            RectTransform rt = btnObj.GetComponent<RectTransform>();
            if (rt == null) rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;

            Image img = btnObj.GetComponent<Image>();
            if (img == null) img = btnObj.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            img.color = Color.white;
            img.raycastTarget = true;

            Button btn = btnObj.GetComponent<Button>();
            if (btn == null) btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            // Hide overlay text as the button sprite contains its own text
            Text txt = btnObj.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                txt.text = "";
                txt.gameObject.SetActive(false);
            }

            TextMeshProUGUI tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.text = "";
                tmp.gameObject.SetActive(false);
            }

            ButtonHoverEffect bhe = btnObj.GetComponent<ButtonHoverEffect>();
            if (bhe != null) Destroy(bhe);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("SetupPauseButtonGraphic exception: " + ex.Message);
        }
    }

    private void CustomizeButton(GameObject btnObj, string label, Color color, Vector2 size, int fontSize = 24)
    {
        if (btnObj == null) return;
        
        // 1. Image Style: Use Bubble_Button with full white for iridescent translucent look
        Image img = btnObj.GetComponent<Image>();
        if (img != null)
        {
            Sprite bubble = GetBubbleSprite();
            if (bubble != null)
            {
                img.sprite = bubble;
                img.name = "Bubble_Button";
            }
            else if (img.sprite == null || img.sprite.name != "ProceduralCircle")
            {
                img.sprite = CreateCircleSprite(256, 2);
                img.sprite.name = "ProceduralCircle";
            }
            img.color = Color.white;
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
        
        // 3. Text Style (White, Bold, No Best Fit - Match Main Menu)
        Text txt = btnObj.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.text = label;
            txt.color = Color.white;
            txt.resizeTextForBestFit = false; // Main Menu uses fixed size
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold; // Main Menu is Bold
            txt.alignment = TextAnchor.MiddleCenter;
            txt.alignByGeometry = true; // Improve centering for fonts with offsets
            
            // Khmer Limon font
            Font standardFont = customFont != null ? customFont : Resources.Load<Font>("lmns1");
            if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

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
            tmp.color = Color.white;
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



    private Sprite LoadSpriteWithFallback(string name)
    {
        Sprite s = Resources.Load<Sprite>(name);
        if (s != null) return s;

        Sprite[] allInRes = Resources.LoadAll<Sprite>(name);
        if (allInRes != null && allInRes.Length > 0 && allInRes[0] != null) return allInRes[0];

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name) return all[i];
        }

#if UNITY_EDITOR
        string[] paths = new string[] {
            $"Assets/Resources/{name}.png",
            $"Assets/Graphics/{name}.png"
        };
        foreach (var p in paths)
        {
            Sprite edSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (edSprite != null) return edSprite;
        }
#endif
        return null;
    }

    private Sprite GetSliderTrackSprite()
    {
        if (volumeSliderTrackSprite != null) return volumeSliderTrackSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Volume_Slider_Track.png", "Volume_Slider_Track", ref _cachedVolumeSliderTrack);
    }

    private Sprite GetSliderFillSprite()
    {
        if (volumeSliderFillSprite != null) return volumeSliderFillSprite;
        Sprite sp = LoadBestSprite("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png", "New_Volume_Slider_Fill", ref _cachedVolumeSliderFill);
        if (sp == null) sp = LoadBestSprite("Assets/Graphics/GUI Components/Volume_Slider_Fill.png", "Volume_Slider_Fill", ref _cachedVolumeSliderFill);
        return sp;
    }

    private static Sprite[] _cachedXpBarTracks = new Sprite[7];
    private static Sprite[] _cachedXpBarFills = new Sprite[7];

    private int GetMappedBarAssetIndex(int segmentCount)
    {
        // Upward shifted mapping:
        // 2 fishes stage -> use 3 fishes asset group
        // 3 fishes stage -> use 4 fishes asset group
        // 4 fishes stage -> use 5 fishes asset group
        // 5+ fishes stage -> use 6 fishes asset group
        int mapped = segmentCount + 1;
        return Mathf.Clamp(mapped, 3, 6);
    }

    private Sprite GetXpBarTrackSprite(int segmentCount)
    {
        int assetIndex = GetMappedBarAssetIndex(segmentCount);
        if (_cachedXpBarTracks[assetIndex] != null) return _cachedXpBarTracks[assetIndex];

        string name = $"Xp_bar_{assetIndex}_Fishes";
        string path = $"Assets/Graphics/GUI Components/Xp Bars/{name}.png";
        Sprite s = LoadBestSprite(path, name, ref _cachedXpBarTracks[assetIndex]);
        if (s != null) return s;

        return GetSliderTrackSprite(); // Fallback
    }

    private Sprite GetXpBarFillSprite(int segmentCount)
    {
        int assetIndex = GetMappedBarAssetIndex(segmentCount);
        if (_cachedXpBarFills[assetIndex] != null) return _cachedXpBarFills[assetIndex];

        string name = $"Xp_bar_{assetIndex}_Fishes_Fill";
        string path = $"Assets/Graphics/GUI Components/Xp Bars/{name}.png";
        Sprite s = LoadBestSprite(path, name, ref _cachedXpBarFills[assetIndex]);
        if (s == null && assetIndex == 4)
        {
            s = LoadBestSprite("Assets/Graphics/GUI Components/Xp Bars/Xp_bar_fill.png", "Xp_bar_fill", ref _cachedXpBarFills[assetIndex]);
        }
        if (s != null) return s;

        return GetSliderFillSprite(); // Fallback
    }

    private void EnsureModularSpritesLoaded()
    {
        for (int i = 2; i <= 6; i++)
        {
            GetXpBarTrackSprite(i);
            GetXpBarFillSprite(i);
        }
        GetSliderTrackSprite();
        GetSliderFillSprite();
    }

    public void SnapSegmentFills()
    {
        currentFillPct = targetFillPct;
        if (xpFillImage != null)
        {
            if (!xpFillImage.gameObject.activeSelf)
            {
                xpFillImage.gameObject.SetActive(true);
            }
            xpFillImage.fillAmount = currentFillPct;
        }
    }

    [ContextMenu("Rebuild XP Bar Now")]
    public void RebuildSegmentedBarEditor()
    {
        RebuildSegmentedBar(LevelManager.GetCurrentConfig().targetPlayerLevel);
        UpdateGrowthIcons(1);
        SnapSegmentFills();
    }

    public void RefreshSegmentLayout()
    {
        RebuildSegmentedBar(currentSegmentCount > 0 ? currentSegmentCount : LevelManager.GetCurrentConfig().targetPlayerLevel);
    }

    public void RebuildSegmentedBar(int segmentCount)
    {
        if (segmentCount <= 0)
        {
            segmentCount = LevelManager.GetCurrentConfig().targetPlayerLevel;
        }
        segmentCount = Mathf.Clamp(segmentCount, 2, (growthIcons != null && growthIcons.Length > 0) ? growthIcons.Length : 6);

        Transform pbTr = (XpBar != null) ? XpBar.transform.parent : null;
        if (pbTr == null)
        {
            GameObject pbGo = GameObject.Find("ProgressBar1");
            if (pbGo != null) pbTr = pbGo.transform;
        }
        if (pbTr == null) return;

        // Hide and clear legacy single glass background and legacy single fill
        Image pbImg = pbTr.GetComponent<Image>();
        if (pbImg != null)
        {
            pbImg.enabled = false;
            pbImg.sprite = null;
        }

        Transform legacyImgChild = pbTr.Find("Image");
        if (legacyImgChild != null) legacyImgChild.gameObject.SetActive(false);

        if (XpBar != null)
        {
            XpBar.gameObject.SetActive(false);
        }

        Sprite trackSprite = GetXpBarTrackSprite(segmentCount);
        Sprite fillSprite = GetXpBarFillSprite(segmentCount);

        barHeight = 52f;
        float aspect = (trackSprite != null && trackSprite.rect.height > 0) 
            ? (trackSprite.rect.width / trackSprite.rect.height) 
            : (segmentCount * 1.4f + 0.8f);
        totalBarWidth = Mathf.Round(barHeight * aspect);

        RectTransform pbRt = pbTr.GetComponent<RectTransform>();
        if (pbRt != null)
        {
            pbRt.anchorMin = new Vector2(0f, 1f);
            pbRt.anchorMax = new Vector2(0f, 1f);
            pbRt.pivot = new Vector2(0f, 1f);
            pbRt.anchoredPosition = new Vector2(67f, -116f);
            pbRt.sizeDelta = new Vector2(totalBarWidth, barHeight);
            pbRt.localScale = Vector3.one;
        }

        // Locate or create segmentedBarRoot (VolumeSliderRoot)
        if (segmentedBarRoot == null)
        {
            Transform existing = pbTr.Find("SegmentedBarRoot");
            if (existing != null)
            {
                segmentedBarRoot = existing.gameObject;
            }
            else
            {
                segmentedBarRoot = new GameObject("SegmentedBarRoot");
                segmentedBarRoot.transform.SetParent(pbTr, false);
            }
        }

        segmentedBarRoot.transform.SetSiblingIndex(0);

        RectTransform rootRt = segmentedBarRoot.GetComponent<RectTransform>();
        if (rootRt == null) rootRt = segmentedBarRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.sizeDelta = Vector2.zero;
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.localScale = Vector3.one;

        // 1. Background Track
        Transform trackTr = segmentedBarRoot.transform.Find("Track");
        GameObject trackObj = trackTr != null ? trackTr.gameObject : null;
        if (trackObj == null)
        {
            trackObj = new GameObject("Track");
            trackObj.transform.SetParent(segmentedBarRoot.transform, false);
        }
        trackObj.transform.SetSiblingIndex(0);

        RectTransform trackRt = trackObj.GetComponent<RectTransform>();
        if (trackRt == null) trackRt = trackObj.AddComponent<RectTransform>();
        trackRt.anchorMin = Vector2.zero;
        trackRt.anchorMax = Vector2.one;
        trackRt.offsetMin = Vector2.zero;
        trackRt.offsetMax = Vector2.zero;
        trackRt.localScale = Vector3.one;

        xpTrackImage = trackObj.GetComponent<Image>();
        if (xpTrackImage == null) xpTrackImage = trackObj.AddComponent<Image>();
        xpTrackImage.sprite = trackSprite;
        xpTrackImage.type = Image.Type.Simple;
        xpTrackImage.preserveAspect = false;
        xpTrackImage.color = Color.white;
        xpTrackImage.raycastTarget = false;

        // 2. Fill Image (Robust Image.Type.Filled horizontal fill)
        Transform fillImgTr = segmentedBarRoot.transform.Find("FillImg");
        GameObject fillImgObj = fillImgTr != null ? fillImgTr.gameObject : null;
        if (fillImgObj == null)
        {
            fillImgObj = new GameObject("FillImg");
            fillImgObj.transform.SetParent(segmentedBarRoot.transform, false);
        }
        fillImgObj.transform.SetSiblingIndex(1);

        RectTransform fillImgRt = fillImgObj.GetComponent<RectTransform>();
        if (fillImgRt == null) fillImgRt = fillImgObj.AddComponent<RectTransform>();
        fillImgRt.anchorMin = Vector2.zero;
        fillImgRt.anchorMax = Vector2.one;
        // Inner padding so the track's rounded borders and caps remain visible around the fill
        fillImgRt.offsetMin = new Vector2(5f, 5f);
        fillImgRt.offsetMax = new Vector2(-5f, -5f);
        fillImgRt.localScale = Vector3.one;

        xpFillImage = fillImgObj.GetComponent<Image>();
        if (xpFillImage == null) xpFillImage = fillImgObj.AddComponent<Image>();
        xpFillImage.sprite = fillSprite;
        xpFillImage.type = Image.Type.Filled;
        xpFillImage.fillMethod = Image.FillMethod.Horizontal;
        xpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        xpFillImage.fillAmount = currentFillPct;
        xpFillImage.preserveAspect = false;
        xpFillImage.color = Color.white;
        xpFillImage.raycastTarget = false;

        // Clean up old FillMask if present
        Transform oldMask = segmentedBarRoot.transform.Find("FillMask");
        if (oldMask != null) Destroy(oldMask.gameObject);

        // Clean up any other old children in segmentedBarRoot
        for (int c = segmentedBarRoot.transform.childCount - 1; c >= 0; c--)
        {
            Transform child = segmentedBarRoot.transform.GetChild(c);
            if (child != trackObj.transform && child != fillImgObj.transform)
            {
                Destroy(child.gameObject);
            }
        }

        currentSegmentCount = segmentCount;
        FormatGrowthIconsLayout(segmentCount);
        UpdateStatusIconsLayout();
    }

    public void SetXp(int currentXP, int maxXp, int currentLevel = 1, int maxLevels = -1)
    {
        if (maxLevels <= 0)
        {
            maxLevels = LevelManager.GetCurrentConfig().targetPlayerLevel;
        }
        maxLevels = Mathf.Clamp(maxLevels, 2, (growthIcons != null && growthIcons.Length > 0) ? growthIcons.Length : 6);

        if (segmentedBarRoot == null || currentSegmentCount != maxLevels)
        {
            RebuildSegmentedBar(maxLevels);
        }

        // Calculate progress within current level (0 to 1)
        float levelProgress = (maxXp > 0) ? Mathf.Clamp01((float)currentXP / (float)maxXp) : 0f;

        UpdateGrowthIcons(currentLevel, maxLevels);

        int currentSegIndex = Mathf.Clamp(currentLevel - 1, 0, maxLevels - 1);
        targetFillPct = Mathf.Clamp01(((float)currentSegIndex + levelProgress) / (float)maxLevels);
        targetXpFill = targetFillPct;
    }

    private void InitializeFloatingTextPool()
    {
        // Fix: Reparent to Main Canvas to avoid layout distortion/squashing from HUD panels
        Transform parent = null;
        Canvas mainCanvas = GetRootCanvas();
        
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
        Canvas mainCanvas = GetRootCanvas();
        
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

    public void ShowFloatingDoubleXp(Vector3 worldPos)
    {
        Canvas rootCanvas = GetRootCanvas();
        if (rootCanvas == null) return;

        Sprite bannerSp = GetDoubleXpBannerSprite();
        if (bannerSp == null)
        {
            ShowLegacyFloatingText(worldPos, "DOUBLE XP", new Color(1f, 0.85f, 0f, 1f));
            return;
        }

        GameObject popupObj = new GameObject("FloatingDoubleXP");
        popupObj.transform.SetParent(rootCanvas.transform, false);

        CanvasGroup cg = popupObj.AddComponent<CanvasGroup>();
        RectTransform rt = popupObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        float height = 48f;
        float aspect = (bannerSp.rect.height > 0) ? (bannerSp.rect.width / bannerSp.rect.height) : 2.5f;
        float width = height * aspect;
        rt.sizeDelta = new Vector2(width, height);

        Image img = popupObj.AddComponent<Image>();
        img.sprite = bannerSp;
        img.preserveAspect = true;
        img.raycastTarget = false;

        if (Camera.main != null)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos + Vector3.up * 0.7f);
            rt.position = screenPos;
        }

        StartCoroutine(AnimateFloatingPopup(popupObj, rt, cg));
    }

    public void ShowFloatingStreakBonus(Vector3 worldPos, int streakAmount = 5)
    {
        Canvas rootCanvas = GetRootCanvas();
        if (rootCanvas == null) return;

        GameObject popupObj = new GameObject("FloatingStreakBonus");
        popupObj.transform.SetParent(rootCanvas.transform, false);

        CanvasGroup cg = popupObj.AddComponent<CanvasGroup>();
        RectTransform rt = popupObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        HorizontalLayoutGroup hlg = popupObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 3f;

        float digitHeight = 36f;
        float multiplierHeight = 22f; // Sized smaller than digits
        float totalWidth = 0f;

        // 1. Multiplier Symbol ('x.png')
        Sprite multSp = GetMultiplierSignSprite();
        if (multSp != null)
        {
            GameObject mObj = new GameObject("MultSign");
            mObj.transform.SetParent(popupObj.transform, false);
            RectTransform mRt = mObj.AddComponent<RectTransform>();
            float mAspect = (multSp.rect.height > 0) ? (multSp.rect.width / multSp.rect.height) : 0.8f;
            float mWidth = multiplierHeight * mAspect;
            mRt.sizeDelta = new Vector2(mWidth, multiplierHeight);

            Image mImg = mObj.AddComponent<Image>();
            mImg.sprite = multSp;
            mImg.preserveAspect = true;
            mImg.raycastTarget = false;
            totalWidth += mWidth + hlg.spacing;
        }

        // 2. Digits (e.g. 5)
        string numStr = Mathf.Abs(streakAmount).ToString();
        for (int i = 0; i < numStr.Length; i++)
        {
            int digit = numStr[i] - '0';
            Sprite digitSp = GetDigitSprite(digit);
            if (digitSp != null)
            {
                GameObject dObj = new GameObject("Digit_" + i);
                dObj.transform.SetParent(popupObj.transform, false);
                RectTransform dRt = dObj.AddComponent<RectTransform>();
                float dAspect = (digitSp.rect.height > 0) ? (digitSp.rect.width / digitSp.rect.height) : 0.71f;
                float dWidth = digitHeight * dAspect;
                dRt.sizeDelta = new Vector2(dWidth, digitHeight);

                Image dImg = dObj.AddComponent<Image>();
                dImg.sprite = digitSp;
                dImg.preserveAspect = true;
                dImg.raycastTarget = false;
                totalWidth += dWidth + hlg.spacing;
            }
        }

        rt.sizeDelta = new Vector2(totalWidth + 10f, digitHeight);

        if (Camera.main != null)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos + Vector3.up * 0.7f);
            rt.position = screenPos;
        }

        StartCoroutine(AnimateFloatingPopup(popupObj, rt, cg));
    }

    public void ShowFloatingXp(Vector3 worldPos, int xpAmount, bool isPenalty = false)
    {
        Canvas rootCanvas = GetRootCanvas();
        if (rootCanvas == null) return;

        GameObject popupObj = new GameObject("FloatingXP");
        popupObj.transform.SetParent(rootCanvas.transform, false);

        CanvasGroup cg = popupObj.AddComponent<CanvasGroup>();
        RectTransform rt = popupObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        HorizontalLayoutGroup hlg = popupObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 3f;

        float digitHeight = 36f;
        float signHeight = 22f; // Sized smaller than digits similar to streak x10 style
        float totalWidth = 0f;

        // 1. Sign (+ or -)
        Sprite signSp = isPenalty ? GetMinusSprite() : GetPlusSprite();
        if (signSp != null)
        {
            GameObject sObj = new GameObject("Sign");
            sObj.transform.SetParent(popupObj.transform, false);
            RectTransform sRt = sObj.AddComponent<RectTransform>();
            
            float sWidth, sHeight;
            if (isPenalty)
            {
                // -.png (344x161) has a thick stroke. Scale height to ~9px and width to ~19px to match +.png's (22x22) line thickness
                sHeight = signHeight * (161f / 402f);
                sWidth = signHeight * (344f / 402f);
            }
            else
            {
                float sAspect = (signSp.rect.height > 0) ? (signSp.rect.width / signSp.rect.height) : 1f;
                sHeight = signHeight;
                sWidth = signHeight * sAspect;
            }
            sRt.sizeDelta = new Vector2(sWidth, sHeight);

            Image sImg = sObj.AddComponent<Image>();
            sImg.sprite = signSp;
            sImg.preserveAspect = true;
            sImg.raycastTarget = false;
            totalWidth += sWidth + hlg.spacing;
        }

        // 2. Digits
        string numStr = Mathf.Abs(xpAmount).ToString();
        for (int i = 0; i < numStr.Length; i++)
        {
            int digit = numStr[i] - '0';
            Sprite digitSp = isPenalty ? GetRedDigitSprite(digit) : GetDigitSprite(digit);
            if (digitSp != null)
            {
                GameObject dObj = new GameObject("Digit_" + i);
                dObj.transform.SetParent(popupObj.transform, false);
                RectTransform dRt = dObj.AddComponent<RectTransform>();
                float dAspect = (digitSp.rect.height > 0) ? (digitSp.rect.width / digitSp.rect.height) : 0.71f;
                float dWidth = digitHeight * dAspect;
                dRt.sizeDelta = new Vector2(dWidth, digitHeight);

                Image dImg = dObj.AddComponent<Image>();
                dImg.sprite = digitSp;
                dImg.preserveAspect = true;
                dImg.raycastTarget = false;
                totalWidth += dWidth + hlg.spacing;
            }
        }

        // 3. XP Badge
        Sprite xpBadgeSp = isPenalty ? GetXpRedLabelSprite() : GetXpLabelSprite();
        if (xpBadgeSp != null)
        {
            GameObject xpObj = new GameObject("XPBadge");
            xpObj.transform.SetParent(popupObj.transform, false);
            RectTransform xpRt = xpObj.AddComponent<RectTransform>();
            float xpAspect = (xpBadgeSp.rect.height > 0) ? (xpBadgeSp.rect.width / xpBadgeSp.rect.height) : 1.35f;
            float xpWidth = (digitHeight * 0.95f) * xpAspect;
            xpRt.sizeDelta = new Vector2(xpWidth, digitHeight * 0.95f);

            Image xpImg = xpObj.AddComponent<Image>();
            xpImg.sprite = xpBadgeSp;
            xpImg.preserveAspect = true;
            xpImg.raycastTarget = false;
            totalWidth += xpWidth;
        }

        rt.sizeDelta = new Vector2(totalWidth + 10f, digitHeight);

        // Position in screen space
        if (Camera.main != null)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos + Vector3.up * 0.6f);
            rt.position = screenPos;
        }

        StartCoroutine(AnimateFloatingPopup(popupObj, rt, cg));
    }

    private IEnumerator AnimateFloatingPopup(GameObject obj, RectTransform rt, CanvasGroup cg)
    {
        float duration = 0.85f;
        float elapsed = 0f;

        Vector3 startPos = (rt != null) ? rt.position : Vector3.zero;
        float driftX = UnityEngine.Random.Range(-15f, 15f);
        Vector3 endPos = startPos + Vector3.up * 85f + Vector3.right * driftX;

        Vector3 targetScale = Vector3.one;
        if (rt != null) rt.localScale = Vector3.zero;

        while (elapsed < duration)
        {
            if (obj == null) yield break;

            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // 1. Pop In (EaseOutBack)
            if (rt != null)
            {
                float scaleDuration = 0.28f;
                if (t < scaleDuration)
                {
                    float st = t / scaleDuration;
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

            // 2. Position Drift
            if (rt != null)
            {
                rt.position = Vector3.Lerp(startPos, endPos, t);
            }

            // 3. Fade Out (Last 45%)
            float fadeStart = 0.55f;
            if (cg != null)
            {
                if (t > fadeStart)
                {
                    float ft = (t - fadeStart) / (1f - fadeStart);
                    cg.alpha = Mathf.Lerp(1f, 0f, ft);
                }
                else
                {
                    cg.alpha = 1f;
                }
            }

            yield return null;
        }

        if (obj != null) Destroy(obj);
    }

    public void ShowFloatingText(Vector3 worldPos, string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 1. Check for Double XP / Golden Fish
        if (text.Equals("RtImas", System.StringComparison.OrdinalIgnoreCase) ||
            text.IndexOf("Double", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            ShowFloatingDoubleXp(worldPos);
            return;
        }

        // 2. Check if string represents an XP amount (e.g. "+15 BinÞú", "÷15 BinÞú", "-10 BinÞú", "15 XP")
        bool hasDigits = false;
        int numValue = 0;
        string cleanNum = "";
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                cleanNum += text[i];
                hasDigits = true;
            }
        }

        if (hasDigits && int.TryParse(cleanNum, out numValue))
        {
            bool isPenalty = text.StartsWith("-") || (color.r > 0.8f && color.g < 0.4f && color.b < 0.4f);
            ShowFloatingXp(worldPos, numValue, isPenalty);
            return;
        }

        // Fallback for non-numeric custom text
        ShowLegacyFloatingText(worldPos, text, color);
    }

    public void ShowLegacyFloatingText(Vector3 worldPos, string text, Color color)
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
            Font activeFont = customFont != null ? customFont : Resources.Load<Font>("lmns1");
            if (activeFont != null) txt.font = activeFont;
            txt.resizeTextForBestFit = false; 
            txt.text = text;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            // High quality trick: Large font size, scaled down object
            txt.fontSize = 64;
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
            tmp.fontSize = 80;
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

        // Scale Logic: Start tiny, target scale 0.6
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

            // 1. Pop In (EaseOutBack)
            if (rt != null)
            {
                float scaleDuration = 0.3f;
                if (t < scaleDuration)
                {
                    float st = t / scaleDuration;
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

    private static Sprite[] s_OceanGrowthSprites = null;
    private static Sprite[] s_RiverGrowthSprites = null;

    private static Sprite LoadGrowthSpriteSafe(string resourceName, string assetPath)
    {
        Sprite s = null;
#if UNITY_EDITOR
        s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
#endif
        if (s == null)
        {
            s = Resources.Load<Sprite>(resourceName);
            if (s == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>(resourceName);
                if (all != null && all.Length > 0) s = all[0];
            }
        }
        return s;
    }

    public static void EnsureGrowthSpritesLoaded()
    {
        if (s_OceanGrowthSprites == null || s_OceanGrowthSprites.Length < 6)
        {
            s_OceanGrowthSprites = new Sprite[6];
            s_OceanGrowthSprites[0] = LoadGrowthSpriteSafe("level 1 fish", "Assets/Graphics/fish/level 1 fish.png");
            s_OceanGrowthSprites[1] = LoadGrowthSpriteSafe("level 2 fish", "Assets/Graphics/fish/level 2 fish.png");
            s_OceanGrowthSprites[2] = LoadGrowthSpriteSafe("level 3 fish", "Assets/Graphics/fish/level 3 fish.png");
            s_OceanGrowthSprites[3] = LoadGrowthSpriteSafe("level 4 fish", "Assets/Graphics/fish/level 4 fish.png");
            s_OceanGrowthSprites[4] = LoadGrowthSpriteSafe("level 5 fish", "Assets/Graphics/fish/level 5 fish.png");
            s_OceanGrowthSprites[5] = LoadGrowthSpriteSafe("level 5 fish", "Assets/Graphics/fish/level 5 fish.png");
        }

        if (s_RiverGrowthSprites == null || s_RiverGrowthSprites.Length < 6)
        {
            s_RiverGrowthSprites = new Sprite[6];
            s_RiverGrowthSprites[0] = LoadGrowthSpriteSafe("river level 1 fish", "Assets/Graphics/fish/river level 1 fish.png");
            s_RiverGrowthSprites[1] = LoadGrowthSpriteSafe("river level 2 fish", "Assets/Graphics/fish/river level 2 fish.png");
            s_RiverGrowthSprites[2] = LoadGrowthSpriteSafe("river level 3 fish", "Assets/Graphics/fish/river level 3 fish.png");
            s_RiverGrowthSprites[3] = LoadGrowthSpriteSafe("river level 4 fish", "Assets/Graphics/fish/river level 4 fish.png");
            if (s_RiverGrowthSprites[3] == null)
            {
                s_RiverGrowthSprites[3] = LoadGrowthSpriteSafe("river player_fish_closed mouth", "Assets/Graphics/fish/river player_fish_closed mouth.png");
            }
            if (s_RiverGrowthSprites[3] == null)
            {
                s_RiverGrowthSprites[3] = LoadGrowthSpriteSafe("new river player_fish", "Assets/Graphics/fish/new river player_fish.png");
            }
            if (s_RiverGrowthSprites[3] == null)
            {
                s_RiverGrowthSprites[3] = LoadGrowthSpriteSafe("river level 3 fish", "Assets/Graphics/fish/river level 3 fish.png");
            }
            s_RiverGrowthSprites[4] = LoadGrowthSpriteSafe("river level 5 fish", "Assets/Graphics/fish/river level 5 fish.png");
            s_RiverGrowthSprites[5] = LoadGrowthSpriteSafe("river level 5 fish", "Assets/Graphics/fish/river level 5 fish.png");
        }
    }

    public static Sprite GetGrowthSprite(int index, bool isLake)
    {
        EnsureGrowthSpritesLoaded();
        Sprite[] target = isLake ? s_RiverGrowthSprites : s_OceanGrowthSprites;
        if (target != null && index >= 0 && index < target.Length)
        {
            return target[index];
        }
        return null;
    }

    public void ApplyGrowthIconSprites(bool isLake)
    {
        EnsureGrowthIconsAssigned();
        EnsureGrowthSpritesLoaded();

        Sprite[] targetSprites = isLake ? s_RiverGrowthSprites : s_OceanGrowthSprites;
        if (growthIcons != null && targetSprites != null)
        {
            for (int i = 0; i < growthIcons.Length; i++)
            {
                if (growthIcons[i] != null && i < targetSprites.Length && targetSprites[i] != null)
                {
                    growthIcons[i].sprite = targetSprites[i];
                }
            }
        }
    }

    public void EnsureGrowthIconsAssigned()
    {
        bool needsPopulation = (growthIcons == null || growthIcons.Length == 0);
        if (!needsPopulation)
        {
            bool hasNull = false;
            for (int i = 0; i < growthIcons.Length; i++)
            {
                if (growthIcons[i] == null) { hasNull = true; break; }
            }
            needsPopulation = hasNull;
        }

        if (needsPopulation)
        {
            GameObject container = GameObject.Find("GrowthIconsContainer");
            if (container != null)
            {
                List<Image> found = new List<Image>();
                for (int i = 1; i <= 6; i++)
                {
                    Transform t = container.transform.Find($"FishIcon_{i}");
                    if (t != null)
                    {
                        Image img = t.GetComponent<Image>();
                        if (img != null) found.Add(img);
                    }
                }
                if (found.Count > 0)
                {
                    growthIcons = found.ToArray();
                }
            }
        }
    }

    public void UpdateGrowthIcons(int currentLevel, int maxLevel = -1)
    {
        EnsureGrowthIconsAssigned();
        ApplyGrowthIconSprites(LevelManager.IsCurrentLakeLevel);
        if (growthIcons == null || growthIcons.Length == 0) return;

        if (maxLevel <= 0)
        {
            maxLevel = LevelManager.GetCurrentConfig().targetPlayerLevel;
        }
        maxLevel = Mathf.Clamp(maxLevel, 1, growthIcons.Length);

        FormatGrowthIconsLayout(maxLevel);

        for (int i = 0; i < growthIcons.Length; i++)
        {
            if (growthIcons[i] == null) continue;

            int iconLevel = i + 1;
            bool isVisible = (iconLevel <= maxLevel);
            growthIcons[i].gameObject.SetActive(isVisible);

            if (!isVisible) continue;

            // --- Warning Icon Cleanup ---
            Transform warningTrans = growthIcons[i].transform.Find("WarningIcon");
            if (warningTrans != null)
            {
                Destroy(warningTrans.gameObject);
            }

            if (iconLevel < currentLevel)
            {
                // Past Levels: Completed Color (White)
                growthIcons[i].color = completedColor;
                growthIcons[i].transform.localScale = Vector3.one;
            }
            else if (iconLevel == currentLevel)
            {
                // Current Level: Highlighted (White)
                growthIcons[i].color = currentColor;
                growthIcons[i].transform.localScale = Vector3.one * 1.05f;
            }
            else
            {
                // Future Levels: Locked (Solid Black Silhouette)
                growthIcons[i].color = lockedColor;
                growthIcons[i].transform.localScale = Vector3.one;
            }
        }
    }

    public void FormatGrowthIconsLayout(int activeCount = -1)
    {
        EnsureGrowthIconsAssigned();
        if (growthIcons == null || growthIcons.Length == 0) return;

        if (activeCount <= 0)
        {
            activeCount = LevelManager.GetCurrentConfig().targetPlayerLevel;
        }
        float totalWidth = totalBarWidth;

        // 1. Position the container cleanly right above the volume slider bar
        Transform container = null;
        for (int cIdx = 0; cIdx < growthIcons.Length; cIdx++)
        {
            if (growthIcons[cIdx] != null)
            {
                container = growthIcons[cIdx].transform.parent;
                break;
            }
        }
        if (container == null)
        {
            GameObject cGo = GameObject.Find("GrowthIconsContainer");
            if (cGo != null) container = cGo.transform;
        }

        if (container != null)
        {
            RectTransform containerRt = container.GetComponent<RectTransform>();
            if (containerRt != null)
            {
                containerRt.anchorMin = new Vector2(0f, 0.5f);
                containerRt.anchorMax = new Vector2(0f, 0.5f);
                containerRt.pivot = new Vector2(0f, 0f);
                containerRt.anchoredPosition = new Vector2(0f, 45f); // Preserves exact screen position while XP bar moves down
                containerRt.sizeDelta = new Vector2(totalWidth, 50f);
            }

            // Completely eliminate HorizontalLayoutGroup so it never scrambles or centers icons in slots
            HorizontalLayoutGroup hlg = container.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.enabled = false;
                Destroy(hlg);
            }
        }

        // 2. Exact positions and sizes matching Inspector screenshots
        // ProgressBar1: PosX=67, PosY=-116, Width=484, Height=52
        // FishIcon_3: PosX=199.6, PosY=-2, Width=47.52, Height=40
        // FishIcon_4: PosX=296.4, PosY=7, Width=48, Height=28
        // FishIcon_5: PosX=393.9, PosY=-4.57, Width=48.86, Height=46.15
        float segmentWidth = totalWidth / activeCount;
        bool isLake = LevelManager.IsCurrentLakeLevel;

        for (int i = 0; i < growthIcons.Length; i++)
        {
            if (growthIcons[i] == null) continue;

            bool isVisible = (i < activeCount);
            growthIcons[i].gameObject.SetActive(isVisible);

            if (!isVisible) continue;

            RectTransform rt = growthIcons[i].rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f); // Left and bottom aligned

            growthIcons[i].preserveAspect = true;

            float posX, posY, iconW, iconH;

            if (isLake)
            {
                // Lake / River (Levels 5 - 8)
                if (i == 0) // River FishIcon_1 (Aspect ~1.01)
                {
                    posX = 6.0f;
                    posY = 2.0f;
                    iconW = 36.0f;
                    iconH = 36.0f;
                }
                else if (i == 1) // River FishIcon_2 (Aspect ~1.01) - exact Inspector values: Pos(103, 0), Size(42, 42)
                {
                    posX = 103.0f;
                    posY = 0.0f;
                    iconW = 42.0f;
                    iconH = 42.0f;
                }
                else if (i == 2) // River FishIcon_3 (Aspect ~1.01) - exact Inspector values: Pos(200, -5.5), Size(51.5208, 53)
                {
                    posX = 200.0f;
                    posY = -5.5f;
                    iconW = 51.5208f;
                    iconH = 53.0f;
                }
                else if (i == 3) // River FishIcon_4 (Elongated Pike/Gar) - exact Inspector values: Pos(292.5, 8.5), Size(63.0059, 29.5)
                {
                    posX = 292.5f;
                    posY = 8.5f;
                    iconW = 63.0059f;
                    iconH = 29.5f;
                }
                else if (i == 4) // River FishIcon_5 (Level 5 Lake Fish) - exact Inspector values: Pos(388, -2.7842), Size(76.0713, 60.0951)
                {
                    posX = 388.0f;
                    posY = -2.7842f;
                    iconW = 76.0713f;
                    iconH = 60.0951f;
                }
                else // River FishIcon_6
                {
                    posX = i * segmentWidth + 6f;
                    posY = 0f;
                    iconW = 46f;
                    iconH = 35f;
                }
            }
            else
            {
                // Ocean / Sea (Levels 1 - 4)
                if (i == 0) // FishIcon_1
                {
                    posX = 6.0f;
                    posY = 5.16f;
                    iconW = 38.93f;
                    iconH = 28.78f;
                }
                else if (i == 1) // FishIcon_2
                {
                    posX = 102.8f;
                    posY = 1.0f;
                    iconW = 42.0f;
                    iconH = 34.0f;
                }
                else if (i == 2) // FishIcon_3
                {
                    posX = 199.6f;
                    posY = -2.0f;
                    iconW = 47.5234f;
                    iconH = 40.0f;
                }
                else if (i == 3) // FishIcon_4
                {
                    posX = 296.4f;
                    posY = 7.0f;
                    iconW = 48.0f;
                    iconH = 28.0f;
                }
                else if (i == 4) // FishIcon_5
                {
                    posX = 393.9f;
                    posY = -4.5742f;
                    iconW = 48.8647f;
                    iconH = 46.1484f;
                }
                else // FishIcon_6
                {
                    posX = i * segmentWidth + 6f;
                    posY = 0f;
                    iconW = 46f;
                    iconH = 35f;
                }
            }

            rt.sizeDelta = new Vector2(iconW, iconH);
            rt.anchoredPosition = new Vector2(posX, posY);
        }
    }

    // Removed duplicate CreateMissingButtons
    private void TogglePauseBtn(bool isPaused)
    {
        // Keep the top-right control button visible and swap its sprite to Unpaused (play) or Pause
        if (pauseBtn != null)
        {
            pauseBtn.SetActive(true);
            Image btnImg = pauseBtn.GetComponent<Image>();
            if (btnImg != null)
            {
                Sprite targetSprite = isPaused ? GetUnpauseButtonSprite() : GetPauseButtonSprite();
                if (targetSprite != null)
                {
                    btnImg.sprite = targetSprite;
                    btnImg.preserveAspect = true;
                }
            }
            if (isPaused)
            {
                pauseBtn.transform.SetAsLastSibling();
            }
        }

        if (isPaused)
        {
            if (pausedBg != null)
            {
                FixPauseLayout();
                pausedBg.SetActive(true);
                pausedBg.transform.SetAsLastSibling();
            }
            if (pauseBtn != null) pauseBtn.transform.SetAsLastSibling();
        }
        else
        {
            if (pausedBg != null) pausedBg.SetActive(false);
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
        if (ScoreScreen != null) ScoreScreen.SetActive(false);
    }

    private void ShowGameMessage(string message)
    {
        if (ScoreScreen != null) ScoreScreen.SetActive(false);
    }

    private void HideScore()
    {
        if (ScoreScreen != null) ScoreScreen.SetActive(false);
        if (ScoreText != null) ScoreText.text = "0";
        if (messageText != null) messageText.gameObject.SetActive(false);
    }

    private void OnEventGameWin()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ShowStageClearModal();
    }

    private void OnEventGameLoss()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ShowGameOverModal();
    }

    private void OnEventGameStart()
    {
        SetXp(0, 1, 1);
        SnapSegmentFills();
        HideScore();
        UpdateGrowthIcons(1);
        if (pauseBtn != null)
        {
            pauseBtn.SetActive(true);
            pauseBtn.transform.SetAsLastSibling();
        }
        else
        {
            SetupTopRightControls();
        }
    }

    private void OnEventGamePaused(bool isPaused)
    {
        TogglePauseBtn(isPaused);
    }

    private void OnEventGameOver(int score)
    {
        ShowScore(score);
    }

    private void OnEventLevelUp(int level)
    {
        UpdateGrowthIcons(level);
    }

    private void OnDestroy()
    {
        EventManager.StopListening("GameWin", OnEventGameWin);
        EventManager.StopListening("GameLoss", OnEventGameLoss);
        EventManager.StopListening("GameStart", OnEventGameStart);
        EventManager.StopListening<bool>("gamePaused", OnEventGamePaused);
        EventManager.StopListening<int>("GameOver", OnEventGameOver);
        EventManager.StopListening<int>("onLevelUp", OnEventLevelUp);
    }

    private GameObject endLevelModalObj;
    private Coroutine stageEndRoutine;

    public void ShowStageClearModal(float delay = 1.8f)
    {
        if (comboMultiplierIconObj != null)
        {
            comboMultiplierIconObj.transform.localScale = Vector3.one;
            comboMultiplierIconObj.SetActive(false);
        }
        if (stageEndRoutine != null) StopCoroutine(stageEndRoutine);
        stageEndRoutine = StartCoroutine(DelayedEndLevelModal(true, delay));
    }

    public void ShowGameOverModal(float delay = 1.6f)
    {
        if (comboMultiplierIconObj != null)
        {
            comboMultiplierIconObj.transform.localScale = Vector3.one;
            comboMultiplierIconObj.SetActive(false);
        }
        if (stageEndRoutine != null) StopCoroutine(stageEndRoutine);
        stageEndRoutine = StartCoroutine(DelayedEndLevelModal(false, delay));
    }

    private IEnumerator DelayedEndLevelModal(bool isVictory, float delay)
    {
        if (delay > 0f)
        {
            float timer = 0f;
            while (timer < delay)
            {
                timer += (Time.timeScale > 0f) ? Time.deltaTime : Time.unscaledDeltaTime;
                yield return null;
            }
        }
        CreateEndLevelModal(isVictory);
    }

    private Font GetStandardFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        if (f == null) f = Resources.Load<Font>("Arial");
        return f;
    }

    private Font GetKhmerFont()
    {
        Font f = customFont;
        if (f == null) f = messageFont;
        if (f == null) f = Resources.Load<Font>("lmns1");
        return f;
    }

    private Sprite LoadBestSprite(string assetPath, string resourceName, ref Sprite cache)
    {
        if (cache != null) return cache;

#if UNITY_EDITOR
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (assets != null && assets.Length > 0)
        {
            Sprite best = null;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s)
                {
                    if (best == null || (s.rect.width * s.rect.height > best.rect.width * best.rect.height))
                    {
                        best = s;
                    }
                }
            }
            if (best != null) { cache = best; return cache; }
        }
#endif

        Sprite[] allRes = Resources.LoadAll<Sprite>(resourceName);
        if (allRes != null && allRes.Length > 0)
        {
            Sprite best = null;
            for (int i = 0; i < allRes.Length; i++)
            {
                if (allRes[i] != null)
                {
                    if (best == null || (allRes[i].rect.width * allRes[i].rect.height > best.rect.width * best.rect.height))
                    {
                        best = allRes[i];
                    }
                }
            }
            if (best != null) { cache = best; return cache; }
        }

        Sprite single = Resources.Load<Sprite>(resourceName);
        if (single != null) { cache = single; return cache; }

        Sprite[] allInMemory = Resources.FindObjectsOfTypeAll<Sprite>();
        Sprite bestMem = null;
        for (int i = 0; i < allInMemory.Length; i++)
        {
            Sprite s = allInMemory[i];
            if (s != null && s.name.Contains(resourceName))
            {
                if (bestMem == null || (s.rect.width * s.rect.height > bestMem.rect.width * bestMem.rect.height))
                    bestMem = s;
            }
        }
        if (bestMem != null) { cache = bestMem; return cache; }

        return null;
    }

    private Sprite GetGameOverModalSprite()
    {
        if (gameOverModalSprite != null) return gameOverModalSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Game_Over_Modal.png", "Game_Over_Modal", ref _cachedGameOverModalSprite);
    }

    private Sprite GetTryAgainButtonSprite()
    {
        if (tryAgainButtonSprite != null) return tryAgainButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Try_Again_Button.png", "Try_Again_Button", ref _cachedTryAgainButtonSprite);
    }

    private Sprite GetBackToMenuButtonSprite()
    {
        if (backToMenuButtonSprite != null) return backToMenuButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Back_To_Menu_Button.png", "Back_To_Menu_Button", ref _cachedBackToMenuButtonSprite);
    }

    private Sprite GetPausedModalSprite()
    {
        if (pausedModalSprite != null) return pausedModalSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Paused_Modal.png", "Paused_Modal", ref _cachedPausedModalSprite);
    }

    private Sprite GetResumeButtonSprite()
    {
        if (resumeButtonSprite != null) return resumeButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Resume_Button.png", "Resume_Button", ref _cachedResumeButtonSprite);
    }

    private Sprite GetRestartButtonSprite()
    {
        if (restartButtonSprite != null) return restartButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Restart_Button.png", "Restart_Button", ref _cachedRestartButtonSprite);
    }

    private Sprite GetLevelCompletionModalSprite()
    {
        if (levelCompletionModalSprite != null) return levelCompletionModalSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Level_Completion_Modal.png", "Level_Completion_Modal", ref _cachedLevelCompletionModalSprite);
    }

    private Sprite GetShortContinueButtonSprite()
    {
        if (shortContinueButtonSprite != null) return shortContinueButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Short_Continue_Button.png", "Short_Continue_Button", ref _cachedShortContinueSprite);
    }

    private Sprite GetShortRestartButtonSprite()
    {
        if (shortRestartButtonSprite != null) return shortRestartButtonSprite;
        return LoadBestSprite("Assets/Graphics/GUI Components/Short_Restart_Button.png", "Short_Restart_Button", ref _cachedShortRestartSprite);
    }

    private Sprite GetTimeLabelSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/TIME_.png", "Texts/TIME_", ref _cachedTimeLabelSprite);
    }

    private Sprite GetColonSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/colon.png", "Texts/colon", ref _cachedColonSprite);
    }

    private Sprite GetDigitSprite(int digit)
    {
        if (digit < 0 || digit > 9) return null;
        if (_cachedDigitSprites[digit] != null) return _cachedDigitSprites[digit];
        _cachedDigitSprites[digit] = LoadBestSprite($"Assets/Graphics/GUI Components/Texts/{digit}.png", $"Texts/{digit}", ref _cachedDigitSprites[digit]);
        return _cachedDigitSprites[digit];
    }

    private Sprite GetRedDigitSprite(int digit)
    {
        if (digit < 0 || digit > 9) return null;
        if (_cachedRedDigitSprites[digit] != null) return _cachedRedDigitSprites[digit];
        _cachedRedDigitSprites[digit] = LoadBestSprite($"Assets/Graphics/GUI Components/Texts/{digit}_Red.png", $"Texts/{digit}_Red", ref _cachedRedDigitSprites[digit]);
        return _cachedRedDigitSprites[digit];
    }

    private Sprite GetPlusSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/+.png", "Texts/+", ref _cachedPlusSprite);
    }

    private Sprite GetMinusSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/-.png", "Texts/-", ref _cachedMinusSprite);
    }

    private Sprite GetXpLabelSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/XP.png", "Texts/XP", ref _cachedXpLabelSprite);
    }

    private Sprite GetXpRedLabelSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/XP_Red.png", "Texts/XP_Red", ref _cachedXpRedLabelSprite);
    }

    private Sprite GetDoubleXpBannerSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/DOUBLE XP.png", "Texts/DOUBLE XP", ref _cachedDoubleXpSprite);
    }

    private Sprite GetMultiplierSignSprite()
    {
        return LoadBestSprite("Assets/Graphics/GUI Components/Texts/x.png", "Texts/x", ref _cachedMultiplierSignSprite);
    }

    private GameObject CreateDigitNumberRow(Transform parent, int number, float digitHeight, string objName = "NumberRow")
    {
        GameObject rowObj = new GameObject(objName);
        rowObj.transform.SetParent(parent, false);

        string numStr = Mathf.Max(0, number).ToString();
        HorizontalLayoutGroup hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 2f;

        float totalW = 0f;
        for (int i = 0; i < numStr.Length; i++)
        {
            int d = numStr[i] - '0';
            Sprite digitSp = GetDigitSprite(d);
            if (digitSp != null)
            {
                GameObject dObj = new GameObject("d_" + i);
                dObj.transform.SetParent(rowObj.transform, false);
                RectTransform dRt = dObj.AddComponent<RectTransform>();
                float dAspect = (digitSp.rect.height > 0) ? (digitSp.rect.width / digitSp.rect.height) : 0.71f;
                float dWidth = digitHeight * dAspect;
                dRt.sizeDelta = new Vector2(dWidth, digitHeight);

                Image dImg = dObj.AddComponent<Image>();
                dImg.sprite = digitSp;
                dImg.preserveAspect = true;
                dImg.raycastTarget = false;
                totalW += dWidth + 2f;
            }
        }

        RectTransform rowRt = rowObj.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(totalW, digitHeight);
        return rowObj;
    }

    private GameObject CreateTimeDisplayRow(Transform parent, int totalSeconds, float height, string objName = "TimeRow")
    {
        GameObject rowObj = new GameObject(objName);
        rowObj.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 3f;

        float totalW = 0f;

        // 1. TIME_ Label
        Sprite timeLblSp = GetTimeLabelSprite();
        if (timeLblSp != null)
        {
            GameObject lblObj = new GameObject("TimeLabel");
            lblObj.transform.SetParent(rowObj.transform, false);
            RectTransform lblRt = lblObj.AddComponent<RectTransform>();
            float aspect = (timeLblSp.rect.height > 0) ? (timeLblSp.rect.width / timeLblSp.rect.height) : 3.136f;
            float lblW = height * aspect;
            lblRt.sizeDelta = new Vector2(lblW, height);

            Image lblImg = lblObj.AddComponent<Image>();
            lblImg.sprite = timeLblSp;
            lblImg.preserveAspect = true;
            lblImg.raycastTarget = false;
            totalW += lblW + hlg.spacing;
        }

        // Spacer between "TIME:" and numbers
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(rowObj.transform, false);
        RectTransform spRt = spacer.AddComponent<RectTransform>();
        float spacerW = 6f;
        spRt.sizeDelta = new Vector2(spacerW, height);
        totalW += spacerW + hlg.spacing;

        // 2. Formatted digits for clock (e.g. 00:59)
        string timeStr = (totalSeconds >= 3600)
            ? string.Format("{0:00}:{1:00}:{2:00}", totalSeconds / 3600, (totalSeconds % 3600) / 60, totalSeconds % 60)
            : string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);

        Sprite colonSp = GetColonSprite();

        for (int i = 0; i < timeStr.Length; i++)
        {
            char ch = timeStr[i];
            if (ch == ':')
            {
                if (colonSp != null)
                {
                    GameObject cObj = new GameObject("colon_" + i);
                    cObj.transform.SetParent(rowObj.transform, false);
                    RectTransform cRt = cObj.AddComponent<RectTransform>();
                    float cAspect = (colonSp.rect.height > 0) ? (colonSp.rect.width / colonSp.rect.height) : 0.38f;
                    float cW = height * cAspect;
                    cRt.sizeDelta = new Vector2(cW, height);

                    Image cImg = cObj.AddComponent<Image>();
                    cImg.sprite = colonSp;
                    cImg.preserveAspect = true;
                    cImg.raycastTarget = false;
                    totalW += cW + hlg.spacing;
                }
            }
            else if (ch >= '0' && ch <= '9')
            {
                int d = ch - '0';
                Sprite digitSp = GetDigitSprite(d);
                if (digitSp != null)
                {
                    GameObject dObj = new GameObject("digit_" + i);
                    dObj.transform.SetParent(rowObj.transform, false);
                    RectTransform dRt = dObj.AddComponent<RectTransform>();
                    float dAspect = (digitSp.rect.height > 0) ? (digitSp.rect.width / digitSp.rect.height) : 0.71f;
                    float dW = height * dAspect;
                    dRt.sizeDelta = new Vector2(dW, height);

                    Image dImg = dObj.AddComponent<Image>();
                    dImg.sprite = digitSp;
                    dImg.preserveAspect = true;
                    dImg.raycastTarget = false;
                    totalW += dW + hlg.spacing;
                }
            }
        }

        RectTransform rowRt = rowObj.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(totalW, height);
        return rowObj;
    }

    private void DisableModalButtons()
    {
        if (endLevelModalObj != null)
        {
            Button[] btns = endLevelModalObj.GetComponentsInChildren<Button>(true);
            foreach (var b in btns)
            {
                if (b != null) b.interactable = false;
            }
        }
    }

    private Sprite GetKillerSprite()
    {
        if (PlayerController.LastKillerSprite != null)
        {
            return PlayerController.LastKillerSprite;
        }

        Sprite s = null;
#if UNITY_EDITOR
        s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Hazard/predator_hazard.png");
        if (s != null) return s;
#endif
        s = Resources.Load<Sprite>("predator_hazard") ?? Resources.Load<Sprite>("shark");
        if (s != null) return s;

        LevelConfig cfg = LevelManager.GetCurrentConfig();
        int maxEnemy = Mathf.Clamp(cfg.maxEnemyLevel, 1, 6);
        return GetGrowthSprite(maxEnemy - 1, LevelManager.IsCurrentLakeLevel);
    }

    private Color GetKillerColor()
    {
        if (PlayerController.LastKillerIsSick)
        {
            return new Color(0.72f, 1f, 0.72f, 1f);
        }
        return PlayerController.LastKillerColor;
    }

    private GameObject CreateSpriteButton(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 anchoredPos, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        rt.localScale = Vector3.one;

        Image img = btnObj.AddComponent<Image>();
        if (sprite != null)
        {
            img.sprite = sprite;
            img.preserveAspect = true;
        }
        img.color = Color.white;
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;

        SetupButton(btnObj, action);

        return btnObj;
    }

    private void CreateEndLevelModal(bool isVictory)
    {
        isSceneTransitionInProgress = false;
        if (endLevelModalObj != null) Destroy(endLevelModalObj);

        // Hide pause button, pausedBg, and old ScoreScreen
        if (pauseBtn != null) pauseBtn.SetActive(false);
        if (pausedBg != null) pausedBg.SetActive(false);
        if (ScoreScreen != null) ScoreScreen.SetActive(false);
        if (comboMultiplierIconObj != null)
        {
            comboMultiplierIconObj.transform.localScale = Vector3.one;
            comboMultiplierIconObj.SetActive(false);
        }

        // Terminate player movement when the modal actually appears
        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) pc.TerminateMovement();

        // Freeze game time cleanly while showing modal (just like Pause menu)
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (GameManager.instance != null)
        {
            GameManager.instance.StopBackgroundMusic();
        }

        // Find the main UI Canvas
        Canvas mainCanvas = GetRootCanvas();

        endLevelModalObj = new GameObject("EndLevelModal");
        if (mainCanvas != null)
        {
            endLevelModalObj.transform.SetParent(mainCanvas.transform, false);
        }

        Canvas modalCanvas = endLevelModalObj.AddComponent<Canvas>();
        modalCanvas.overrideSorting = true;
        modalCanvas.sortingOrder = 3000; // Topmost

        endLevelModalObj.AddComponent<GraphicRaycaster>();

        // Dark transparent background matching pause menu (0.45f opacity)
        Image bgImg = endLevelModalObj.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.45f);
        bgImg.raycastTarget = true;

        RectTransform rtBg = endLevelModalObj.GetComponent<RectTransform>();
        rtBg.anchorMin = Vector2.zero;
        rtBg.anchorMax = Vector2.one;
        rtBg.offsetMin = Vector2.zero;
        rtBg.offsetMax = Vector2.zero;
        rtBg.pivot = new Vector2(0.5f, 0.5f);
        rtBg.localScale = Vector3.one;

        endLevelModalObj.transform.SetAsLastSibling();

        // Dynamic scale factor matching canvas
        float scaleFactor = 1.0f;
        if (mainCanvas != null)
        {
            RectTransform canvasRect = mainCanvas.GetComponent<RectTransform>();
            if (canvasRect != null && canvasRect.rect.height > 0)
            {
                scaleFactor = Mathf.Clamp(canvasRect.rect.height / 1080f, 0.5f, 2.0f);
            }
        }

        int currentLvl = LevelManager.CurrentLevel;
        bool hasNext = isVictory && (currentLvl < LevelManager.TOTAL_LEVELS);

        if (isVictory)
        {
            // 1. Victory Modal Background Card (Level_Completion_Modal.png - aspect 1.500)
            GameObject modalCardObj = new GameObject("VictoryModalCard");
            modalCardObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtCard = modalCardObj.AddComponent<RectTransform>();
            rtCard.anchorMin = new Vector2(0.5f, 0.5f);
            rtCard.anchorMax = new Vector2(0.5f, 0.5f);
            rtCard.pivot = new Vector2(0.5f, 0.5f);
            rtCard.anchoredPosition = Vector2.zero;
            // Level_Completion_Modal.png aspect is 1.500 (enlarged to 1050x700 for better modal presence)
            rtCard.sizeDelta = new Vector2(1050f * scaleFactor, 700f * scaleFactor);
            rtCard.localScale = Vector3.one;

            Image cardImg = modalCardObj.AddComponent<Image>();
            Sprite modalSprite = GetLevelCompletionModalSprite();
            if (modalSprite != null)
            {
                cardImg.sprite = modalSprite;
                cardImg.preserveAspect = true;
            }
            cardImg.color = Color.white;
            cardImg.raycastTarget = false;

            // 2. Duration / Time Display (e.g. "TIME: 00:59") - pushed up
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(LevelManager.LevelTimer));
            GameObject timeRow = CreateTimeDisplayRow(modalCardObj.transform, totalSeconds, 28f * scaleFactor, "TimeRow");
            RectTransform rtTime = timeRow.GetComponent<RectTransform>();
            if (rtTime != null)
            {
                rtTime.anchorMin = new Vector2(0.5f, 0.5f);
                rtTime.anchorMax = new Vector2(0.5f, 0.5f);
                rtTime.pivot = new Vector2(0.5f, 0.5f);
                rtTime.anchoredPosition = new Vector2(0f, 95f * scaleFactor);
            }

            // 3. Fish Eaten Row - pushed up to Y = 5
            LevelConfig cfg = LevelManager.GetCurrentConfig();
            int numFish = Mathf.Clamp(cfg.maxEnemyLevel, 1, 6);

            var resultItems = new System.Collections.Generic.List<(Sprite sprite, int count, string id)>();
            for (int i = 1; i <= numFish; i++)
            {
                Sprite fishSprite = null;
                if (growthIcons != null && (i - 1) < growthIcons.Length && growthIcons[i - 1] != null && growthIcons[i - 1].sprite != null)
                {
                    fishSprite = growthIcons[i - 1].sprite;
                }
                if (fishSprite == null)
                {
                    fishSprite = GetGrowthSprite(i - 1, LevelManager.IsCurrentLakeLevel);
                }
                int eatenCount = (i < LevelManager.FishEatenCounts.Length) ? LevelManager.FishEatenCounts[i] : 0;
                resultItems.Add((fishSprite, eatenCount, "FishItem_" + i));
            }

            if (LevelManager.SickFishEatenCount > 0)
            {
                Sprite sickSprite = LoadGrowthSpriteSafe("level 2 fish sick", "Assets/Graphics/fish/level 2 fish sick.png");
                resultItems.Add((sickSprite, LevelManager.SickFishEatenCount, "SickFishItem"));
            }

            int totalItems = resultItems.Count;
            float itemSpacing = (totalItems <= 2) ? (150f * scaleFactor)
                              : (totalItems <= 3) ? (135f * scaleFactor)
                              : (totalItems <= 4) ? (122f * scaleFactor)
                              : (115f * scaleFactor);
            float startX = -(totalItems - 1) * 0.5f * itemSpacing;
            float fishRowY = 5f * scaleFactor;

            for (int k = 0; k < totalItems; k++)
            {
                var item = resultItems[k];
                float itemX = startX + (k * itemSpacing);

                GameObject fishItemObj = new GameObject(item.id);
                fishItemObj.transform.SetParent(modalCardObj.transform, false);
                RectTransform rtItem = fishItemObj.AddComponent<RectTransform>();
                rtItem.anchorMin = new Vector2(0.5f, 0.5f);
                rtItem.anchorMax = new Vector2(0.5f, 0.5f);
                rtItem.pivot = new Vector2(0.5f, 0.5f);
                rtItem.anchoredPosition = new Vector2(itemX, fishRowY);
                rtItem.sizeDelta = new Vector2(80f * scaleFactor, 80f * scaleFactor);

                // Fish Icon (Top) - keeps original size
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(fishItemObj.transform, false);
                RectTransform rtIcon = iconObj.AddComponent<RectTransform>();
                rtIcon.anchorMin = new Vector2(0.5f, 0.5f);
                rtIcon.anchorMax = new Vector2(0.5f, 0.5f);
                rtIcon.pivot = new Vector2(0.5f, 0.5f);
                rtIcon.anchoredPosition = new Vector2(0f, 16f * scaleFactor);
                rtIcon.sizeDelta = new Vector2(65f * scaleFactor, 42f * scaleFactor);

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = item.sprite;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                // Eaten Amount (Bottom - using digit sprites 0.png..9.png)
                GameObject numRow = CreateDigitNumberRow(fishItemObj.transform, item.count, 22f * scaleFactor, "CountRow");
                RectTransform rtNum = numRow.GetComponent<RectTransform>();
                if (rtNum != null)
                {
                    rtNum.anchorMin = new Vector2(0.5f, 0.5f);
                    rtNum.anchorMax = new Vector2(0.5f, 0.5f);
                    rtNum.pivot = new Vector2(0.5f, 0.5f);
                    rtNum.anchoredPosition = new Vector2(0f, -20f * scaleFactor);
                }
            }

            // 4. Bottom Action Buttons: Enlarged to match modal proportions
            // RestartBtn: PosX = -158, PosY = -196, Size = 128x128
            // ContinueBtn: PosX = 172, PosY = -193, Size = 128x128
            // BackToMenuBtn: PosX = 8, PosY = -196, Size = 272x152
            Vector2 squareBtnSize = new Vector2(128f * scaleFactor, 128f * scaleFactor);
            Vector2 menuBtnSize = new Vector2(272f * scaleFactor, 152f * scaleFactor);

            // Short_Restart_Button (Left) - matching exact Inspector value: (-158, -193)
            Sprite restartSp = GetShortRestartButtonSprite();
            CreateSpriteButton("RestartBtn", modalCardObj.transform, restartSp, squareBtnSize, new Vector2(-158f * scaleFactor, -193f * scaleFactor), () =>
            {
                if (isSceneTransitionInProgress) return;
                isSceneTransitionInProgress = true;
                DisableModalButtons();
                Time.timeScale = 1f;
                SceneManager.LoadScene("SampleScene");
            });

            // Back_To_Menu_Button (Middle) - X offset compensates for right transparent padding so visual center is at X = 0
            Sprite menuSp = GetBackToMenuButtonSprite();
            CreateSpriteButton("BackToMenuBtn", modalCardObj.transform, menuSp, menuBtnSize, new Vector2(8f * scaleFactor, -196f * scaleFactor), () =>
            {
                if (isSceneTransitionInProgress) return;
                isSceneTransitionInProgress = true;
                DisableModalButtons();
                MainMenuManager.OpenLevelSelectOnLoad = false;
                Time.timeScale = 1f;
                SceneManager.LoadScene("MainMenu");
            });

            // Short_Continue_Button (Right)
            Sprite continueSp = GetShortContinueButtonSprite();
            CreateSpriteButton("ContinueBtn", modalCardObj.transform, continueSp, squareBtnSize, new Vector2(172f * scaleFactor, -193f * scaleFactor), () =>
            {
                if (isSceneTransitionInProgress) return;
                isSceneTransitionInProgress = true;
                DisableModalButtons();
                if (hasNext)
                {
                    LevelManager.CurrentLevel = currentLvl + 1;
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("SampleScene");
                }
                else
                {
                    MainMenuManager.OpenLevelSelectOnLoad = false;
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("MainMenu");
                }
            });
        }
        else
        {
            // Defeat (Game Over Modal matching mockup)
            // 1. Modal Background Card (matches Paused modal dimensions 640x788)
            GameObject modalCardObj = new GameObject("GameOverModalCard");
            modalCardObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtCard = modalCardObj.AddComponent<RectTransform>();
            rtCard.anchorMin = new Vector2(0.5f, 0.5f);
            rtCard.anchorMax = new Vector2(0.5f, 0.5f);
            rtCard.pivot = new Vector2(0.5f, 0.5f);
            rtCard.anchoredPosition = Vector2.zero;
            // Matches Paused modal 640x788
            rtCard.sizeDelta = new Vector2(640f * scaleFactor, 788f * scaleFactor);

            Image cardImg = modalCardObj.AddComponent<Image>();
            Sprite modalSprite = GetGameOverModalSprite();
            if (modalSprite != null)
            {
                cardImg.sprite = modalSprite;
                cardImg.preserveAspect = true;
            }
            cardImg.raycastTarget = false;

            // 2. Dynamic Killer Graphic (e.g. Shark / Predator / Clam / Hazard placed in the blue glass window)
            // KillerGraphic: PosX = 0, PosY = 90, Size = 150x95
            Sprite killerSprite = GetKillerSprite();
            if (killerSprite != null)
            {
                GameObject killerObj = new GameObject("KillerGraphic");
                killerObj.transform.SetParent(modalCardObj.transform, false);
                RectTransform rtKiller = killerObj.AddComponent<RectTransform>();
                rtKiller.anchorMin = new Vector2(0.5f, 0.5f);
                rtKiller.anchorMax = new Vector2(0.5f, 0.5f);
                rtKiller.pivot = new Vector2(0.5f, 0.5f);
                rtKiller.anchoredPosition = new Vector2(0f, 90f * scaleFactor);
                rtKiller.sizeDelta = new Vector2(150f * scaleFactor, 95f * scaleFactor);

                Image killerImg = killerObj.AddComponent<Image>();
                killerImg.sprite = killerSprite;
                killerImg.color = GetKillerColor();
                killerImg.preserveAspect = true;
                killerImg.raycastTarget = false;
            }

            // 3. Action Buttons (Try Again & Menu) - matching exact Inspector measurements
            // TryAgainBtn: PosX = 2, PosY = -128, Size = 280x158
            // BackToMenuBtn: PosX = 2, PosY = -248, Size = 280x158
            Vector2 btnSize = new Vector2(280f * scaleFactor, 158f * scaleFactor);

            // Try Again Button (Top button below viewport)
            Sprite tryAgainSp = GetTryAgainButtonSprite();
            GameObject retryBtn = CreateSpriteButton("TryAgainBtn", modalCardObj.transform, tryAgainSp, btnSize, new Vector2(2f * scaleFactor, -128f * scaleFactor), () =>
            {
                if (isSceneTransitionInProgress) return;
                isSceneTransitionInProgress = true;
                DisableModalButtons();
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            });

            // Back To Menu Button (Bottom button)
            Sprite menuSp = GetBackToMenuButtonSprite();
            GameObject menuBtnObj = CreateSpriteButton("BackToMenuBtn", modalCardObj.transform, menuSp, btnSize, new Vector2(2f * scaleFactor, -248f * scaleFactor), () =>
            {
                if (isSceneTransitionInProgress) return;
                isSceneTransitionInProgress = true;
                DisableModalButtons();
                MainMenuManager.OpenLevelSelectOnLoad = false;
                Time.timeScale = 1f;
                SceneManager.LoadScene("MainMenu");
            });
        }
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



