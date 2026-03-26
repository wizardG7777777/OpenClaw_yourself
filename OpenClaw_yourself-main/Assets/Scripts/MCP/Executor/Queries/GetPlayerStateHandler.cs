using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MCP.Core;
using MCP.Router;

namespace MCP.Executor
{
    public static class GetPlayerStateHandler
    {
        public static MCPResponse Handle(MCPRequest request)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = ErrorCodes.TARGET_NOT_FOUND,
                        Message = "Player not found in scene."
                    }
                };
            }

            var pos = player.transform.position;
            var rot = player.transform.eulerAngles;

            var data = new Dictionary<string, object>
            {
                { "position", new Dictionary<string, float> { { "x", pos.x }, { "y", pos.y }, { "z", pos.z } } },
                { "rotation", new Dictionary<string, float> { { "x", rot.x }, { "y", rot.y }, { "z", rot.z } } },
                { "scene", SceneManager.GetActiveScene().name },
                { "has_active_action", HasActiveAction() }
            };

            return new MCPResponse { Ok = true, Data = data };
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
