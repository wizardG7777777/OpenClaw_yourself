using System.Collections.Generic;
using MCP.Core;

namespace MCP.Executor
{
    public static class GetInventoryHandler
    {
        public static MCPResponse Handle(MCPRequest request)
        {
            var registry = ItemRegistry.Instance;
            if (registry == null)
            {
                return new MCPResponse { Ok = false, Error = new MCPError { Code = "NO_REGISTRY", Message = "ItemRegistry not found." } };
            }

            var items = new List<Dictionary<string, object>>();
            foreach (Item item in registry.GetAll())
            {
                items.Add(new Dictionary<string, object>
                {
                    { "item_id", item.itemId },
                    { "display_name", item.displayName },
                    { "description", item.description },
                    { "type", item.itemType.ToString() },
                    { "quantity", item.quantity },
                    { "is_usable", item.isUsable },
                    { "is_equippable", item.isEquippable }
                });
            }

            return new MCPResponse { Ok = true, Data = new Dictionary<string, object> { { "items", items } } };
        }
    }
}
