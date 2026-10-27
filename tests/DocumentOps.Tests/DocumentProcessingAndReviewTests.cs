using DocumentOps.Application.DTOs;
using DocumentOps.Application.Services;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;
using DocumentOps.Infrastructure.Extractors;
using DocumentOps.Infrastructure.Persistence;
using DocumentOps.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class DocumentProcessingAndReviewTests
{
    private InMemoryDocumentRepository _repo = null!;
    private RegexDocumentExtractor _extractor = null!;
    private ValidationEngine _validator = null!;
    private ConfidenceScorer _scorer = null!;
    private ErpExportService _exportService = null!;
    private DocumentProcessingService _processingService = null!;
    private DocumentReviewService _reviewService = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryDocumentRepository();
        _extractor = new RegexDocumentExtractor();
        _validator = new ValidationEngine();
        _scorer = new ConfidenceScorer();
        _exportService = new ErpExportService();
        _processingService = new DocumentProcessingService(_repo, _extractor, _validator, _scorer);
        _reviewService = new DocumentReviewService(_repo, _validator, _scorer, _exportService);
    }

    [TestMethod]
    public async Task ProcessDocumentAsync_ValidInvoice_AutoApproves()
    {
        var request = new DocumentUploadRequest
        {
            FileName = "valid-saas-bill.txt",
            RawText = @"SaaS Metrics Inc.
INVOICE
Invoice #: INV-9901
Date: 2026-10-01
Due Date: 2026-10-31

Analytics Pro Subscription    1    $500.00    $500.00

Subtotal: $500.00
Tax: $50.00
Total: $550.00"
        };

        var doc = await _processingService.ProcessDocumentAsync(request);

        Assert.IsNotNull(doc);
        Assert.AreEqual(DocumentStatus.Approved, doc.Status);
        Assert.IsTrue(doc.ConfidenceScore >= 0.85);
        Assert.IsNotNull(doc.ReviewedBy);
        Assert.IsTrue(doc.ReviewedBy.Contains("Auto-Approval"));
    }

    [TestMethod]
    public async Task ProcessDocumentAsync_ArithmeticMismatch_RoutesToRequiresReview()
    {
        var request = new DocumentUploadRequest
        {
            FileName = "faulty-freight.txt",
            RawText = @"FastFreight Logistics
INVOICE
Invoice #: FF-120
Date: 2026-10-05

Courier Transit    1    $200.00    $200.00

Subtotal: $200.00
Tax: $20.00
Total: $250.00" // Mismatch: 200 + 20 != 250
        };

        var doc = await _processingService.ProcessDocumentAsync(request);

        Assert.AreEqual(DocumentStatus.RequiresReview, doc.Status);
        Assert.IsTrue(doc.Discrepancies.Any(d => d.RuleCode == "INV_ARITHMETIC_MISMATCH"));
    }

    [TestMethod]
    public async Task ProcessDocumentAsync_DuplicateHash_ReturnsExistingWithoutDuplicate()
    {
        var request = new DocumentUploadRequest
        {
            FileName = "duplicate-test.txt",
            RawText = @"Original Supplier Inc.
Invoice #: ORIG-101
Date: 2026-10-10
Total: $100.00"
        };

        var doc1 = await _processingService.ProcessDocumentAsync(request);
        var doc2 = await _processingService.ProcessDocumentAsync(request);

        Assert.AreEqual(doc1.Id, doc2.Id);
    }

    [TestMethod]
    public async Task ApproveAsync_WithUnresolvedBlockingError_ThrowsInvalidOperationException()
    {
        // Pinnacle logistics seeded sample has an unresolved arithmetic error
        Guid pinnacleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await _reviewService.ApproveAsync(pinnacleId, "Auditor John");
        });
    }

    [TestMethod]
    public async Task UpdateReviewAsync_FixesErrorAndAllowsApproval()
    {
        Guid pinnacleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Correct total to £3,900.00 (Subtotal 3500 + Tax 400)
        var updateRequest = new DocumentReviewUpdateRequest
        {
            ReviewerName = "Auditor Sarah",
            TotalAmount = 3900.00m,
            ResolutionComment = "Adjusted total amount to reflect subtotal + tax."
        };

        var updatedDoc = await _reviewService.UpdateReviewAsync(pinnacleId, updateRequest);
        Assert.IsFalse(updatedDoc.Discrepancies.Any(d => d.Severity == DiscrepancySeverity.Error));

        // Now approval must succeed
        var approvedDoc = await _reviewService.ApproveAsync(pinnacleId, "Auditor Sarah");
        Assert.AreEqual(DocumentStatus.Approved, approvedDoc.Status);
        Assert.AreEqual("Auditor Sarah", approvedDoc.ReviewedBy);
    }

    [TestMethod]
    public async Task ExportAsync_ApprovedDocument_ProducesValidJsonAndMarksExported()
    {
        Guid acmeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string json = await _reviewService.ExportAsync(acmeId, "json");

        Assert.IsFalse(string.IsNullOrWhiteSpace(json));
        Assert.IsTrue(json.Contains("INV-2026-8891"));
        Assert.IsTrue(json.Contains("Acme Cloud Services Inc."));

        var exportedDoc = await _repo.GetByIdAsync(acmeId);
        Assert.AreEqual(DocumentStatus.Exported, exportedDoc!.Status);
    }

    [TestMethod]
    public async Task ExportAsync_CsvFormat_ProducesValidHeadersAndRows()
    {
        Guid acmeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string csv = await _reviewService.ExportAsync(acmeId, "csv");

        Assert.IsFalse(string.IsNullOrWhiteSpace(csv));
        Assert.IsTrue(csv.Contains("DocumentId,InvoiceNumber,VendorName"));
        Assert.IsTrue(csv.Contains("Acme Cloud Services Inc."));
        Assert.IsTrue(csv.Contains("Cloud Kubernetes Compute Engine"));
    }

    [TestMethod]
    public async Task GetMetricsAsync_CalculatesKPIsCorrectly()
    {
        var metrics = await _reviewService.GetMetricsAsync();

        Assert.IsTrue(metrics.TotalDocuments >= 3);
        Assert.IsTrue(metrics.RequiresReviewCount >= 1);
        Assert.IsTrue(metrics.TotalApprovedSpend > 0);
    }
}
