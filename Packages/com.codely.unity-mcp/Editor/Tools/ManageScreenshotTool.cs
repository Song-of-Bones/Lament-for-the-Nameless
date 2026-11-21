using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    /// <summary>
    /// PNG capture from the Scene view or a camera. Images are written under
    /// <project>/screenshots/ and optionally returned as base64 so a client can render
    /// them without reading the file system.
    ///
    /// GIF and MP4 are intentionally not faked: unsupported actions return a clear
    /// not_supported error rather than a broken artifact.
    /// </summary>
    public static class ManageScreenshotTool
    {
        private const int MaxBase64Bytes = 4 * 1024 * 1024;

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "capture_scene_view") ?? "capture_scene_view").Trim().ToLowerInvariant();
            switch (action)
            {
                case "capture_scene_view":
                case "capture_scene_camera":
                    return CaptureSceneView(p);
                case "capture_main_camera":
                    return CaptureMainCamera(p);
                case "capture_specific_camera":
                    return CaptureSpecificCamera(p);
                case "capture_asset":
                case "capture_ui_toolkit":
                case "start_game_view_recording":
                case "finish_game_view_recording":
                    throw new ToolException("not_supported",
                        $"manage_screenshot.{action} is not implemented in this build. " +
                        "Use capture_scene_view, capture_main_camera or capture_specific_camera for PNG output.",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["implemented_actions"] = new List<object>
                            {
                                "capture_scene_view", "capture_scene_camera",
                                "capture_main_camera", "capture_specific_camera"
                            }
                        });
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_screenshot action '{action}'. Implemented: capture_scene_view, capture_scene_camera, " +
                        "capture_main_camera, capture_specific_camera.");
            }
        }

        private static ToolResult CaptureSceneView(IDictionary<string, object> p)
        {
            string view = (JsonUtil.Str(p, "view", "current") ?? "current").ToLowerInvariant();
            if (view != "current")
                throw new ToolException("unsupported_view",
                    "Only view='current' is supported. Move the Scene view manually, or use capture_specific_camera for a scene camera.");

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                throw new ToolException("scene_view_missing", "No active Scene view is available.");

            return Render(sceneView.camera, p, "scene_view");
        }

        private static ToolResult CaptureMainCamera(IDictionary<string, object> p)
        {
            var camera = Camera.main;
            if (camera == null)
                throw new ToolException("camera_missing",
                    "Camera.main was not found. Ensure a camera is tagged MainCamera, or use capture_specific_camera.");
            return Render(camera, p, "main_camera");
        }

        private static ToolResult CaptureSpecificCamera(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "camera_name");
            if (string.IsNullOrWhiteSpace(name))
                throw new ToolException("missing_param", "camera_name is required.");

            var camera = Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(c => c != null && c.gameObject.scene.IsValid() &&
                                     string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase));
            if (camera == null)
                throw new ToolException("camera_missing",
                    $"No camera named '{name}' in the loaded scenes. Use manage_scene.get_hierarchy to inspect names.");
            return Render(camera, p, PathUtil.SanitizeFileName(name, "camera"));
        }

        private static ToolResult Render(Camera camera, IDictionary<string, object> p, string label)
        {
            int width = Mathf.Clamp(JsonUtil.Int(p, "width", camera.pixelWidth > 0 ? camera.pixelWidth : 960), 16, 4096);
            int height = Mathf.Clamp(JsonUtil.Int(p, "height", camera.pixelHeight > 0 ? camera.pixelHeight : 540), 16, 4096);
            bool orthographic = JsonUtil.Bool(p, "orthographic", false);
            bool includeBase64 = JsonUtil.Bool(p, "include_base64", true, "includeBase64");

            RenderTexture renderTexture = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            bool previousOrthographic = camera.orthographic;

            try
            {
                renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                camera.targetTexture = renderTexture;
                if (orthographic) camera.orthographic = true;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                byte[] png = FlipAndEncode(texture, width, height);
                if (png == null) throw new ToolException("encode_failed", "PNG encoding failed.");

                string fileName = JsonUtil.Str(p, "filename");
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"{label}_{DateTime.Now:yyyyMMdd_HHmmss}";
                if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) fileName += ".png";

                string directory = Path.Combine(McpBridgeServer.ProjectRoot, "screenshots");
                Directory.CreateDirectory(directory);
                string absolutePath = Path.Combine(directory, PathUtil.SanitizeFileName(fileName));
                File.WriteAllBytes(absolutePath, png);

                var data = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["absolute_path"] = absolutePath.Replace('\\', '/'),
                    ["relative_path"] = "screenshots/" + Path.GetFileName(absolutePath),
                    ["width"] = width,
                    ["height"] = height,
                    ["bytes"] = png.Length,
                    ["camera"] = camera.name,
                    ["orthographic"] = camera.orthographic
                };

                if (includeBase64)
                {
                    if (png.Length <= MaxBase64Bytes)
                    {
                        data["image_base64"] = Convert.ToBase64String(png);
                        data["mime_type"] = "image/png";
                    }
                    else
                    {
                        data["base64_omitted"] = true;
                        data["note"] = "PNG is larger than 4 MB; read the file path instead.";
                    }
                }

                return new ToolResult($"Captured {width}x{height} PNG from '{camera.name}'.", data);
            }
            catch (ToolException) { throw; }
            catch (Exception ex)
            {
                throw new ToolException("capture_failed", "Screenshot failed: " + ex.Message);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.orthographic = previousOrthographic;
                RenderTexture.active = previousActive;
                if (renderTexture != null) RenderTexture.ReleaseTemporary(renderTexture);
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// RenderTexture rows arrive bottom-up relative to Texture2D's expected order, so
        /// flip into a second texture before encoding instead of shipping an upside-down
        /// image the model would misread.
        /// </summary>
        private static byte[] FlipAndEncode(Texture2D source, int width, int height)
        {
            var pixels = source.GetPixels32();
            var flipped = new Color32[pixels.Length];
            for (int y = 0; y < height; y++)
                Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);

            var target = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                target.SetPixels32(flipped);
                target.Apply();
                return target.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
