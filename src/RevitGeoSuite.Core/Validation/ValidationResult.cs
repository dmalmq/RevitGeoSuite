namespace RevitGeoSuite.Core.Validation;

public sealed class ValidationResult
{
    public ValidationResult(string code, ValidationSeverity severity, string message)
    {
        Code = code;
        Severity = severity;
        Message = message;
    }

    public string Code { get; }

    public ValidationSeverity Severity { get; }

    public string Message { get; }
}
