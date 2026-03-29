using System;
using System.Collections.Generic;
using UnityEngine;
using MCP.Core;
using MCP.Entity;
using MCP.Executor;

namespace MCP.Router
{
    public class MCPRouter : MonoBehaviour
    {
        private ToolRegistry _registry;
        public ToolRegistry Registry => _registry ??= CreateRegistry();

        private static ToolRegistry CreateRegistry()
        {
            var r = new ToolRegistry();
            r.RegisterMVPTools();
            return r;
        }

        private ActionInstance currentAction;
        private readonly List<ActionInstance> actionHistory = new List<ActionInstance>();
        private IActionHandler activeHandler;

        private void Update()
        {
            // Tick active handler
            if (activeHandler != null)
                activeHandler.UpdateAction();

            // Move terminal actions to history
            if (currentAction != null &&
                (currentAction.Status == ActionStatus.Completed ||
                 currentAction.Status == ActionStatus.Failed ||
                 currentAction.Status == ActionStatus.Cancelled))
            {
                actionHistory.Add(currentAction);
                currentAction = null;
                activeHandler = null;
            }

            // Check timeout on running action
            if (currentAction != null && currentAction.Status == ActionStatus.Running)
            {
                if (Time.time - currentAction.CreatedAt > currentAction.Timeout)
                {
                    currentAction.Status = ActionStatus.Failed;
                    currentAction.ErrorCode = ErrorCodes.ACTION_TIMEOUT;
                }
            }
        }

        public MCPResponse Route(MCPRequest request)
        {
            // 1. Lookup tool
            var tool = Registry.GetTool(request.Tool);
            if (tool == null)
            {
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = ErrorCodes.INVALID_TOOL,
                        Message = $"Unknown tool '{request.Tool}'.",
                        Retryable = false
                    }
                };
            }

            // 2. Parameter normalization
            var normalized = ParameterNormalizer.Normalize(request.Args, tool, out string missingParam);
            if (normalized == null)
            {
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = ErrorCodes.INVALID_PARAMS,
                        Message = $"Missing required parameter '{missingParam}'.",
                        Retryable = false
                    }
                };
            }

            // 3. Target resolution for args containing target_id
            ResolvedTarget resolvedTarget = null;
            if (normalized.ContainsKey("target_id") && normalized["target_id"] != null)
            {
                string targetId = normalized["target_id"].ToString();
                resolvedTarget = ResolveTargetId(targetId);
                if (resolvedTarget == null)
                    return lastResolveError;
            }

            // 4. Dispatch
            if (!tool.IsExclusive)
            {
                return DispatchQuery(request, tool, normalized);
            }

            return DispatchAction(request, tool, normalized, resolvedTarget);
        }

        private MCPResponse lastResolveError;

        private ResolvedTarget ResolveTargetId(string targetId)
        {
            // Try direct ID lookup
            var entity = EntityRegistry.Instance?.GetById(targetId);
            if (entity != null)
            {
                lastResolveError = null;
                return ResolvedTarget.FromEntity(entity.runtimeId ?? entity.entityId, entity.gameObject, entity.transform.position);
            }

            // Semantic resolution
            Vector3 playerPos = GetPlayerPosition();
            var result = SemanticResolver.ResolveTarget(targetId, playerPos);
            if (result.Success)
            {
                lastResolveError = null;
                return result.Target;
            }

            // Build error response
            var error = new MCPError
            {
                Code = result.ErrorCode,
                Message = result.Message,
                Retryable = false
            };

            if (result.Candidates != null && result.Candidates.Count > 0)
            {
                error.Details = new Dictionary<string, object> { { "candidates", result.Candidates } };
                error.SuggestedNextActions = new List<SuggestedAction>
                {
                    new SuggestedAction
                    {
                        Tool = "get_nearby_entities",
                        Args = new Dictionary<string, object>()
                    }
                };
            }

            lastResolveError = new MCPResponse { Ok = false, Error = error };
            return null;
        }

        private MCPResponse DispatchQuery(MCPRequest request, ToolDefinition tool, Dictionary<string, object> args)
        {
            // Route to the appropriate query handler based on tool name.
            switch (tool.ToolName)
            {
                case "get_inventory":
                    return GetInventoryHandler.Handle(request);
                case "get_player_state":
                    return GetPlayerStateHandler.Handle(request);
                case "get_world_summary":
                    return GetWorldSummaryHandler.Handle(request);
                case "get_nearby_entities":
                    return GetNearbyEntitiesHandler.Handle(request);
                default:
                    return new MCPResponse
                    {
                        Ok = true,
                        Data = new Dictionary<string, object>
                        {
                            { "tool", tool.ToolName },
                            { "args", args },
                            { "message", "Query dispatched. No handler registered." }
                        }
                    };
            }
        }

        private MCPResponse DispatchAction(
            MCPRequest request,
            ToolDefinition tool,
            Dictionary<string, object> args,
            ResolvedTarget target)
        {
            string cancelledId = null;

            // Last-Write-Wins: cancel running action
            if (currentAction != null && currentAction.Status == ActionStatus.Running)
            {
                currentAction.Status = ActionStatus.Cancelled;
                cancelledId = currentAction.ActionId;
                if (activeHandler != null)
                    activeHandler.Cancel();
            }

            // Determine timeout
            float timeout = tool.DefaultTimeout;
            if (args.ContainsKey("timeout") && args["timeout"] is float t)
                timeout = t;

            // Create new action instance
            var action = new ActionInstance
            {
                ActionId = Guid.NewGuid().ToString("N").Substring(0, 12),
                ToolName = tool.ToolName,
                Status = ActionStatus.Running,
                Target = target,
                CreatedAt = Time.time,
                Timeout = timeout,
                CancelledActionId = cancelledId,
                // Preserve normalized args for handlers that need action parameters at start.
                Result = args
            };

            currentAction = action;

            // Instantiate and start handler if HandlerType is defined
            if (tool.HandlerType != null && typeof(IActionHandler).IsAssignableFrom(tool.HandlerType))
            {
                activeHandler = (IActionHandler)Activator.CreateInstance(tool.HandlerType);
                activeHandler.StartAction(action);
            }

            return new MCPResponse
            {
                Ok = true,
                ActionId = action.ActionId,
                Status = "running",
                CancelledActionId = cancelledId
            };
        }

        public ActionInstance GetCurrentAction()
        {
            return currentAction;
        }

        public List<ActionInstance> GetActionHistory()
        {
            return new List<ActionInstance>(actionHistory);
        }

        private Vector3 GetPlayerPosition()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }
    }
}
