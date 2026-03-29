using System.Collections.Generic;
using MCP.Core;

namespace MCP.Entity
{
    public class ResolveResult
    {
        public bool Success;
        public ResolvedTarget Target;
        public string ErrorCode;
        public string Message;
        public List<CandidateInfo> Candidates;

        public static ResolveResult Ok(ResolvedTarget target)
        {
            return new ResolveResult { Success = true, Target = target };
        }

        public static ResolveResult Error(string code, string message, List<CandidateInfo> candidates = null)
        {
            return new ResolveResult
            {
                Success = false,
                ErrorCode = code,
                Message = message,
                Candidates = candidates
            };
        }
    }
}
