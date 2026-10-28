using DocumentOps.Application.DTOs;
using DocumentOps.Application.Services;
using DocumentOps.Infrastructure.Extractors;
using DocumentOps.Infrastructure.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class BatchProcessingTests
{
    [TestMethod]
    public async Task ProcessBatchAsync_MixedBatch_ProcessesConcurrentlyAndIsolatesErrors()
    {
        var repo = new InMemoryDocumentRepository();
        var extractor = new RegexDocumentExtractor();
        var validator = new ValidationEngine();
        var scorer = new ConfidenceScorer();
        var service = new DocumentProcessingService(repo, extractor, validator, scorer);

        var batch = new List<DocumentUploadRequest>
        {
            new()
            {
                FileName = "valid_invoice_1.txt",
                RawText = "Acme Corp\nInvoice #: INV-B1\nDate: 2026-10-01\nTotal: $500.00\nSubtotal: $500.00\nTax: $0.00"
            },
            new()
            {
                FileName = "corrupted_empty.txt",
                RawText = "" // Empty text triggers ArgumentException
            },
            new()
            {
                FileName = "valid_invoice_2.txt",
                RawText = "Apex Consulting\nInvoice #: INV-B2\nDate: 2026-10-02\nTotal: $1,200.00\nSubtotal: $1,000.00\nTax: $200.00"
            }
        };

        var summary = await service.ProcessBatchAsync(batch);

        Assert.AreEqual(3, summary.TotalReceived);
        Assert.AreEqual(2, summary.SucceededCount);
        Assert.AreEqual(1, summary.FailedCount);
        Assert.AreEqual(1, summary.Errors.Count);
        Assert.IsTrue(summary.Errors[0].Contains("corrupted_empty.txt"));
        Assert.IsTrue(summary.TotalDurationMs >= 0);
    }
}
