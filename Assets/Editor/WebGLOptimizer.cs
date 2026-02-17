using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

#if UNITY_EDITOR
public class WebGLOptimizer : EditorWindow
{
    [MenuItem("Tools/Ocean Invader/Optimize WebGL Build")]
    public static void ShowWindow()
    {
        GetWindow<WebGLOptimizer>("WebGL Optimizer");
    }

    void OnGUI()
    {
        GUILayout.Label("WebGL Build Optimization", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("Apply Recommended WebGL Settings"))
        {
            EditorApplication.delayCall += ApplyPlayerSettings;
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Optimize All Audio Clips"))
        {
            EditorApplication.delayCall += OptimizeAudio;
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Optimize Large Textures"))
        {
            EditorApplication.delayCall += OptimizeTextures;
        }

        GUILayout.Space(10);
        GUILayout.Label("Mobile Loading Screen Fix:", EditorStyles.boldLabel);
        GUILayout.Label("1. Switch to 'MobileFriendly' WebGL Template in Project Settings.");
        GUILayout.Label("2. Ensure 'Run in Background' is disabled for mobile battery life.");
        
        GUILayout.Space(20);
        GUILayout.Label("Build:", EditorStyles.boldLabel);
        if (GUILayout.Button("Build for GitHub Pages (docs folder)"))
        {
            EditorApplication.delayCall += BuildForDocs;
        }
    }

    static void BuildForDocs()
    {
        string buildPath = "docs"; 
        
        // Ensure folder exists
        if (!Directory.Exists(buildPath))
        {
            Directory.CreateDirectory(buildPath);
        }

        // Guard: Ensure WebGL build support is installed for this Editor
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("WebGL build target is not supported in this Editor. " +
                           "Install the WebGL Build Support module for this Unity version via Unity Hub, " +
                           "then re-open the project and try again.");
            return;
        }

        // Force correct template setting
        if (PlayerSettings.WebGL.template != "PROJECT:MobileFriendly")
        {
            Debug.Log("Setting WebGL Template to PROJECT:MobileFriendly");
            PlayerSettings.WebGL.template = "PROJECT:MobileFriendly";
        }

        Debug.Log($"Starting WebGL Build to: {buildPath}...");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);
        
        if (buildPlayerOptions.scenes.Length == 0)
        {
            Debug.LogError("No scenes are enabled in the Build Settings! Go to File > Build Settings and add your scenes.");
            return;
        }

        // Build to "docs" folder directly. 
        // Unity will name files based on the folder name (e.g. docs.loader.js).
        // The index.html generated from the template will automatically reference these correct names.
        buildPlayerOptions.locationPathName = buildPath; 
        buildPlayerOptions.target = BuildTarget.WebGL;
        buildPlayerOptions.options = BuildOptions.None;

        UnityEditor.Build.Reporting.BuildReport report = null;
        try
        {
            report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        }
        catch (UnityException ue)
        {
            Debug.LogError($"WebGL build failed early: {ue.Message}. " +
                           "Ensure the WebGL Build Support module is installed via Unity Hub and retry.");
            return;
        }
        UnityEditor.Build.Reporting.BuildSummary summary = report.summary;

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("Build succeeded: " + summary.totalSize + " bytes");
            EditorUtility.RevealInFinder(buildPath);
        }

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Failed)
        {
            Debug.LogError("Build failed");
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogError($"Build Error: {message.content}");
                    }
                }
            }
        }
    }

    static void ApplyPlayerSettings()
    {
        // 1. Code Stripping (Reduces WASM size)
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

        // 2. Compression (Brotli is best for size, but Gzip is safer if server config is unknown. Unity Play supports Brotli.)
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true; // Essential for some hosting envs

        // 3. Performance
        PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = false; // Better for mobile behavior

        // 4. Mobile Specifics
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        
        // 5. Orientation (Force Landscape)
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        // Remove unnecessary code modules if possible (Physics 3D if 2D game?)
        // Note: This is risky to do blindly, so we stick to safe settings.

        Debug.Log("✅ Applied Recommended WebGL Player Settings.");
    }

    static void OptimizeAudio()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip");
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

            if (importer != null)
            {
                bool changed = false;

                // Force Mono (huge space saver)
                if (!importer.forceToMono)
                {
                    importer.forceToMono = true;
                    changed = true;
                }

                // Load in Background (helps startup time)
                if (!importer.loadInBackground)
                {
                    importer.loadInBackground = true;
                    changed = true;
                }

                // WebGL Override
                AudioImporterSampleSettings settings = importer.GetOverrideSampleSettings("WebGL");
                if (settings.loadType != AudioClipLoadType.CompressedInMemory || settings.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    settings.loadType = AudioClipLoadType.CompressedInMemory; // Best for WebGL RAM usage
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.7f; // Good balance
                    importer.SetOverrideSampleSettings("WebGL", settings);
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }
        Debug.Log($"✅ Optimized {count} Audio Clips for WebGL.");
    }

    static void OptimizeTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null)
            {
                // Skip icons/sprites if they are small (check file size or assumption)
                // We focus on large backgrounds or non-sprite textures primarily, 
                // but for 2D games, Sprites are Textures too.
                
                // Only touch it if it doesn't have a specific WebGL override yet
                bool changed = false;
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("WebGL");

                if (!settings.overridden)
                {
                    settings.overridden = true;
                    settings.maxTextureSize = 2048; // Limit to 2K
                    settings.format = TextureImporterFormat.DXT5Crunched; // Good for size
                    settings.compressionQuality = 50; // Aggressive compression
                    
                    importer.SetPlatformTextureSettings(settings);
                    changed = true;
                }
                else
                {
                    // Enforce Crunched if not already
                    if (settings.format != TextureImporterFormat.DXT5Crunched && settings.format != TextureImporterFormat.ETC2_RGBA8Crunched)
                    {
                        settings.format = TextureImporterFormat.DXT5Crunched;
                        settings.compressionQuality = 50;
                        importer.SetPlatformTextureSettings(settings);
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }
        Debug.Log($"✅ Optimized {count} Textures for WebGL (Crunched Compression).");
    }
}
#endif
