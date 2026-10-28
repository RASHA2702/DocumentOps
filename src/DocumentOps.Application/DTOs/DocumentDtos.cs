using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.DTOs;

public class DocumentUploadRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    public string RawText { get; set; } = string.Empty;
}

public class DocumentSummaryDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DocumentStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public double ConfidenceScore { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? VendorName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public int DiscrepancyCount { get; set; }
    public int UnresolvedErrorCount { get; set; }
}

public class DocumentDetailDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DocumentStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public double ConfidenceScore { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public InvoiceData? Invoice { get; set; }
    public List<ValidationDiscrepancy> Discrepancies { get; set; } = new();
    public List<DocumentAuditLog> AuditTrail { get; set; } = new();
}

public class DocumentReviewUpdateRequest
{
    public string ReviewerName { get; set; } = "Reviewer";
    public string? InvoiceNumber { get; set; }
    public string? VendorName { get; set; }
    public string? VendorTaxId { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Notes { get; set; }
    public List<Guid>? ResolvedDiscrepancyIds { get; set; }
    public string? ResolutionComment { get; set; }
}

public class MetricsSummaryDto
{
    public int TotalDocuments { get; set; }
    public int RequiresReviewCount { get; set; }
    public int ApprovedCount { get; set; }
    public int ExportedCount { get; set; }
    public int RejectedCount { get; set; }
    public double AutoApprovalRatePercent { get; set; }
    public double AverageConfidencePercent { get; set; }
    public decimal TotalApprovedSpend { get; set; }
}

public record BatchProcessingSummaryDto(
    int TotalReceived,
    int SucceededCount,
    int FailedCount,
    int AutoApprovedCount,
    int RequiresReviewCount,
    long TotalDurationMs,
    IReadOnlyList<DocumentSummaryDto> Documents,
    IReadOnlyList<string> Errors
);
