using UnityEngine;
using UnityEngine.AI;
using MCP.Core;

namespace MCP.Executor
{
    public class MoveToHandler : IActionHandler
    {
        private ActionInstance action;
        private NavMeshAgent agent;
        private float interactionRange;
        private Vector3 _lastEntityPos = new Vector3(float.PositiveInfinity, 0, 0);
        private const float kRepathThreshold = 0.5f;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                return;
            }

            agent = player.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.UNREACHABLE;
                return;
            }

            interactionRange = 1.5f;

            Vector3 rawDestination = action.Target.Position;
            _lastEntityPos = rawDestination;

            // Sample the NavMesh for the nearest walkable point to the target
            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(rawDestination, out navHit, 5f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[MoveToHandler] No NavMesh within 5m of {rawDestination}. Failing.");
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.UNREACHABLE;
                return;
            }

            Vector3 destination = navHit.position;
            Debug.Log($"[MoveToHandler] Sampled NavMesh: {rawDestination} → {destination}");

            // Use synchronous path calculation to avoid background-mode async throttling.
            var navPath = new NavMeshPath();
            bool found = NavMesh.CalculatePath(agent.transform.position, destination, NavMesh.AllAreas, navPath);
            Debug.Log($"[MoveToHandler] CalculatePath from={agent.transform.position} to={destination} found={found} status={navPath.status} corners={navPath.corners.Length}");
            if (!found || navPath.status == NavMeshPathStatus.PathInvalid)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.UNREACHABLE;
                return;
            }

            bool pathSet = agent.SetPath(navPath);
            Debug.Log($"[MoveToHandler] SetPath={pathSet} pathPending={agent.pathPending} remainingDist={agent.remainingDistance}");
            action.Status = ActionStatus.Running;
        }

        public void UpdateAction()
        {
            if (action == null || action.Status != ActionStatus.Running)
                return;

            // Timeout check
            if (Time.time - action.CreatedAt > action.Timeout)
            {
                agent.ResetPath();
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.ACTION_TIMEOUT;
                return;
            }

            // Re-path only when target entity has moved significantly
            if (action.Target.Type == TargetType.Entity && action.Target.EntityObject != null)
            {
                Vector3 entityPos = action.Target.EntityObject.transform.position;
                if (Vector3.Distance(entityPos, _lastEntityPos) > kRepathThreshold)
                {
                    _lastEntityPos = entityPos;
                    var rePath = new NavMeshPath();
                    if (NavMesh.CalculatePath(agent.transform.position, entityPos, NavMesh.AllAreas, rePath))
                        agent.SetPath(rePath);
                }
            }

            // Check if arrived (pathPending will always be false with SetPath)
            if (!agent.pathPending && agent.remainingDistance <= interactionRange)
            {
                action.Status = ActionStatus.Completed;
                action.Result = new { message = "Arrived at destination." };
            }
        }

        public void Cancel()
        {
            if (agent != null)
                agent.ResetPath();

            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
