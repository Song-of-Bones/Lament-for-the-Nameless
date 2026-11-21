using System;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Small status window so a human can see the bridge port, toggle it, and copy the
    /// heartbeat path without digging through logs.
    /// </summary>
    public sealed class McpBridgeWindow : EditorWindow
    {
        [MenuItem("Tools/Codely/Unity MCP")]
        public static void Open()
        {
            var window = GetWindow<McpBridgeWindow>("Unity MCP");
            window.minSize = new Vector2(360, 220);
        }

        private void OnEnable() => EditorApplication.update += Repaint;
        private void OnDisable() => EditorApplication.update -= Repaint;

        private void OnGUI()
        {
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            EditorGUILayout.LabelField("Codely Unity MCP Bridge", title);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status", McpBridgeServer.IsRunning ? "Running" : "Stopped");
                EditorGUILayout.LabelField("Port", McpBridgeServer.IsRunning ? McpBridgeServer.Port.ToString() : "-");
                EditorGUILayout.LabelField("Clients", McpBridgeServer.ClientCount.ToString());
                EditorGUILayout.LabelField("Commands handled", McpBridgeServer.HandledCommands.ToString());
                EditorGUILayout.LabelField("Package version", McpBridgeServer.PackageVersion);
                EditorGUILayout.LabelField("Heartbeat", "Temp/.codely-unity-mcp.json");
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (McpBridgeServer.IsRunning)
                {
                    if (GUILayout.Button("Stop")) McpBridgeServer.Stop(true, "manually_stopped");
                }
                else
                {
                    if (GUILayout.Button("Start")) McpBridgeServer.Start();
                }

                if (GUILayout.Button("Copy heartbeat path"))
                    EditorGUIUtility.systemCopyBuffer = McpBridgeServer.HeartbeatPath;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Point any MCP client at Server/dist/index.js inside this package. " +
                "The client discovers the port from the heartbeat file, so the port may change between sessions.",
                MessageType.Info);
        }
    }
}
