// ManageUguiBake.cs
// MCP 工具：内置 UGUI 烘焙能力，暴露为标准 MCP 工具 bake_ugui。
// 核心实现位于 MCPForUnity.Editor.UguiBake 命名空间，零外部依赖。
//
// 注册方式：[McpForUnityTool("bake_ugui", Group = "core")]
// Python 端对应文件：Server/src/services/tools/bake_ugui.py

using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("bake_ugui", AutoRegister = false, Group = "core")]
    public static class ManageUguiBake
    {
        // ──────────────────── Action 常量 ────────────────────

        private const string ActionBake = "bake";
        private const string ActionBakeBatch = "bake_batch";
        private const string ActionBakePartial = "bake_partial";
        private const string ActionList = "list";
        private const string ActionDelete = "delete";
        private const string ActionGetDsl = "get_dsl";
        private const string ActionGenerateScript = "generate_view_script";
        private const string ActionParseHtml = "parse_html";
        private const string ActionBakeFromHtml = "bake_from_html";
        private const string ActionParseDsl = "parse_dsl";
        private const string ActionBakeFromDsl = "bake_from_dsl";

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
                case ActionBake:
                    return HandleBake(@params);
                case ActionBakeBatch:
                    return HandleBakeBatch(@params);
                case ActionBakePartial:
                    return HandleBakePartial(@params);
                case ActionList:
                    return HandleList(@params);
                case ActionDelete:
                    return HandleDelete(@params);
                case ActionGetDsl:
                    return HandleGetDsl();
                case ActionGenerateScript:
                    return HandleGenerateScript(@params);
                case ActionParseHtml:
                    return HandleParseHtml(@params);
                case ActionBakeFromHtml:
                    return HandleBakeFromHtml(@params);
                case ActionParseDsl:
                    return HandleParseDsl(@params);
                case ActionBakeFromDsl:
                    return HandleBakeFromDsl(@params);
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: {ActionBake}, {ActionBakeBatch}, " +
                        $"{ActionBakePartial}, {ActionList}, {ActionDelete}, {ActionGetDsl}, {ActionGenerateScript}, " +
                        $"{ActionParseHtml}, {ActionBakeFromHtml}, {ActionParseDsl}, {ActionBakeFromDsl}");
            }
        }

        // ──────────────────── Action: bake ────────────────────

        private static object HandleBake(JObject @params)
        {
            string jsonContent = @params["json_content"]?.ToString();
            if (string.IsNullOrWhiteSpace(jsonContent))
                return new ErrorResponse("Required parameter 'json_content' is missing or empty.");

            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing or empty.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;
            bool useTMP = @params["use_tmp"]?.Value<bool>() ?? true;
            string templatePrefab = @params["template_prefab"]?.ToString();
            string sourceHtml = @params["source_html"]?.ToString();
            bool saveSnapshot = @params["save_snapshot"]?.Value<bool>() ?? true;

            try
            {
                var result = UguiBake.UguiBakeBridge.Bake(
                    jsonContent, prefabPath, width, height, useTMP,
                    templatePrefab, sourceHtml, saveSnapshot);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Bake failed: {e.Message}");
            }
        }

        // ──────────────────── Action: bake_batch ────────────────────

        private static object HandleBakeBatch(JObject @params)
        {
            string jsonArray = @params["json_array"]?.ToString();
            if (string.IsNullOrWhiteSpace(jsonArray))
                return new ErrorResponse("Required parameter 'json_array' is missing or empty.");

            string outputDir = @params["output_dir"]?.ToString();
            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;
            bool useTMP = @params["use_tmp"]?.Value<bool>() ?? true;

            try
            {
                string jsonArrayStr;
                var jsonToken = @params["json_array"];
                if (jsonToken.Type == JTokenType.Array)
                    jsonArrayStr = jsonToken.ToString(Newtonsoft.Json.Formatting.None);
                else
                    jsonArrayStr = jsonToken.ToString();

                var result = UguiBake.UguiBakeBridge.BakeBatch(jsonArrayStr, outputDir, width, height, useTMP);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakeBatch failed: {e.Message}");
            }
        }

        // ──────────────────── Action: bake_partial ────────────────────

        private static object HandleBakePartial(JObject @params)
        {
            string prefabPath = @params["prefab_path"]?.ToString();
            string nodePath = @params["node_path"]?.ToString();
            string jsonContent = @params["json_content"]?.ToString();

            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");
            if (string.IsNullOrWhiteSpace(nodePath))
                return new ErrorResponse("Required parameter 'node_path' is missing.");
            if (string.IsNullOrWhiteSpace(jsonContent))
                return new ErrorResponse("Required parameter 'json_content' is missing.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;
            bool useTMP = @params["use_tmp"]?.Value<bool>() ?? true;

            try
            {
                var result = UguiBake.UguiBakeBridge.BakePartial(prefabPath, nodePath, jsonContent, width, height, useTMP);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakePartial failed: {e.Message}");
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

        // ──────────────────── Action: get_dsl ────────────────────

        private static object HandleGetDsl()
        {
            try
            {
                var result = UguiBake.UguiBakeBridge.GetDsl();
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"GetDsl failed: {e.Message}");
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

        // ──────────────────── Action: parse_html ────────────────────

        private static object HandleParseHtml(JObject @params)
        {
            string htmlContent = @params["html_content"]?.ToString();
            if (string.IsNullOrWhiteSpace(htmlContent))
                return new ErrorResponse("Required parameter 'html_content' is missing or empty.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;

            try
            {
                var result = UguiBake.UguiBakeBridge.ParseHtml(htmlContent, width, height);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"ParseHtml failed: {e.Message}");
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

            try
            {
                var result = UguiBake.UguiBakeBridge.BakeFromHtml(htmlContent, prefabPath, width, height, useTMP, sourceHtml);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakeFromHtml failed: {e.Message}");
            }
        }

        // ──────────────────── Action: parse_dsl ────────────────────

        private static object HandleParseDsl(JObject @params)
        {
            string dslContent = @params["dsl_content"]?.ToString();
            if (string.IsNullOrWhiteSpace(dslContent))
                return new ErrorResponse("Required parameter 'dsl_content' is missing or empty.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;

            try
            {
                var result = UguiBake.UguiBakeBridge.ParseDsl(dslContent, width, height);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"ParseDsl failed: {e.Message}");
            }
        }

        // ──────────────────── Action: bake_from_dsl ────────────────────

        private static object HandleBakeFromDsl(JObject @params)
        {
            string dslContent = @params["dsl_content"]?.ToString();
            if (string.IsNullOrWhiteSpace(dslContent))
                return new ErrorResponse("Required parameter 'dsl_content' is missing or empty.");

            string prefabPath = @params["prefab_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabPath))
                return new ErrorResponse("Required parameter 'prefab_path' is missing.");

            int width = @params["reference_width"]?.Value<int>() ?? 942;
            int height = @params["reference_height"]?.Value<int>() ?? 2048;
            bool useTMP = @params["use_tmp"]?.Value<bool>() ?? true;
            string sourceHtml = @params["source_html"]?.ToString();

            try
            {
                var result = UguiBake.UguiBakeBridge.BakeFromDsl(dslContent, prefabPath, width, height, useTMP, sourceHtml);
                return ToResponse(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"BakeFromDsl failed: {e.Message}");
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
