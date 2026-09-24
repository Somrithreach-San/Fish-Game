using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Lightweight, non-intrusive Testing & Cheat Modal for developer testing.
/// Press F1 (or Tilde ~) to toggle on/off.
/// When opened, automatically pauses the game and overlays on top of everything.
/// When closed, smoothly resumes gameplay.
/// </summary>
public class CheatTestingMenu : MonoBehaviour
{
    private static CheatTestingMenu _instance;
    public static CheatTestingMenu Instance => _instance;

    private Canvas canvas;
    private GameObject modalRoot;
    private TextMeshProUGUI statusText;
    private bool isVisible = false;
    private bool didPauseGameOnOpen = false;
    private Coroutine statusClearCoroutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("CheatTestingMenu_Manager");
            _instance = go.AddComponent<CheatTestingMenu>();
            DontDestroyOnLoad(go);
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        BuildMenuUI();
    }

    private void Update()
    {
        // Toggle on F1 or Backquote (~)
        bool f1Pressed = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
        bool tildePressed = Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame;

        if (f1Pressed || tildePressed)
        {
            ToggleMenu();
        }
        else if (isVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetMenuVisible(false);
        }
    }

    public void ToggleMenu()
    {
        SetMenuVisible(!isVisible);
    }

    public void SetMenuVisible(bool visible)
    {
        if (isVisible == visible) return;
        isVisible = visible;

        if (canvas != null)
        {
            canvas.enabled = visible;
        }

        if (modalRoot != null)
        {
            modalRoot.SetActive(visible);
        }

        if (visible)
        {
            // Auto-pause the game on open
            if (GameManager.instance != null && !GameManager.instance.isPaused)
            {
                didPauseGameOnOpen = true;
                GameManager.instance.PlayPause();
            }
            else
            {
                didPauseGameOnOpen = false;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            ShowStatus("Game Paused while Testing Modal is Open. Click any button to spawn.");
        }
        else
        {
            // Auto-unpause the game on close if we paused it on opening
            if (didPauseGameOnOpen && GameManager.instance != null && GameManager.instance.isPaused)
            {
                didPauseGameOnOpen = false;
                GameManager.instance.PlayPause();
            }
        }
    }

    private void BuildMenuUI()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999; // Top-most overlay (in front of Pause modal and game over screens)
        canvas.enabled = isVisible;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster == null) gameObject.AddComponent<GraphicRaycaster>();

        // Modal Root Window
        modalRoot = new GameObject("CheatModalRoot");
        modalRoot.transform.SetParent(transform, false);

        RectTransform rootRt = modalRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(920f, 640f);

        // Background Panel
        Image bg = modalRoot.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.14f, 0.98f); // Frosted deep dark navy

        // Header Panel
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(modalRoot.transform, false);
        RectTransform headerRt = headerObj.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 60f);

        Image headerBg = headerObj.AddComponent<Image>();
        headerBg.color = new Color(0.08f, 0.15f, 0.25f, 0.98f);

        // Title Text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(headerObj.transform, false);
        RectTransform titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(20f, 0f);
        titleRt.offsetMax = new Vector2(-70f, 0f);

        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "🛠️ TESTING & CHEAT MODAL  <size=17><color=#00FFAA>(PAUSED - Press F1 or ~ to Close & Resume)</color></size>";
        titleText.fontSize = 21;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.color = Color.white;

        // Close Button
        CreateButton(headerObj.transform, new Vector2(850f, -30f), new Vector2(48f, 40f), "✕", new Color(0.85f, 0.25f, 0.25f), () => SetMenuVisible(false));

        // Content Area Container
        GameObject contentObj = new GameObject("ContentArea");
        contentObj.transform.SetParent(modalRoot.transform, false);
        RectTransform contentRt = contentObj.AddComponent<RectTransform>();
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.offsetMin = new Vector2(25f, 45f);
        contentRt.offsetMax = new Vector2(-25f, -70f);

        // --- SECTION 1: HAZARDS & CREATURES ---
        CreateCategoryHeader(contentObj.transform, new Vector2(0f, 0f), "⚡ HAZARDS & SPECIAL CREATURES");
        CreateButton(contentObj.transform, new Vector2(100f, -40f), new Vector2(190f, 42f), "🦈 Spawn Shark", new Color(0.20f, 0.40f, 0.70f), OnSpawnSharkClicked);
        CreateButton(contentObj.transform, new Vector2(305f, -40f), new Vector2(190f, 42f), "🦑 Spawn Cuttlefish", new Color(0.55f, 0.25f, 0.65f), OnSpawnCuttlefishClicked);
        CreateButton(contentObj.transform, new Vector2(510f, -40f), new Vector2(190f, 42f), "🎣 Spawn Boat Hazard", new Color(0.65f, 0.45f, 0.20f), OnSpawnBoatClicked);
        CreateButton(contentObj.transform, new Vector2(715f, -40f), new Vector2(190f, 42f), "🦪 Open All Clams", new Color(0.25f, 0.60f, 0.50f), OnOpenClamsClicked);

        // --- SECTION 2: FISH VARIANTS & PREFABS ---
        CreateCategoryHeader(contentObj.transform, new Vector2(0f, -90f), "🐟 FISH VARIANTS & TIERS");
        CreateButton(contentObj.transform, new Vector2(100f, -130f), new Vector2(190f, 42f), "✨ Spawn Golden Fish", new Color(0.85f, 0.70f, 0.15f), () => OnSpawnFishClicked(1, isGolden: true));
        CreateButton(contentObj.transform, new Vector2(305f, -130f), new Vector2(190f, 42f), "🤢 Spawn Sick Fish (Lv2)", new Color(0.35f, 0.65f, 0.25f), () => OnSpawnFishClicked(2, isSick: true));
        CreateButton(contentObj.transform, new Vector2(510f, -130f), new Vector2(190f, 42f), "🐡 Spawn Pufferfish", new Color(0.80f, 0.50f, 0.20f), () => OnSpawnFishClicked(2, isSpiked: true));
        CreateButton(contentObj.transform, new Vector2(715f, -130f), new Vector2(190f, 42f), "🐟 Spawn Lv1 Minnows", new Color(0.20f, 0.50f, 0.60f), () => OnSpawnFishClicked(1));

        CreateButton(contentObj.transform, new Vector2(100f, -180f), new Vector2(190f, 42f), "🐠 Spawn Level 2 Fish", new Color(0.25f, 0.55f, 0.75f), () => OnSpawnFishClicked(2));
        CreateButton(contentObj.transform, new Vector2(305f, -180f), new Vector2(190f, 42f), "🐡 Spawn Level 3 Fish", new Color(0.30f, 0.60f, 0.55f), () => OnSpawnFishClicked(3));
        CreateButton(contentObj.transform, new Vector2(510f, -180f), new Vector2(190f, 42f), "🦈 Spawn Level 4 Fish", new Color(0.40f, 0.45f, 0.70f), () => OnSpawnFishClicked(4));
        CreateButton(contentObj.transform, new Vector2(715f, -180f), new Vector2(190f, 42f), "🐋 Spawn Level 5 Fish", new Color(0.50f, 0.35f, 0.65f), () => OnSpawnFishClicked(5));

        // --- SECTION 3: PLAYER & ABILITY CHEATS ---
        CreateCategoryHeader(contentObj.transform, new Vector2(0f, -230f), "🎮 PLAYER & ABILITY CONTROLS");
        CreateButton(contentObj.transform, new Vector2(100f, -270f), new Vector2(190f, 42f), "⬆️ Level Up (+1)", new Color(0.18f, 0.65f, 0.40f), OnLevelUpClicked);
        CreateButton(contentObj.transform, new Vector2(305f, -270f), new Vector2(190f, 42f), "⬇️ Level Down (-1)", new Color(0.70f, 0.35f, 0.25f), OnLevelDownClicked);
        CreateButton(contentObj.transform, new Vector2(510f, -270f), new Vector2(190f, 42f), "🔥 Max Streak (x10)", new Color(0.95f, 0.45f, 0.15f), OnMaxStreakClicked);
        CreateButton(contentObj.transform, new Vector2(715f, -270f), new Vector2(190f, 42f), "⚡ Trigger Ability", new Color(0.90f, 0.25f, 0.55f), OnTriggerAbilityClicked);

        CreateButton(contentObj.transform, new Vector2(100f, -320f), new Vector2(190f, 42f), "🌫️ Test Blinding Ink", new Color(0.35f, 0.25f, 0.45f), OnTestInkBlindClicked);
        CreateButton(contentObj.transform, new Vector2(305f, -320f), new Vector2(190f, 42f), "💰 +5,000 Score", new Color(0.85f, 0.75f, 0.20f), OnAddScoreClicked);
        CreateButton(contentObj.transform, new Vector2(510f, -320f), new Vector2(190f, 42f), "🧹 Clear All Fish", new Color(0.65f, 0.20f, 0.20f), OnClearFishClicked);
        CreateButton(contentObj.transform, new Vector2(715f, -320f), new Vector2(190f, 42f), "🔄 Reset Shark Hits", new Color(0.30f, 0.50f, 0.50f), OnResetSharkStateClicked);

        // Status Feedback Bar at Bottom
        GameObject statusObj = new GameObject("StatusBar");
        statusObj.transform.SetParent(modalRoot.transform, false);
        RectTransform statusRt = statusObj.AddComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0f, 0f);
        statusRt.anchorMax = new Vector2(1f, 0f);
        statusRt.pivot = new Vector2(0.5f, 0f);
        statusRt.sizeDelta = new Vector2(0f, 40f);

        Image statusBg = statusObj.AddComponent<Image>();
        statusBg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);

        GameObject statusTextObj = new GameObject("StatusText");
        statusTextObj.transform.SetParent(statusObj.transform, false);
        RectTransform stRt = statusTextObj.AddComponent<RectTransform>();
        stRt.anchorMin = Vector2.zero;
        stRt.anchorMax = Vector2.one;
        stRt.offsetMin = new Vector2(20f, 0f);
        stRt.offsetMax = new Vector2(-20f, 0f);

        statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
        statusText.text = "Ready.";
        statusText.fontSize = 15;
        statusText.fontStyle = FontStyles.Bold;
        statusText.alignment = TextAlignmentOptions.MidlineLeft;
        statusText.color = new Color(0.20f, 0.95f, 0.70f);

        modalRoot.SetActive(false);
    }

    private void CreateCategoryHeader(Transform parent, Vector2 pos, string title)
    {
        GameObject obj = new GameObject("Category_" + title);
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(850f, 24f);

        TextMeshProUGUI txt = obj.AddComponent<TextMeshProUGUI>();
        txt.text = title;
        txt.fontSize = 14;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(0.20f, 0.85f, 0.95f);
        txt.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private Button CreateButton(Transform parent, Vector2 centerPos, Vector2 size, string label, Color btnColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject("Btn_" + label);
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = centerPos;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = btnColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = btnColor;
        cb.highlightedColor = btnColor * 1.25f;
        cb.pressedColor = btnColor * 0.8f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        // Label Text
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 13;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;

        return btn;
    }

    public void ShowStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    // --- BUTTON ACTIONS ---

    private GridController GetGrid()
    {
        return GridController.Instance != null ? GridController.Instance : FindFirstObjectByType<GridController>();
    }

    private void OnSpawnSharkClicked()
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatSpawnShark();
            ShowStatus("🦈 Spawned Shark Hazard in arena!");
        }
    }

    private void OnSpawnCuttlefishClicked()
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatSpawnCuttlefish();
            ShowStatus("🦑 Spawned Cuttlefish near player with vision defense!");
        }
    }

    private void OnSpawnBoatClicked()
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatSpawnBoat();
            ShowStatus("🎣 Spawned Boat Hazard!");
        }
    }

    private void OnOpenClamsClicked()
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatOpenAllClams();
            ShowStatus("🦪 All seafloor clams opened!");
        }
    }

    private void OnSpawnFishClicked(int level, bool isGolden = false, bool isSick = false, bool isSpiked = false)
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatSpawnFish(level, isGolden, isSick, isSpiked);
            string type = isGolden ? "Golden Fish" : (isSick ? "Sick Fish (Lv2)" : (isSpiked ? "Spiked Puffer" : $"Level {level} Fish"));
            ShowStatus($"🐟 Spawned {type} near camera!");
        }
    }

    private void OnLevelUpClicked()
    {
        PlayerController player = GetPlayer();
        if (player != null)
        {
            player.CheatLevelUp();
            ShowStatus($"⬆️ Player Leveled Up to Level {player.Level}!");
        }
    }

    private void OnLevelDownClicked()
    {
        PlayerController player = GetPlayer();
        if (player != null)
        {
            player.CheatLevelDown();
            ShowStatus($"⬇️ Player Leveled Down to Level {player.Level}!");
        }
    }

    private void OnMaxStreakClicked()
    {
        if (PlayerAbilitySystem.Instance != null)
        {
            PlayerAbilitySystem.Instance.CheatSetMaxStreak();
            ShowStatus("🔥 Max Streak (x10) Activated!");
        }
    }

    private void OnTriggerAbilityClicked()
    {
        if (PlayerAbilitySystem.Instance != null)
        {
            PlayerAbilitySystem.Instance.CheatTriggerAbility();
            ShowStatus("⚡ Apex Ability Activated!");
        }
    }

    private void OnTestInkBlindClicked()
    {
        InkScreenOverlay.TriggerBlinding(3.5f);
        ShowStatus("🌫️ Triggered 40-50% Blinding Ink Screen Overlay!");
    }

    private void OnAddScoreClicked()
    {
        PlayerController player = GetPlayer();
        if (player != null)
        {
            player.CheatAddScore(5000);
            ShowStatus("💰 Added +5,000 Score!");
        }
    }

    private void OnClearFishClicked()
    {
        GridController grid = GetGrid();
        if (grid != null)
        {
            grid.CheatClearAllFish();
            ShowStatus("🧹 Cleared all active AI fish from arena!");
        }
    }

    private void OnResetSharkStateClicked()
    {
        SharkHazard.ResetSessionState();
        RiverBoat.ResetSessionState();
        ShowStatus("🔄 Shark & Harpoon Boat session state reset!");
    }

    private PlayerController GetPlayer()
    {
        if (GameManager.instance != null && GameManager.instance.playerGameObject != null)
        {
            return GameManager.instance.playerGameObject.GetComponent<PlayerController>();
        }
        return FindFirstObjectByType<PlayerController>();
    }
}
