using System.Collections.Concurrent;
using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Infrastructure.Persistence;

public class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<Guid, DocumentRecord> _documents = new();

    public InMemoryDocumentRepository()
    {
        SeedSampleData();
    }

    public Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _documents.TryGetValue(id, out var doc);
        return Task.FromResult(doc);
    }

    public Task<DocumentRecord?> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        var doc = _documents.Values.FirstOrDefault(d => d.DocumentHash.Equals(hash, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(doc);
    }

    public Task<DocumentRecord?> FindDuplicateInvoiceAsync(string vendorName, string invoiceNumber, Guid excludeId, CancellationToken ct = default)
    {
        var doc = _documents.Values.FirstOrDefault(d =>
            d.Id != excludeId &&
            d.Invoice != null &&
            d.Invoice.VendorName.Equals(vendorName, StringComparison.OrdinalIgnoreCase) &&
            d.Invoice.InvoiceNumber.Equals(invoiceNumber, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(doc);
    }

    public Task<IReadOnlyList<DocumentRecord>> ListAsync(
        DocumentStatus? status = null,
        string? search = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        IEnumerable<DocumentRecord> query = _documents.Values.OrderByDescending(d => d.UploadedAt);

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(d =>
                d.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (d.Invoice != null && (
                    d.Invoice.InvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    d.Invoice.VendorName.Contains(term, StringComparison.OrdinalIgnoreCase)
                )));
        }

        var results = query.Skip(skip).Take(take).ToList();
        return Task.FromResult<IReadOnlyList<DocumentRecord>>(results);
    }

    public Task<int> CountAsync(DocumentStatus? status = null, string? search = null, CancellationToken ct = default)
    {
        IEnumerable<DocumentRecord> query = _documents.Values;

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(d =>
                d.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (d.Invoice != null && (
                    d.Invoice.InvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    d.Invoice.VendorName.Contains(term, StringComparison.OrdinalIgnoreCase)
                )));
        }

        return Task.FromResult(query.Count());
    }

    public Task AddAsync(DocumentRecord doc, CancellationToken ct = default)
    {
        _documents[doc.Id] = doc;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DocumentRecord doc, CancellationToken ct = default)
    {
        _documents[doc.Id] = doc;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _documents.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    private void SeedSampleData()
    {
        // 1. Acme Cloud - Auto-Approved
        var doc1 = new DocumentRecord
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FileName = "acme-cloud-invoice-oct2026.txt",
            ContentType = "text/plain",
            FileSizeBytes = 1024,
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ProcessedAt = DateTimeOffset.UtcNow.AddDays(-2),
            Status = DocumentStatus.Approved,
            ConfidenceScore = 0.98,
            DocumentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ReviewedBy = "System Auto-Approval Engine",
            ReviewedAt = DateTimeOffset.UtcNow.AddDays(-2),
            RawText = @"Acme Cloud Services Inc.
Tax ID: US-EIN-9481920
INVOICE
Invoice #: INV-2026-8891
Date: 2026-10-15
Due Date: 2026-11-15

Cloud Kubernetes Compute Engine    1    $1,000.00    $1,000.00
Managed PostgreSQL Database        1      $200.00      $200.00

Subtotal: $1,200.00
Tax: $240.00
Total: $1,440.00",
            Invoice = new InvoiceData
            {
                Id = Guid.NewGuid(),
                DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                InvoiceNumber = "INV-2026-8891",
                VendorName = "Acme Cloud Services Inc.",
                VendorTaxId = "US-EIN-9481920",
                IssueDate = new DateTime(2026, 10, 15),
                DueDate = new DateTime(2026, 11, 15),
                Currency = "USD",
                Subtotal = 1200.00m,
                TaxAmount = 240.00m,
                TotalAmount = 1440.00m,
                LineItems = new List<InvoiceLineItem>
                {
                    new() { Description = "Cloud Kubernetes Compute Engine", Quantity = 1, UnitPrice = 1000.00m, LineTotal = 1000.00m },
                    new() { Description = "Managed PostgreSQL Database", Quantity = 1, UnitPrice = 200.00m, LineTotal = 200.00m }
                }
            }
        };
        doc1.AddAudit("DocumentUploaded", "User", "Uploaded initial invoice.");
        doc1.AddAudit("AutoApproved", "System", "Zero blocking errors. Verified arithmetic consistency.");
        _documents[doc1.Id] = doc1;

        // 2. Pinnacle Logistics - Flagged: Arithmetic Discrepancy (Subtotal 3500 + Tax 400 != Total 4000)
        var doc2 = new DocumentRecord
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FileName = "pinnacle-freight-october.txt",
            ContentType = "text/plain",
            FileSizeBytes = 840,
            UploadedAt = DateTimeOffset.UtcNow.AddHours(-12),
            ProcessedAt = DateTimeOffset.UtcNow.AddHours(-12),
            Status = DocumentStatus.RequiresReview,
            ConfidenceScore = 0.65,
            DocumentHash = "a4b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1",
            RawText = @"Pinnacle Logistics LLC
VAT No: GB-99482104
INVOICE
Invoice Number: PL-49201
Date: 2026-10-20
Due Date: 2026-11-20

Overland Freight Transit           1    £3,500.00    £3,500.00

Subtotal: £3,500.00
Tax: £400.00
Total: £4,000.00",
            Invoice = new InvoiceData
            {
                Id = Guid.NewGuid(),
                DocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                InvoiceNumber = "PL-49201",
                VendorName = "Pinnacle Logistics LLC",
                VendorTaxId = "GB-99482104",
                IssueDate = new DateTime(2026, 10, 20),
                DueDate = new DateTime(2026, 11, 20),
                Currency = "GBP",
                Subtotal = 3500.00m,
                TaxAmount = 400.00m,
                TotalAmount = 4000.00m,
                LineItems = new List<InvoiceLineItem>
                {
                    new() { Description = "Overland Freight Transit", Quantity = 1, UnitPrice = 3500.00m, LineTotal = 3500.00m }
                }
            },
            Discrepancies = new List<ValidationDiscrepancy>
            {
                new()
                {
                    RuleCode = "INV_ARITHMETIC_MISMATCH",
                    Severity = DiscrepancySeverity.Error,
                    FieldName = "TotalAmount",
                    Message = "Cross-field arithmetic mismatch: Subtotal (£3,500.00) + Tax (£400.00) = £3,900.00, but Total is £4,000.00.",
                    ExpectedValue = "3900.00",
                    ActualValue = "4000.00"
                }
            }
        };
        doc2.AddAudit("DocumentUploaded", "User", "Uploaded freight bill.");
        doc2.AddAudit("RoutedToReview", "System", "Flagged arithmetic mismatch on total calculation.");
        _documents[doc2.Id] = doc2;

        // 3. Apex Tech - Temporal Anomaly (Due date earlier than issue date)
        var doc3 = new DocumentRecord
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            FileName = "apex-tech-servers.txt",
            ContentType = "text/plain",
            FileSizeBytes = 910,
            UploadedAt = DateTimeOffset.UtcNow.AddHours(-6),
            ProcessedAt = DateTimeOffset.UtcNow.AddHours(-6),
            Status = DocumentStatus.RequiresReview,
            ConfidenceScore = 0.75,
            DocumentHash = "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3",
            RawText = @"Apex Tech Hardware
INVOICE
Invoice #: APX-9902
Date: 2026-10-25
Due Date: 2026-10-10

Enterprise Rackmount Server 2U     2    $4,000.00    $8,000.00

Subtotal: $8,000.00
Tax: $990.00
Total: $8,990.00",
            Invoice = new InvoiceData
            {
                Id = Guid.NewGuid(),
                DocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                InvoiceNumber = "APX-9902",
                VendorName = "Apex Tech Hardware",
                IssueDate = new DateTime(2026, 10, 25),
                DueDate = new DateTime(2026, 10, 10),
                Currency = "USD",
                Subtotal = 8000.00m,
                TaxAmount = 990.00m,
                TotalAmount = 8990.00m,
                LineItems = new List<InvoiceLineItem>
                {
                    new() { Description = "Enterprise Rackmount Server 2U", Quantity = 2, UnitPrice = 4000.00m, LineTotal = 8000.00m }
                }
            },
            Discrepancies = new List<ValidationDiscrepancy>
            {
                new()
                {
                    RuleCode = "INV_DUE_BEFORE_ISSUE",
                    Severity = DiscrepancySeverity.Warning,
                    FieldName = "DueDate",
                    Message = "Due date (2026-10-10) is earlier than invoice issue date (2026-10-25).",
                    ExpectedValue = ">= 2026-10-25",
                    ActualValue = "2026-10-10"
                }
            }
        };
        doc3.AddAudit("DocumentUploaded", "User", "Uploaded server procurement invoice.");
        doc3.AddAudit("RoutedToReview", "System", "Flagged date sequence anomaly.");
        _documents[doc3.Id] = doc3;
    }
}
