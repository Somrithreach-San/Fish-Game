using UnityEngine;

public class MenuMusic : MonoBehaviour
{
    // Ensure this script is loaded
    private static MenuMusic instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Kill()
    {
        Destroy(gameObject);
    }
}
