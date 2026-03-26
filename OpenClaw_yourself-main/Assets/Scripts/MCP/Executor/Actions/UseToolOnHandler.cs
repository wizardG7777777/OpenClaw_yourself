using System.Collections.Generic;
using UnityEngine;
using MCP.Core;

namespace MCP.Executor
{
    public class UseToolOnHandler : IActionHandler
    {
        private ActionInstance action;

        // MVP hardcoded inventory
        private static readonly HashSet<string> InventoryItems = new HashSet<string>
        {
            "wrench", "shovel", "postcard"
        };

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            // Check tool_id from action args (stored in Result temporarily during routing, or read from a convention)
            string toolId = null;
            if (action.Result is Dictionary<string, object> args && args.TryGetValue("tool_id", out var tid))
                toolId = tid?.ToString();

            if (string.IsNullOrEmpty(toolId) || !InventoryItems.Contains(toolId))
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                action.Result = new { message = $"Tool '{toolId}' not found in inventory." };
                return;
            }

            if (action.Target == null || action.Target.EntityObject == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                return;
            }

            var interactable = action.Target.EntityObject.GetComponent<IInteractable>();
            if (interactable == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TOOL_NOT_APPLICABLE;
                action.Result = new { message = $"Cannot use '{toolId}' on this target." };
                return;
            }

            bool executed = interactable.Interact();
            if (!executed)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.ACTION_CONFLICT;
                action.Result = new { message = $"Target is busy. Cannot use '{toolId}' right now." };
                return;
            }
            action.Status = ActionStatus.Completed;
            action.Result = new { message = $"Used {toolId} on target." };
        }

        public void UpdateAction()
        {
            // Immediate action; nothing to update.
        }

        public void Cancel()
        {
            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
