using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MCP.Core;
using MCP.Entity;
using MCP.Router;

namespace MCP.Executor
{
    public static class GetWorldSummaryHandler
    {
        public static MCPResponse Handle(MCPRequest request)
        {
            var summary = new Dictionary<string, object>();

            // Scene name
            summary["scene"] = SceneManager.GetActiveScene().name;

            // Player state
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var pos = player.transform.position;
                summary["player_position"] = new Dictionary<string, float>
                {
                    { "x", pos.x }, { "y", pos.y }, { "z", pos.z }
                };
            }

            // Nearby entity count
            if (player != null && EntityRegistry.Instance != null)
            {
                var nearby = EntityRegistry.Instance.GetNearby(player.transform.position, 50f);
                summary["nearby_entity_count"] = nearby.Count;
            }
            else
            {
                summary["nearby_entity_count"] = 0;
            }

            // Active action
            summary["has_active_action"] = HasActiveAction();

            return new MCPResponse { Ok = true, Data = summary };
        }

        private static bool HasActiveAction()
        {
            var router = Object.FindAnyObjectByType<MCPRouter>();
            if (router == null) return false;
            var current = router.GetCurrentAction();
            return current != null && current.Status == ActionStatus.Running;
        }
    }
}
