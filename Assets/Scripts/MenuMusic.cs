using UnityEngine;

public class MenuMusic : MonoBehaviour
{
    // Ensure this script is loaded
    private static MenuMusic instance;
    private AudioSource audioSource;
    private float defaultVolume = 0.7f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        if (audioSource != null && audioSource.volume > 0f)
        {
            defaultVolume = audioSource.volume;
        }

        AudioSettingsManager.OnMusicSettingChanged += ApplyMusicSetting;
    }

    private void Start()
    {
        ApplyMusicSetting(AudioSettingsManager.IsMusicEnabled);
    }

    private void OnDestroy()
    {
        AudioSettingsManager.OnMusicSettingChanged -= ApplyMusicSetting;
    }

    public void ApplyMusicSetting(bool isEnabled)
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.mute = !isEnabled;
            audioSource.volume = isEnabled ? defaultVolume : 0f;
            if (!isEnabled)
            {
                audioSource.Pause();
            }
            else
            {
                if (!audioSource.isPlaying) audioSource.Play();
                else audioSource.UnPause();
            }
        }
    }

    public void Kill()
    {
        if (audioSource != null) audioSource.Stop();
        Destroy(gameObject);
    }
}
