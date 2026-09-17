#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Prevents the Unity MCP package from overwriting ~/.gemini/config/mcp_config.json
/// with properties ('type', 'disabled') not recognized by Antigravity's JSON schema.
/// </summary>
[InitializeOnLoad]
public static class McpConfigGuard
{
    static McpConfigGuard()
    {
        EditorPrefs.SetBool("MCPForUnity.LockCursorConfig", true);
        EditorPrefs.SetBool("MCPForUnity.AutoRegisterEnabled", false);
    }
}
#endif
