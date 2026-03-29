using Newtonsoft.Json;

namespace MCP.Core
{
    public class MCPResponse
    {
        [JsonProperty("ok")]
        public bool Ok;

        [JsonProperty("action_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ActionId;

        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status;

        [JsonProperty("cancelled_action_id", NullValueHandling = NullValueHandling.Ignore)]
        public string CancelledActionId;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data;

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public MCPError Error;
    }
}
