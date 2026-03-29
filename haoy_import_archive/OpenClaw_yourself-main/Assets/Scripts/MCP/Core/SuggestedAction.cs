using System.Collections.Generic;
using Newtonsoft.Json;

namespace MCP.Core
{
    public class SuggestedAction
    {
        [JsonProperty("tool")]
        public string Tool;

        [JsonProperty("args")]
        public Dictionary<string, object> Args;
    }
}
