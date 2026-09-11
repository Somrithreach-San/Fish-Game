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

    [Header("Modular Segmented XP Bar")]
    public Sprite domeLeftGlass;
    public Sprite domeLeftFill;
    public Sprite tubeGlass;
    public Sprite tubeFill;
    public Sprite domeRightGlass;
    public Sprite domeRightFill;
    // Legacy fallback references
    public Sprite capLeftGlass;
    public Sprite capLeftFill;
    public Sprite midGlass;
    public Sprite midFill;
    public Sprite capRightGlass;
    public Sprite capRightFill;

    private GameObject segmentedBarRoot;
    private RectTransform fillMaskRt;
    private GameObject fillMaskObj;
    private float targetFillPct = 0f;
    private float currentFillPct = 0f;
    public float sectionWidth = 85f;
    public float barHeight = 26f;
    [Range(0f, 2f)]
    public float segmentGap = 0f;
    private float _lastSectionWidth = -1f;
    private float _lastBarHeight = -1f;
    private float _lastSegmentGap = -1f;
    private float totalBarWidth = 285f;
    private List<RectTransform> segmentMasks = new List<RectTransform>();
    private List<float> segmentWidths = new List<float>();
    private List<float> targetSegmentFills = new List<float>();
    private List<float> currentSegmentFills = new List<float>();
    private int currentSegmentCount = -1;
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
             // Ensure cursor is visible for UI interaction
             Cursor.visible = true;
             Cursor.lockState = CursorLockMode.None;
             ShowStageClearModal();
        });

        EventManager.StartListening("GameLoss", () => {
             // Ensure cursor is visible for UI interaction
             Cursor.visible = true;
             Cursor.lockState = CursorLockMode.None;
             ShowGameOverModal();
        });

        EventManager.StartListening("GameStart", () => {
            SetXp(0, 1, 1);
            SnapSegmentFills();
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
                     pbRt.anchoredPosition = new Vector2(35f, -45f);
                 }
             }
        }

        // Initialize dynamic segmented XP bar and icons for current stage
        EnsureModularSpritesLoaded();
        RebuildSegmentedBar(LevelManager.GetCurrentConfig().targetPlayerLevel);
        UpdateGrowthIcons(1); // Initial state
        SnapSegmentFills();

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

    private Sprite GetPauseIconSprite()
    {
        if (pauseSprite != null) return pauseSprite;
        if (_cachedPauseIcon != null) return _cachedPauseIcon;
        _cachedPauseIcon = Resources.Load<Sprite>("pause_icon");
        if (_cachedPauseIcon == null)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>("pause_icon");
            if (sprites != null && sprites.Length > 0) _cachedPauseIcon = sprites[0];
        }
        if (_cachedPauseIcon == null)
        {
            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (Sprite s in all)
            {
                if (s != null && s.name.ToLower().Contains("pause") && !s.name.ToLower().Contains("button"))
                {
                    _cachedPauseIcon = s;
                    break;
                }
            }
        }
        return _cachedPauseIcon;
    }

    private void SetupTopRightControls()
    {
        // 1. Get Reference to Main Canvas (Parent of Controls)
        Canvas mainCanvas = null;
        if (pausedBg != null) mainCanvas = pausedBg.GetComponentInParent<Canvas>();
        if (mainCanvas == null) mainCanvas = FindFirstObjectByType<Canvas>();
        
        if (mainCanvas == null) return; 

        // 2. Handle Pause Button
        if (pauseBtn != null)
        {
            Destroy(pauseBtn);
            pauseBtn = null;
        }
        
        // Create Pause Button matching the X close button (bubble style, 95x95, anchored top-right at -60, -60)
        pauseBtn = CreatePauseBubbleButton(mainCanvas, () => {
             PlayButtonSound();
             GameManager.instance.PlayPause();
        });
        
        if (pauseBtn != null)
        {
             pauseBtn.transform.SetAsLastSibling();
        }

        // Ensure any old HackWinButton is removed
        GameObject oldHackBtn = GameObject.Find("HackWinButton");
        if (oldHackBtn != null) Destroy(oldHackBtn);
    }

    private GameObject CreatePauseBubbleButton(Canvas parentCanvas, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = new GameObject("PauseButton");
        btnObj.transform.SetParent(parentCanvas.transform, false);

        // RectTransform: Compact bubble button for in-game HUD (size 48x48, anchored top-right at -35, -25)
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(48f, 48f);
        rt.anchoredPosition = new Vector2(-35f, -25f);
        rt.localScale = Vector3.one;

        // Bubble Background Image
        Image bgImg = btnObj.AddComponent<Image>();
        Sprite bubble = GetBubbleSprite();
        if (bubble != null)
        {
            bgImg.sprite = bubble;
            bgImg.type = Image.Type.Simple;
        }
        bgImg.color = Color.white;
        bgImg.raycastTarget = true;

        // Button component
        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(() => PlayButtonSound());
        btn.onClick.AddListener(action);

        // Hover Effect
        if (btnObj.GetComponent<ButtonHoverEffect>() == null)
            btnObj.AddComponent<ButtonHoverEffect>();

        // Layout Element (Ignore Layout)
        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // Canvas & Raycaster for Sorting & Touch Reliability
        Canvas c = btnObj.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 2001; // Above normal canvas elements
        btnObj.AddComponent<GraphicRaycaster>();

        // Hit Area padding for easier touch interaction
        GameObject hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(btnObj.transform, false);
        RectTransform rtHit = hitArea.AddComponent<RectTransform>();
        rtHit.anchorMin = new Vector2(0.5f, 0.5f);
        rtHit.anchorMax = new Vector2(0.5f, 0.5f);
        rtHit.pivot = new Vector2(0.5f, 0.5f);
        rtHit.sizeDelta = new Vector2(65f, 65f);
        Image imgHit = hitArea.AddComponent<Image>();
        imgHit.color = new Color(0, 0, 0, 0); // Transparent
        imgHit.raycastTarget = true;
        Button btnHit = hitArea.AddComponent<Button>();
        btnHit.onClick.AddListener(() => btn.onClick.Invoke());

        // Child: Pause Icon Centered inside bubble
        GameObject iconObj = new GameObject("PauseIcon");
        iconObj.transform.SetParent(btnObj.transform, false);

        RectTransform rtIcon = iconObj.AddComponent<RectTransform>();
        rtIcon.anchorMin = new Vector2(0.5f, 0.5f);
        rtIcon.anchorMax = new Vector2(0.5f, 0.5f);
        rtIcon.pivot = new Vector2(0.5f, 0.5f);
        rtIcon.anchoredPosition = Vector2.zero;
        rtIcon.sizeDelta = new Vector2(16f, 20f); // Cleanly proportioned inside the 48x48 bubble

        Sprite pauseIco = GetPauseIconSprite();
        if (pauseIco != null)
        {
            Image imgIcon = iconObj.AddComponent<Image>();
            imgIcon.sprite = pauseIco;
            imgIcon.color = Color.white;
            imgIcon.preserveAspect = true;
            imgIcon.raycastTarget = false;
        }
        else
        {
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
        // Live update segment layout if inspector parameters changed
        if (currentSegmentCount > 0 && segmentedBarRoot != null &&
            (Mathf.Abs(_lastSectionWidth - sectionWidth) > 0.01f ||
             Mathf.Abs(_lastBarHeight - barHeight) > 0.01f ||
             Mathf.Abs(_lastSegmentGap - segmentGap) > 0.01f))
        {
            RefreshSegmentLayout();
        }

        // Smooth Fill Modular XP Bar via RectMask2D per segment
        for (int i = 0; i < segmentMasks.Count; i++)
        {
            if (segmentMasks[i] != null && i < targetSegmentFills.Count && i < currentSegmentFills.Count)
            {
                currentSegmentFills[i] = Mathf.Lerp(currentSegmentFills[i], targetSegmentFills[i], Time.deltaTime * 6f);
                if (Mathf.Abs(currentSegmentFills[i] - targetSegmentFills[i]) < 0.001f)
                {
                    currentSegmentFills[i] = targetSegmentFills[i];
                }
                float fillW = sectionWidth * currentSegmentFills[i];
                segmentMasks[i].sizeDelta = new Vector2(fillW, 0f);
                bool shouldShow = currentSegmentFills[i] > 0.001f;
                if (segmentMasks[i].gameObject.activeSelf != shouldShow)
                {
                    segmentMasks[i].gameObject.SetActive(shouldShow);
                }
            }
        }

        // Continuous XP Bar fallback if single fill mask used
        if (fillMaskRt != null && segmentMasks.Count == 0)
        {
            currentFillPct = Mathf.Lerp(currentFillPct, targetFillPct, Time.deltaTime * 6f);
            if (Mathf.Abs(currentFillPct - targetFillPct) < 0.001f)
            {
                currentFillPct = targetFillPct;
            }
            float fillW = totalBarWidth * currentFillPct;
            fillMaskRt.sizeDelta = new Vector2(fillW, 0f);
            bool shouldShow = currentFillPct > 0.0001f;
            if (fillMaskObj != null && fillMaskObj.activeSelf != shouldShow)
            {
                fillMaskObj.SetActive(shouldShow);
            }
        }

        // Legacy fill fallback if legacy XpBar active
        if (XpBar != null && XpBar.gameObject.activeSelf)
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

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => PlayButtonSound()); // Add standard click sound
        btn.onClick.AddListener(action);

        if (btnObj.GetComponent<ButtonHoverEffect>() == null)
            btnObj.AddComponent<ButtonHoverEffect>();
    }

    public void PlayButtonSound()
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
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
            img.color = new Color(0, 0, 0, 0.40f); // Dark overlay
        }
        else
        {
             // Ensure existing background is also darkened
             Image img = pausedBg.GetComponent<Image>();
             if (img != null)
             {
                 img.color = new Color(0, 0, 0, 0.40f); // Dark overlay
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

        // 2. Define Style (Match Main Menu Bubble Button)
        float baseSize = 150f;
        Vector2 buttonSize = new Vector2(baseSize * scaleFactor, baseSize * scaleFactor);
        
        // Full white to show translucent iridescent bubble shader
        Color buttonColor = Color.white; 

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

    private void EnsureModularSpritesLoaded()
    {
        if (capLeftGlass == null) capLeftGlass = LoadSpriteWithFallback("xp_cap_left_glass");
        if (capLeftFill == null) capLeftFill = LoadSpriteWithFallback("xp_cap_left_fill");
        if (midGlass == null) midGlass = LoadSpriteWithFallback("xp_mid_glass");
        if (midFill == null) midFill = LoadSpriteWithFallback("xp_mid_fill");
        if (capRightGlass == null) capRightGlass = LoadSpriteWithFallback("xp_cap_right_glass");
        if (capRightFill == null) capRightFill = LoadSpriteWithFallback("xp_cap_right_fill");

        // Fallbacks
        if (capLeftGlass == null) capLeftGlass = LoadSpriteWithFallback("xp_dome_left_glass");
        if (capLeftFill == null) capLeftFill = LoadSpriteWithFallback("xp_dome_left_fill");
        if (midGlass == null) midGlass = LoadSpriteWithFallback("xp_tube_glass");
        if (midFill == null) midFill = LoadSpriteWithFallback("xp_tube_fill");
        if (capRightGlass == null) capRightGlass = LoadSpriteWithFallback("xp_dome_right_glass");
        if (capRightFill == null) capRightFill = LoadSpriteWithFallback("xp_dome_right_fill");
    }

    private GameObject CreateImageChild(Transform parent, string name, Sprite sprite,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        rt.localScale = Vector3.one;

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        img.raycastTarget = false;
        return go;
    }

    private GameObject CreateStretchChild(Transform parent, string name, Sprite sprite,
        float leftOffset, float bottomOffset, float rightOffset, float topOffset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(leftOffset, bottomOffset);
        rt.offsetMax = new Vector2(-rightOffset, -topOffset);
        rt.localScale = Vector3.one;

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        img.raycastTarget = false;
        return go;
    }

    public void SnapSegmentFills()
    {
        currentFillPct = targetFillPct;
        for (int i = 0; i < segmentMasks.Count; i++)
        {
            if (i < targetSegmentFills.Count && i < currentSegmentFills.Count)
            {
                currentSegmentFills[i] = targetSegmentFills[i];
                if (segmentMasks[i] != null)
                {
                    float fillW = sectionWidth * currentSegmentFills[i];
                    segmentMasks[i].sizeDelta = new Vector2(fillW, 0f);
                    bool shouldShow = currentSegmentFills[i] > 0.001f;
                    if (segmentMasks[i].gameObject.activeSelf != shouldShow)
                    {
                        segmentMasks[i].gameObject.SetActive(shouldShow);
                    }
                }
            }
        }
        if (fillMaskRt != null && segmentMasks.Count == 0)
        {
            float fillW = totalBarWidth * currentFillPct;
            fillMaskRt.sizeDelta = new Vector2(fillW, 0f);
            bool shouldShow = currentFillPct > 0.0001f;
            if (fillMaskObj != null && fillMaskObj.activeSelf != shouldShow)
            {
                fillMaskObj.SetActive(shouldShow);
            }
        }
    }

    [ContextMenu("Rebuild XP Bar Now")]
    public void RebuildSegmentedBarEditor()
    {
        EnsureModularSpritesLoaded();
        RebuildSegmentedBar(LevelManager.GetCurrentConfig().targetPlayerLevel);
        UpdateGrowthIcons(1);
        SnapSegmentFills();
    }

    public void RefreshSegmentLayout()
    {
        if (segmentedBarRoot == null || currentSegmentCount <= 0) return;

        totalBarWidth = currentSegmentCount * sectionWidth + (currentSegmentCount - 1) * segmentGap;

        Transform pbTr = (XpBar != null) ? XpBar.transform.parent : null;
        if (pbTr == null)
        {
            GameObject pbGo = GameObject.Find("ProgressBar1");
            if (pbGo != null) pbTr = pbGo.transform;
        }
        if (pbTr != null)
        {
            RectTransform pbRt = pbTr.GetComponent<RectTransform>();
            if (pbRt != null)
            {
                pbRt.sizeDelta = new Vector2(totalBarWidth, barHeight);
            }
        }

        for (int i = 0; i < segmentedBarRoot.transform.childCount; i++)
        {
            Transform segChild = segmentedBarRoot.transform.GetChild(i);
            RectTransform segRt = segChild.GetComponent<RectTransform>();
            if (segRt != null)
            {
                segRt.anchoredPosition = new Vector2(i * (sectionWidth + segmentGap), 0f);
                segRt.sizeDelta = new Vector2(sectionWidth, 0f);
            }

            Transform maskChild = segChild.Find("FillMask");
            if (maskChild != null)
            {
                Transform fillImgChild = maskChild.Find("FillImg");
                if (fillImgChild != null)
                {
                    RectTransform fillImgRt = fillImgChild.GetComponent<RectTransform>();
                    if (fillImgRt != null)
                    {
                        fillImgRt.sizeDelta = new Vector2(sectionWidth, 0f);
                    }
                }
            }
        }

        // Clean up any old DividersRoot
        Transform divRoot = segmentedBarRoot.transform.Find("DividersRoot");
        if (divRoot != null) Destroy(divRoot.gameObject);

        FormatGrowthIconsLayout(currentSegmentCount);

        _lastSectionWidth = sectionWidth;
        _lastBarHeight = barHeight;
        _lastSegmentGap = segmentGap;
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

        // Hide legacy single glass background and legacy single fill
        Image pbImg = pbTr.GetComponent<Image>();
        if (pbImg != null) pbImg.enabled = false;

        if (XpBar != null)
        {
            XpBar.gameObject.SetActive(false);
        }

        EnsureModularSpritesLoaded();

        if (currentSegmentCount == segmentCount && segmentedBarRoot != null && segmentMasks.Count == segmentCount)
        {
            return;
        }

        // Locate or create segmentedBarRoot
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

        // Ensure segmentedBarRoot is behind GrowthIconsContainer
        segmentedBarRoot.transform.SetSiblingIndex(0);

        // Dynamically expand total bar width based on number of modular sections and intentional gap
        totalBarWidth = segmentCount * sectionWidth + (segmentCount - 1) * segmentGap;
        float totalHeight = barHeight;
        float capRadius = totalHeight * 0.5f; // EXACT 1:1 circular dome radius

        RectTransform pbRt = pbTr.GetComponent<RectTransform>();
        if (pbRt != null)
        {
            pbRt.sizeDelta = new Vector2(totalBarWidth, totalHeight);
        }

        RectTransform rootRt = segmentedBarRoot.GetComponent<RectTransform>();
        if (rootRt == null) rootRt = segmentedBarRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.sizeDelta = Vector2.zero;
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.localScale = Vector3.one;

        // Clean out any old children
        for (int c = segmentedBarRoot.transform.childCount - 1; c >= 0; c--)
        {
            Transform child = segmentedBarRoot.transform.GetChild(c);
            child.SetParent(null);
            Destroy(child.gameObject);
        }

        segmentMasks.Clear();
        targetSegmentFills.Clear();
        currentSegmentFills.Clear();

        // Build modular segment GameObjects separated by small visible gap
        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segObj = new GameObject($"Seg_{i}");
            segObj.transform.SetParent(segmentedBarRoot.transform, false);

            RectTransform segRt = segObj.AddComponent<RectTransform>();
            segRt.anchorMin = new Vector2(0f, 0f);
            segRt.anchorMax = new Vector2(0f, 1f);
            segRt.pivot = new Vector2(0f, 0.5f);
            segRt.anchoredPosition = new Vector2(i * (sectionWidth + segmentGap), 0f);
            segRt.sizeDelta = new Vector2(sectionWidth, 0f);
            segRt.localScale = Vector3.one;

            // Pick modular sprites for this segment
            Sprite gSprite, fSprite;
            if (segmentCount == 2)
            {
                gSprite = (i == 0) ? capLeftGlass : capRightGlass;
                fSprite = (i == 0) ? capLeftFill : capRightFill;
            }
            else
            {
                if (i == 0)
                {
                    gSprite = capLeftGlass;
                    fSprite = capLeftFill;
                }
                else if (i == segmentCount - 1)
                {
                    gSprite = capRightGlass;
                    fSprite = capRightFill;
                }
                else
                {
                    gSprite = midGlass;
                    fSprite = midFill;
                }
            }

            // Fallbacks
            if (gSprite == null) gSprite = (i == 0) ? domeLeftGlass : ((i == segmentCount - 1) ? domeRightGlass : tubeGlass);
            if (fSprite == null) fSprite = (i == 0) ? domeLeftFill : ((i == segmentCount - 1) ? domeRightFill : tubeFill);

            // 1. Fill Layer (revealed horizontally by RectMask2D, rendered underneath glass)
            GameObject maskObj = new GameObject("FillMask");
            maskObj.transform.SetParent(segObj.transform, false);

            RectTransform maskRt = maskObj.AddComponent<RectTransform>();
            maskRt.anchorMin = new Vector2(0f, 0f);
            maskRt.anchorMax = new Vector2(0f, 1f);
            maskRt.pivot = new Vector2(0f, 0.5f);
            maskRt.anchoredPosition = Vector2.zero;
            maskRt.sizeDelta = new Vector2(0f, 0f);
            maskRt.localScale = Vector3.one;

            maskObj.AddComponent<RectMask2D>();

            // Fill Image inside mask (fixed sectionWidth x full height)
            GameObject fillImgObj = new GameObject("FillImg");
            fillImgObj.transform.SetParent(maskObj.transform, false);

            RectTransform fillImgRt = fillImgObj.AddComponent<RectTransform>();
            fillImgRt.anchorMin = new Vector2(0f, 0f);
            fillImgRt.anchorMax = new Vector2(0f, 1f);
            fillImgRt.pivot = new Vector2(0f, 0.5f);
            fillImgRt.anchoredPosition = Vector2.zero;
            fillImgRt.sizeDelta = new Vector2(sectionWidth, 0f);
            fillImgRt.localScale = Vector3.one;

            Image fImg = fillImgObj.AddComponent<Image>();
            fImg.sprite = fSprite;
            fImg.type = Image.Type.Simple;
            fImg.color = Color.white;
            fImg.raycastTarget = false;

            maskObj.SetActive(false);

            // 2. Glass Layer (rendered on top of fill)
            GameObject glassImgObj = new GameObject("GlassImg");
            glassImgObj.transform.SetParent(segObj.transform, false);

            RectTransform glassImgRt = glassImgObj.AddComponent<RectTransform>();
            glassImgRt.anchorMin = Vector2.zero;
            glassImgRt.anchorMax = Vector2.one;
            glassImgRt.sizeDelta = Vector2.zero;
            glassImgRt.anchoredPosition = Vector2.zero;
            glassImgRt.localScale = Vector3.one;

            Image gImg = glassImgObj.AddComponent<Image>();
            gImg.sprite = gSprite;
            gImg.type = Image.Type.Simple;
            gImg.color = Color.white;
            gImg.raycastTarget = false;

            segmentMasks.Add(maskRt);
            targetSegmentFills.Add(0f);
            currentSegmentFills.Add(0f);
        }

        currentSegmentCount = segmentCount;
        _lastSectionWidth = sectionWidth;
        _lastBarHeight = barHeight;
        _lastSegmentGap = segmentGap;
    }

    public void SetXp(int currentXP, int maxXp, int currentLevel = 1, int maxLevels = -1)
    {
        if (maxLevels <= 0)
        {
            maxLevels = LevelManager.GetCurrentConfig().targetPlayerLevel;
        }
        maxLevels = Mathf.Clamp(maxLevels, 2, (growthIcons != null && growthIcons.Length > 0) ? growthIcons.Length : 6);

        // Ensure modular bar is built for the current stage segment count
        RebuildSegmentedBar(maxLevels);

        // Calculate progress within current level (0 to 1)
        float levelProgress = (maxXp > 0) ? Mathf.Clamp01((float)currentXP / (float)maxXp) : 0f;

        UpdateGrowthIcons(currentLevel, maxLevels);

        // Feeding frenzy modular segment logic:
        // Segments before currentLevel-1 are fully completed (100%)
        // Current segment reveals levelProgress (0 to 1)
        // Future segments stay empty (0%)
        int currentSegIndex = Mathf.Clamp(currentLevel - 1, 0, maxLevels - 1);
        for (int j = 0; j < targetSegmentFills.Count; j++)
        {
            if (j < currentSegIndex)
            {
                targetSegmentFills[j] = 1.0f;
            }
            else if (j == currentSegIndex)
            {
                targetSegmentFills[j] = levelProgress;
            }
            else
            {
                targetSegmentFills[j] = 0.0f;
            }
        }

        // Backward compatibility
        targetFillPct = Mathf.Clamp01(((float)currentSegIndex + levelProgress) / (float)maxLevels);
        targetXpFill = targetFillPct;
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
                // Current Level: Highlighted (White) + Slightly Enlarged (1.12x)
                growthIcons[i].color = currentColor;
                growthIcons[i].transform.localScale = Vector3.one * 1.12f;
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
        activeCount = Mathf.Clamp(activeCount, 1, growthIcons.Length);

        float totalWidth = activeCount * sectionWidth + (activeCount - 1) * segmentGap;

        // 1. Position the container cleanly right above the glass bar
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
                containerRt.pivot = new Vector2(0f, 0.5f);
                containerRt.anchoredPosition = new Vector2(0f, 26f);
                containerRt.sizeDelta = new Vector2(totalWidth, 26f);
            }

            // Completely eliminate HorizontalLayoutGroup so it never scrambles or centers icons in slots
            HorizontalLayoutGroup hlg = container.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.enabled = false;
                Destroy(hlg);
            }
        }

        // 2. Compact, balanced fish icon sizes
        Vector2[] targetSizes = new Vector2[]
        {
            new Vector2(26f, 17f),
            new Vector2(29f, 19f),
            new Vector2(33f, 22f),
            new Vector2(37f, 24f),
            new Vector2(41f, 27f),
            new Vector2(46f, 30f)
        };

        for (int i = 0; i < growthIcons.Length; i++)
        {
            if (growthIcons[i] == null) continue;

            bool isVisible = (i < activeCount);
            growthIcons[i].gameObject.SetActive(isVisible);

            if (!isVisible) continue;

            RectTransform rt = growthIcons[i].rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f); // Align left to its own bar section

            growthIcons[i].preserveAspect = true;

            float maxW = Mathf.Min(sectionWidth * 0.65f, 40f);
            float baseW = (i < targetSizes.Length) ? targetSizes[i].x : 35f;
            float baseH = (i < targetSizes.Length) ? targetSizes[i].y : 23f;
            float aspect = (baseW > 0) ? (baseH / baseW) : 0.67f;
            float actualW = Mathf.Min(baseW, maxW);
            rt.sizeDelta = new Vector2(actualW, actualW * aspect);

            // Align to the left of its own bar section
            float sectionLeft = i * (sectionWidth + segmentGap);
            float leftPad = (i == 0) ? 5f : 2f; // Small padding for dome curvature
            float posX = sectionLeft + leftPad;
            rt.anchoredPosition = new Vector2(posX, 0f);
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

    private GameObject endLevelModalObj;

    public void ShowStageClearModal()
    {
        CreateEndLevelModal(true);
    }

    public void ShowGameOverModal()
    {
        CreateEndLevelModal(false);
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

    private void CreateEndLevelModal(bool isVictory)
    {
        if (endLevelModalObj != null) Destroy(endLevelModalObj);

        // Hide pause button, pausedBg, and old ScoreScreen
        if (pauseBtn != null) pauseBtn.SetActive(false);
        if (pausedBg != null) pausedBg.SetActive(false);
        if (ScoreScreen != null) ScoreScreen.SetActive(false);

        // Freeze game time cleanly while showing modal (just like Pause menu)
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (GameManager.instance != null)
        {
            GameManager.instance.StopBackgroundMusic();
        }

        // Find the main UI Canvas (the exact same one pausedBg lives in)
        Canvas mainCanvas = null;
        if (pausedBg != null) mainCanvas = pausedBg.GetComponentInParent<Canvas>();
        if (mainCanvas == null) mainCanvas = FindFirstObjectByType<Canvas>();

        endLevelModalObj = new GameObject("EndLevelModal");
        if (mainCanvas != null)
        {
            endLevelModalObj.transform.SetParent(mainCanvas.transform, false);
        }

        Canvas modalCanvas = endLevelModalObj.AddComponent<Canvas>();
        modalCanvas.overrideSorting = true;
        modalCanvas.sortingOrder = 3000; // Topmost

        endLevelModalObj.AddComponent<GraphicRaycaster>();

        // Dark transparent background matching pause menu (0.40f opacity)
        Image bgImg = endLevelModalObj.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.40f);
        bgImg.raycastTarget = true;

        RectTransform rtBg = endLevelModalObj.GetComponent<RectTransform>();
        rtBg.anchorMin = Vector2.zero;
        rtBg.anchorMax = Vector2.one;
        rtBg.offsetMin = Vector2.zero;
        rtBg.offsetMax = Vector2.zero;
        rtBg.pivot = new Vector2(0.5f, 0.5f);
        rtBg.localScale = Vector3.one;

        endLevelModalObj.transform.SetAsLastSibling();

        // Dynamic scale factor matching pause menu
        float scaleFactor = 1.0f;
        if (mainCanvas != null)
        {
            RectTransform canvasRect = mainCanvas.GetComponent<RectTransform>();
            if (canvasRect != null && canvasRect.rect.height > 0)
            {
                scaleFactor = Mathf.Clamp(canvasRect.rect.height / 1080f, 0.5f, 2.0f);
            }
        }

        Font stdFont = GetStandardFont();
        Font khFont = GetKhmerFont();

        int currentLvl = LevelManager.CurrentLevel;
        bool hasNext = isVictory && (currentLvl < LevelManager.TOTAL_LEVELS);
        Font activeFont = (khFont != null) ? khFont : stdFont;

        if (isVictory)
        {
            LevelConfig cfg = LevelManager.GetCurrentConfig();
            int numFish = Mathf.Clamp(cfg.maxEnemyLevel, 1, 6);
            bool isTwoColumn = (numFish > 3);
            int maxRows = isTwoColumn ? 3 : numFish;
            float rowSpacing = 58f * scaleFactor;
            float colOffset = 160f * scaleFactor;
            float fishListHeight = (maxRows - 1) * rowSpacing;

            // Centering math: perfectly balances title, subtitle, duration, vertical fish list, and buttons
            float fishListCenterY = -15f * scaleFactor;
            float startY = fishListCenterY + (fishListHeight / 2f);

            float timeY = startY + (64f * scaleFactor);
            float subtitleY = timeY + (58f * scaleFactor);
            float titleY = subtitleY + (68f * scaleFactor);
            float buttonY = (fishListCenterY - (fishListHeight / 2f)) - (125f * scaleFactor);

            // 1. Victory Title: "sUmGbGrsaTr" (សូមអបអរសាទរ - Congratulations)
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtTitle = titleObj.AddComponent<RectTransform>();
            rtTitle.anchorMin = new Vector2(0.5f, 0.5f);
            rtTitle.anchorMax = new Vector2(0.5f, 0.5f);
            rtTitle.pivot = new Vector2(0.5f, 0.5f);
            rtTitle.anchoredPosition = new Vector2(0, titleY);
            rtTitle.sizeDelta = new Vector2(1000f * scaleFactor, 95f * scaleFactor);

            Text titleTxt = titleObj.AddComponent<Text>();
            if (activeFont != null) titleTxt.font = activeFont;
            titleTxt.text = !string.IsNullOrEmpty(victoryMessage) ? victoryMessage : "sUmGbGrsaTr"; // សូមអបអរសាទរ
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.fontSize = Mathf.RoundToInt(72f * scaleFactor); // Larger font size
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = new Color(1f, 0.88f, 0.25f, 1f); // Bright Gold
            Shadow tShadow = titleObj.AddComponent<Shadow>();
            tShadow.effectColor = new Color(0, 0, 0, 0.85f);
            tShadow.effectDistance = new Vector2(2.5f * scaleFactor, -2.5f * scaleFactor);

            // 2. Level Subtitle: "kmrit X )anbBa©ab;" (កម្រិត X បានបញ្ចប់ - Level X Completed)
            GameObject subTitleObj = new GameObject("KhmerSubtitle");
            subTitleObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtSubTitle = subTitleObj.AddComponent<RectTransform>();
            rtSubTitle.anchorMin = new Vector2(0.5f, 0.5f);
            rtSubTitle.anchorMax = new Vector2(0.5f, 0.5f);
            rtSubTitle.pivot = new Vector2(0.5f, 0.5f);
            rtSubTitle.anchoredPosition = new Vector2(0, subtitleY);
            rtSubTitle.sizeDelta = new Vector2(800f * scaleFactor, 65f * scaleFactor);

            Text subTitleTxt = subTitleObj.AddComponent<Text>();
            if (activeFont != null) subTitleTxt.font = activeFont;
            subTitleTxt.text = "kmrit " + currentLvl + " )anbBa©ab;"; // កម្រិត X បានបញ្ចប់
            subTitleTxt.alignment = TextAnchor.MiddleCenter;
            subTitleTxt.fontSize = Mathf.RoundToInt(44f * scaleFactor); // Larger font size
            subTitleTxt.fontStyle = FontStyle.Bold;
            subTitleTxt.color = new Color(0.85f, 0.95f, 1f, 0.95f);
            Shadow stShadow = subTitleObj.AddComponent<Shadow>();
            stShadow.effectColor = new Color(0, 0, 0, 0.85f);
            stShadow.effectDistance = new Vector2(2f * scaleFactor, -2f * scaleFactor);

            // 3. Duration: "ryHeBl : 00:00" (Khmer Label + Clean standard digital clock)
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(LevelManager.LevelTimer));
            string timeFormatted = (totalSeconds >= 3600)
                ? string.Format("{0:00}:{1:00}:{2:00}", totalSeconds / 3600, (totalSeconds % 3600) / 60, totalSeconds % 60)
                : string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);

            GameObject timeRowObj = new GameObject("TimeSpentRow");
            timeRowObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtTime = timeRowObj.AddComponent<RectTransform>();
            rtTime.anchorMin = new Vector2(0.5f, 0.5f);
            rtTime.anchorMax = new Vector2(0.5f, 0.5f);
            rtTime.pivot = new Vector2(0.5f, 0.5f);
            rtTime.anchoredPosition = new Vector2(0, timeY);
            rtTime.sizeDelta = new Vector2(600f * scaleFactor, 55f * scaleFactor);

            // Duration Label (Khmer: ryHeBl = រយៈពេល)
            GameObject lblObj = new GameObject("Label");
            lblObj.transform.SetParent(timeRowObj.transform, false);
            RectTransform rtLbl = lblObj.AddComponent<RectTransform>();
            rtLbl.anchorMin = new Vector2(0.5f, 0.5f);
            rtLbl.anchorMax = new Vector2(0.5f, 0.5f);
            rtLbl.pivot = new Vector2(1f, 0.5f);
            rtLbl.anchoredPosition = new Vector2(-10f * scaleFactor, 0);
            rtLbl.sizeDelta = new Vector2(300f * scaleFactor, 55f * scaleFactor);

            Text lblTxt = lblObj.AddComponent<Text>();
            if (activeFont != null) lblTxt.font = activeFont;
            lblTxt.text = "ryHeBl"; // រយៈពេល
            lblTxt.alignment = TextAnchor.MiddleRight;
            lblTxt.fontSize = Mathf.RoundToInt(42f * scaleFactor); // Larger font size
            lblTxt.fontStyle = FontStyle.Bold;
            lblTxt.color = new Color(0.4f, 0.9f, 1f, 1f); // Bright Aqua/Cyan
            Shadow lblShadow = lblObj.AddComponent<Shadow>();
            lblShadow.effectColor = new Color(0, 0, 0, 0.85f);
            lblShadow.effectDistance = new Vector2(2f * scaleFactor, -2f * scaleFactor);

            // Duration Clock Value (: 00:00 in standard font so digits and colon are crisp)
            GameObject valObj = new GameObject("Value");
            valObj.transform.SetParent(timeRowObj.transform, false);
            RectTransform rtVal = valObj.AddComponent<RectTransform>();
            rtVal.anchorMin = new Vector2(0.5f, 0.5f);
            rtVal.anchorMax = new Vector2(0.5f, 0.5f);
            rtVal.pivot = new Vector2(0f, 0.5f);
            rtVal.anchoredPosition = new Vector2(0f, 0);
            rtVal.sizeDelta = new Vector2(300f * scaleFactor, 55f * scaleFactor);

            Text valTxt = valObj.AddComponent<Text>();
            if (stdFont != null) valTxt.font = stdFont;
            valTxt.text = ": " + timeFormatted;
            valTxt.alignment = TextAnchor.MiddleLeft;
            valTxt.fontSize = Mathf.RoundToInt(42f * scaleFactor); // Larger font size
            valTxt.fontStyle = FontStyle.Bold;
            valTxt.color = new Color(0.4f, 0.9f, 1f, 1f);
            Shadow valShadow = valObj.AddComponent<Shadow>();
            valShadow.effectColor = new Color(0, 0, 0, 0.85f);
            valShadow.effectDistance = new Vector2(2f * scaleFactor, -2f * scaleFactor);

            // 4. Fish Rows (Max 3 per vertical column, 2 columns if > 3 fish)
            for (int i = 1; i <= numFish; i++)
            {
                float colX = 0f;
                int rowIndex = i - 1;
                if (isTwoColumn)
                {
                    if (i <= 3)
                    {
                        colX = -colOffset;
                        rowIndex = i - 1;
                    }
                    else
                    {
                        colX = colOffset;
                        rowIndex = i - 4;
                    }
                }
                float rowY = startY - (rowIndex * rowSpacing);

                GameObject fishItemObj = new GameObject("FishItem_" + i);
                fishItemObj.transform.SetParent(endLevelModalObj.transform, false);
                RectTransform rtItem = fishItemObj.AddComponent<RectTransform>();
                rtItem.anchorMin = new Vector2(0.5f, 0.5f);
                rtItem.anchorMax = new Vector2(0.5f, 0.5f);
                rtItem.pivot = new Vector2(0.5f, 0.5f);
                rtItem.anchoredPosition = new Vector2(colX, rowY);
                rtItem.sizeDelta = new Vector2(280f * scaleFactor, 52f * scaleFactor);

                // Fish Icon (Left of column divider: pivot (1, 0.5), right edge at X = -12)
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(fishItemObj.transform, false);
                RectTransform rtIcon = iconObj.AddComponent<RectTransform>();
                rtIcon.anchorMin = new Vector2(0.5f, 0.5f);
                rtIcon.anchorMax = new Vector2(0.5f, 0.5f);
                rtIcon.pivot = new Vector2(1f, 0.5f);
                rtIcon.anchoredPosition = new Vector2(-12f * scaleFactor, 0);
                rtIcon.sizeDelta = new Vector2(70f * scaleFactor, 48f * scaleFactor);

                Image iconImg = iconObj.AddComponent<Image>();
                if (growthIcons != null && (i - 1) < growthIcons.Length && growthIcons[i - 1] != null)
                {
                    iconImg.sprite = growthIcons[i - 1].sprite;
                }
                iconImg.preserveAspect = true;

                // Fish Count Text (Right of column divider: pivot (0, 0.5), left edge at X = 0)
                int eatenCount = (i < LevelManager.FishEatenCounts.Length) ? LevelManager.FishEatenCounts[i] : 0;

                GameObject countObj = new GameObject("Count");
                countObj.transform.SetParent(fishItemObj.transform, false);
                RectTransform rtCount = countObj.AddComponent<RectTransform>();
                rtCount.anchorMin = new Vector2(0.5f, 0.5f);
                rtCount.anchorMax = new Vector2(0.5f, 0.5f);
                rtCount.pivot = new Vector2(0f, 0.5f);
                rtCount.anchoredPosition = new Vector2(0f, 0);
                rtCount.sizeDelta = new Vector2(150f * scaleFactor, 50f * scaleFactor);

                Text countTxt = countObj.AddComponent<Text>();
                if (stdFont != null) countTxt.font = stdFont;
                countTxt.text = ":  " + eatenCount;
                countTxt.alignment = TextAnchor.MiddleLeft;
                countTxt.fontSize = Mathf.RoundToInt(42f * scaleFactor); // Larger font size
                countTxt.fontStyle = FontStyle.Bold;
                countTxt.color = Color.white;
                Shadow cShadow = countObj.AddComponent<Shadow>();
                cShadow.effectColor = new Color(0, 0, 0, 0.85f);
                cShadow.effectDistance = new Vector2(2f * scaleFactor, -2f * scaleFactor);
            }

            // 6. Action Buttons: Menu, Play Again, Next (Clean buttons with no English subtitles)
            Vector2 btnSize = new Vector2(135f * scaleFactor, 135f * scaleFactor);
            int btnFontSize = Mathf.RoundToInt(50f * scaleFactor);

            if (hasNext)
            {
                // Menu (Left)
                GameObject menuBtnObj = CreateButton("MenuBtn", endLevelModalObj.transform);
                CustomizeButton(menuBtnObj, "muWnuy", Color.white, btnSize, btnFontSize);
                PositionButton(menuBtnObj, new Vector2(-180f * scaleFactor, buttonY));
                SetupButton(menuBtnObj, () =>
                {
                    MainMenuManager.OpenLevelSelectOnLoad = false;
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("MainMenu");
                });

                // Play Again (Center)
                GameObject replayBtn = CreateButton("PlayAgainBtn", endLevelModalObj.transform);
                CustomizeButton(replayBtn, "safµI", Color.white, btnSize, btnFontSize);
                PositionButton(replayBtn, new Vector2(0f, buttonY));
                SetupButton(replayBtn, () =>
                {
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("SampleScene");
                });

                // Next Level (Right) - Short "Next" text: "bnÞab;" (បន្ទាប់)
                GameObject nextBtn = CreateButton("NextLevelBtn", endLevelModalObj.transform);
                CustomizeButton(nextBtn, "bnÞab;", Color.white, btnSize, btnFontSize);
                PositionButton(nextBtn, new Vector2(180f * scaleFactor, buttonY));
                SetupButton(nextBtn, () =>
                {
                    LevelManager.CurrentLevel = currentLvl + 1;
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("SampleScene");
                });
            }
            else
            {
                // All levels completed: Menu & Play Again
                GameObject menuBtnObj = CreateButton("MenuBtn", endLevelModalObj.transform);
                CustomizeButton(menuBtnObj, "muWnuy", Color.white, btnSize, btnFontSize);
                PositionButton(menuBtnObj, new Vector2(-110f * scaleFactor, buttonY));
                SetupButton(menuBtnObj, () =>
                {
                    MainMenuManager.OpenLevelSelectOnLoad = false;
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("MainMenu");
                });

                GameObject replayBtn = CreateButton("PlayAgainBtn", endLevelModalObj.transform);
                CustomizeButton(replayBtn, "safµI", Color.white, btnSize, btnFontSize);
                PositionButton(replayBtn, new Vector2(110f * scaleFactor, buttonY));
                SetupButton(replayBtn, () =>
                {
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("SampleScene");
                });
            }
        }
        else
        {
            // Defeat (Try Again) - Clean 0.40f overlay
            GameObject titleObj = new GameObject("DefeatTitle");
            titleObj.transform.SetParent(endLevelModalObj.transform, false);
            RectTransform rtTitle = titleObj.AddComponent<RectTransform>();
            rtTitle.anchorMin = new Vector2(0.5f, 0.5f);
            rtTitle.anchorMax = new Vector2(0.5f, 0.5f);
            rtTitle.pivot = new Vector2(0.5f, 0.5f);
            rtTitle.anchoredPosition = new Vector2(0, 100f * scaleFactor);
            rtTitle.sizeDelta = new Vector2(800f * scaleFactor, 105f * scaleFactor);

            Text titleTxt = titleObj.AddComponent<Text>();
            if (activeFont != null) titleTxt.font = activeFont;
            titleTxt.text = !string.IsNullOrEmpty(defeatMessage) ? defeatMessage : "B\xfcayamm\xfegeTot"; // ព្យាយាមម្តងទៀត
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.fontSize = Mathf.RoundToInt(78f * scaleFactor); // Larger font size
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = Color.white;
            Shadow dtShadow = titleObj.AddComponent<Shadow>();
            dtShadow.effectColor = new Color(0, 0, 0, 0.85f);
            dtShadow.effectDistance = new Vector2(2.5f * scaleFactor, -2.5f * scaleFactor);

            Vector2 btnSize = new Vector2(140f * scaleFactor, 140f * scaleFactor);
            int btnFontSize = Mathf.RoundToInt(50f * scaleFactor);

            // Retry Button (Left)
            GameObject retryBtn = CreateButton("RetryBtn", endLevelModalObj.transform);
            CustomizeButton(retryBtn, "safµI", Color.white, btnSize, btnFontSize);
            PositionButton(retryBtn, new Vector2(-120f * scaleFactor, -60f * scaleFactor));
            SetupButton(retryBtn, () =>
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene("SampleScene");
            });

            // Menu Button (Right)
            GameObject menuBtnObj = CreateButton("MenuBtn", endLevelModalObj.transform);
            CustomizeButton(menuBtnObj, "muWnuy", Color.white, btnSize, btnFontSize);
            PositionButton(menuBtnObj, new Vector2(120f * scaleFactor, -60f * scaleFactor));
            SetupButton(menuBtnObj, () =>
            {
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



