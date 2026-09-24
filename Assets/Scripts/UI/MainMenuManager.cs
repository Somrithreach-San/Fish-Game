using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro; // Add TextMeshPro Namespace

public class MainMenuManager : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("The name of the game scene to load")]
    [SerializeField] public string gameSceneName = "SampleScene";

    [Header("UI References")]
    [SerializeField] public GameObject mainPanel;
    [SerializeField] public GameObject settingsPanel;
    [SerializeField] public GameObject levelPanel;
    [SerializeField] public Slider volumeSlider;
    [SerializeField] public Text volumeLabel; // Reverted to Text for Legacy UI
    
    [Header("Buttons")]
    [SerializeField] public Button playButton;
    [SerializeField] public Sprite playButtonSprite;
    private static Sprite _cachedPlayButtonSprite;
    [SerializeField] public Button settingsButton;
    [SerializeField] public Sprite settingsButtonSprite;
    private static Sprite _cachedSettingsButtonSprite;
    [SerializeField] public Button quitButton;
    [SerializeField] public Sprite quitButtonSprite;
    private static Sprite _cachedQuitButtonSprite;
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

    [Header("Video Background")]
    [SerializeField] public bool useVideoBackground = false;
    [SerializeField] public VideoClip menuBgVideoClip;
    private VideoPlayer menuVideoPlayer;
    private RenderTexture menuVideoRenderTexture;
    private GameObject videoBgObject;

    [Header("Custom Assets")]
    [SerializeField] public Font customFont; // For lmns1
    [SerializeField] public Sprite buttonShape; // For Knob
    [SerializeField] public Sprite menuModalSprite; // For Main Menu Modal (Menu_Modal.png)
    [SerializeField] public Sprite settingModalSprite; // For Setting Modal (Setting_BG.png)
    [SerializeField] public Sprite closeButtonSprite; // For Close Button (Close_Button.png)
    [SerializeField] public Sprite sliderTrackSprite; // For Slider Track (Xp_Bar.png)
    [SerializeField] public Sprite sliderFillSprite; // For Slider Fill (Xp_bar_fill.png)
    [SerializeField] public Sprite sliderThumbSprite; // For Slider Knob (Thumb_Slider.png)
    [SerializeField] public Sprite musicIconSprite; // For Music Icon (music_icon_setting.png)
    [SerializeField] public Sprite sfxIconSprite; // For SFX Icon (sfx_icon_setting.png)
    [SerializeField] public Sprite menuBackgroundSprite; // For Main Menu background (Menu_Background_V1.png)
    [SerializeField] public Sprite noTextBackgroundSprite; // Fallback background
    [SerializeField] public Sprite levelBackgroundSprite; // For Level selection page (Level_Page_BG.png)
    [SerializeField] public Sprite settingBackgroundSprite; // For Settings page (Setting_Page_BG.png)
    [SerializeField] public Sprite levelSelectionModalSprite; // For Level Selection Modal (Level_Selection_Modal.png)
    [SerializeField] public Sprite[] levelButtonSprites; // For Level_1_Button.png to Level_8_Button.png
    [SerializeField] public Sprite shinyLightSprite; // For Shiny_Light.png aura
    [SerializeField] public Material uiAdditiveMaterial; // For UI_Additive_Material.mat

    public static bool OpenLevelSelectOnLoad = false;

    private void Awake()
    {
        FindReferences();
        SetupMainMenuBackground();
        SetupMainMenuModal();
        RedesignMenuLayout();
        UpdateMenuButtons();
    }

    private void Start()
    {
        // 1. Initialize Audio Settings and ensure AudioListener is active in Scene
        AudioSettingsManager.InitializeAudio();
        AudioListener.pause = false;
        if (FindFirstObjectByType<AudioListener>() == null)
        {
            Camera cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
            else gameObject.AddComponent<AudioListener>();
        }

        // Find References
        FindReferences();

        // Setup Audio Source for SFX
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.volume = AudioSettingsManager.SfxVolume;
        AudioSettingsManager.RouteToSfx(sfxSource);

        // Auto-find or create Music Source and ensure clip is loaded
        EnsureMusicSource();

        AudioSettingsManager.OnMusicSettingChanged += HandleMusicSettingChanged;
        AudioSettingsManager.OnMusicVolumeChanged += HandleMusicVolumeChanged;
        ApplyMenuMusicState(AudioSettingsManager.IsMusicEnabled);

        // Setup Listeners and Hover Effects
        SetupButton(playButton, OpenLevelSelect);
        SetupButton(settingsButton, OpenSettings);
        SetupButton(backButton, CloseSettings);
        SetupButton(quitButton, QuitGame);

        // Setup settings and level page controls
        EnsureSettingsControls();
        EnsureLevelPageUI();

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
        if (OpenLevelSelectOnLoad)
        {
            OpenLevelSelectOnLoad = false;
            OpenLevelSelect();
        }
        else
        {
            ShowMain();
        }

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

        // Setup Main Menu Background (Menu_Background_V1.png)
        SetupMainMenuBackground();

        // Setup Main Menu Modal (Menu_Modal.png)
        SetupMainMenuModal();

        // Apply Layout matching mockup (Menu Modal + Vertical Column Buttons)
        RedesignMenuLayout();

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

        // FIX: Autoplay unblock check on Web/Mobile
        if (musicSource != null && AudioSettingsManager.IsMusicEnabled && !musicSource.isPlaying && Time.timeScale > 0f)
        {
            bool inputDetected = false;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) inputDetected = true;
            var ptr = UnityEngine.InputSystem.Pointer.current;
            if (ptr != null && ptr.press.wasPressedThisFrame) inputDetected = true;
            try {
                if (Input.touchCount > 0 || Input.GetMouseButtonDown(0) || Input.anyKeyDown) inputDetected = true;
            } catch { }

            if (inputDetected)
            {
                musicSource.Play();
            }
        }
    }

    private void OnValidate()
    {
        // Allow live updates in Editor safely without SendMessage layout recursion
        #if UNITY_EDITOR
        if (playButtonSprite == null || (!playButtonSprite.name.Equals("Play_Button") && !playButtonSprite.name.Contains("Play_Button")))
        {
            playButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Play_Button.png");
            if (playButtonSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Play_Button.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { playButtonSprite = s; break; }
                    }
                }
            }
        }
        if (quitButtonSprite == null || (!quitButtonSprite.name.Equals("Quit_Button") && !quitButtonSprite.name.Contains("Quit_Button")))
        {
            quitButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Quit_Button.png");
            if (quitButtonSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Quit_Button.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { quitButtonSprite = s; break; }
                    }
                }
            }
        }
        if (settingsButtonSprite == null || !settingsButtonSprite.name.Contains("Setting_Button"))
        {
            settingsButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Setting_Button.png");
            if (settingsButtonSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Setting_Button.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { settingsButtonSprite = s; break; }
                    }
                }
            }
        }
        if (menuModalSprite == null || !menuModalSprite.name.Contains("Menu_Modal"))
        {
            menuModalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Menu_Modal.png");
            if (menuModalSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Menu_Modal.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { menuModalSprite = s; break; }
                    }
                }
            }
        }
        if (settingModalSprite == null || !settingModalSprite.name.Contains("Setting_BG"))
        {
            settingModalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Setting_BG.png");
            if (settingModalSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Setting_BG.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { settingModalSprite = s; break; }
                    }
                }
            }
        }
        if (closeButtonSprite == null || !closeButtonSprite.name.Contains("Close_Button"))
        {
            closeButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Close_Button.png");
            if (closeButtonSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Close_Button.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { closeButtonSprite = s; break; }
                    }
                }
            }
        }
        if (sliderTrackSprite == null || !sliderTrackSprite.name.Contains("Volume_Slider_Track"))
        {
            sliderTrackSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Track.png");
            if (sliderTrackSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Volume_Slider_Track.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { sliderTrackSprite = s; break; }
                    }
                }
            }
        }
        if (sliderFillSprite == null || (!sliderFillSprite.name.Contains("New_Volume_Slider_Fill") && !sliderFillSprite.name.Contains("Volume_Slider_Fill")))
        {
            sliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png");
            if (sliderFillSprite == null)
            {
                sliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Fill.png");
            }
            if (sliderFillSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { sliderFillSprite = s; break; }
                    }
                }
            }
        }
        if (sliderThumbSprite == null || !sliderThumbSprite.name.Contains("Volume_Slider_Thumb"))
        {
            sliderThumbSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Thumb.png");
            if (sliderThumbSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Volume_Slider_Thumb.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { sliderThumbSprite = s; break; }
                    }
                }
            }
        }
        if (musicIconSprite == null || !musicIconSprite.name.Contains("music_icon_setting"))
        {
            musicIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/music_icon_setting.png");
            if (musicIconSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/music_icon_setting.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { musicIconSprite = s; break; }
                    }
                }
            }
        }
        if (sfxIconSprite == null || !sfxIconSprite.name.Contains("sfx_icon_setting"))
        {
            sfxIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/sfx_icon_setting.png");
            if (sfxIconSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/sfx_icon_setting.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { sfxIconSprite = s; break; }
                    }
                }
            }
        }
        if (menuBackgroundSprite == null || !menuBackgroundSprite.name.Contains("Menu_Background_V1"))
        {
            menuBackgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Backgrounds/Menu_Background_V1.png");
            if (menuBackgroundSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/Backgrounds/Menu_Background_V1.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { menuBackgroundSprite = s; break; }
                    }
                }
            }
        }
        if (levelSelectionModalSprite == null || !levelSelectionModalSprite.name.Contains("Level_Selection_Modal"))
        {
            levelSelectionModalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Level_Selection_Modal.png");
            if (levelSelectionModalSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Level_Selection_Modal.png");
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Sprite s) { levelSelectionModalSprite = s; break; }
                    }
                }
            }
        }
        if (levelButtonSprites == null || levelButtonSprites.Length < 8)
        {
            levelButtonSprites = new Sprite[8];
            for (int i = 1; i <= 8; i++)
            {
                levelButtonSprites[i - 1] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Graphics/GUI Components/Level_{i}_Button.png");
                if (levelButtonSprites[i - 1] == null)
                {
                    var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"Assets/Graphics/GUI Components/Level_{i}_Button.png");
                    if (assets != null)
                    {
                        for (int j = 0; j < assets.Length; j++)
                        {
                            if (assets[j] is Sprite s) { levelButtonSprites[i - 1] = s; break; }
                        }
                    }
                }
            }
        }
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            _cachedSettingModalSprite = null;
            _cachedCloseButtonSprite = null;
            _cachedMenuModalSprite = null;
            _cachedSliderTrackSprite = null;
            _cachedSliderFillSprite = null;
            _cachedSliderThumbSprite = null;
            _cachedMusicIconSprite = null;
            _cachedSfxIconSprite = null;
            _cachedLevelButtonSprites = null;
            _cachedShinyLightSprite = null;
            _cachedUIAdditiveMat = null;
            _cachedOceanSelectionBgSprite = null;
            _cachedLakeSelectionBgSprite = null;
            _cachedCoralCoastModalSprite = null;
            _cachedLostLakeModalSprite = null;
            _cachedArrowLeftSprite = null;
            _cachedArrowRightSprite = null;
            _cachedEmptyPaginationSprite = null;
            _cachedFilledPaginationSprite = null;
            FindReferences();
            FormatCloseButton(backButton);
            SetupMainMenuBackground();
            SetupMainMenuModal();
            SetupSettingsBackground();
            SetupSettingsModal();
            EnsureSettingsControls();
            SetupLevelSelectionModal();
            EnsureLevelPageUI();
            RedesignMenuLayout();
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

    private Sprite GetPlayButtonSprite()
    {
        if (playButtonSprite != null && (playButtonSprite.name.Equals("Play_Button") || playButtonSprite.name.Contains("Play_Button")))
            return playButtonSprite;
        if (_cachedPlayButtonSprite != null) return _cachedPlayButtonSprite;

#if UNITY_EDITOR
        _cachedPlayButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Play_Button.png");
        if (_cachedPlayButtonSprite != null) return _cachedPlayButtonSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Play_Button.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedPlayButtonSprite = s; return _cachedPlayButtonSprite; }
            }
        }
#endif

        _cachedPlayButtonSprite = Resources.Load<Sprite>("Play_Button");
        if (_cachedPlayButtonSprite != null) return _cachedPlayButtonSprite;

        Sprite[] allRes = Resources.LoadAll<Sprite>("Play_Button");
        if (allRes != null && allRes.Length > 0)
        {
            _cachedPlayButtonSprite = allRes[0];
            return _cachedPlayButtonSprite;
        }

        // Fallbacks to older names if needed
        _cachedPlayButtonSprite = Resources.Load<Sprite>("Play_Button_EN");
        if (_cachedPlayButtonSprite != null) return _cachedPlayButtonSprite;

        _cachedPlayButtonSprite = Resources.Load<Sprite>("Play_Button_KH");
        if (_cachedPlayButtonSprite != null) return _cachedPlayButtonSprite;

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < all.Length; i++)
        {
            Sprite s = all[i];
            if (s != null && (s.name.Equals("Play_Button") || s.name.Contains("Play_Button_EN") || s.name.Contains("Play_Button_KH")))
            {
                _cachedPlayButtonSprite = s;
                return _cachedPlayButtonSprite;
            }
        }

        return playButtonSprite;
    }

    private Sprite GetQuitButtonSprite()
    {
        if (quitButtonSprite != null && (quitButtonSprite.name.Equals("Quit_Button") || quitButtonSprite.name.Contains("Quit_Button")))
            return quitButtonSprite;
        if (_cachedQuitButtonSprite != null) return _cachedQuitButtonSprite;

#if UNITY_EDITOR
        _cachedQuitButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Quit_Button.png");
        if (_cachedQuitButtonSprite != null) return _cachedQuitButtonSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Quit_Button.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedQuitButtonSprite = s; return _cachedQuitButtonSprite; }
            }
        }
#endif

        _cachedQuitButtonSprite = Resources.Load<Sprite>("Quit_Button");
        if (_cachedQuitButtonSprite != null) return _cachedQuitButtonSprite;

        Sprite[] allRes = Resources.LoadAll<Sprite>("Quit_Button");
        if (allRes != null && allRes.Length > 0)
        {
            _cachedQuitButtonSprite = allRes[0];
            return _cachedQuitButtonSprite;
        }

        _cachedQuitButtonSprite = Resources.Load<Sprite>("Quit_Button_EN");
        if (_cachedQuitButtonSprite != null) return _cachedQuitButtonSprite;

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < all.Length; i++)
        {
            Sprite s = all[i];
            if (s != null && (s.name.Equals("Quit_Button") || s.name.Contains("Quit_Button_EN")))
            {
                _cachedQuitButtonSprite = s;
                return _cachedQuitButtonSprite;
            }
        }

        return quitButtonSprite;
    }

    private Sprite GetSettingsButtonSprite()
    {
        if (settingsButtonSprite != null && settingsButtonSprite.name.Contains("Setting_Button"))
            return settingsButtonSprite;
        if (_cachedSettingsButtonSprite != null) return _cachedSettingsButtonSprite;

#if UNITY_EDITOR
        _cachedSettingsButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Setting_Button.png");
        if (_cachedSettingsButtonSprite != null) return _cachedSettingsButtonSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Setting_Button.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSettingsButtonSprite = s; return _cachedSettingsButtonSprite; }
            }
        }
#endif

        _cachedSettingsButtonSprite = Resources.Load<Sprite>("Setting_Button");
        if (_cachedSettingsButtonSprite != null) return _cachedSettingsButtonSprite;

        Sprite[] allRes = Resources.LoadAll<Sprite>("Setting_Button");
        if (allRes != null && allRes.Length > 0)
        {
            _cachedSettingsButtonSprite = allRes[0];
            return _cachedSettingsButtonSprite;
        }

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < all.Length; i++)
        {
            Sprite s = all[i];
            if (s != null && s.name.Contains("Setting_Button"))
            {
                _cachedSettingsButtonSprite = s;
                return _cachedSettingsButtonSprite;
            }
        }

        return settingsButtonSprite;
    }

    private Sprite _cachedMenuModalSprite;

    public Sprite GetMenuModalSprite()
    {
        if (_cachedMenuModalSprite != null) return _cachedMenuModalSprite;
        if (menuModalSprite != null)
        {
            _cachedMenuModalSprite = menuModalSprite;
            return _cachedMenuModalSprite;
        }

#if UNITY_EDITOR
        _cachedMenuModalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Menu_Modal.png");
        if (_cachedMenuModalSprite != null) return _cachedMenuModalSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Menu_Modal.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedMenuModalSprite = s; return _cachedMenuModalSprite; }
            }
        }
#endif

        // 1. Try Resources
        Sprite res = Resources.Load<Sprite>("Menu_Modal");
        if (res != null)
        {
            _cachedMenuModalSprite = res;
            return _cachedMenuModalSprite;
        }

        // 2. Try finding loaded sprites in memory
        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains("Menu_Modal"))
            {
                _cachedMenuModalSprite = s;
                return _cachedMenuModalSprite;
            }
        }

        // 3. Direct disk loader fallback
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Graphics", "GUI Components", "Menu_Modal.png");
            if (System.IO.File.Exists(path))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    _cachedMenuModalSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    return _cachedMenuModalSprite;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to load runtime Menu_Modal: " + ex.Message);
        }
        return null;
    }

    public void SetupMainMenuModal()
    {
        if (mainPanel == null) return;

        Sprite modalSprite = GetMenuModalSprite();
        if (modalSprite == null) return;

        Transform modalTr = mainPanel.transform.Find("MenuModal");
        GameObject modalObj;
        if (modalTr == null)
        {
            modalObj = new GameObject("MenuModal");
            modalObj.transform.SetParent(mainPanel.transform, false);
            modalTr = modalObj.transform;
        }
        else
        {
            modalObj = modalTr.gameObject;
        }

        // Ensure it is rendered behind the buttons
        modalTr.SetAsFirstSibling();

        RectTransform rt = modalObj.GetComponent<RectTransform>();
        if (rt == null) rt = modalObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(640, 788);

        Image img = modalObj.GetComponent<Image>();
        if (img == null) img = modalObj.AddComponent<Image>();
        img.sprite = modalSprite;
        img.color = Color.white;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private Sprite _cachedSettingModalSprite;

    public Sprite GetSettingModalSprite()
    {
        if (_cachedSettingModalSprite != null) return _cachedSettingModalSprite;
        if (settingModalSprite != null)
        {
            _cachedSettingModalSprite = settingModalSprite;
            return _cachedSettingModalSprite;
        }

#if UNITY_EDITOR
        _cachedSettingModalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Setting_BG.png");
        if (_cachedSettingModalSprite != null) return _cachedSettingModalSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Setting_BG.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSettingModalSprite = s; return _cachedSettingModalSprite; }
            }
        }
#endif

        // 1. Try Resources
        Sprite res = Resources.Load<Sprite>("Setting_BG");
        if (res != null)
        {
            _cachedSettingModalSprite = res;
            return _cachedSettingModalSprite;
        }

        // 2. Try finding loaded sprites in memory
        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains("Setting_BG"))
            {
                _cachedSettingModalSprite = s;
                return _cachedSettingModalSprite;
            }
        }

        // 3. Direct disk loader fallback
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Graphics", "GUI Components", "Setting_BG.png");
            if (System.IO.File.Exists(path))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    _cachedSettingModalSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    return _cachedSettingModalSprite;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to load runtime Setting_BG: " + ex.Message);
        }
        return null;
    }

    public void SetupSettingsModal()
    {
        if (settingsPanel == null) return;

        Sprite modalSprite = GetSettingModalSprite();
        if (modalSprite == null) return;

        Transform modalTr = settingsPanel.transform.Find("SettingModal");
        GameObject modalObj;
        if (modalTr == null)
        {
            modalObj = new GameObject("SettingModal");
            modalObj.transform.SetParent(settingsPanel.transform, false);
            modalTr = modalObj.transform;
        }
        else
        {
            modalObj = modalTr.gameObject;
        }

        // Place right after Background
        Transform bgTr = settingsPanel.transform.Find("Background");
        if (bgTr != null)
        {
            modalTr.SetSiblingIndex(bgTr.GetSiblingIndex() + 1);
        }
        else
        {
            modalTr.SetAsFirstSibling();
        }

        RectTransform rt = modalObj.GetComponent<RectTransform>();
        if (rt == null) rt = modalObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 0);
        // Setting_BG.png is 1522x1033 (aspect ratio 1.473)
        rt.sizeDelta = new Vector2(700, 475);

        Image img = modalObj.GetComponent<Image>();
        if (img == null) img = modalObj.AddComponent<Image>();
        img.sprite = modalSprite;
        img.color = Color.white;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    public static Sprite LoadSpriteFromPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

#if UNITY_EDITOR
        UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(relativePath);
        if (assets != null && assets.Length > 0)
        {
            Sprite bestSprite = null;
            float maxArea = 0f;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s && s != null)
                {
                    float area = s.rect.width * s.rect.height;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestSprite = s;
                    }
                }
            }
            if (bestSprite != null) return bestSprite;
        }

        Sprite direct = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(relativePath);
        if (direct != null) return direct;
#endif

        string filenameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(relativePath);
        Sprite res = Resources.Load<Sprite>(filenameWithoutExt);
        if (res != null) return res;

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        if (all != null && all.Length > 0)
        {
            Sprite bestSprite = null;
            float maxArea = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.Contains(filenameWithoutExt))
                {
                    float area = all[i].rect.width * all[i].rect.height;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestSprite = all[i];
                    }
                }
            }
            if (bestSprite != null) return bestSprite;
        }

        try
        {
            string fullPath = relativePath.StartsWith("Assets") 
                ? System.IO.Path.Combine(Application.dataPath, relativePath.Substring("Assets/".Length))
                : relativePath;
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
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Failed to load disk sprite {relativePath}: {ex.Message}");
        }

        return null;
    }

    private int currentLevelPage = 0; // 0 = Coral Coast (Ocean), 1 = Lost Lake (River)

    private Sprite _cachedOceanSelectionBgSprite;
    public Sprite GetOceanSelectionBgSprite()
    {
        if (_cachedOceanSelectionBgSprite == null)
        {
            _cachedOceanSelectionBgSprite = LoadSpriteFromPath("Assets/Graphics/Backgrounds/Ocean_Level_Selection_BG.png");
        }
        return _cachedOceanSelectionBgSprite;
    }

    private Sprite _cachedLakeSelectionBgSprite;
    public Sprite GetLakeSelectionBgSprite()
    {
        if (_cachedLakeSelectionBgSprite == null)
        {
            _cachedLakeSelectionBgSprite = LoadSpriteFromPath("Assets/Graphics/Backgrounds/Lake_Level_Selection_BG.png");
        }
        return _cachedLakeSelectionBgSprite;
    }

    private Sprite _cachedCoralCoastModalSprite;
    public Sprite GetCoralCoastModalSprite()
    {
        if (_cachedCoralCoastModalSprite == null)
        {
            _cachedCoralCoastModalSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Coral_Coast_Level_Selection_Modal.png");
        }
        return _cachedCoralCoastModalSprite;
    }

    private Sprite _cachedLostLakeModalSprite;
    public Sprite GetLostLakeModalSprite()
    {
        if (_cachedLostLakeModalSprite == null)
        {
            _cachedLostLakeModalSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Lost_Lake_Level_Selection_Modal.png");
            if (_cachedLostLakeModalSprite == null)
            {
                _cachedLostLakeModalSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Lost_Lake_Level_Selectiom_Modal.png");
            }
        }
        return _cachedLostLakeModalSprite;
    }

    private Sprite _cachedArrowLeftSprite;
    public Sprite GetArrowLeftSprite()
    {
        if (_cachedArrowLeftSprite == null)
        {
            _cachedArrowLeftSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Arrow Left_Button.png");
        }
        return _cachedArrowLeftSprite;
    }

    private Sprite _cachedArrowRightSprite;
    public Sprite GetArrowRightSprite()
    {
        if (_cachedArrowRightSprite == null)
        {
            _cachedArrowRightSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Arrow Right_Button.png");
        }
        return _cachedArrowRightSprite;
    }

    private Sprite _cachedEmptyPaginationSprite;
    public Sprite GetEmptyPaginationSprite()
    {
        if (_cachedEmptyPaginationSprite == null)
        {
            _cachedEmptyPaginationSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Empty_Pagination.png");
        }
        return _cachedEmptyPaginationSprite;
    }

    private Sprite _cachedFilledPaginationSprite;
    public Sprite GetFilledPaginationSprite()
    {
        if (_cachedFilledPaginationSprite == null)
        {
            _cachedFilledPaginationSprite = LoadSpriteFromPath("Assets/Graphics/GUI Components/Filled_Pagination.png");
        }
        return _cachedFilledPaginationSprite;
    }

    public Sprite GetLevelSelectionModalSprite()
    {
        return (currentLevelPage == 0) ? GetCoralCoastModalSprite() : GetLostLakeModalSprite();
    }

    private static Sprite[] _cachedLevelButtonSprites;

    public Sprite GetLevelButtonSprite(int level)
    {
        int displayNum = (level <= 8) ? level : (level - 8);
        if (displayNum < 1 || displayNum > 8) return null;
        int idx = displayNum - 1;
        if (_cachedLevelButtonSprites == null || _cachedLevelButtonSprites.Length < 8)
        {
            _cachedLevelButtonSprites = new Sprite[8];
        }

        if (levelButtonSprites != null && levelButtonSprites.Length > idx && levelButtonSprites[idx] != null)
        {
            return levelButtonSprites[idx];
        }

        if (_cachedLevelButtonSprites[idx] != null) return _cachedLevelButtonSprites[idx];

        string assetName = $"Level_{displayNum}_Button";
#if UNITY_EDITOR
        _cachedLevelButtonSprites[idx] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Graphics/GUI Components/{assetName}.png");
        if (_cachedLevelButtonSprites[idx] != null) return _cachedLevelButtonSprites[idx];

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"Assets/Graphics/GUI Components/{assetName}.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedLevelButtonSprites[idx] = s; return _cachedLevelButtonSprites[idx]; }
            }
        }
#endif

        _cachedLevelButtonSprites[idx] = Resources.Load<Sprite>(assetName);
        if (_cachedLevelButtonSprites[idx] != null) return _cachedLevelButtonSprites[idx];

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains(assetName))
            {
                _cachedLevelButtonSprites[idx] = s;
                return _cachedLevelButtonSprites[idx];
            }
        }

        return null;
    }

    private static Sprite _cachedShinyLightSprite;
    public Sprite GetShinyLightSprite()
    {
        if (shinyLightSprite != null) return shinyLightSprite;
        if (_cachedShinyLightSprite != null) return _cachedShinyLightSprite;

#if UNITY_EDITOR
        _cachedShinyLightSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Shiny_Light.png");
        if (_cachedShinyLightSprite != null) return _cachedShinyLightSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Shiny_Light.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedShinyLightSprite = s; return _cachedShinyLightSprite; }
            }
        }
#endif

        _cachedShinyLightSprite = Resources.Load<Sprite>("Shiny_Light");
        if (_cachedShinyLightSprite != null) return _cachedShinyLightSprite;

        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains("Shiny_Light"))
            {
                _cachedShinyLightSprite = s;
                return _cachedShinyLightSprite;
            }
        }

        return null;
    }

    private static Material _cachedUIAdditiveMat;
    public Material GetUIAdditiveMaterial()
    {
        if (uiAdditiveMaterial != null) return uiAdditiveMaterial;
        if (_cachedUIAdditiveMat != null) return _cachedUIAdditiveMat;

#if UNITY_EDITOR
        _cachedUIAdditiveMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Graphics/GUI Components/UI_Additive_Material.mat");
        if (_cachedUIAdditiveMat != null) return _cachedUIAdditiveMat;
#endif

        Shader addShader = Shader.Find("UI/Additive");
        if (addShader == null) addShader = Shader.Find("Particles/Standard Unlit");
        if (addShader == null) addShader = Shader.Find("Sprites/Default");

        if (addShader != null)
        {
            _cachedUIAdditiveMat = new Material(addShader);
        }

        return _cachedUIAdditiveMat;
    }

    public void SetupLevelSelectionModal()
    {
        if (levelPanel == null) return;

        Sprite modalSprite = GetLevelSelectionModalSprite();
        if (modalSprite == null) return;

        Transform modalTr = levelPanel.transform.Find("LevelModal");
        GameObject modalObj;
        if (modalTr == null)
        {
            modalObj = new GameObject("LevelModal");
            modalObj.transform.SetParent(levelPanel.transform, false);
            modalTr = modalObj.transform;
        }
        else
        {
            modalObj = modalTr.gameObject;
        }

        // Place right after Background
        Transform bgTr = levelPanel.transform.Find("Background");
        if (bgTr != null)
        {
            modalTr.SetSiblingIndex(bgTr.GetSiblingIndex() + 1);
        }
        else
        {
            modalTr.SetAsFirstSibling();
        }

        RectTransform rt = modalObj.GetComponent<RectTransform>();
        if (rt == null) rt = modalObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 0);
        // Level_Selection_Modal.png is 15320x10166 (aspect ratio ~1.507)
        rt.sizeDelta = new Vector2(860, 570);

        Image img = modalObj.GetComponent<Image>();
        if (img == null) img = modalObj.AddComponent<Image>();
        img.sprite = modalSprite;
        img.color = Color.white;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private Sprite _cachedCloseButtonSprite;

    public Sprite GetCloseButtonSprite()
    {
        if (_cachedCloseButtonSprite != null) return _cachedCloseButtonSprite;
        if (closeButtonSprite != null)
        {
            _cachedCloseButtonSprite = closeButtonSprite;
            return _cachedCloseButtonSprite;
        }

#if UNITY_EDITOR
        _cachedCloseButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Close_Button.png");
        if (_cachedCloseButtonSprite != null) return _cachedCloseButtonSprite;

        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Close_Button.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedCloseButtonSprite = s; return _cachedCloseButtonSprite; }
            }
        }
#endif

        // 1. Try Resources
        Sprite res = Resources.Load<Sprite>("Close_Button");
        if (res != null)
        {
            _cachedCloseButtonSprite = res;
            return _cachedCloseButtonSprite;
        }

        // 2. Try finding loaded sprites in memory
        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains("Close_Button"))
            {
                _cachedCloseButtonSprite = s;
                return _cachedCloseButtonSprite;
            }
        }

        // 3. Direct disk loader fallback
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Graphics", "GUI Components", "Close_Button.png");
            if (System.IO.File.Exists(path))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    _cachedCloseButtonSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    return _cachedCloseButtonSprite;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to load runtime Close_Button: " + ex.Message);
        }
        return null;
    }

    private Sprite _cachedSliderTrackSprite;
    public Sprite GetSliderTrackSprite()
    {
        if (_cachedSliderTrackSprite != null) return _cachedSliderTrackSprite;
        if (sliderTrackSprite != null)
        {
            _cachedSliderTrackSprite = sliderTrackSprite;
            return _cachedSliderTrackSprite;
        }
#if UNITY_EDITOR
        _cachedSliderTrackSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Track.png");
        if (_cachedSliderTrackSprite != null) return _cachedSliderTrackSprite;
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Volume_Slider_Track.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSliderTrackSprite = s; return _cachedSliderTrackSprite; }
            }
        }
#endif
        Sprite res = Resources.Load<Sprite>("Volume_Slider_Track");
        if (res != null) { _cachedSliderTrackSprite = res; return _cachedSliderTrackSprite; }
        res = Resources.Load<Sprite>("Xp_Bar");
        if (res != null) { _cachedSliderTrackSprite = res; return _cachedSliderTrackSprite; }
        return null;
    }

    private Sprite _cachedSliderFillSprite;
    public Sprite GetSliderFillSprite()
    {
        if (_cachedSliderFillSprite != null) return _cachedSliderFillSprite;
        if (sliderFillSprite != null)
        {
            _cachedSliderFillSprite = sliderFillSprite;
            return _cachedSliderFillSprite;
        }
#if UNITY_EDITOR
        _cachedSliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png");
        if (_cachedSliderFillSprite == null)
            _cachedSliderFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Fill.png");
        if (_cachedSliderFillSprite != null) return _cachedSliderFillSprite;
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/New_Volume_Slider_Fill.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSliderFillSprite = s; return _cachedSliderFillSprite; }
            }
        }
#endif
        Sprite res = Resources.Load<Sprite>("New_Volume_Slider_Fill");
        if (res != null) { _cachedSliderFillSprite = res; return _cachedSliderFillSprite; }
        res = Resources.Load<Sprite>("Volume_Slider_Fill");
        if (res != null) { _cachedSliderFillSprite = res; return _cachedSliderFillSprite; }
        res = Resources.Load<Sprite>("Xp_bar_fill");
        if (res != null) { _cachedSliderFillSprite = res; return _cachedSliderFillSprite; }
        return null;
    }

    private Sprite _cachedSliderThumbSprite;
    public Sprite GetSliderThumbSprite()
    {
        if (_cachedSliderThumbSprite != null) return _cachedSliderThumbSprite;
        if (sliderThumbSprite != null)
        {
            _cachedSliderThumbSprite = sliderThumbSprite;
            return _cachedSliderThumbSprite;
        }
#if UNITY_EDITOR
        _cachedSliderThumbSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/Volume_Slider_Thumb.png");
        if (_cachedSliderThumbSprite != null) return _cachedSliderThumbSprite;
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/Volume_Slider_Thumb.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSliderThumbSprite = s; return _cachedSliderThumbSprite; }
            }
        }
#endif
        Sprite res = Resources.Load<Sprite>("Volume_Slider_Thumb");
        if (res != null) { _cachedSliderThumbSprite = res; return _cachedSliderThumbSprite; }
        res = Resources.Load<Sprite>("Thumb_Slider");
        if (res != null) { _cachedSliderThumbSprite = res; return _cachedSliderThumbSprite; }
        return null;
    }

    private Sprite _cachedMusicIconSprite;
    public Sprite GetMusicIconSprite()
    {
        if (_cachedMusicIconSprite != null) return _cachedMusicIconSprite;
        if (musicIconSprite != null)
        {
            _cachedMusicIconSprite = musicIconSprite;
            return _cachedMusicIconSprite;
        }
#if UNITY_EDITOR
        _cachedMusicIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/music_icon_setting.png");
        if (_cachedMusicIconSprite != null) return _cachedMusicIconSprite;
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/music_icon_setting.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedMusicIconSprite = s; return _cachedMusicIconSprite; }
            }
        }
#endif
        Sprite res = Resources.Load<Sprite>("music_icon_setting");
        if (res != null) { _cachedMusicIconSprite = res; return _cachedMusicIconSprite; }
        return null;
    }

    private Sprite _cachedSfxIconSprite;
    public Sprite GetSfxIconSprite()
    {
        if (_cachedSfxIconSprite != null) return _cachedSfxIconSprite;
        if (sfxIconSprite != null)
        {
            _cachedSfxIconSprite = sfxIconSprite;
            return _cachedSfxIconSprite;
        }
#if UNITY_EDITOR
        _cachedSfxIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/GUI Components/sfx_icon_setting.png");
        if (_cachedSfxIconSprite != null) return _cachedSfxIconSprite;
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Graphics/GUI Components/sfx_icon_setting.png");
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite s) { _cachedSfxIconSprite = s; return _cachedSfxIconSprite; }
            }
        }
#endif
        Sprite res = Resources.Load<Sprite>("sfx_icon_setting");
        if (res != null) { _cachedSfxIconSprite = res; return _cachedSfxIconSprite; }
        return null;
    }

    public void UpdateMenuButtons()
    {
        // Apply Bubble_Button styling to back button
        Sprite bubble = GetButtonSprite();
        ApplyBubbleStyle(backButton, bubble);

        Font activeFont = customFont != null ? customFont : Resources.Load<Font>("lmns1");

        // 1. Play Button (uses custom Play_Button_EN artwork if available, otherwise bubble style)
        Sprite playSp = GetPlayButtonSprite();
        if (playButton != null)
        {
            if (playSp != null)
            {
                Image playImg = playButton.GetComponent<Image>();
                if (playImg != null)
                {
                    playImg.sprite = playSp;
                    playImg.color = Color.white;
                    playImg.preserveAspect = true;
                }
                UpdateButtonText(playButton, "", activeFont, 75);
                Text t = playButton.GetComponentInChildren<Text>();
                if (t != null) t.gameObject.SetActive(false);
                TextMeshProUGUI tmp = playButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.gameObject.SetActive(false);
            }
            else
            {
                ApplyBubbleStyle(playButton, bubble);
                if (activeFont != null) UpdateButtonText(playButton, "elg", activeFont, 75);
            }
        }

        // 2. Quit Button (uses custom Quit_Button_EN artwork if available, otherwise bubble style)
        Sprite quitSp = GetQuitButtonSprite();
        if (quitButton != null)
        {
            if (quitSp != null)
            {
                Image quitImg = quitButton.GetComponent<Image>();
                if (quitImg != null)
                {
                    quitImg.sprite = quitSp;
                    quitImg.color = Color.white;
                    quitImg.preserveAspect = true;
                }
                UpdateButtonText(quitButton, "", activeFont, 75);
                Text t = quitButton.GetComponentInChildren<Text>();
                if (t != null) t.gameObject.SetActive(false);
                TextMeshProUGUI tmp = quitButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.gameObject.SetActive(false);
            }
            else
            {
                ApplyBubbleStyle(quitButton, bubble);
                if (activeFont != null) UpdateButtonText(quitButton, "ecj", activeFont, 75);
            }
        }

        // 3. Settings Button (uses custom Setting_Button artwork if available, otherwise bubble style)
        Sprite settingSp = GetSettingsButtonSprite();
        if (settingsButton != null)
        {
            if (settingSp != null)
            {
                Image setImg = settingsButton.GetComponent<Image>();
                if (setImg != null)
                {
                    setImg.sprite = settingSp;
                    setImg.color = Color.white;
                    setImg.preserveAspect = true;
                }
                UpdateButtonText(settingsButton, "", activeFont, 75);
                Text t = settingsButton.GetComponentInChildren<Text>();
                if (t != null) t.gameObject.SetActive(false);
                TextMeshProUGUI tmp = settingsButton.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.gameObject.SetActive(false);
            }
            else
            {
                ApplyBubbleStyle(settingsButton, bubble);
                if (activeFont != null) UpdateButtonText(settingsButton, "kMNt;", activeFont, 75);
            }
        }

        if (activeFont == null) return;

        // 4. Back/Close Button Text -> "X"
        if (backButton != null)
        {
            UpdateCloseButtonText(backButton);
        }

        // Refresh settings UI text
        UpdateAudioSettingsUI();
    }

    private void UpdateButtonText(Button btn, string newText, Font font, int targetFontSize = 75)
    {
        if (btn == null) return;

        // Try Legacy Text
        Text txt = btn.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.font = font;
            txt.text = newText;
            txt.color = Color.white;
            txt.fontSize = targetFontSize;
        }
        
        // Try TMP (If used)
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = newText;
            tmp.color = Color.white;
            tmp.fontSize = targetFontSize;
        }
    }

    public void RedesignMenuLayout()
    {
        // Layout matching user reference: Modal in Center with Play, Setting, Quit stacked vertically
        Vector2 buttonSize = new Vector2(280, 158); // Aspect: ~1.777

        // Ensure modal is present
        SetupMainMenuModal();

        // Ensure buttons are active
        if (playButton != null) playButton.gameObject.SetActive(true);
        if (settingsButton != null) settingsButton.gameObject.SetActive(true);
        if (quitButton != null) quitButton.gameObject.SetActive(true);

        // 1. Play Button (Top)
        if (playButton != null)
        {
            Sprite playSp = GetPlayButtonSprite();
            CustomizeButton(playButton, buttonSize, Color.white);
            if (playSp != null)
            {
                Image pImg = playButton.GetComponent<Image>();
                if (pImg != null)
                {
                    pImg.sprite = playSp;
                    pImg.color = Color.white;
                    pImg.preserveAspect = true;
                }
            }
            Text t = playButton.GetComponentInChildren<Text>();
            if (t != null) t.gameObject.SetActive(false);
            TextMeshProUGUI tmp = playButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.gameObject.SetActive(false);

            PositionButton(playButton, new Vector2(0, 95));
        }

        // 2. Settings Button (Middle)
        if (settingsButton != null)
        {
            Sprite settingSp = GetSettingsButtonSprite();
            CustomizeButton(settingsButton, buttonSize, Color.white);
            if (settingSp != null)
            {
                Image sImg = settingsButton.GetComponent<Image>();
                if (sImg != null)
                {
                    sImg.sprite = settingSp;
                    sImg.color = Color.white;
                    sImg.preserveAspect = true;
                }
            }
            Text t = settingsButton.GetComponentInChildren<Text>();
            if (t != null) t.gameObject.SetActive(false);
            TextMeshProUGUI tmp = settingsButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.gameObject.SetActive(false);

            PositionButton(settingsButton, new Vector2(0, -25));
        }

        // 3. Quit Button (Bottom)
        if (quitButton != null)
        {
            Sprite quitSp = GetQuitButtonSprite();
            CustomizeButton(quitButton, buttonSize, Color.white);
            if (quitSp != null)
            {
                Image qImg = quitButton.GetComponent<Image>();
                if (qImg != null)
                {
                    qImg.sprite = quitSp;
                    qImg.color = Color.white;
                    qImg.preserveAspect = true;
                }
            }
            Text t = quitButton.GetComponentInChildren<Text>();
            if (t != null) t.gameObject.SetActive(false);
            TextMeshProUGUI tmp = quitButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.gameObject.SetActive(false);

            PositionButton(quitButton, new Vector2(0, -145));
        }
    }

    private void CustomizeButton(Button btn, Vector2 size, Color color)
    {
        if (btn == null) return;
        btn.transition = Selectable.Transition.None;
        
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
            btn.transition = Selectable.Transition.None;
            btn.onClick.RemoveAllListeners(); // Clean slate
            btn.onClick.AddListener(action);
            btn.onClick.AddListener(PlayButtonSound);
            
            ButtonHoverEffect bhe = btn.gameObject.GetComponent<ButtonHoverEffect>();
            if (bhe != null) Destroy(bhe);
        }
    }

    public void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (levelPanel != null) levelPanel.SetActive(false);
    }

    public void OpenLevelSelect()
    {
        currentLevelPage = 0; // Default to Coral Coast (Ocean)
        if (mainPanel != null) mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (levelPanel != null)
        {
            levelPanel.SetActive(true);
            EnsureLevelPageUI();
        }
    }

    public void CloseLevelSelect()
    {
        if (levelPanel != null) levelPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
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
        if (levelPanel != null) levelPanel.SetActive(false);
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

    private void HandleMusicVolumeChanged(float vol)
    {
        ApplyMenuMusicState(AudioSettingsManager.IsMusicEnabled);
        UpdateAudioSettingsUI();
    }

    public AudioClip GetMenuMusicClip()
    {
        #if UNITY_EDITOR
        AudioClip clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/menu_theme.ogg");
        if (clip != null) return clip;
        clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/music_theme.mp3");
        if (clip != null) return clip;
        #endif
        AudioClip res = Resources.Load<AudioClip>("menu_theme");
        if (res != null) return res;
        res = Resources.Load<AudioClip>("music_theme");
        return res;
    }

    public void EnsureMusicSource()
    {
        if (musicSource == null)
        {
            MenuMusic mm = FindFirstObjectByType<MenuMusic>();
            if (mm != null) musicSource = mm.GetComponent<AudioSource>();
            if (musicSource == null)
            {
                GameObject mPlayer = GameObject.Find("MenuMusicPlayer");
                if (mPlayer != null) musicSource = mPlayer.GetComponent<AudioSource>();
            }
            if (musicSource == null)
            {
                musicSource = gameObject.GetComponent<AudioSource>();
                if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (musicSource != null)
        {
            if (musicSource.clip == null)
            {
                musicSource.clip = GetMenuMusicClip();
            }
            musicSource.loop = true;
            musicSource.playOnAwake = true;
            musicSource.spatialBlend = 0f;
            AudioSettingsManager.RouteToMusic(musicSource);
        }
    }

    public void ApplyMenuMusicState(bool isEnabled)
    {
        EnsureMusicSource();

        bool shouldPlay = isEnabled && AudioSettingsManager.MusicVolume > 0.001f;
        float targetVolume = shouldPlay ? AudioSettingsManager.MusicVolume * defaultMenuMusicVolume : 0f;

        // 1. Control MainMenuManager's musicSource
        if (musicSource != null)
        {
            musicSource.mute = !shouldPlay;
            musicSource.volume = targetVolume;
            if (!shouldPlay)
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
                    s.mute = !shouldPlay;
                    s.volume = shouldPlay ? AudioSettingsManager.MusicVolume * 0.7f : 0f;
                    if (!shouldPlay)
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
        AudioSettingsManager.OnMusicVolumeChanged -= HandleMusicVolumeChanged;

        if (menuVideoRenderTexture != null)
        {
            menuVideoRenderTexture.Release();
            Destroy(menuVideoRenderTexture);
            menuVideoRenderTexture = null;
        }
    }

    private void EnsureSettingsControls()
    {
        if (settingsPanel == null) return;

        // Ensure Settings Panel uses the Menu_Background_V1 background
        SetupSettingsBackground();

        // Ensure Settings Modal (Setting_BG.png) is created and positioned
        SetupSettingsModal();

        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");

        // Hide ControlsInfo so Settings panel is clean and focused
        Transform controls = settingsPanel.transform.Find("ControlsInfo");
        if (controls != null) controls.gameObject.SetActive(false);

        // Hide Volume Slider and Label completely
        if (volumeSlider != null) volumeSlider.gameObject.SetActive(false);
        if (volumeLabel != null) volumeLabel.gameObject.SetActive(false);
        Transform vSliderTr = settingsPanel.transform.Find("VolumeSlider");
        if (vSliderTr != null) vSliderTr.gameObject.SetActive(false);
        Transform vLabelTr = settingsPanel.transform.Find("VolumeLabel");
        if (vLabelTr != null) vLabelTr.gameObject.SetActive(false);

        // Format Back/Close Button as top-right 'X' close button
        if (backButton == null && settingsPanel != null)
        {
            Transform bTr = settingsPanel.transform.Find("BackButton");
            if (bTr == null) bTr = settingsPanel.transform.Find("CloseButton");
            if (bTr != null) backButton = bTr.GetComponent<Button>();
        }
        if (backButton == null && settingsPanel != null)
        {
            GameObject bObj = new GameObject("BackButton");
            bObj.transform.SetParent(settingsPanel.transform, false);
            backButton = bObj.AddComponent<Button>();
            SetupButton(backButton, CloseSettings);
        }
        if (backButton != null)
        {
            backButton.gameObject.SetActive(true);
            FormatCloseButton(backButton);
            backButton.transform.SetAsLastSibling();
        }

        // Hide sound effect and music background labels and toggle buttons per user request
        Transform existingMusicRow = settingsPanel.transform.Find("MusicRow");
        if (existingMusicRow != null)
        {
            existingMusicRow.gameObject.SetActive(false);
        }

        Transform existingSfxRow = settingsPanel.transform.Find("SfxRow");
        if (existingSfxRow != null)
        {
            existingSfxRow.gameObject.SetActive(false);
        }

        Transform existingLangRow = settingsPanel.transform.Find("LanguageRow");
        if (existingLangRow != null)
        {
            existingLangRow.gameObject.SetActive(false);
        }

        // Create Custom Music and SFX Volume Sliders aligned directly beside the embedded icons in Setting_BG
        CreateCustomSliderRow("MusicSliderRow", new Vector2(15f, 20f), AudioSettingsManager.MusicVolume, (val) =>
        {
            AudioSettingsManager.MusicVolume = val;
            if (musicSource != null)
            {
                musicSource.mute = val <= 0.001f;
                musicSource.volume = val * defaultMenuMusicVolume;
                if (val <= 0.001f) musicSource.Pause();
                else if (!musicSource.isPlaying) musicSource.Play();
                else musicSource.UnPause();
            }
            ApplyMenuMusicState(AudioSettingsManager.IsMusicEnabled);
        });

        CreateCustomSliderRow("SfxSliderRow", new Vector2(15f, -63f), AudioSettingsManager.SfxVolume, (val) =>
        {
            AudioSettingsManager.SfxVolume = val;
            if (sfxSource != null)
            {
                sfxSource.mute = val <= 0.001f;
                sfxSource.volume = val;
            }
        });
    }

    public Sprite GetMenuBackgroundSprite()
    {
        return GetOceanSelectionBgSprite();
    }

    private void EnsureMenuVideoClip()
    {
#if UNITY_EDITOR
        if (menuBgVideoClip == null)
        {
            menuBgVideoClip = UnityEditor.AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Graphics/Backgrounds/Menu_BG_Loop.mp4");
        }
#endif
    }

    public Canvas GetMainMenuCanvas()
    {
        if (mainPanel != null)
        {
            Canvas c = mainPanel.GetComponentInParent<Canvas>();
            if (c != null && c.gameObject.name != "CheatTestingMenu_Manager") return c;
        }
        Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in allCanvases)
        {
            if (c != null && c.gameObject.name == "MainMenuCanvas") return c;
        }
        foreach (var c in allCanvases)
        {
            if (c != null && c.gameObject.name != "CheatTestingMenu_Manager") return c;
        }
        return FindFirstObjectByType<Canvas>();
    }

    public void SetupMenuVideoBackground()
    {
        EnsureMenuVideoClip();

        // 0. Clean up any accidental VideoBackground on CheatTestingMenu canvas
        RawImage[] allRaws = FindObjectsByType<RawImage>(FindObjectsSortMode.None);
        foreach (var r in allRaws)
        {
            if (r != null && r.gameObject.name == "VideoBackground")
            {
                Canvas parentCanvas = r.GetComponentInParent<Canvas>();
                if (parentCanvas != null && parentCanvas.gameObject.name == "CheatTestingMenu_Manager")
                {
                    if (Application.isPlaying) Destroy(r.gameObject);
                    else DestroyImmediate(r.gameObject);
                }
            }
        }

        // 1. Find MainMenu root canvas
        Canvas canvas = GetMainMenuCanvas();
        Transform rootTr = (canvas != null) ? canvas.transform : ((mainPanel != null && mainPanel.transform.parent != null) ? mainPanel.transform.parent : transform);

        // 2. Setup Static Fallback Background at Sibling 0 (so there is NEVER a grey screen while video loads)
        SetupStaticFallbackBackground(rootTr);

        // 3. Find or create VideoBackground GameObject at Sibling 1
        Transform existingVideoTr = rootTr.Find("VideoBackground");
        if (existingVideoTr != null)
        {
            videoBgObject = existingVideoTr.gameObject;
        }
        else
        {
            videoBgObject = new GameObject("VideoBackground");
            videoBgObject.transform.SetParent(rootTr, false);
        }

        // Place VideoBackground directly above static Background (Sibling 1)
        videoBgObject.transform.SetSiblingIndex(1);

        // 4. RectTransform for full-screen stretch
        RectTransform rt = videoBgObject.GetComponent<RectTransform>();
        if (rt == null) rt = videoBgObject.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;

        // 5. RawImage for displaying the RenderTexture
        RawImage rawImg = videoBgObject.GetComponent<RawImage>();
        if (rawImg == null) rawImg = videoBgObject.AddComponent<RawImage>();
        rawImg.raycastTarget = false;
        
        // Start transparent so static crisp artwork shows instantly until video starts decoding
        if (Application.isPlaying)
        {
            rawImg.color = Color.clear;
        }
        else
        {
            rawImg.color = Color.white;
        }

        // 6. AspectRatioFitter for seamless full-screen fill without distortion (EnvelopeParent)
        AspectRatioFitter arf = videoBgObject.GetComponent<AspectRatioFitter>();
        if (arf == null) arf = videoBgObject.AddComponent<AspectRatioFitter>();
        arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        arf.aspectRatio = 16f / 9f;

        // 7. RenderTexture
        if (menuVideoRenderTexture == null)
        {
            menuVideoRenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            menuVideoRenderTexture.name = "Menu_BG_RenderTexture";
            menuVideoRenderTexture.wrapMode = TextureWrapMode.Clamp;
        }
        rawImg.texture = menuVideoRenderTexture;

        // 8. VideoPlayer
        if (menuVideoPlayer == null)
        {
            menuVideoPlayer = videoBgObject.GetComponent<VideoPlayer>();
            if (menuVideoPlayer == null) menuVideoPlayer = videoBgObject.AddComponent<VideoPlayer>();
        }

        menuVideoPlayer.playOnAwake = true;
        menuVideoPlayer.isLooping = true;
        menuVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        menuVideoPlayer.targetTexture = menuVideoRenderTexture;
        menuVideoPlayer.aspectRatio = VideoAspectRatio.FitHorizontally;
        menuVideoPlayer.audioOutputMode = VideoAudioOutputMode.None; // Zero conflict with background music

        if (menuBgVideoClip != null)
        {
            menuVideoPlayer.clip = menuBgVideoClip;
        }
        else
        {
            string videoPath = System.IO.Path.Combine(Application.dataPath, "Graphics", "Backgrounds", "Menu_BG_Loop.mp4");
            if (System.IO.File.Exists(videoPath))
            {
                menuVideoPlayer.url = videoPath;
            }
        }

        if (!menuVideoPlayer.isPlaying)
        {
            menuVideoPlayer.Play();
        }

        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartCoroutine(FadeInVideoBackground(rawImg));
        }
    }

    private System.Collections.IEnumerator FadeInVideoBackground(RawImage rawImg)
    {
        if (rawImg == null || menuVideoPlayer == null) yield break;

        // Wait until video has prepared and began delivering frames
        float timeout = 2.0f;
        while (menuVideoPlayer != null && (!menuVideoPlayer.isPlaying || menuVideoPlayer.time < 0.04f) && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (rawImg == null) yield break;

        // Smooth fast 0.15s blend
        float elapsed = 0f;
        float duration = 0.15f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (rawImg != null)
            {
                float a = Mathf.Clamp01(elapsed / duration);
                rawImg.color = new Color(1f, 1f, 1f, a);
            }
            yield return null;
        }
        if (rawImg != null) rawImg.color = Color.white;
    }

    private void SetupStaticFallbackBackground(Transform rootTr)
    {
        if (rootTr == null) return;

        Transform bgTr = rootTr.Find("Background");
        GameObject bgObj;
        if (bgTr != null)
        {
            bgObj = bgTr.gameObject;
        }
        else
        {
            bgObj = new GameObject("Background");
            bgObj.transform.SetParent(rootTr, false);
            bgTr = bgObj.transform;
        }

        bgTr.SetSiblingIndex(0); // Sibling 0: Lowest layer behind VideoBackground

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        if (rt == null) rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;

        Image img = bgObj.GetComponent<Image>();
        if (img == null) img = bgObj.AddComponent<Image>();
        
        Sprite bgSprite = GetMenuBackgroundSprite();
        if (bgSprite == null) bgSprite = GetNoTextBackgroundSprite();
        if (bgSprite != null) img.sprite = bgSprite;

        img.color = Color.white;
        img.raycastTarget = false;

        AspectRatioFitter arf = bgObj.GetComponent<AspectRatioFitter>();
        if (arf == null) arf = bgObj.AddComponent<AspectRatioFitter>();
        arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        arf.aspectRatio = 16f / 9f;

        bgObj.SetActive(true);
    }

    public void SetupMainMenuBackground()
    {
        // Set solid camera background to ocean navy instead of default grey skybox
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.04f, 0.10f, 0.20f);
        }

        Canvas canvas = GetMainMenuCanvas();
        Transform rootTr = (canvas != null) ? canvas.transform : ((mainPanel != null && mainPanel.transform.parent != null) ? mainPanel.transform.parent : transform);

        if (useVideoBackground)
        {
            SetupMenuVideoBackground();
        }
        else
        {
            // Clean up / hide video background
            if (rootTr != null)
            {
                Transform existingVideoTr = rootTr.Find("VideoBackground");
                if (existingVideoTr != null)
                {
                    if (Application.isPlaying) existingVideoTr.gameObject.SetActive(false);
                    else DestroyImmediate(existingVideoTr.gameObject);
                }
            }
            if (videoBgObject != null)
            {
                if (Application.isPlaying) videoBgObject.SetActive(false);
                else DestroyImmediate(videoBgObject);
                videoBgObject = null;
            }

            SetupStaticFallbackBackground(rootTr);
        }

        // Ensure static panel backgrounds don't block the video/canvas background
        if (mainPanel != null)
        {
            Image panelImg = mainPanel.GetComponent<Image>();
            if (panelImg != null) panelImg.color = Color.clear;

            Transform bgTr = mainPanel.transform.Find("Background");
            if (bgTr != null)
            {
                bgTr.gameObject.SetActive(false);
            }
        }
    }

    private void SetupSettingsBackground()
    {
        SetupPanelBackground(settingsPanel, GetSettingBackgroundSprite());
    }

    public Sprite GetLevelBackgroundSprite()
    {
        return (currentLevelPage == 0) ? GetOceanSelectionBgSprite() : GetLakeSelectionBgSprite();
    }

    public Sprite GetSettingBackgroundSprite()
    {
        return GetOceanSelectionBgSprite();
    }

    public void SwitchLevelPage(int pageIndex)
    {
        currentLevelPage = Mathf.Clamp(pageIndex, 0, 1);
        PlayButtonSound();
        EnsureLevelPageUI();
    }

    private GameObject EnsureUIChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        Transform tr = parent.Find(childName);
        if (tr != null)
        {
            if (tr.GetComponent<RectTransform>() == null)
            {
                // In case a legacy non-RectTransform child existed
                DestroyImmediate(tr.gameObject);
            }
            else
            {
                return tr.gameObject;
            }
        }
        GameObject go = new GameObject(childName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    public void EnsureNavigationControls()
    {
        if (levelPanel == null) return;

        // 1. Left Arrow Button (Visible only on Page 1 / Lost Lake)
        GameObject leftArrowObj = EnsureUIChild(levelPanel.transform, "ArrowLeftButton");
        RectTransform leftRt = leftArrowObj.GetComponent<RectTransform>();
        leftRt.anchorMin = new Vector2(0.5f, 0.5f);
        leftRt.anchorMax = new Vector2(0.5f, 0.5f);
        leftRt.pivot = new Vector2(0.5f, 0.5f);
        leftRt.anchoredPosition = new Vector2(-540f, -48f);
        leftRt.sizeDelta = new Vector2(100f, 100f);
        leftRt.localScale = Vector3.one;

        Image leftImg = leftArrowObj.GetComponent<Image>() ?? leftArrowObj.AddComponent<Image>();
        leftImg.sprite = GetArrowLeftSprite();
        leftImg.preserveAspect = true;
        leftImg.color = Color.white;

        Button leftBtn = leftArrowObj.GetComponent<Button>() ?? leftArrowObj.AddComponent<Button>();
        leftBtn.transition = Selectable.Transition.None;
        leftBtn.onClick.RemoveAllListeners();
        leftBtn.onClick.AddListener(() => SwitchLevelPage(0));

        leftArrowObj.SetActive(currentLevelPage > 0);
        leftArrowObj.transform.SetAsLastSibling();

        // 2. Right Arrow Button (Visible only on Page 0 / Coral Coast)
        GameObject rightArrowObj = EnsureUIChild(levelPanel.transform, "ArrowRightButton");
        RectTransform rightRt = rightArrowObj.GetComponent<RectTransform>();
        rightRt.anchorMin = new Vector2(0.5f, 0.5f);
        rightRt.anchorMax = new Vector2(0.5f, 0.5f);
        rightRt.pivot = new Vector2(0.5f, 0.5f);
        rightRt.anchoredPosition = new Vector2(540f, -48f);
        rightRt.sizeDelta = new Vector2(100f, 100f);
        rightRt.localScale = Vector3.one;

        Image rightImg = rightArrowObj.GetComponent<Image>() ?? rightArrowObj.AddComponent<Image>();
        rightImg.sprite = GetArrowRightSprite();
        rightImg.preserveAspect = true;
        rightImg.color = Color.white;

        Button rightBtn = rightArrowObj.GetComponent<Button>() ?? rightArrowObj.AddComponent<Button>();
        rightBtn.transition = Selectable.Transition.None;
        rightBtn.onClick.RemoveAllListeners();
        rightBtn.onClick.AddListener(() => SwitchLevelPage(1));

        rightArrowObj.SetActive(currentLevelPage < 1);
        rightArrowObj.transform.SetAsLastSibling();

        // 3. Pagination Dots Container (Bottom Center with generous gap below modal)
        GameObject pagObj = EnsureUIChild(levelPanel.transform, "PaginationContainer");
        RectTransform pagRt = pagObj.GetComponent<RectTransform>();
        pagRt.anchorMin = new Vector2(0.5f, 0.5f);
        pagRt.anchorMax = new Vector2(0.5f, 0.5f);
        pagRt.pivot = new Vector2(0.5f, 0.5f);
        pagRt.anchoredPosition = new Vector2(0f, -330f);
        pagRt.sizeDelta = new Vector2(120f, 32f);
        pagRt.localScale = Vector3.one;
        pagObj.SetActive(true);
        pagObj.transform.SetAsLastSibling();

        // Dot 0 (Coral Coast)
        GameObject dot0Obj = EnsureUIChild(pagObj.transform, "Dot_0");
        RectTransform dot0Rt = dot0Obj.GetComponent<RectTransform>();
        dot0Rt.anchorMin = new Vector2(0.5f, 0.5f);
        dot0Rt.anchorMax = new Vector2(0.5f, 0.5f);
        dot0Rt.pivot = new Vector2(0.5f, 0.5f);
        dot0Rt.anchoredPosition = new Vector2(-18f, 0f);
        dot0Rt.sizeDelta = new Vector2(24f, 24f);
        dot0Rt.localScale = Vector3.one;

        Image dot0Img = dot0Obj.GetComponent<Image>() ?? dot0Obj.AddComponent<Image>();
        dot0Img.sprite = (currentLevelPage == 0) ? GetFilledPaginationSprite() : GetEmptyPaginationSprite();
        dot0Img.preserveAspect = true;
        dot0Img.color = Color.white;

        Button dot0Btn = dot0Obj.GetComponent<Button>() ?? dot0Obj.AddComponent<Button>();
        dot0Btn.transition = Selectable.Transition.None;
        dot0Btn.onClick.RemoveAllListeners();
        dot0Btn.onClick.AddListener(() => SwitchLevelPage(0));
        dot0Obj.SetActive(true);

        // Dot 1 (Lost Lake)
        GameObject dot1Obj = EnsureUIChild(pagObj.transform, "Dot_1");
        RectTransform dot1Rt = dot1Obj.GetComponent<RectTransform>();
        dot1Rt.anchorMin = new Vector2(0.5f, 0.5f);
        dot1Rt.anchorMax = new Vector2(0.5f, 0.5f);
        dot1Rt.pivot = new Vector2(0.5f, 0.5f);
        dot1Rt.anchoredPosition = new Vector2(18f, 0f);
        dot1Rt.sizeDelta = new Vector2(24f, 24f);
        dot1Rt.localScale = Vector3.one;

        Image dot1Img = dot1Obj.GetComponent<Image>() ?? dot1Obj.AddComponent<Image>();
        dot1Img.sprite = (currentLevelPage == 1) ? GetFilledPaginationSprite() : GetEmptyPaginationSprite();
        dot1Img.preserveAspect = true;
        dot1Img.color = Color.white;

        Button dot1Btn = dot1Obj.GetComponent<Button>() ?? dot1Obj.AddComponent<Button>();
        dot1Btn.transition = Selectable.Transition.None;
        dot1Btn.onClick.RemoveAllListeners();
        dot1Btn.onClick.AddListener(() => SwitchLevelPage(1));
        dot1Obj.SetActive(true);
    }

    private Sprite _cachedNoTextSprite;

    public Sprite GetNoTextBackgroundSprite()
    {
        if (_cachedNoTextSprite != null) return _cachedNoTextSprite;
        if (noTextBackgroundSprite != null)
        {
            _cachedNoTextSprite = noTextBackgroundSprite;
            return _cachedNoTextSprite;
        }

#if UNITY_EDITOR
        Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/Backgrounds/Background_No_Text.png");
        if (editorSprite != null)
        {
            _cachedNoTextSprite = editorSprite;
            return _cachedNoTextSprite;
        }
#endif

        // 1. Try Resources
        Sprite res = Resources.Load<Sprite>("Background_No_Text");
        if (res != null)
        {
            _cachedNoTextSprite = res;
            return _cachedNoTextSprite;
        }
        // 2. Try finding loaded sprites in memory
        Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in all)
        {
            if (s != null && s.name.Contains("Background_No_Text"))
            {
                _cachedNoTextSprite = s;
                return _cachedNoTextSprite;
            }
        }
        return null;
    }

    private void SetupPanelBackground(GameObject panel, Sprite customBg = null)
    {
        if (panel == null) return;

        // Clear panel's tint so it doesn't darken or whiten the screen
        Image panelImg = panel.GetComponent<Image>();
        if (panelImg != null)
        {
            panelImg.color = Color.clear;
            panelImg.raycastTarget = true;
        }

        // Ensure Background child exists inside panel
        Transform bgTr = panel.transform.Find("Background");
        GameObject bgObj = null;
        if (bgTr == null)
        {
            bgObj = new GameObject("Background");
            bgObj.transform.SetParent(panel.transform, false);
            bgObj.transform.SetAsFirstSibling(); // Behind all UI controls
        }
        else
        {
            bgObj = bgTr.gameObject;
            bgObj.transform.SetAsFirstSibling();
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        if (rt == null) rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;

        Image img = bgObj.GetComponent<Image>();
        if (img == null) img = bgObj.AddComponent<Image>();

        // If VideoBackground is active, make panel background image transparent so video shows through
        if (useVideoBackground && ((videoBgObject != null && videoBgObject.activeSelf) || menuBgVideoClip != null))
        {
            img.color = Color.clear;
            img.raycastTarget = false;
        }
        else
        {
            Sprite bgSprite = customBg;
            if (bgSprite == null)
            {
                if (panel == levelPanel) bgSprite = GetLevelBackgroundSprite();
                else if (panel == settingsPanel) bgSprite = GetSettingBackgroundSprite();
                else bgSprite = GetNoTextBackgroundSprite();
            }

            if (bgSprite != null)
            {
                img.sprite = bgSprite;
                img.color = Color.white;
                img.type = Image.Type.Simple;
                img.raycastTarget = false;
            }
            else
            {
                img.color = Color.clear;
                img.raycastTarget = false;
            }
        }

        AspectRatioFitter arf = bgObj.GetComponent<AspectRatioFitter>();
        if (arf == null) arf = bgObj.AddComponent<AspectRatioFitter>();
        arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        arf.aspectRatio = 16f / 9f; // 2400 / 1350 = 16:9
    }

    public void EnsureLevelPageUI()
    {
        if (levelPanel == null) return;

        // 1. Background setup (Uses Ocean_Level_Selection_BG / Lake_Level_Selection_BG)
        SetupPanelBackground(levelPanel, GetLevelBackgroundSprite());

        // 2. Level Selection Modal (Uses Coral_Coast_Level_Selection_Modal / Lost_Lake_Level_Selection_Modal)
        SetupLevelSelectionModal();

        // 3. Close 'X' button
        Transform closeBtnTr = levelPanel.transform.Find("CloseButton");
        GameObject closeBtnObj = null;
        if (closeBtnTr == null)
        {
            closeBtnObj = new GameObject("CloseButton");
            closeBtnObj.transform.SetParent(levelPanel.transform, false);
        }
        else
        {
            closeBtnObj = closeBtnTr.gameObject;
        }

        Button closeBtn = closeBtnObj.GetComponent<Button>();
        if (closeBtn == null) closeBtn = closeBtnObj.AddComponent<Button>();
        SetupButton(closeBtn, CloseLevelSelect);
        FormatCloseButton(closeBtn);
        closeBtn.transform.SetAsLastSibling();

        // 4. Level Grid Container (Centered 4x2 grid for 8 levels inside the glass modal viewport)
        Transform gridTr = levelPanel.transform.Find("LevelGrid");
        GameObject gridObj = null;
        if (gridTr == null)
        {
            gridObj = new GameObject("LevelGrid");
            gridObj.transform.SetParent(levelPanel.transform, false);
        }
        else
        {
            gridObj = gridTr.gameObject;
        }

        gridObj.transform.SetAsLastSibling();

        RectTransform rtGrid = gridObj.GetComponent<RectTransform>();
        if (rtGrid == null) rtGrid = gridObj.AddComponent<RectTransform>();
        rtGrid.anchorMin = new Vector2(0.5f, 0.5f);
        rtGrid.anchorMax = new Vector2(0.5f, 0.5f);
        rtGrid.pivot = new Vector2(0.5f, 0.5f);
        rtGrid.anchoredPosition = new Vector2(0f, -48f); // Centered nicely inside the glass area below ribbon
        rtGrid.sizeDelta = new Vector2(600f, 310f); // 2 rows of 4 buttons
        rtGrid.localScale = Vector3.one;

        GridLayoutGroup grid = gridObj.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = gridObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(165f, 165f); // Generous size so visible button core fills the area
        grid.spacing = new Vector2(-26f, -26f); // Snug negative spacing so transparent PNG borders overlap and buttons sit close together
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4; // 4 levels per row (4x2 grid)

        Font limonFont = customFont;
        if (limonFont == null) limonFont = Resources.Load<Font>("lmns1");
        Sprite bubble = GetButtonSprite();
        Sprite shinyLightSp = GetShinyLightSprite();
        Material uiAdditiveMat = GetUIAdditiveMaterial();
        int pageLevelCount = (currentLevelPage == 0) ? LevelManager.OCEAN_LEVELS : LevelManager.LAKE_LEVELS; // 8 for Ocean, 5 for Lake
        int pageLevelOffset = (currentLevelPage == 0) ? 0 : LevelManager.OCEAN_LEVELS; // 0 for Ocean, 8 for Lake

        int activePlayLevel = Mathf.Clamp(LevelManager.HighestUnlockedLevel, 1, LevelManager.TOTAL_LEVELS);

        // Hide any old buttons beyond 8 slots
        for (int i = 9; i <= 16; i++)
        {
            Transform old = gridObj.transform.Find($"LevelBtn_{i}");
            if (old != null) old.gameObject.SetActive(false);
        }

        // 5. Create / Configure 8 static level buttons with LevelManager unlocking and Level_X_Button sprites
        for (int slot = 1; slot <= 8; slot++)
        {
            string btnName = $"LevelBtn_{slot}";
            Transform btnTr = gridObj.transform.Find(btnName);
            GameObject btnObj = null;
            if (btnTr == null)
            {
                btnObj = new GameObject(btnName);
                btnObj.transform.SetParent(gridObj.transform, false);
            }
            else
            {
                btnObj = btnTr.gameObject;
            }

            if (slot > pageLevelCount)
            {
                btnObj.SetActive(false);
                continue;
            }

            btnObj.SetActive(true);

            int globalLevelNum = pageLevelOffset + slot;
            int displayLevelNum = slot;

            RectTransform btnRt = btnObj.GetComponent<RectTransform>();
            if (btnRt == null) btnRt = btnObj.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(165f, 165f);
            btnRt.localScale = Vector3.one;

            Sprite levelBtnSp = GetLevelButtonSprite(displayLevelNum);
            bool isUnlocked = LevelManager.IsLevelUnlocked(globalLevelNum);

            // Button root Image component: clear color so it intercepts raycasts without drawing over child layers
            Image btnImg = btnObj.GetComponent<Image>();
            if (btnImg == null) btnImg = btnObj.AddComponent<Image>();
            btnImg.sprite = null;
            btnImg.color = Color.clear;
            btnImg.raycastTarget = true;

            Button btn = btnObj.GetComponent<Button>();
            if (btn == null) btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.interactable = isUnlocked;

            if (isUnlocked)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(PlayButtonSound);
                btn.onClick.AddListener(() =>
                {
                    LevelManager.CurrentLevel = globalLevelNum;
                    PlayGame();
                });
            }
            else
            {
                btn.onClick.RemoveAllListeners();
            }

            ButtonHoverEffect hover = btnObj.GetComponent<ButtonHoverEffect>();
            if (hover != null) Destroy(hover);

            // Layer 1: Shiny Light Sunburst Aura (Rendered BEHIND icon)
            Transform shinyTr = btnObj.transform.Find("ShinyLight");
            if (globalLevelNum == activePlayLevel && shinyLightSp != null)
            {
                GameObject shinyObj = null;
                if (shinyTr == null)
                {
                    shinyObj = new GameObject("ShinyLight");
                    shinyObj.transform.SetParent(btnObj.transform, false);
                }
                else
                {
                    shinyObj = shinyTr.gameObject;
                }

                shinyObj.layer = levelPanel.layer;
                shinyObj.SetActive(true);
                shinyObj.transform.SetSiblingIndex(0);

                RectTransform sRt = shinyObj.GetComponent<RectTransform>();
                if (sRt == null) sRt = shinyObj.AddComponent<RectTransform>();
                sRt.anchorMin = new Vector2(0.5f, 0.5f);
                sRt.anchorMax = new Vector2(0.5f, 0.5f);
                sRt.pivot = new Vector2(0.5f, 0.5f);
                sRt.anchoredPosition = Vector2.zero;
                sRt.sizeDelta = new Vector2(240f, 240f);
                sRt.localScale = Vector3.one;

                Image sImg = shinyObj.GetComponent<Image>();
                if (sImg == null) sImg = shinyObj.AddComponent<Image>();
                sImg.sprite = shinyLightSp;
                sImg.type = Image.Type.Simple;
                sImg.preserveAspect = true;
                sImg.color = Color.white;
                if (uiAdditiveMat != null) sImg.material = uiAdditiveMat;
                sImg.raycastTarget = false;

                if (shinyObj.GetComponent<Rhinotap.UI.ShinyLightRotator>() == null)
                {
                    shinyObj.AddComponent<Rhinotap.UI.ShinyLightRotator>();
                }
            }
            else if (shinyTr != null)
            {
                shinyTr.gameObject.SetActive(false);
            }

            // Layer 2: Level Button Icon (Rendered in FRONT of ShinyLight)
            Transform iconTr = btnObj.transform.Find("Icon");
            GameObject iconObj = null;
            if (iconTr == null)
            {
                iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
            }
            else
            {
                iconObj = iconTr.gameObject;
            }

            iconObj.layer = levelPanel.layer;
            iconObj.SetActive(true);
            iconObj.transform.SetSiblingIndex(1);

            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            if (iconRt == null) iconRt = iconObj.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(165f, 165f);
            iconRt.localScale = Vector3.one;

            Image iconImg = iconObj.GetComponent<Image>();
            if (iconImg == null) iconImg = iconObj.AddComponent<Image>();
            if (levelBtnSp != null)
            {
                iconImg.sprite = levelBtnSp;
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
            }
            else if (bubble != null)
            {
                iconImg.sprite = bubble;
                iconImg.type = Image.Type.Simple;
            }
            iconImg.color = isUnlocked ? Color.white : new Color(0.70f, 0.70f, 0.75f, 0.65f);
            iconImg.raycastTarget = false;

            // Child Text: Only displayed if button sprite is missing
            Transform textTr = btnObj.transform.Find("Text");
            if (textTr != null)
            {
                textTr.gameObject.SetActive(levelBtnSp == null);
                textTr.SetSiblingIndex(2);
            }
            else if (levelBtnSp == null)
            {
                GameObject textObj = new GameObject("Text");
                textObj.transform.SetParent(btnObj.transform, false);
                textObj.layer = levelPanel.layer;
                textObj.transform.SetSiblingIndex(2);

                Text t = textObj.AddComponent<Text>();
                if (limonFont != null) t.font = limonFont;
                t.text = displayLevelNum.ToString();
                t.fontSize = 28;
                t.fontStyle = FontStyle.Bold;
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.raycastTarget = false;

                RectTransform textRt = textObj.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.sizeDelta = Vector2.zero;
            }
        }

        // 6. Navigation arrows and pagination dots (Topmost UI layer)
        EnsureNavigationControls();
    }

    private void FormatCloseButton(Button btn)
    {
        if (btn == null) return;
        btn.transition = Selectable.Transition.None;

        // 1. RectTransform: Scaled size 130 x 130, Top-Right corner
        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(130f, 130f);
            rt.anchoredPosition = new Vector2(-45f, -45f);
        }

        // 2. Image: Close_Button sprite
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            Sprite closeSp = GetCloseButtonSprite();
            if (closeSp != null)
            {
                img.sprite = closeSp;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            else
            {
                Sprite bubble = GetButtonSprite();
                if (bubble != null)
                {
                    img.sprite = bubble;
                    img.type = Image.Type.Simple;
                }
            }
            img.color = Color.white;
            img.raycastTarget = true;
        }

        // 3. Text Child: Hide text if Close_Button has graphic, otherwise show 'X'
        Sprite activeCloseSp = GetCloseButtonSprite();
        if (activeCloseSp != null)
        {
            Text txt = btn.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                txt.text = "";
                txt.gameObject.SetActive(false);
            }
            TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.text = "";
                tmp.gameObject.SetActive(false);
            }
        }
        else
        {
            UpdateCloseButtonText(btn);
        }

        ButtonHoverEffect bhe = btn.gameObject.GetComponent<ButtonHoverEffect>();
        if (bhe != null) Destroy(bhe);
    }

    private void UpdateCloseButtonText(Button btn)
    {
        if (btn == null) return;

        Font activeFont = customFont != null ? customFont : Resources.Load<Font>("lmns1");
        if (activeFont == null) activeFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        Text txt = btn.GetComponentInChildren<Text>(true);
        if (txt != null)
        {
            txt.font = activeFont;
            txt.text = "X";
            txt.fontSize = 24;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.gameObject.SetActive(true);
        }

        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = "X";
            tmp.fontSize = 24;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.gameObject.SetActive(true);
        }
    }

    private void CreateToggleRow(string rowName, string labelText, int labelFontSize, int statusFontSize, Vector2 position, ref Button toggleBtn, ref Text labelComp, ref Text statusComp, Font activeFont)
    {
        if (settingsPanel == null) return;

        // Cleanup old row
        Transform oldRow = settingsPanel.transform.Find(rowName);
        if (oldRow != null) Destroy(oldRow.gameObject);

        // Row Container
        GameObject row = new GameObject(rowName);
        row.transform.SetParent(settingsPanel.transform, false);
        row.layer = settingsPanel.layer;

        RectTransform rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = position;
        rowRt.sizeDelta = new Vector2(720f, 180f);

        // Label - Left Aligned, clean without stroke
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
        if (activeFont != null) labelComp.font = activeFont;
        labelComp.text = labelText;
        labelComp.fontSize = labelFontSize;
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
        toggleBtn.transition = Selectable.Transition.None;

        ButtonHoverEffect bhe = btnObj.GetComponent<ButtonHoverEffect>();
        if (bhe != null) Destroy(bhe);

        // Text Child
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        txtObj.layer = settingsPanel.layer;
        RectTransform txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.sizeDelta = Vector2.zero;

        statusComp = txtObj.AddComponent<Text>();
        if (activeFont != null) statusComp.font = activeFont;
        statusComp.fontSize = statusFontSize;
        statusComp.fontStyle = FontStyle.Bold;
        statusComp.alignment = TextAnchor.MiddleCenter;
        statusComp.color = Color.white;
        statusComp.horizontalOverflow = HorizontalWrapMode.Overflow;
        statusComp.verticalOverflow = VerticalWrapMode.Overflow;
        statusComp.raycastTarget = false;
    }

    private GameObject CreateCustomSliderRow(string sliderName, Vector2 position, float initialValue, UnityEngine.Events.UnityAction<float> onValueChanged)
    {
        if (settingsPanel == null) return null;

        Transform existing = settingsPanel.transform.Find(sliderName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        Sprite trackSp = GetSliderTrackSprite();
        Sprite fillSp = GetSliderFillSprite();
        Sprite thumbSp = GetSliderThumbSprite();

        // Slider Root - positioned directly beside the embedded icon in Setting_BG
        GameObject sliderObj = new GameObject(sliderName);
        sliderObj.transform.SetParent(settingsPanel.transform, false);
        sliderObj.layer = settingsPanel.layer;
        RectTransform sliderRt = sliderObj.AddComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.anchoredPosition = position;
        sliderRt.sizeDelta = new Vector2(260f, 40f);

        // Background Track (Volume_Slider_Track.png)
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(sliderObj.transform, false);
        bgObj.layer = settingsPanel.layer;
        RectTransform bgRt = bgObj.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        if (trackSp != null) bgImg.sprite = trackSp;
        bgImg.type = Image.Type.Simple;
        bgImg.color = Color.white;
        bgImg.raycastTarget = true;

        // Fill Area (with RectMask2D so the fill reveals smoothly without stretching)
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        fillArea.layer = settingsPanel.layer;
        RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRt.pivot = new Vector2(0f, 0.5f);
        fillAreaRt.anchoredPosition = new Vector2(3f, 0f);
        fillAreaRt.sizeDelta = new Vector2(-6f, 34f);
        fillArea.AddComponent<RectMask2D>();

        // Fill Image (fixed full width inside mask so sprite never distorts or squishes)
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillArea.transform, false);
        fillObj.layer = settingsPanel.layer;
        RectTransform fillRt = fillObj.AddComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0.5f);
        fillRt.anchorMax = new Vector2(0f, 0.5f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(254f, 34f);
        Image fillImg = fillObj.AddComponent<Image>();
        if (fillSp != null) fillImg.sprite = fillSp;
        fillImg.type = Image.Type.Simple;
        fillImg.color = Color.white;
        fillImg.raycastTarget = false;

        // Handle Slide Area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        handleArea.layer = settingsPanel.layer;
        RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
        handleAreaRt.anchorMin = new Vector2(0f, 0f);
        handleAreaRt.anchorMax = new Vector2(1f, 1f);
        handleAreaRt.sizeDelta = new Vector2(-16f, 0f);

        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(handleArea.transform, false);
        handleObj.layer = settingsPanel.layer;
        RectTransform handleRt = handleObj.AddComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(38f, 50f);
        Image handleImg = handleObj.AddComponent<Image>();
        if (thumbSp != null) handleImg.sprite = thumbSp;
        handleImg.type = Image.Type.Simple;
        handleImg.preserveAspect = true;
        handleImg.color = Color.white;
        handleImg.raycastTarget = true;

        // Slider Component
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.fillRect = fillAreaRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = initialValue;
        if (onValueChanged != null)
        {
            slider.onValueChanged.AddListener(onValueChanged);
        }

        return sliderObj;
    }

    public void UpdateAudioSettingsUI()
    {
        Font activeFont = customFont != null ? customFont : Resources.Load<Font>("lmns1");
        int labelFontSize = 100;
        int statusFontSize = 75;

        // Refresh label fonts, font size, and alignment if legacy elements exist
        if (musicLabel != null)
        {
            if (activeFont != null) musicLabel.font = activeFont;
            musicLabel.text = "eP\xf8g";
            musicLabel.alignment = TextAnchor.MiddleLeft;
            musicLabel.fontSize = labelFontSize;
        }
        if (sfxLabel != null)
        {
            if (activeFont != null) sfxLabel.font = activeFont;
            sfxLabel.text = "sMeLg";
            sfxLabel.alignment = TextAnchor.MiddleLeft;
            sfxLabel.fontSize = labelFontSize;
        }

        // Music toggle status
        bool musicOn = AudioSettingsManager.IsMusicEnabled;
        if (musicStatusText != null)
        {
            if (activeFont != null) musicStatusText.font = activeFont;
            musicStatusText.text = musicOn ? "ebIk" : "biT";
            musicStatusText.color = Color.white;
            musicStatusText.fontSize = statusFontSize;
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
            if (activeFont != null) sfxStatusText.font = activeFont;
            sfxStatusText.text = sfxOn ? "ebIk" : "biT";
            sfxStatusText.color = Color.white;
            sfxStatusText.fontSize = statusFontSize;
        }
        if (sfxToggleButton != null)
        {
            Image img = sfxToggleButton.GetComponent<Image>();
            if (img != null)
            {
                img.color = Color.white;
            }
        }

        // Update Slider UI positions if instantiated
        if (settingsPanel != null)
        {
            Transform musicRow = settingsPanel.transform.Find("MusicSliderRow");
            if (musicRow != null)
            {
                Slider s = musicRow.GetComponentInChildren<Slider>();
                if (s != null && !Mathf.Approximately(s.value, AudioSettingsManager.MusicVolume))
                {
                    s.SetValueWithoutNotify(AudioSettingsManager.MusicVolume);
                }
            }

            Transform sfxRow = settingsPanel.transform.Find("SfxSliderRow");
            if (sfxRow != null)
            {
                Slider s = sfxRow.GetComponentInChildren<Slider>();
                if (s != null && !Mathf.Approximately(s.value, AudioSettingsManager.SfxVolume))
                {
                    s.SetValueWithoutNotify(AudioSettingsManager.SfxVolume);
                }
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
        if (!AudioSettingsManager.CanPlayButtonSound()) return;

        if (sfxSource != null && buttonSoundClip != null)
        {
            sfxSource.volume = AudioSettingsManager.SfxVolume;
            sfxSource.PlayOneShot(buttonSoundClip, AudioSettingsManager.SfxVolume);
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
