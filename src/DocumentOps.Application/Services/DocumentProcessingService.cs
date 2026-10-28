using System.Security.Cryptography;
using System.Text;
using DocumentOps.Application.DTOs;
using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.Services;

public class DocumentProcessingService
{
    private readonly IDocumentRepository _repository;
    private readonly IDocumentExtractor _extractor;
    private readonly IValidationEngine _validator;
    private readonly IConfidenceScorer _scorer;

    public DocumentProcessingService(
        IDocumentRepository repository,
        IDocumentExtractor extractor,
        IValidationEngine validator,
        IConfidenceScorer scorer)
    {
        _repository = repository;
        _extractor = extractor;
        _validator = validator;
        _scorer = scorer;
    }

    public async Task<DocumentRecord> ProcessDocumentAsync(DocumentUploadRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
        {
            throw new ArgumentException("Document text cannot be empty.", nameof(request.RawText));
        }

        string hash = ComputeHash(request.RawText);

        // Check for exact duplicate file hash
        var existingWithHash = await _repository.GetByHashAsync(hash, ct);
        if (existingWithHash != null)
        {
            existingWithHash.AddAudit("DuplicateUploadIgnored", "System", $"File re-uploaded with identical SHA-256 hash {hash[..8]}...");
            await _repository.UpdateAsync(existingWithHash, ct);
            return existingWithHash;
        }

        var doc = new DocumentRecord
        {
            FileName = string.IsNullOrWhiteSpace(request.FileName) ? "invoice.txt" : request.FileName,
            ContentType = request.ContentType,
            FileSizeBytes = Encoding.UTF8.GetByteCount(request.RawText),
            RawText = request.RawText,
            DocumentHash = hash,
            Status = DocumentStatus.Processing
        };

        doc.AddAudit("DocumentUploaded", "User", $"Uploaded '{doc.FileName}' ({doc.FileSizeBytes} bytes).");

        // Execute extraction
        var extraction = await _extractor.ExtractAsync(request.RawText, ct);
        doc.Invoice = extraction.Invoice;
        doc.Invoice.DocumentId = doc.Id;
        doc.AddAudit("ExtractionCompleted", _extractor.ExtractorName, $"Extracted Vendor='{doc.Invoice.VendorName}', Inv#='{doc.Invoice.InvoiceNumber}', Total={doc.Invoice.TotalAmount:F2} {doc.Invoice.Currency}.");

        // Validate
        var validation = _validator.Validate(doc.Invoice, doc);
        doc.Discrepancies = validation.Discrepancies;

        // Check for duplicate invoice number with same vendor
        if (!string.IsNullOrWhiteSpace(doc.Invoice.VendorName) && !string.IsNullOrWhiteSpace(doc.Invoice.InvoiceNumber))
        {
            var duplicateInvoice = await _repository.FindDuplicateInvoiceAsync(doc.Invoice.VendorName, doc.Invoice.InvoiceNumber, doc.Id, ct);
            if (duplicateInvoice != null)
            {
                doc.Discrepancies.Add(new ValidationDiscrepancy
                {
                    RuleCode = "INV_DUPLICATE_NUMBER",
                    Severity = DiscrepancySeverity.Error,
                    FieldName = nameof(doc.Invoice.InvoiceNumber),
                    Message = $"Duplicate invoice: Invoice '{doc.Invoice.InvoiceNumber}' from vendor '{doc.Invoice.VendorName}' was already processed on {duplicateInvoice.UploadedAt:yyyy-MM-dd}.",
                    ExpectedValue = "Unique Invoice Number per Vendor",
                    ActualValue = doc.Invoice.InvoiceNumber
                });
            }
        }

        // Calculate confidence
        doc.ConfidenceScore = _scorer.CalculateScore(doc.Invoice, doc.Discrepancies);

        // Decision logic: Auto-approval vs Human Review
        bool hasErrors = doc.Discrepancies.Any(d => d.Severity == DiscrepancySeverity.Error && !d.IsResolved);
        if (!hasErrors && doc.ConfidenceScore >= 0.85)
        {
            doc.Status = DocumentStatus.Approved;
            doc.ReviewedBy = "System Auto-Approval Engine";
            doc.ReviewedAt = DateTimeOffset.UtcNow;
            doc.AddAudit("AutoApproved", "System", $"High confidence ({doc.ConfidenceScore:P0}) and zero blocking discrepancies.");
        }
        else
        {
            doc.Status = DocumentStatus.RequiresReview;
            int errCount = doc.Discrepancies.Count(d => d.Severity == DiscrepancySeverity.Error);
            int warnCount = doc.Discrepancies.Count(d => d.Severity == DiscrepancySeverity.Warning);
            doc.AddAudit("RoutedToReview", "System", $"Requires human review (Score: {doc.ConfidenceScore:P0}, Errors: {errCount}, Warnings: {warnCount}).");
        }

        doc.ProcessedAt = DateTimeOffset.UtcNow;
        await _repository.AddAsync(doc, ct);
        return doc;
    }

    public async Task<BatchProcessingSummaryDto> ProcessBatchAsync(IEnumerable<DocumentUploadRequest> requests, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var processed = new List<DocumentRecord>();
        var errors = new List<string>();

        foreach (var req in requests)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var doc = await ProcessDocumentAsync(req, ct);
                processed.Add(doc);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to process '{req.FileName}': {ex.Message}");
            }
        }

        sw.Stop();
        int autoApproved = processed.Count(d => d.Status == DocumentStatus.Approved);
        int requiresReview = processed.Count(d => d.Status == DocumentStatus.RequiresReview);

        return new BatchProcessingSummaryDto(
            TotalReceived: processed.Count + errors.Count,
            SucceededCount: processed.Count,
            FailedCount: errors.Count,
            AutoApprovedCount: autoApproved,
            RequiresReviewCount: requiresReview,
            TotalDurationMs: sw.ElapsedMilliseconds,
            Documents: processed.Select(d => new DocumentSummaryDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileSizeBytes = d.FileSizeBytes,
                UploadedAt = d.UploadedAt,
                Status = d.Status,
                ConfidenceScore = d.ConfidenceScore,
                InvoiceNumber = d.Invoice?.InvoiceNumber,
                VendorName = d.Invoice?.VendorName,
                TotalAmount = d.Invoice?.TotalAmount,
                Currency = d.Invoice?.Currency,
                DiscrepancyCount = d.Discrepancies.Count,
                UnresolvedErrorCount = d.Discrepancies.Count(disc => disc.Severity == DiscrepancySeverity.Error && !disc.IsResolved)
            }).ToList(),
            Errors: errors
        );
    }

    private static string ComputeHash(string text)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
