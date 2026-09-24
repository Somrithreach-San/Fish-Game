using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Manages global audio settings (Music, SFX, MasterVolume) with PlayerPrefs persistence and AudioMixer integration.
/// </summary>
public static class AudioSettingsManager
{
    private const string MusicKey = "MusicEnabled";
    private const string SfxKey = "SfxEnabled";
    private const string VolumeKey = "MasterVolume";

    private static AudioMixer s_MainMixer;
    private static AudioMixerGroup s_MusicGroup;
    private static AudioMixerGroup s_SfxGroup;
    private static bool s_MixerInitialized = false;

    private static float s_LastButtonSoundTime = -1f;
    public const float BUTTON_SOUND_DEBOUNCE_INTERVAL = 0.15f; // Prevents race conditions / rapid double clicks from triggering multiple overlapping sounds

    /// <summary>
    /// Checks if a button click sound can be played, debouncing rapid clicks within a short time frame (150ms).
    /// </summary>
    public static bool CanPlayButtonSound()
    {
        float now = Time.unscaledTime;
        if (now - s_LastButtonSoundTime < BUTTON_SOUND_DEBOUNCE_INTERVAL)
        {
            return false;
        }
        s_LastButtonSoundTime = now;
        return true;
    }

    public static event Action<bool> OnMusicSettingChanged;
    public static event Action<bool> OnSfxSettingChanged;
    public static event Action<float> OnMusicVolumeChanged;
    public static event Action<float> OnSfxVolumeChanged;
    public static event Action<float> OnVolumeSettingChanged;

    public static AudioMixer MainMixer
    {
        get
        {
            EnsureMixerInitialized();
            return s_MainMixer;
        }
    }

    public static AudioMixerGroup MusicMixerGroup
    {
        get
        {
            EnsureMixerInitialized();
            return s_MusicGroup;
        }
    }

    public static AudioMixerGroup SfxMixerGroup
    {
        get
        {
            EnsureMixerInitialized();
            return s_SfxGroup;
        }
    }

    public static bool IsMusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicKey, 1) == 1 && PlayerPrefs.GetFloat("MusicVolume", 1.0f) > 0.001f;
        set
        {
            PlayerPrefs.SetInt(MusicKey, value ? 1 : 0);
            if (value && PlayerPrefs.GetFloat("MusicVolume", 1.0f) <= 0.001f)
            {
                PlayerPrefs.SetFloat("MusicVolume", 1.0f);
            }
            PlayerPrefs.Save();
            ApplyMixerSettings();
            OnMusicSettingChanged?.Invoke(IsMusicEnabled);
            OnMusicVolumeChanged?.Invoke(MusicVolume);
        }
    }

    public static bool IsSfxEnabled
    {
        get => PlayerPrefs.GetInt(SfxKey, 1) == 1 && PlayerPrefs.GetFloat("SfxVolume", 1.0f) > 0.001f;
        set
        {
            PlayerPrefs.SetInt(SfxKey, value ? 1 : 0);
            if (value && PlayerPrefs.GetFloat("SfxVolume", 1.0f) <= 0.001f)
            {
                PlayerPrefs.SetFloat("SfxVolume", 1.0f);
            }
            PlayerPrefs.Save();
            ApplyMixerSettings();
            OnSfxSettingChanged?.Invoke(IsSfxEnabled);
            OnSfxVolumeChanged?.Invoke(SfxVolume);
        }
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat("MusicVolume", 1.0f);
        set
        {
            bool wasEnabled = IsMusicEnabled;
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("MusicVolume", clamped);
            PlayerPrefs.SetInt(MusicKey, clamped > 0.001f ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMixerSettings();
            bool nowEnabled = IsMusicEnabled;
            if (wasEnabled != nowEnabled)
            {
                OnMusicSettingChanged?.Invoke(nowEnabled);
            }
            OnMusicVolumeChanged?.Invoke(clamped);
        }
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat("SfxVolume", 1.0f);
        set
        {
            bool wasEnabled = IsSfxEnabled;
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("SfxVolume", clamped);
            PlayerPrefs.SetInt(SfxKey, clamped > 0.001f ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMixerSettings();
            bool nowEnabled = IsSfxEnabled;
            if (wasEnabled != nowEnabled)
            {
                OnSfxSettingChanged?.Invoke(nowEnabled);
            }
            OnSfxVolumeChanged?.Invoke(clamped);
        }
    }

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1.0f);
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, clamped);
            PlayerPrefs.Save();
            AudioListener.volume = clamped;
            ApplyMixerSettings();
            OnVolumeSettingChanged?.Invoke(clamped);
        }
    }

    public static void InitializeAudio()
    {
        EnsureMixerInitialized();
        float master = MasterVolume;
        if (master <= 0.001f)
        {
            master = 1.0f;
            PlayerPrefs.SetFloat(VolumeKey, 1.0f);
            PlayerPrefs.Save();
        }
        AudioListener.volume = master;
        AudioListener.pause = false;
        ApplyMixerSettings();
    }

    public static void RouteToSfx(AudioSource source)
    {
        if (source == null) return;
        if (SfxMixerGroup != null)
        {
            source.outputAudioMixerGroup = SfxMixerGroup;
        }
    }

    public static void RouteToMusic(AudioSource source)
    {
        if (source == null) return;
        if (MusicMixerGroup != null)
        {
            source.outputAudioMixerGroup = MusicMixerGroup;
        }
    }

    private static void EnsureMixerInitialized()
    {
        if (s_MixerInitialized) return;
        s_MixerInitialized = true;

        s_MainMixer = Resources.Load<AudioMixer>("GameAudioMixer");
        if (s_MainMixer == null)
        {
            #if UNITY_EDITOR
            s_MainMixer = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/GameAudioMixer.mixer");
            #endif
        }

        if (s_MainMixer != null)
        {
            AudioMixerGroup[] musicGroups = s_MainMixer.FindMatchingGroups("Music");
            if (musicGroups != null && musicGroups.Length > 0) s_MusicGroup = musicGroups[0];

            AudioMixerGroup[] sfxGroups = s_MainMixer.FindMatchingGroups("SFX");
            if (sfxGroups != null && sfxGroups.Length > 0) s_SfxGroup = sfxGroups[0];

            ApplyMixerSettings();
        }
    }

    private static void ApplyMixerSettings()
    {
        if (s_MainMixer == null) return;

        float masterVol = MasterVolume;
        float masterDb = masterVol > 0.001f ? Mathf.Log10(masterVol) * 20f : -80f;
        s_MainMixer.SetFloat("MasterVolume", masterDb);

        float musicVol = IsMusicEnabled ? MusicVolume : 0f;
        float musicDb = musicVol > 0.001f ? Mathf.Log10(musicVol) * 20f : -80f;
        s_MainMixer.SetFloat("MusicVolume", musicDb);

        float sfxVol = IsSfxEnabled ? SfxVolume : 0f;
        float sfxDb = sfxVol > 0.001f ? Mathf.Log10(sfxVol) * 20f : -80f;
        s_MainMixer.SetFloat("SFXVolume", sfxDb);
    }
}
