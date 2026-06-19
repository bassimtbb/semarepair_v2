namespace SearchService.Models;

// See docs/SemaRepair_Architecture.md section 5.10 Rule 12 / section 9.
public enum ValidationResultType
{
    Valid,
    TooVague,
    RedirectToFaultCode,
}

public class ValidationResult
{
    public ValidationResultType Type { get; init; }
    public string? Reason { get; init; }
    public string? FaultCode { get; init; }

    public static ValidationResult Valid() =>
        new() { Type = ValidationResultType.Valid };

    public static ValidationResult TooVague(string reason) =>
        new() { Type = ValidationResultType.TooVague, Reason = reason };

    public static ValidationResult RedirectToFaultCode(string faultCode) =>
        new() { Type = ValidationResultType.RedirectToFaultCode, FaultCode = faultCode };
}
