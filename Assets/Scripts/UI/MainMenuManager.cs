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

    [Header("Audio")]
    [SerializeField] public AudioClip buttonSoundClip;
    [SerializeField] public AudioSource musicSource; // Reference to Music Source
    private AudioSource sfxSource;

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

        // Setup Listeners and Hover Effects
        SetupButton(playButton, PlayGame);
        SetupButton(settingsButton, OpenSettings);
        SetupButton(backButton, CloseSettings);
        SetupButton(quitButton, QuitGame);

        // Initialize Volume
        if (volumeSlider != null)
        {
            float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
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
        // Allow live updates in Editor
        FindReferences();
        UpdateMenuButtons();
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

    private void UpdateMenuButtons()
    {
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
            // Ensure size is good (readable) - Increased to 75 per user request
            if (txt.fontSize < 60) txt.fontSize = 75;
        }
        
        // Try TMP (If used)
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = newText;
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
            txt.color = Color.black; // Ensure readability
        }
        
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14;
            tmp.fontSizeMax = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
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
        // Settings disabled for now per user request
        Debug.Log("Settings button clicked (Menu disabled)");
    }

    public void CloseSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
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
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
        PlayerPrefs.Save();
        UpdateVolumeLabel(value);
    }

    public void PlayButtonSound()
    {
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
