// ManageUguiBake.cs
// MCP 工具：内置 UGUI 烘焙能力，暴露为标准 MCP 工具 bake_ugui。
// 核心实现位于 MCPForUnity.Editor.UguiBake 命名空间，零外部依赖。
//
// 注册方式：[McpForUnityTool("bake_ugui", Group = "core")]
// Python 端对应文件：Server/src/services/tools/bake_ugui.py
//
// 唯一烘焙入口：bake_from_html（HTML → JSON → Prefab 一步到位）。
// AI 助手负责将任意输入（自然语言/截图/HTML）转换为标准 UI-DSL HTML 后调用本工具。

using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("bake_ugui", AutoRegister = true, Group = "core")]
    public static class ManageUguiBake
    {
        // ──────────────────── Action 常量 ────────────────────

        private const string ActionBakeFromHtml = "bake_from_html";
        private const string ActionList = "list";
        private const string ActionDelete = "delete";
        private const string ActionGetSpec = "get_spec";
        private const string ActionGenerateScript = "generate_view_script";

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
                case ActionBakeFromHtml:
                    return HandleBakeFromHtml(@params);
                case ActionList:
                    return HandleList(@params);
                case ActionDelete:
                    return HandleDelete(@params);
                case ActionGetSpec:
                    return HandleGetSpec();
                case ActionGenerateScript:
                    return HandleGenerateScript(@params);
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: {ActionBakeFromHtml}, " +
                        $"{ActionList}, {ActionDelete}, {ActionGetSpec}, {ActionGenerateScript}");
            }
        }

        // ──────────────────── Action: bake_from_html ────────────────────

        private static object HandleBakeFromHtml(JObject @params)
        {
            string htmlContent = @params["html_content"]?.ToString();
            if (string.IsNullOrWhiteSpace(htmlContent))
                return new ErrorResponse("Required parameter 'html_content' is missing or empty.");

            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;
            bool useTMP = @params["use_tmp"]?.Value<bool>() ?? true;
            string sourceHtml = @params["source_html"]?.ToString();
            string templatePrefab = @params["template_prefab"]?.ToString();
            string fontPath = @params["font_path"]?.ToString();
            string userInputContent = @params["user_input_content"]?.ToString();
            string userInputExtension = @params["user_input_extension"]?.ToString() ?? "txt";
            string userInputSourcePath = @params["user_input_source_path"]?.ToString();

            try
            {
                var result = UguiBake.UguiBakeBridge.BakeFromHtml(
                    htmlContent, prefabPath, width, height, useTMP,
                    sourceHtml, templatePrefab, fontPath, true,
                    userInputContent, userInputExtension, userInputSourcePath);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakeFromHtml failed: {e.Message}");
            }
        }

        // ──────────────────── Action: list ────────────────────

        private static object HandleList(JObject @params)
        {
            string searchDir = @params["output_dir"]?.ToString();

            try
            {
                var result = UguiBake.UguiBakeBridge.ListBaked(searchDir);
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
                var result = UguiBake.UguiBakeBridge.DeleteBaked(prefabPath);
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
                var result = UguiBake.UguiBakeBridge.GetSpec();
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"GetSpec failed: {e.Message}");
            }
        }

        // ──────────────────── Action: generate_view_script ────────────────────

        private static object HandleGenerateScript(JObject @params)
        {
            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");

            string scriptPath = @params["script_path"]?.ToString();
            string namespaceName = @params["namespace"]?.ToString() ?? "Game.UI";

            try
            {
                var result = UguiBake.UguiBakeBridge.GenerateViewScript(prefabPath, scriptPath, namespaceName);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"GenerateViewScript failed: {e.Message}");
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
