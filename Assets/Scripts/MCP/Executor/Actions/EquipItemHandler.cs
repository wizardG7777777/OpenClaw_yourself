using System.Collections.Generic;
using UnityEngine;
using MCP.Core;

namespace MCP.Executor
{
    public class EquipItemHandler : IActionHandler
    {
        private ActionInstance action;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            string itemId = action.Target?.EntityId;
            if (string.IsNullOrEmpty(itemId) || ItemRegistry.Instance == null || !ItemRegistry.Instance.Contains(itemId))
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                action.Result = new { message = $"Item '{itemId}' not found in inventory." };
                return;
            }

            Debug.Log($"[MCP] Equipped item: {itemId}");
            action.Status = ActionStatus.Completed;
            action.Result = new { message = $"Equipped {itemId}." };
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
