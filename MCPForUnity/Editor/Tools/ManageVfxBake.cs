// ManageVfxBake.cs
// MCP 工具：内置粒子特效烘焙能力，暴露为标准 MCP 工具 bake_vfx。
// 核心实现位于 MCPForUnity.Editor.VfxBake 命名空间，零外部依赖。
//
// 注册方式：[McpForUnityTool("bake_vfx", Group = "core")]
// Python 端对应文件：Server/src/services/tools/bake_vfx.py
//
// 唯一烘焙入口：bake_from_json（JSON DSL → Prefab 一步到位）。
// AI 助手负责将任意输入（自然语言/参考描述等）转换为标准 VFX-DSL JSON 后调用本工具。

using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("bake_vfx", AutoRegister = true, Group = "core")]
    public static class ManageVfxBake
    {
        // ──────────────────── Action 常量 ────────────────────

        private const string ActionBakeFromJson = "bake_from_json";
        private const string ActionList = "list";
        private const string ActionDelete = "delete";
        private const string ActionGetSpec = "get_spec";
        private const string ActionListTextures = "list_textures";

        // ──────────────────── 命令处理 ────────────────────

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case ActionBakeFromJson:
                    return HandleBakeFromJson(@params);
                case ActionList:
                    return HandleList(@params);
                case ActionDelete:
                    return HandleDelete(@params);
                case ActionGetSpec:
                    return HandleGetSpec();
                case ActionListTextures:
                    return HandleListTextures(@params);
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: {ActionBakeFromJson}, " +
                        $"{ActionList}, {ActionDelete}, {ActionGetSpec}, {ActionListTextures}");
            }
        }

        // ──────────────────── Action: bake_from_json ────────────────────

        private static object HandleBakeFromJson(JObject @params)
        {
            string jsonContent = @params["json_content"]?.ToString();
            string jsonPath = @params["json_path"]?.ToString();

            // 支持传入 JSON 文件路径：json_content 为空时改为读取文件。
            // 便于「首次生成 JSON 文件后，后续只改文件即可快速重烘」的迭代流程。
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                if (string.IsNullOrWhiteSpace(jsonPath))
                    return new ErrorResponse("Required parameter 'json_content' or 'json_path' is missing or empty.");

                jsonContent = ReadJsonFile(jsonPath, out string readError);
                if (jsonContent == null)
                    return new ErrorResponse(readError);
            }

            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");

            string sourceJson = @params["source_json"]?.ToString();
            // 使用 json_path 时，若未显式指定 source_json 且路径是 Assets 相对路径，
            // 自动将其作为 source_json 记录溯源信息。
            if (string.IsNullOrWhiteSpace(sourceJson)
                && !string.IsNullOrWhiteSpace(jsonPath)
                && jsonPath.Replace('\\', '/').StartsWith("Assets/"))
            {
                sourceJson = jsonPath.Replace('\\', '/');
            }

            try
            {
                var result = VfxBake.VfxBakeBridge.BakeFromJson(
                    jsonContent, prefabPath, sourceJson, true);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakeFromJson failed: {e.Message}");
            }
        }

        // ──────────────────── Action: list ────────────────────

        /// <summary>
        /// 读取 JSON 文件内容。支持 Assets 相对路径与绝对路径。
        /// </summary>
        private static string ReadJsonFile(string path, out string error)
        {
            error = null;
            string fullPath = path;
            if (!System.IO.Path.IsPathRooted(fullPath))
            {
                // 相对路径按项目根目录解析（当前工作目录即项目根）
                fullPath = System.IO.Path.GetFullPath(fullPath);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                error = $"JSON file not found: '{fullPath}'.";
                return null;
            }

            try
            {
                return System.IO.File.ReadAllText(fullPath);
            }
            catch (Exception e)
            {
                error = $"Failed to read JSON file '{fullPath}': {e.Message}";
                return null;
            }
        }

        private static object HandleList(JObject @params)
        {
            string searchDir = @params["output_dir"]?.ToString();

            try
            {
                var result = VfxBake.VfxBakeBridge.ListBaked(searchDir);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"List failed: {e.Message}");
            }
        }

        // ──────────────────── Action: delete ────────────────────

        private static object HandleDelete(JObject @params)
        {
            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");

            try
            {
                var result = VfxBake.VfxBakeBridge.DeleteBaked(prefabPath);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Delete failed: {e.Message}");
            }
        }

        // ──────────────────── Action: get_spec ────────────────────

        private static object HandleGetSpec()
        {
            try
            {
                var result = VfxBake.VfxBakeBridge.GetSpec();
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"GetSpec failed: {e.Message}");
            }
        }

        // ──────────────────── Action: list_textures ────────────────────

        private static object HandleListTextures(JObject @params)
        {
            string searchDir = @params["search_dir"]?.ToString();

            try
            {
                var result = VfxBake.VfxBakeBridge.ListTextures(searchDir);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"ListTextures failed: {e.Message}");
            }
        }

        // ──────────────────── 工具方法 ────────────────────

        /// <summary>
        /// 将桥接层返回值（Dictionary&lt;string, object&gt;）转换为 MCP 响应。
        /// </summary>
        private static object ToResponse(object result)
        {
            if (result == null)
                return new SuccessResponse("Operation completed (no data returned).");

            if (result is IDictionary<string, object> dict)
            {
                bool success = true;
                if (dict.TryGetValue("success", out var suc) && suc is bool b)
                    success = b;

                string message = dict.TryGetValue("message", out var msg) ? msg?.ToString() :
                                 dict.TryGetValue("error", out var err) ? err?.ToString() :
                                 "Operation completed.";

                var data = JObject.FromObject(dict);
                if (success)
                    return new SuccessResponse(message, data);
                else
                    return new ErrorResponse(message, data);
            }

            return new SuccessResponse("Operation completed.", new { result = result.ToString() });
        }
    }
}
