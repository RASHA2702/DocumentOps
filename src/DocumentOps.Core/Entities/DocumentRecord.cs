using DocumentOps.Core.Enums;

namespace DocumentOps.Core.Entities;

public class DocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    public long FileSizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public double ConfidenceScore { get; set; } = 0.0;
    public string RawText { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public InvoiceData? Invoice { get; set; }
    public List<ValidationDiscrepancy> Discrepancies { get; set; } = new();
    public List<DocumentAuditLog> AuditTrail { get; set; } = new();

    public void AddAudit(string action, string actor, string details)
    {
        AuditTrail.Add(new DocumentAuditLog
        {
            Action = action,
            Actor = actor,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
