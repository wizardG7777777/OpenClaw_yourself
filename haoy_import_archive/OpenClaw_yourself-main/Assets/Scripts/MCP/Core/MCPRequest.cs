using System.Collections.Generic;
using Newtonsoft.Json;

namespace MCP.Core
{
    public class MCPRequest
    {
        [JsonProperty("request_id")]
        public string RequestId;

        [JsonProperty("tool")]
        public string Tool;

        [JsonProperty("args")]
        public Dictionary<string, object> Args;

        [JsonProperty("player_id")]
        public string PlayerId;
    }
}
