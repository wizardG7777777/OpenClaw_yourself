using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCP.Core;
using MCP.Entity;
using MCP.Executor;
using MCP.Gateway;
using MCP.Router;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Unity MCP Bridge entry point for E2E Agent integration tests.
/// Inlines Gateway + Router logic without MonoBehaviour dependency,
/// so it works in both Edit Mode and Play Mode.
/// </summary>
[McpForUnityTool("mcp_game_bridge")]
public static class MCPGameBridgeTool
{
    private static ToolRegistry _registry;
    private static ToolRegistry Registry =>
        _registry ??= CreateRegistry();

    private static ToolRegistry CreateRegistry()
    {
        var r = new ToolRegistry();
        r.RegisterMVPTools();
        return r;
    }

    public static object HandleCommand(JObject @params)
    {
        // 1. Extract raw_json
        string rawJson = @params?["raw_json"]?.Value<string>();
        if (string.IsNullOrEmpty(rawJson))
            return new ErrorResponse("raw_json parameter is required");

        // 2. Parse JSON
        JObject parsed;
        try
        {
            parsed = JObject.Parse(rawJson);
        }
        catch (Exception ex)
        {
            return WrapResponse(BuildError("INVALID_PARAMS", $"Malformed JSON: {ex.Message}", false));
        }

        // 3. Structural validation (reuse RequestValidator)
        ValidationResult sv = RequestValidator.ValidateStructure(parsed);
        if (!sv.IsValid)
            return WrapResponse(BuildError("INVALID_PARAMS", sv.ErrorReason, false, sv.ErrorField));

        string toolName = parsed["tool"].Value<string>();

        // 4. Whitelist check
        ValidationResult tv = RequestValidator.ValidateToolExists(toolName, Registry.GetAllowedTools());
        if (!tv.IsValid)
            return WrapResponse(BuildError("INVALID_TOOL", tv.ErrorReason, false, tv.ErrorField));

        // 5. Build MCPRequest + normalize parameters
        var args = parsed["args"].ToObject<Dictionary<string, object>>();
        ToolDefinition tool = Registry.GetTool(toolName);
        Dictionary<string, object> normalized = ParameterNormalizer.Normalize(args, tool, out string missing);
        if (normalized == null)
            return WrapResponse(BuildError("INVALID_PARAMS", $"Missing required parameter '{missing}'.", false));

        var request = new MCPRequest
        {
            RequestId = Guid.NewGuid().ToString("N").Substring(0, 8),
            Tool = toolName,
            Args = normalized,
            PlayerId = "e2e_agent"
        };

        // 6. Dispatch
        MCPResponse response = tool.IsExclusive
            ? DispatchAction(request, tool, normalized)
            : DispatchQuery(request, toolName);

        return WrapResponse(response);
    }

    // Queries: call static handlers directly (no Unity runtime dependency)
    private static MCPResponse DispatchQuery(MCPRequest req, string toolName)
    {
        switch (toolName)
        {
            case "get_inventory":       return GetInventoryHandler.Handle(req);
            case "get_player_state":    return GetPlayerStateHandler.Handle(req);
            case "get_nearby_entities": return GetNearbyEntitiesHandler.Handle(req);
            case "get_world_summary":   return GetWorldSummaryHandler.Handle(req);
            default:
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = ErrorCodes.INVALID_TOOL,
                        Message = $"No handler registered for '{toolName}'."
                    }
                };
        }
    }

    // Actions: validate target, then forward to MCPGateway in Play Mode or return stub in Edit Mode
    private static MCPResponse DispatchAction(MCPRequest req, ToolDefinition tool,
        Dictionary<string, object> args)
    {
        // Resolve semantic entity reference: target_id (most tools) or npc_id (talk_to_npc)
        string entityRefKey = args.ContainsKey("npc_id") ? "npc_id" : "target_id";
        if (args.TryGetValue(entityRefKey, out var tid) && tid != null)
        {
            Vector3 playerPos = GameObject.FindGameObjectWithTag("Player")?.transform.position
                                ?? Vector3.zero;
            ResolveResult resolve = SemanticResolver.ResolveTarget(tid.ToString(), playerPos);
            if (!resolve.Success)
            {
                var details = resolve.Candidates != null && resolve.Candidates.Count > 0
                    ? new Dictionary<string, object> { ["candidates"] = resolve.Candidates }
                    : null;
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = resolve.ErrorCode,
                        Message = resolve.Message,
                        Retryable = false,
                        Details = details
                    }
                };
            }
        }

        // In Play Mode: forward to the live MCPGateway
        if (Application.isPlaying)
        {
            var gw = UnityEngine.Object.FindAnyObjectByType<MCPGateway>();
            if (gw != null)
            {
                string forwardJson = JsonConvert.SerializeObject(
                    new { tool = req.Tool, args = req.Args });
                return gw.ProcessRequest(forwardJson);
            }
        }

        // Edit Mode stub: parameters are valid, action accepted but not executed
        return new MCPResponse
        {
            Ok = true,
            ActionId = Guid.NewGuid().ToString("N").Substring(0, 12),
            Status = "accepted_edit_mode"
        };
    }

    // Wrap MCPResponse into SuccessResponse.data (Bridge protocol always expects SuccessResponse)
    private static SuccessResponse WrapResponse(MCPResponse r) =>
        new SuccessResponse("processed", JObject.Parse(JsonConvert.SerializeObject(r)));

    private static MCPResponse BuildError(string code, string msg, bool retryable,
        string field = null)
    {
        return new MCPResponse
        {
            Ok = false,
            Error = new MCPError
            {
                Code = code,
                Message = msg,
                Retryable = retryable,
                Details = field != null
                    ? new Dictionary<string, object> { ["field"] = field }
                    : null
            }
        };
    }
}
