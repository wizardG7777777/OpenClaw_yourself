using System.Collections.Generic;
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

        /// <summary>True when the actor is an NPC rather than the player.</summary>
        private bool _isNpcActor;
        /// <summary>Cached NPC controller when actor is an NPC.</summary>
        private NpcController _npcController;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            // ── Determine actor ──────────────────────────────────────
            string actorId = null;
            if (action.Result is Dictionary<string, object> args &&
                args.TryGetValue("actor_id", out object actorObj))
            {
                actorId = actorObj?.ToString();
            }

            bool isPlayer = string.IsNullOrEmpty(actorId) || actorId == "player";

            if (!isPlayer)
            {
                StartNpcMovement(actorId);
            }
            else
            {
                StartPlayerMovement();
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Player movement (original logic, unchanged)
        // ──────────────────────────────────────────────────────────────

        private void StartPlayerMovement()
        {
            _isNpcActor = false;

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

        // ──────────────────────────────────────────────────────────────
        //  NPC movement
        // ──────────────────────────────────────────────────────────────

        private void StartNpcMovement(string actorId)
        {
            _isNpcActor = true;

            // Resolve NPC via NpcRegistry singleton
            if (NpcRegistry.Instance == null)
            {
                Debug.LogWarning("[MoveToHandler] NpcRegistry.Instance is null — cannot resolve NPC.");
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                return;
            }

            _npcController = NpcRegistry.Instance.GetByCharacterId(actorId);
            if (_npcController == null)
            {
                Debug.LogWarning($"[MoveToHandler] No NPC found with actor_id '{actorId}'.");
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                return;
            }

            Vector3 rawDestination = action.Target.Position;

            // Sample NavMesh to ensure the target is reachable
            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(rawDestination, out navHit, 5f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[MoveToHandler] No NavMesh within 5m of {rawDestination} for NPC '{actorId}'. Failing.");
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.UNREACHABLE;
                return;
            }

            Vector3 destination = navHit.position;
            Debug.Log($"[MoveToHandler] NPC '{actorId}' moving to NavMesh-sampled position: {rawDestination} → {destination}");

            // Set arrival callback so NpcController.Update() marks us complete
            ActionInstance capturedAction = action;
            _npcController.OnArrivalCallback = () =>
            {
                if (capturedAction.Status == ActionStatus.Running)
                {
                    capturedAction.Status = ActionStatus.Completed;
                    capturedAction.Result = new { message = $"NPC '{actorId}' arrived at destination." };
                    Debug.Log($"[MoveToHandler] NPC '{actorId}' arrival callback fired — action completed.");
                }
            };

            _npcController.MoveTo(destination);
            action.Status = ActionStatus.Running;
        }

        // ──────────────────────────────────────────────────────────────
        //  Update
        // ──────────────────────────────────────────────────────────────

        public void UpdateAction()
        {
            if (action == null || action.Status != ActionStatus.Running)
                return;

            // Timeout check (applies to both player and NPC)
            if (Time.time - action.CreatedAt > action.Timeout)
            {
                if (_isNpcActor)
                {
                    if (_npcController != null)
                        _npcController.StopMovement();
                }
                else
                {
                    if (agent != null)
                        agent.ResetPath();
                }

                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.ACTION_TIMEOUT;
                return;
            }

            // NPC movement completion is handled via OnArrivalCallback — nothing to poll here.
            if (_isNpcActor)
                return;

            // ── Player movement update (original logic) ──────────────

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

        // ──────────────────────────────────────────────────────────────
        //  Cancel
        // ──────────────────────────────────────────────────────────────

        public void Cancel()
        {
            if (_isNpcActor)
            {
                if (_npcController != null)
                {
                    _npcController.OnArrivalCallback = null;
                    _npcController.StopMovement();
                }
            }
            else
            {
                if (agent != null)
                    agent.ResetPath();
            }

            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
