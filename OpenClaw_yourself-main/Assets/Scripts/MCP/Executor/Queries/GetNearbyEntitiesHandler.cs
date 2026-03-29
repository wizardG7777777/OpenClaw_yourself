using System.Collections.Generic;
using UnityEngine;
using MCP.Core;
using MCP.Entity;

namespace MCP.Executor
{
    public static class GetNearbyEntitiesHandler
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

            float radius = 50f;
            if (request.Args != null && request.Args.TryGetValue("radius", out var radiusObj) && radiusObj != null)
                float.TryParse(radiusObj.ToString(), out radius);

            bool interactableOnly = true;
            if (request.Args != null && request.Args.TryGetValue("interactable_only", out var interObj) && interObj != null)
                bool.TryParse(interObj.ToString(), out interactableOnly);

            string[] entityTypes = null;
            if (request.Args != null && request.Args.TryGetValue("entity_types", out var typesObj) && typesObj is Newtonsoft.Json.Linq.JArray arr)
                entityTypes = arr.ToObject<string[]>();

            var registry = EntityRegistry.Instance;
            if (registry == null)
            {
                return new MCPResponse
                {
                    Ok = false,
                    Error = new MCPError
                    {
                        Code = ErrorCodes.TARGET_NOT_FOUND,
                        Message = "EntityRegistry not initialized. No entities available."
                    }
                };
            }

            var nearby = registry.GetNearby(player.transform.position, radius, entityTypes, interactableOnly);

            var entities = new List<Dictionary<string, object>>();
            foreach (var e in nearby)
            {
                float dist = Vector3.Distance(player.transform.position, e.transform.position);
                var pos = e.transform.position;
                var entry = new Dictionary<string, object>
                {
                    { "entity_id", e.runtimeId ?? e.entityId },
                    { "display_name", e.displayName },
                    { "entity_type", e.entityType },
                    { "distance", Mathf.Round(dist * 100f) / 100f },
                    { "interactable", e.interactable },
                    { "position", new Dictionary<string, float> { { "x", pos.x }, { "y", pos.y }, { "z", pos.z } } }
                };

                // Include state if the entity has IInteractable
                var interactable = e.GetComponent<IInteractable>();
                if (interactable != null)
                {
                    var state = interactable.GetState();
                    if (state != null && state.Count > 0)
                        entry["state"] = state;
                }

                entities.Add(entry);
            }

            return new MCPResponse { Ok = true, Data = new Dictionary<string, object> { { "entities", entities } } };
        }
    }
}
