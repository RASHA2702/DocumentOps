using DocumentOps.Application.Services;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class ConfidenceScorerTests
{
    private readonly ConfidenceScorer _scorer = new();

    [TestMethod]
    public void CalculateScore_CompleteAndAccurateInvoice_ReturnsHighScore()
    {
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-100",
            VendorName = "Globex Corp",
            IssueDate = DateTime.UtcNow,
            Subtotal = 100.00m,
            TaxAmount = 10.00m,
            TotalAmount = 110.00m,
            LineItems = new List<InvoiceLineItem>
            {
                new() { Description = "Widget", Quantity = 1, UnitPrice = 100.00m, LineTotal = 100.00m }
            }
        };

        var score = _scorer.CalculateScore(invoice, Array.Empty<ValidationDiscrepancy>());

        Assert.IsTrue(score >= 0.90, $"Score {score} expected to be >= 0.90");
        Assert.IsTrue(score <= 1.0);
    }

    [TestMethod]
    public void CalculateScore_UnresolvedBlockingError_PenalizesScore()
    {
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-101",
            VendorName = "Globex Corp",
            IssueDate = DateTime.UtcNow,
            Subtotal = 100.00m,
            TaxAmount = 10.00m,
            TotalAmount = 150.00m // Mismatch
        };

        var discrepancies = new List<ValidationDiscrepancy>
        {
            new() { RuleCode = "INV_ARITHMETIC_MISMATCH", Severity = DiscrepancySeverity.Error, IsResolved = false }
        };

        var score = _scorer.CalculateScore(invoice, discrepancies);

        Assert.IsTrue(score < 0.60, $"Score {score} expected to be severely penalized below 0.60");
    }

    [TestMethod]
    public void CalculateScore_ResolvedError_DoesNotPenalizeScore()
    {
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-102",
            VendorName = "Globex Corp",
            IssueDate = DateTime.UtcNow,
            Subtotal = 100.00m,
            TaxAmount = 10.00m,
            TotalAmount = 110.00m
        };

        var discrepancies = new List<ValidationDiscrepancy>
        {
            new() { RuleCode = "INV_ARITHMETIC_MISMATCH", Severity = DiscrepancySeverity.Error, IsResolved = true }
        };

        var score = _scorer.CalculateScore(invoice, discrepancies);

        Assert.IsTrue(score >= 0.70);
    }
}
