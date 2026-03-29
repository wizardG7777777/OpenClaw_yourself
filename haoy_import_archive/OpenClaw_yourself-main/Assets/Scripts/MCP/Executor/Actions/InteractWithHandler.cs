using UnityEngine;
using MCP.Core;

namespace MCP.Executor
{
    public class InteractWithHandler : IActionHandler
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
                return;
            }

            // Distance check
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                float dist = Vector3.Distance(player.transform.position, action.Target.EntityObject.transform.position);
                if (dist > 3f)
                {
                    action.Status = ActionStatus.Failed;
                    action.ErrorCode = ErrorCodes.OUT_OF_RANGE;
                    return;
                }
            }

            var interactable = action.Target.EntityObject.GetComponent<IInteractable>();
            if (interactable == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TOOL_NOT_APPLICABLE;
                return;
            }

            bool executed = interactable.Interact();
            if (!executed)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.ACTION_CONFLICT;
                action.Result = new { message = "Target is busy (animation in progress). Try again shortly." };
                return;
            }
            action.Status = ActionStatus.Completed;
            action.Result = new { message = "Interacted with target." };
        }

        public void UpdateAction()
        {
            // Interaction is immediate; nothing to update.
        }

        public void Cancel()
        {
            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
