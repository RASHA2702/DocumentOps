using DocumentOps.Application.DTOs;
using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.Services;

public class DocumentReviewService
{
    private readonly IDocumentRepository _repository;
    private readonly IValidationEngine _validator;
    private readonly IConfidenceScorer _scorer;
    private readonly IErpExportService _exportService;

    public DocumentReviewService(
        IDocumentRepository repository,
        IValidationEngine validator,
        IConfidenceScorer scorer,
        IErpExportService exportService)
    {
        _repository = repository;
        _validator = validator;
        _scorer = scorer;
        _exportService = exportService;
    }

    public async Task<DocumentRecord> UpdateReviewAsync(Guid id, DocumentReviewUpdateRequest request, CancellationToken ct = default)
    {
        var doc = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Document with ID '{id}' was not found.");

        if (doc.Status == DocumentStatus.Exported)
        {
            throw new InvalidOperationException("Cannot modify a document that has already been exported to ERP.");
        }

        doc.Invoice ??= new InvoiceData { DocumentId = doc.Id };

        // Apply edits if specified
        if (!string.IsNullOrWhiteSpace(request.VendorName)) doc.Invoice.VendorName = request.VendorName;
        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber)) doc.Invoice.InvoiceNumber = request.InvoiceNumber;
        if (!string.IsNullOrWhiteSpace(request.VendorTaxId)) doc.Invoice.VendorTaxId = request.VendorTaxId;
        if (request.IssueDate.HasValue) doc.Invoice.IssueDate = request.IssueDate.Value;
        if (request.DueDate.HasValue) doc.Invoice.DueDate = request.DueDate.Value;
        if (!string.IsNullOrWhiteSpace(request.Currency)) doc.Invoice.Currency = request.Currency;
        if (request.Subtotal.HasValue) doc.Invoice.Subtotal = request.Subtotal.Value;
        if (request.TaxAmount.HasValue) doc.Invoice.TaxAmount = request.TaxAmount.Value;
        if (request.TotalAmount.HasValue) doc.Invoice.TotalAmount = request.TotalAmount.Value;
        if (!string.IsNullOrWhiteSpace(request.Notes)) doc.Invoice.Notes = request.Notes;

        // Resolve discrepancies
        if (request.ResolvedDiscrepancyIds != null && request.ResolvedDiscrepancyIds.Count > 0)
        {
            foreach (var discId in request.ResolvedDiscrepancyIds)
            {
                var target = doc.Discrepancies.FirstOrDefault(d => d.Id == discId);
                if (target != null)
                {
                    target.IsResolved = true;
                    target.ResolvedBy = request.ReviewerName;
                    target.ResolutionNote = request.ResolutionComment ?? "Resolved by reviewer.";
                }
            }
        }

        // Re-validate to check if new discrepancies arose or existing arithmetic was resolved
        var recheck = _validator.Validate(doc.Invoice, doc);
        
        // Retain resolved status on matching rules
        foreach (var newDisc in recheck.Discrepancies)
        {
            var prior = doc.Discrepancies.FirstOrDefault(d => d.RuleCode == newDisc.RuleCode);
            if (prior != null && prior.IsResolved)
            {
                newDisc.IsResolved = true;
                newDisc.ResolvedBy = prior.ResolvedBy;
                newDisc.ResolutionNote = prior.ResolutionNote;
            }
        }
        doc.Discrepancies = recheck.Discrepancies;

        // Re-score confidence
        doc.ConfidenceScore = _scorer.CalculateScore(doc.Invoice, doc.Discrepancies);
        doc.ReviewedBy = request.ReviewerName;
        doc.ReviewedAt = DateTimeOffset.UtcNow;

        doc.AddAudit("HumanReviewEdited", request.ReviewerName, 
            $"Updated fields. Remaining unresolved errors: {doc.Discrepancies.Count(d => d.Severity == DiscrepancySeverity.Error && !d.IsResolved)}.");

        await _repository.UpdateAsync(doc, ct);
        return doc;
    }

    public async Task<DocumentRecord> ApproveAsync(Guid id, string reviewerName, CancellationToken ct = default)
    {
        var doc = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Document with ID '{id}' was not found.");

        if (doc.Status == DocumentStatus.Exported)
        {
            throw new InvalidOperationException("Document is already exported.");
        }

        int unresolvedErrors = doc.Discrepancies.Count(d => d.Severity == DiscrepancySeverity.Error && !d.IsResolved);
        if (unresolvedErrors > 0)
        {
            throw new InvalidOperationException($"Cannot approve document: {unresolvedErrors} blocking discrepancy error(s) must be resolved first.");
        }

        doc.Status = DocumentStatus.Approved;
        doc.ReviewedBy = reviewerName;
        doc.ReviewedAt = DateTimeOffset.UtcNow;
        doc.AddAudit("ApprovedByHuman", reviewerName, "Document successfully verified and marked as approved.");

        await _repository.UpdateAsync(doc, ct);
        return doc;
    }

    public async Task<DocumentRecord> RejectAsync(Guid id, string reviewerName, string reason, CancellationToken ct = default)
    {
        var doc = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Document with ID '{id}' was not found.");

        if (doc.Status == DocumentStatus.Exported)
        {
            throw new InvalidOperationException("Cannot reject a document that has already been exported to ERP.");
        }

        doc.Status = DocumentStatus.Rejected;
        doc.ReviewedBy = reviewerName;
        doc.ReviewedAt = DateTimeOffset.UtcNow;
        doc.AddAudit("RejectedByHuman", reviewerName, $"Document rejected. Reason: {reason}");

        await _repository.UpdateAsync(doc, ct);
        return doc;
    }

    public async Task<string> ExportAsync(Guid id, string format, CancellationToken ct = default)
    {
        var doc = await _repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Document with ID '{id}' was not found.");

        if (doc.Status != DocumentStatus.Approved && doc.Status != DocumentStatus.Exported)
        {
            throw new InvalidOperationException("Only approved documents can be exported to ERP systems.");
        }

        string exportPayload = await _exportService.ExportAsync(doc, format, ct);

        if (doc.Status != DocumentStatus.Exported)
        {
            doc.Status = DocumentStatus.Exported;
            doc.AddAudit("ExportedToErp", "System", $"Generated {format.ToUpperInvariant()} payload for ERP integration.");
            await _repository.UpdateAsync(doc, ct);
        }

        return exportPayload;
    }

    public async Task<MetricsSummaryDto> GetMetricsAsync(CancellationToken ct = default)
    {
        var all = await _repository.ListAsync(take: 1000, ct: ct);
        int total = all.Count;
        if (total == 0) return new MetricsSummaryDto();

        int review = all.Count(d => d.Status == DocumentStatus.RequiresReview);
        int approved = all.Count(d => d.Status == DocumentStatus.Approved);
        int exported = all.Count(d => d.Status == DocumentStatus.Exported);
        int rejected = all.Count(d => d.Status == DocumentStatus.Rejected);

        int autoApproved = all.Count(d => (d.Status == DocumentStatus.Approved || d.Status == DocumentStatus.Exported) 
                                           && d.ReviewedBy != null && d.ReviewedBy.Contains("Auto-Approval"));

        double autoRate = total > 0 ? (double)autoApproved / total * 100.0 : 0.0;
        double avgConfidence = all.Average(d => d.ConfidenceScore) * 100.0;

        decimal totalSpend = all
            .Where(d => d.Status == DocumentStatus.Approved || d.Status == DocumentStatus.Exported)
            .Sum(d => d.Invoice?.TotalAmount ?? 0);

        return new MetricsSummaryDto
        {
            TotalDocuments = total,
            RequiresReviewCount = review,
            ApprovedCount = approved,
            ExportedCount = exported,
            RejectedCount = rejected,
            AutoApprovalRatePercent = Math.Round(autoRate, 1),
            AverageConfidencePercent = Math.Round(avgConfidence, 1),
            TotalApprovedSpend = Math.Round(totalSpend, 2)
        };
    }
}
