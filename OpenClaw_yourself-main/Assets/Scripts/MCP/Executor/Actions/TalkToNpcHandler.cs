using UnityEngine;
using MCP.Core;

namespace MCP.Executor
{
    public class TalkToNpcHandler : IActionHandler
    {
        private ActionInstance action;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            if (action.Target == null || action.Target.EntityObject == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                action.Result = new { message = "NPC not found." };
                return;
            }

            string npcName = action.Target.EntityId ?? "Unknown NPC";
            Debug.Log($"[MCP] Talking to NPC: {npcName}");

            action.Status = ActionStatus.Completed;
            action.Result = new { message = $"Initiated dialogue with {npcName}." };
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
