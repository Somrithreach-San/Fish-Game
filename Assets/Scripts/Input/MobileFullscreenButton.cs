using UnityEngine;
using UnityEngine.UI;

public class MobileFullscreenButton : MonoBehaviour
{
    private Button btn;

    void Start()
    {
        btn = GetComponent<Button>();
        if (btn == null) btn = gameObject.AddComponent<Button>();
        
        btn.onClick.AddListener(ToggleFullscreen);
    }

    public void ToggleFullscreen()
    {
        if (GuiManager.instance != null)
        {
            GuiManager.instance.GoFullScreen();
        }
        else
        {
            Debug.LogWarning("GuiManager instance not found, falling back to standard fullscreen toggle.");
            // Fallback logic
            if (!Screen.fullScreen)
            {
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                Screen.fullScreen = true;
            }
            else
            {
                Screen.fullScreen = false;
            }
        }
    }
}
