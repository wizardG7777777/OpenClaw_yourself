namespace MCP.Gateway
{
    public class ValidationResult
    {
        public bool IsValid;
        public string ErrorField;
        public string ErrorValue;
        public string ErrorReason;
        public string Suggestion;

        public static ValidationResult Success()
        {
            return new ValidationResult { IsValid = true };
        }

        public static ValidationResult Failure(string field, string value, string reason, string suggestion = null)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorField = field,
                ErrorValue = value,
                ErrorReason = reason,
                Suggestion = suggestion
            };
        }
    }
}
