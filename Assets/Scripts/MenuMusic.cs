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
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (audioSource != null)
        {
            if (audioSource.clip == null)
            {
                #if UNITY_EDITOR
                audioSource.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/menu_theme.ogg");
                if (audioSource.clip == null) audioSource.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/music_theme.mp3");
                #endif
                if (audioSource.clip == null) audioSource.clip = Resources.Load<AudioClip>("menu_theme");
                if (audioSource.clip == null) audioSource.clip = Resources.Load<AudioClip>("music_theme");
            }

            audioSource.loop = true;
            audioSource.playOnAwake = true;
            audioSource.spatialBlend = 0f;
            defaultVolume = 0.5f;
            AudioSettingsManager.RouteToMusic(audioSource);
        }

        AudioSettingsManager.OnMusicSettingChanged += ApplyMusicSetting;
        AudioSettingsManager.OnMusicVolumeChanged += HandleMusicVolumeChanged;
    }

    private void Start()
    {
        ApplyMusicSetting(AudioSettingsManager.IsMusicEnabled);
    }

    private void OnDestroy()
    {
        AudioSettingsManager.OnMusicSettingChanged -= ApplyMusicSetting;
        AudioSettingsManager.OnMusicVolumeChanged -= HandleMusicVolumeChanged;
    }

    private void HandleMusicVolumeChanged(float vol)
    {
        ApplyMusicSetting(AudioSettingsManager.IsMusicEnabled);
    }

    public void ApplyMusicSetting(bool isEnabled)
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            bool shouldPlay = isEnabled && AudioSettingsManager.MusicVolume > 0.001f;
            audioSource.mute = !shouldPlay;
            audioSource.volume = shouldPlay ? defaultVolume * AudioSettingsManager.MusicVolume : 0f;
            if (!shouldPlay)
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
