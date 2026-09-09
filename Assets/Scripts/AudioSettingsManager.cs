using System;
using UnityEngine;

/// <summary>
/// Manages global audio settings (Music, SFX, MasterVolume) with PlayerPrefs persistence.
/// </summary>
public static class AudioSettingsManager
{
    private const string MusicKey = "MusicEnabled";
    private const string SfxKey = "SfxEnabled";
    private const string VolumeKey = "MasterVolume";

    public static event Action<bool> OnMusicSettingChanged;
    public static event Action<bool> OnSfxSettingChanged;
    public static event Action<float> OnVolumeSettingChanged;

    public static bool IsMusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(MusicKey, value ? 1 : 0);
            PlayerPrefs.Save();
            OnMusicSettingChanged?.Invoke(value);
        }
    }

    public static bool IsSfxEnabled
    {
        get => PlayerPrefs.GetInt(SfxKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(SfxKey, value ? 1 : 0);
            PlayerPrefs.Save();
            OnSfxSettingChanged?.Invoke(value);
        }
    }

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1.0f);
        set
        {
            PlayerPrefs.SetFloat(VolumeKey, value);
            PlayerPrefs.Save();
            AudioListener.volume = value;
            OnVolumeSettingChanged?.Invoke(value);
        }
    }
}
