using DocumentOps.Core.Enums;

namespace DocumentOps.Core.Entities;

public class ValidationDiscrepancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RuleCode { get; set; } = string.Empty;
    public DiscrepancySeverity Severity { get; set; } = DiscrepancySeverity.Warning;
    public string Message { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public bool IsResolved { get; set; } = false;
    public string? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
