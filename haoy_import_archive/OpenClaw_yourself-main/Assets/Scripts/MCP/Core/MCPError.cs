using System.Collections.Generic;
using Newtonsoft.Json;

namespace MCP.Core
{
    public class MCPError
    {
        [JsonProperty("code")]
        public string Code;

        [JsonProperty("message")]
        public string Message;

        [JsonProperty("retryable")]
        public bool Retryable;

        [JsonProperty("details", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Details;

        [JsonProperty("suggested_next_actions", NullValueHandling = NullValueHandling.Ignore)]
        public List<SuggestedAction> SuggestedNextActions;
    }
}
