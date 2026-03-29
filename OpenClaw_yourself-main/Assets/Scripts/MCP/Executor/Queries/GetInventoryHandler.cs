using System.Collections.Generic;
using MCP.Core;

namespace MCP.Executor
{
    public static class GetInventoryHandler
    {
        public static MCPResponse Handle(MCPRequest request)
        {
            var items = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "item_id", "wrench" }, { "display_name", "扳手" }, { "quantity", 1 } },
                new Dictionary<string, object> { { "item_id", "shovel" }, { "display_name", "铲子" }, { "quantity", 1 } },
                new Dictionary<string, object> { { "item_id", "postcard" }, { "display_name", "明信片" }, { "quantity", 1 } }
            };

            return new MCPResponse { Ok = true, Data = new Dictionary<string, object> { { "items", items } } };
        }
    }
}
