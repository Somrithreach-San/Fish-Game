using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro; // Add TextMeshPro Namespace

public class MainMenuManager : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("The name of the game scene to load")]
    [SerializeField] public string gameSceneName = "SampleScene";

    [Header("UI References")]
    [SerializeField] public GameObject mainPanel;
    [SerializeField] public GameObject settingsPanel;
    [SerializeField] public Slider volumeSlider;
    [SerializeField] public Text volumeLabel; // Reverted to Text for Legacy UI
    
    [Header("Buttons")]
    [SerializeField] public Button playButton;
    [SerializeField] public Button settingsButton;
    [SerializeField] public Button quitButton;
    [SerializeField] public Button backButton;

    [Header("Audio Settings UI")]
    [SerializeField] public Button musicToggleButton;
    [SerializeField] public Text musicLabel;
    [SerializeField] public Text musicStatusText;
    [SerializeField] public Button sfxToggleButton;
    [SerializeField] public Text sfxLabel;
    [SerializeField] public Text sfxStatusText;

    [Header("Audio")]
    [SerializeField] public AudioClip buttonSoundClip;
    [SerializeField] public AudioSource musicSource; // Reference to Music Source
    private AudioSource sfxSource;
    private float defaultMenuMusicVolume = 0.5f;

    [Header("Custom Assets")]
    [SerializeField] public Font customFont; // For lmns1
    [SerializeField] public Sprite buttonShape; // For Knob

    private void Start()
    {
        // Find References
        FindReferences();

        // Setup Audio Source for SFX
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        // Auto-find Music Source if not wired
        if (musicSource == null)
        {
            MenuMusic mm = FindFirstObjectByType<MenuMusic>();
            if (mm != null) musicSource = mm.GetComponent<AudioSource>();
            if (musicSource == null)
            {
                GameObject mPlayer = GameObject.Find("MenuMusicPlayer");
                if (mPlayer != null) musicSource = mPlayer.GetComponent<AudioSource>();
            }
        }
        if (musicSource != null && musicSource.volume > 0f)
        {
            defaultMenuMusicVolume = musicSource.volume;
        }

        AudioSettingsManager.OnMusicSettingChanged += HandleMusicSettingChanged;
        ApplyMenuMusicState(AudioSettingsManager.IsMusicEnabled);

        // Setup Listeners and Hover Effects
        SetupButton(playButton, PlayGame);
        SetupButton(settingsButton, OpenSettings);
        SetupButton(backButton, CloseSettings);
        SetupButton(quitButton, QuitGame);

        // Setup settings controls in SettingsPanel
        EnsureSettingsControls();

        // Initialize Volume
        if (volumeSlider != null)
        {
            float savedVolume = AudioSettingsManager.MasterVolume;
            AudioListener.volume = savedVolume;
            volumeSlider.value = savedVolume;
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            UpdateVolumeLabel(savedVolume);
        }

        // Ensure correct initial state
        ShowMain();

        // Enforce Mobile Responsiveness (Canvas Scaling)
        SetupMobileUI();

        // Quit Button Logic:
        // We no longer hide it on WebGL, instead we make it do nothing (see QuitGameRoutine)
        
        // Hide unwanted "Comic" or "Credits" button if present
        if (mainPanel != null)
        {
            Transform comicBtn = mainPanel.transform.Find("ComicButton");
            if (comicBtn != null) comicBtn.gameObject.SetActive(false);
            Transform creditsBtn = mainPanel.transform.Find("CreditsButton");
            if (creditsBtn != null) creditsBtn.gameObject.SetActive(false);
        }

        // Apply New Layout (Vertical Capsules)
        // RedesignMenuLayout();

        // Update All Menu Buttons Text & Font (User Request)
        UpdateMenuButtons();
    }

    private void Update()
    {
        // Mobile Portrait Check (Similar to GameManager)
        // Ensure we pause/mute if the device is rotated to portrait (User Request)
        if (Application.isMobilePlatform || Application.isEditor)
        {
             if (Screen.height > Screen.width) // Portrait
             {
                 if (Time.timeScale != 0f)
                 {
                     Time.timeScale = 0f;
                     AudioListener.pause = true;
                 }
                 // Force mute ensuring no slippage
                 if (!AudioListener.pause) AudioListener.pause = true;
             }
             else // Landscape
             {
                 if (Time.timeScale == 0f)
                 {
                     Time.timeScale = 1f;
                     AudioListener.pause = false;
                 }
             }
        }
    }

    private void OnValidate()
    {
        // Allow live updates in Editor safely without SendMessage layout recursion
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            FindReferences();
            FormatCloseButton(backButton);
            UpdateMenuButtons();
        };
        #endif
    }

    private void FindReferences()
    {
        // Auto-Link References if missing (Robustness)
        if (mainPanel == null) mainPanel = GameObject.Find("MainPanel");
        if (settingsPanel == null) settingsPanel = GameObject.Find("SettingsPanel");
        if (volumeSlider == null) volumeSlider = FindFirstObjectByType<Slider>(); // Simplification
        
        // Find Buttons if missing (Robust Search)
        if (playButton == null && mainPanel != null) 
        {
            Transform t = mainPanel.transform.Find("PlayButton");
            if (t == null) t = mainPanel.transform.Find("StartButton");
            if (t == null) t = mainPanel.transform.Find("Play");
            if (t != null) playButton = t.GetComponent<Button>();
        }
        
        if (settingsButton == null && mainPanel != null) 
        {
            Transform t = mainPanel.transform.Find("SettingsButton");
            if (t == null) t = mainPanel.transform.Find("OptionsButton");
            if (t == null) t = mainPanel.transform.Find("ConfigButton");
            if (t != null) settingsButton = t.GetComponent<Button>();
        }
        
        if (backButton == null && settingsPanel != null) backButton = settingsPanel.transform.Find("BackButton")?.GetComponent<Button>();
        
        if (quitButton == null && mainPanel != null) 
        {
            Transform t = mainPanel.transform.Find("QuitButton");
            if (t == null) t = mainPanel.transform.Find("ExitButton");
            if (t != null) quitButton = t.GetComponent<Button>();
        }
    }

    private Sprite GetButtonSprite()
    {
        if (buttonShape != null) return buttonShape;

        // Try to get from PlayButton image
        if (playButton != null)
        {
            Image playImg = playButton.GetComponent<Image>();
            if (playImg != null && playImg.sprite != null && playImg.sprite.name.Contains("Bubble"))
                return playImg.sprite;
        }

        // Try to load from Resources
        Sprite[] sprites = Resources.LoadAll<Sprite>("Bubble_Button");
        if (sprites != null && sprites.Length > 0) return sprites[0];
        Sprite single = Resources.Load<Sprite>("Bubble_Button");
        if (single != null) return single;

        // Try finding loaded sprites
        Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (Sprite s in allSprites)
        {
            if (s.name.Contains("Bubble_Button")) return s;
        }

        // Fallbacks
        if (playButton != null)
        {
            Image playImg = playButton.GetComponent<Image>();
            if (playImg != null && playImg.sprite != null) return playImg.sprite;
        }
        return Resources.Load<Sprite>("Knob");
    }

    private void ApplyBubbleStyle(Button btn, Sprite bubble)
    {
        if (btn == null || bubble == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = bubble;
            img.color = Color.white; // Show full translucent glossy bubble texture
        }
    }

    private void UpdateMenuButtons()
    {
        // Apply Bubble_Button styling to all landing and close buttons
        Sprite bubble = GetButtonSprite();
        ApplyBubbleStyle(playButton, bubble);
        ApplyBubbleStyle(settingsButton, bubble);
        ApplyBubbleStyle(quitButton, bubble);
        ApplyBubbleStyle(backButton, bubble);

        // Use assigned font or fallback to Resources
        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");

        if (limonFont == null) return;

        // 1. Play Button -> "elg"
        UpdateButtonText(playButton, "elg", limonFont);

        // 2. Settings Button -> "kMNt;"
        UpdateButtonText(settingsButton, "kMNt;", limonFont);

        // 3. Quit Button -> "ecj"
        UpdateButtonText(quitButton, "ecj", limonFont);

        // 4. Back/Close Button Text -> "X"
        if (backButton != null)
        {
            UpdateCloseButtonText(backButton);
        }

        // Refresh settings UI text
        UpdateAudioSettingsUI();
    }

    private void UpdateButtonText(Button btn, string newText, Font font)
    {
        if (btn == null) return;

        // Try Legacy Text
        Text txt = btn.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.font = font;
            txt.text = newText;
            txt.color = Color.white;
            // Ensure size is good (readable) - Increased to 75 per user request
            if (txt.fontSize < 60) txt.fontSize = 75;
        }
        
        // Try TMP (If used)
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = newText;
            tmp.color = Color.white;
            // TMP Font Asset handling would go here if we had a generated asset
        }
    }

    private void RedesignMenuLayout()
    {
        // REVERTED TO OLD STYLE (Triangle Layout, Circles)
        // Play (Top), Quit (Bottom Left), Settings (Bottom Right)
        
        Vector2 buttonSize = new Vector2(180, 180); // Circle Shape
        Color buttonColor = new Color(0.53f, 0.81f, 0.92f, 0.8f); // Light Blue Semi-Transparent

        // Ensure buttons are active
        if (playButton != null) playButton.gameObject.SetActive(true);
        if (settingsButton != null) settingsButton.gameObject.SetActive(true);
        
        if (quitButton != null) quitButton.gameObject.SetActive(true);

        // Position Logic
        if (playButton != null)
        {
            CustomizeButton(playButton, buttonSize, buttonColor);
            PositionButton(playButton, new Vector2(0, -50)); // Top Center (Lowered from 0)
        }

        if (settingsButton != null)
        {
            CustomizeButton(settingsButton, buttonSize, buttonColor);
            PositionButton(settingsButton, new Vector2(150, -250)); // Bottom Right (Lowered from -200)
        }

        if (quitButton != null)
        {
            CustomizeButton(quitButton, buttonSize, buttonColor);
            PositionButton(quitButton, new Vector2(-150, -250)); // Bottom Left (Lowered from -200)
        }
    }

    private void CustomizeButton(Button btn, Vector2 size, Color color)
    {
        if (btn == null) return;
        
        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }

        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            // Use assigned shape or fallback to Resources
            Sprite circle = buttonShape;
            if (circle == null) circle = Resources.Load<Sprite>("Knob");
            if (circle == null) circle = Resources.Load<Sprite>("UI/Skin/Knob");
            
            if (circle != null) 
            {
                img.sprite = circle;
                img.type = Image.Type.Simple; // Simple circle
            }
            img.color = color;
        }

        // Text Styling
        Text txt = btn.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 14;
            txt.resizeTextMaxSize = 28;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white; // Ensure white text
        }
        
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14;
            tmp.fontSizeMax = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }
    }

    private void PositionButton(Button btn, Vector2 pos)
    {
        if (btn != null)
        {
            RectTransform rt = btn.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = pos;
        }
    }

    private void SetupMobileUI()
    {
        // Find Canvas (attached to this or parent)
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && mainPanel != null) canvas = mainPanel.GetComponentInParent<Canvas>();

        if (canvas != null)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

            // Set to Scale With Screen Size (Crucial for Mobile)
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // Standard HD Landscape
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f; // Balance width/height matching
        }
    }

    private void SetupButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners(); // Clean slate
            btn.onClick.AddListener(action);
            btn.onClick.AddListener(PlayButtonSound);
            
            // Auto-Add Hover Effect
            if (btn.gameObject.GetComponent<ButtonHoverEffect>() == null)
            {
                btn.gameObject.AddComponent<ButtonHoverEffect>();
            }
        }
    }

    public void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void PlayGame()
    {
        StartCoroutine(PlayGameRoutine());
    }

    private System.Collections.IEnumerator PlayGameRoutine()
    {
        // Kill menu music object entirely
        MenuMusic menuMusic = FindFirstObjectByType<MenuMusic>();
        if (menuMusic != null)
        {
            menuMusic.Kill();
        }

        // Wait for button click sound (played on sfxSource)
        yield return new WaitForSeconds(0.4f);

        if (Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
        }
        else
        {
            Debug.LogError($"Scene '{gameSceneName}' not found! Please check Build Settings.");
        }
    }

    public void OpenSettings()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            EnsureSettingsControls();
            UpdateAudioSettingsUI();
        }
    }

    public void CloseSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void ToggleMusic()
    {
        AudioSettingsManager.IsMusicEnabled = !AudioSettingsManager.IsMusicEnabled;
        ApplyMenuMusicState(AudioSettingsManager.IsMusicEnabled);
        UpdateAudioSettingsUI();
    }

    public void ToggleSfx()
    {
        AudioSettingsManager.IsSfxEnabled = !AudioSettingsManager.IsSfxEnabled;
        UpdateAudioSettingsUI();
    }

    private void HandleMusicSettingChanged(bool isEnabled)
    {
        ApplyMenuMusicState(isEnabled);
        UpdateAudioSettingsUI();
    }

    public void ApplyMenuMusicState(bool isEnabled)
    {
        // 1. Control MainMenuManager's musicSource if assigned
        if (musicSource != null)
        {
            musicSource.mute = !isEnabled;
            musicSource.volume = isEnabled ? defaultMenuMusicVolume : 0f;
            if (!isEnabled)
            {
                musicSource.Pause();
            }
            else
            {
                if (!musicSource.isPlaying) musicSource.Play();
                else musicSource.UnPause();
            }
        }

        // 2. Control MenuMusic singleton if present
        MenuMusic mm = FindFirstObjectByType<MenuMusic>();
        if (mm != null)
        {
            mm.ApplyMusicSetting(isEnabled);
        }

        // 3. Control any other AudioSource on MenuMusicPlayer GameObject
        GameObject mmPlayer = GameObject.Find("MenuMusicPlayer");
        if (mmPlayer != null)
        {
            AudioSource[] sources = mmPlayer.GetComponentsInChildren<AudioSource>(true);
            foreach (var s in sources)
            {
                if (s != null)
                {
                    s.mute = !isEnabled;
                    s.volume = isEnabled ? 0.7f : 0f;
                    if (!isEnabled)
                    {
                        s.Pause();
                    }
                    else
                    {
                        if (!s.isPlaying) s.Play();
                        else s.UnPause();
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        AudioSettingsManager.OnMusicSettingChanged -= HandleMusicSettingChanged;
    }

    private void EnsureSettingsControls()
    {
        if (settingsPanel == null) return;

        // Ensure settings panel overlay color matches the pause in-game overlay (0, 0, 0, 0.20f)
        Image panelImg = settingsPanel.GetComponent<Image>();
        if (panelImg == null) panelImg = settingsPanel.AddComponent<Image>();
        if (panelImg != null)
        {
            panelImg.color = new Color(0f, 0f, 0f, 0.20f);
            panelImg.raycastTarget = true;
        }

        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");

        // Hide ControlsInfo so Settings panel is clean and focused
        Transform controls = settingsPanel.transform.Find("ControlsInfo");
        if (controls != null) controls.gameObject.SetActive(false);

        // Hide Volume Slider and Label completely per user request
        if (volumeSlider != null) volumeSlider.gameObject.SetActive(false);
        if (volumeLabel != null) volumeLabel.gameObject.SetActive(false);
        Transform vSliderTr = settingsPanel.transform.Find("VolumeSlider");
        if (vSliderTr != null) vSliderTr.gameObject.SetActive(false);
        Transform vLabelTr = settingsPanel.transform.Find("VolumeLabel");
        if (vLabelTr != null) vLabelTr.gameObject.SetActive(false);

        // Format Back Button as top-right 'X' close button (same size as landing page buttons: 180x180)
        if (backButton != null)
        {
            FormatCloseButton(backButton);
        }

        // 1. Music Row Setup
        Transform existingMusicRow = settingsPanel.transform.Find("MusicRow");
        if (existingMusicRow != null)
        {
            if (Application.isPlaying) Destroy(existingMusicRow.gameObject);
            else DestroyImmediate(existingMusicRow.gameObject);
            musicToggleButton = null;
            musicLabel = null;
            musicStatusText = null;
        }
        CreateAudioToggleRow("MusicRow", new Vector2(0, 60f), "eP\xf8g", out musicLabel, out musicToggleButton, out musicStatusText);
        if (musicToggleButton != null)
        {
            SetupButton(musicToggleButton, ToggleMusic);
        }

        // 2. SFX Row Setup
        Transform existingSfxRow = settingsPanel.transform.Find("SfxRow");
        if (existingSfxRow != null)
        {
            if (Application.isPlaying) Destroy(existingSfxRow.gameObject);
            else DestroyImmediate(existingSfxRow.gameObject);
            sfxToggleButton = null;
            sfxLabel = null;
            sfxStatusText = null;
        }
        CreateAudioToggleRow("SfxRow", new Vector2(0, -150f), "sMeLg", out sfxLabel, out sfxToggleButton, out sfxStatusText);
        if (sfxToggleButton != null)
        {
            SetupButton(sfxToggleButton, ToggleSfx);
        }

        UpdateAudioSettingsUI();
    }

    private void FormatCloseButton(Button btn)
    {
        if (btn == null) return;

        // 1. RectTransform: Size 180 x 180 (Exact same size as the 3 landing page buttons), Top-Right corner
        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(180f, 180f);
            rt.anchoredPosition = new Vector2(-60f, -60f);
        }

        // 2. Image: Bubble_Button sprite with full white to show its rich translucent colors
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            Sprite bubble = GetButtonSprite();
            if (bubble != null)
            {
                img.sprite = bubble;
                img.type = Image.Type.Simple;
            }
            img.color = Color.white;
            img.raycastTarget = true;
        }

        // 3. Text Child: Standard font for Latin 'X', size 70, Bold, White, Center
        UpdateCloseButtonText(btn);

        // 4. Hover effect
        if (Application.isPlaying && btn.gameObject.GetComponent<ButtonHoverEffect>() == null)
        {
            btn.gameObject.AddComponent<ButtonHoverEffect>();
        }
    }

    private void UpdateCloseButtonText(Button btn)
    {
        if (btn == null) return;

        Font standardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (standardFont == null) standardFont = Font.CreateDynamicFontFromOSFont("Arial", 70);

        Text txt = btn.GetComponentInChildren<Text>();
        if (txt != null)
        {
            if (standardFont != null) txt.font = standardFont;
            txt.text = "X";
            txt.fontSize = 70;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.resizeTextForBestFit = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
        }

        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = "X";
            tmp.fontSize = 70;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }
    }

    private void CreateAudioToggleRow(string rowName, Vector2 position, string labelText, out Text labelComp, out Button toggleBtn, out Text statusComp)
    {
        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");

        // Row Root - 720px width accommodates 500px label and 180px toggle button
        GameObject row = new GameObject(rowName);
        row.transform.SetParent(settingsPanel.transform, false);
        row.layer = settingsPanel.layer;
        RectTransform rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = position;
        rowRt.sizeDelta = new Vector2(720f, 180f);

        // Label - Left Aligned, bigger font (100), clean without stroke
        string labelName = rowName.Replace("Row", "") + "Label";
        GameObject lblObj = new GameObject(labelName);
        lblObj.transform.SetParent(row.transform, false);
        lblObj.layer = settingsPanel.layer;
        RectTransform lblRt = lblObj.AddComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0f, 0.5f);
        lblRt.anchorMax = new Vector2(0f, 0.5f);
        lblRt.pivot = new Vector2(0f, 0.5f);
        lblRt.anchoredPosition = new Vector2(0f, 0);
        lblRt.sizeDelta = new Vector2(500f, 180f);

        labelComp = lblObj.AddComponent<Text>();
        labelComp.font = limonFont;
        labelComp.text = labelText;
        labelComp.fontSize = 100;
        labelComp.fontStyle = FontStyle.Bold;
        labelComp.alignment = TextAnchor.MiddleLeft;
        labelComp.color = Color.white;
        labelComp.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelComp.verticalOverflow = VerticalWrapMode.Overflow;
        labelComp.raycastTarget = false;

        // Button - Circle styled like back button, size 180x180 (matching landing page 3 main buttons)
        string toggleName = rowName.Replace("Row", "") + "Toggle";
        GameObject btnObj = new GameObject(toggleName);
        btnObj.transform.SetParent(row.transform, false);
        btnObj.layer = settingsPanel.layer;
        RectTransform btnRt = btnObj.AddComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(1f, 0.5f);
        btnRt.anchorMax = new Vector2(1f, 0.5f);
        btnRt.pivot = new Vector2(1f, 0.5f);
        btnRt.anchoredPosition = new Vector2(0f, 0);
        btnRt.sizeDelta = new Vector2(180f, 180f);

        Image img = btnObj.AddComponent<Image>();
        Sprite bubble = GetButtonSprite();
        if (bubble != null) img.sprite = bubble;
        img.type = Image.Type.Simple;
        img.color = Color.white; // Show full translucent glossy bubble texture
        img.raycastTarget = true;

        toggleBtn = btnObj.AddComponent<Button>();

        if (btnObj.GetComponent<ButtonHoverEffect>() == null)
        {
            btnObj.AddComponent<ButtonHoverEffect>();
        }

        // Text Child - Font size 75 (matching landing page 3 main buttons)
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        txtObj.layer = settingsPanel.layer;
        RectTransform txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.sizeDelta = Vector2.zero;

        statusComp = txtObj.AddComponent<Text>();
        statusComp.font = limonFont;
        statusComp.fontSize = 75;
        statusComp.fontStyle = FontStyle.Bold;
        statusComp.alignment = TextAnchor.MiddleCenter;
        statusComp.color = Color.white;
        statusComp.horizontalOverflow = HorizontalWrapMode.Overflow;
        statusComp.verticalOverflow = VerticalWrapMode.Overflow;
        statusComp.raycastTarget = false;
    }

    public void UpdateAudioSettingsUI()
    {
        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");

        // Refresh label fonts, font size, and alignment
        if (musicLabel != null)
        {
            if (limonFont != null) musicLabel.font = limonFont;
            musicLabel.text = "eP\xf8g"; // ភ្លេង (Music)
            musicLabel.alignment = TextAnchor.MiddleLeft;
            musicLabel.fontSize = 100;
        }
        if (sfxLabel != null)
        {
            if (limonFont != null) sfxLabel.font = limonFont;
            sfxLabel.text = "sMeLg"; // សំឡេង (Sound)
            sfxLabel.alignment = TextAnchor.MiddleLeft;
            sfxLabel.fontSize = 100;
        }

        // Music toggle status
        bool musicOn = AudioSettingsManager.IsMusicEnabled;
        if (musicStatusText != null)
        {
            if (limonFont != null) musicStatusText.font = limonFont;
            musicStatusText.text = musicOn ? "ebIk" : "biT"; // ebIk = បើក (ON), biT = បិទ (OFF)
            musicStatusText.color = Color.white;
            musicStatusText.fontSize = 75;
        }
        if (musicToggleButton != null)
        {
            Image img = musicToggleButton.GetComponent<Image>();
            if (img != null)
            {
                img.color = Color.white;
            }
        }

        // SFX toggle status
        bool sfxOn = AudioSettingsManager.IsSfxEnabled;
        if (sfxStatusText != null)
        {
            if (limonFont != null) sfxStatusText.font = limonFont;
            sfxStatusText.text = sfxOn ? "ebIk" : "biT"; // ebIk = បើក (ON), biT = បិទ (OFF)
            sfxStatusText.color = Color.white;
            sfxStatusText.fontSize = 75;
        }
        if (sfxToggleButton != null)
        {
            Image img = sfxToggleButton.GetComponent<Image>();
            if (img != null)
            {
                img.color = Color.white;
            }
        }
    }

    public void QuitGame()
    {
        StartCoroutine(QuitGameRoutine());
    }

    private System.Collections.IEnumerator QuitGameRoutine()
    {
        // Wait for sound to play (0.4s)
        yield return new WaitForSeconds(0.4f);

        Debug.Log("Quitting Game...");
        
        #if UNITY_WEBGL
        Debug.Log("Quit button clicked - Action disabled on WebGL");
        #else
        Application.Quit();
        #endif

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    public void OnVolumeChanged(float value)
    {
        AudioSettingsManager.MasterVolume = value;
        UpdateVolumeLabel(value);
    }

    public void PlayButtonSound()
    {
        if (!AudioSettingsManager.IsSfxEnabled) return;
        if (sfxSource != null && buttonSoundClip != null)
        {
            sfxSource.volume = 1.0f; // Ensure volume is up (AudioListener controls master)
            sfxSource.PlayOneShot(buttonSoundClip);
        }
    }

    private void UpdateVolumeLabel(float value)
    {
        if (volumeLabel != null)
        {
            volumeLabel.text = $"Volume: {Mathf.RoundToInt(value * 100)}%";
        }
    }
}
