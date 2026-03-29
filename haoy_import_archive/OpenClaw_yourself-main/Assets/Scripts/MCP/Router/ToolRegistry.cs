using System.Collections.Generic;
using MCP.Executor;

namespace MCP.Router
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolDefinition> tools = new Dictionary<string, ToolDefinition>();

        public void RegisterTool(ToolDefinition definition)
        {
            if (definition != null && !string.IsNullOrEmpty(definition.ToolName))
                tools[definition.ToolName] = definition;
        }

        public ToolDefinition GetTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return null;
            tools.TryGetValue(toolName, out var def);
            return def;
        }

        public bool IsToolRegistered(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && tools.ContainsKey(toolName);
        }

        public IReadOnlyCollection<string> GetAllowedTools()
        {
            return tools.Keys;
        }

        public void RegisterMVPTools()
        {
            // Queries (non-exclusive)
            RegisterTool(new ToolDefinition(
                "get_player_state", "Returns current player position, health, and status",
                isExclusive: false,
                requiredParams: new string[] { },
                optionalParams: new string[] { "include_inventory" }));

            RegisterTool(new ToolDefinition(
                "get_world_summary", "Returns a high-level summary of the world state",
                isExclusive: false));

            RegisterTool(new ToolDefinition(
                "get_nearby_entities", "Returns entities near the player",
                isExclusive: false,
                requiredParams: new string[] { },
                optionalParams: new string[] { "radius", "entity_types", "interactable_only" }));

            RegisterTool(new ToolDefinition(
                "get_inventory", "Returns the player's inventory contents",
                isExclusive: false));

            // Actions (exclusive)
            RegisterTool(new ToolDefinition(
                "move_to", "Move the player to a target location or entity",
                isExclusive: true, defaultTimeout: 30f,
                requiredParams: new string[] { "target_id" },
                optionalParams: new string[] { "timeout" },
                handlerType: typeof(MoveToHandler)));

            RegisterTool(new ToolDefinition(
                "interact_with", "Interact with a target entity",
                isExclusive: true, defaultTimeout: 10f,
                requiredParams: new string[] { "target_id" },
                optionalParams: new string[] { "interaction_type", "timeout" },
                handlerType: typeof(InteractWithHandler)));

            RegisterTool(new ToolDefinition(
                "use_tool_on", "Use an inventory tool on a target entity",
                isExclusive: true, defaultTimeout: 15f,
                requiredParams: new string[] { "tool_id", "target_id" },
                optionalParams: new string[] { "timeout" },
                handlerType: typeof(UseToolOnHandler)));

            RegisterTool(new ToolDefinition(
                "talk_to_npc", "Initiate dialogue with an NPC",
                isExclusive: true, defaultTimeout: 20f,
                requiredParams: new string[] { "npc_id" },
                optionalParams: new string[] { "dialogue_option", "timeout" },
                handlerType: typeof(TalkToNpcHandler)));

            RegisterTool(new ToolDefinition(
                "equip_item", "Equip an item from inventory",
                isExclusive: true, defaultTimeout: 5f,
                requiredParams: new string[] { "item_id" },
                optionalParams: new string[] { "timeout" },
                handlerType: typeof(EquipItemHandler)));
        }
    }
}
