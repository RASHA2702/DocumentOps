using DocumentOps.Infrastructure.Extractors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class RegexDocumentExtractorTests
{
    private readonly RegexDocumentExtractor _extractor = new();

    [TestMethod]
    public async Task ExtractAsync_StandardInvoice_ExtractsAllFieldsCorrectly()
    {
        string rawText = @"TechCorp Solutions Inc.
Tax ID: US-EIN-1234567
INVOICE
Invoice #: INV-2026-101
Date: 2026-10-01
Due Date: 2026-10-31

Cloud Hosting Cluster        1    $1,200.00    $1,200.00
Database Backup Storage      2      $150.00      $300.00

Subtotal: $1,500.00
Tax: $150.00
Total: $1,650.00";

        var result = await _extractor.ExtractAsync(rawText);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Invoice);
        Assert.AreEqual("TechCorp Solutions Inc.", result.Invoice.VendorName);
        Assert.AreEqual("INV-2026-101", result.Invoice.InvoiceNumber);
        Assert.AreEqual("US-EIN-1234567", result.Invoice.VendorTaxId);
        Assert.AreEqual(new DateTime(2026, 10, 1), result.Invoice.IssueDate);
        Assert.AreEqual(new DateTime(2026, 10, 31), result.Invoice.DueDate);
        Assert.AreEqual("USD", result.Invoice.Currency);
        Assert.AreEqual(1500.00m, result.Invoice.Subtotal);
        Assert.AreEqual(150.00m, result.Invoice.TaxAmount);
        Assert.AreEqual(1650.00m, result.Invoice.TotalAmount);
        Assert.AreEqual(2, result.Invoice.LineItems.Count);
        Assert.IsTrue(result.ExtractorConfidence >= 0.85);
    }

    [TestMethod]
    public async Task ExtractAsync_EuroInvoiceWithAltDateFormat_ExtractsEuroAndDates()
    {
        string rawText = @"Berlin Digital GmbH
VAT No: DE-9918231
Rechnung / INVOICE
Invoice Number: DE-88192
Issue Date: October 15, 2026
Payment Due: November 15, 2026

Consulting Engineering      10      €120.00    €1,200.00

Subtotal: €1,200.00
VAT: €228.00
Total Amount: €1,428.00";

        var result = await _extractor.ExtractAsync(rawText);

        Assert.AreEqual("EUR", result.Invoice.Currency);
        Assert.AreEqual("DE-88192", result.Invoice.InvoiceNumber);
        Assert.AreEqual(new DateTime(2026, 10, 15), result.Invoice.IssueDate);
        Assert.AreEqual(new DateTime(2026, 11, 15), result.Invoice.DueDate);
        Assert.AreEqual(1200.00m, result.Invoice.Subtotal);
        Assert.AreEqual(228.00m, result.Invoice.TaxAmount);
        Assert.AreEqual(1428.00m, result.Invoice.TotalAmount);
    }

    [TestMethod]
    public async Task ExtractAsync_WhenSubtotalMissing_ComputesFromLineItems()
    {
        string rawText = @"Speedy Courier Ltd
Invoice #: SC-401
Date: 2026-10-10

Same Day Delivery          1       $40.00       $40.00
Express Packaging          2       $10.00       $20.00

Tax: $6.00";

        var result = await _extractor.ExtractAsync(rawText);

        Assert.AreEqual(60.00m, result.Invoice.Subtotal);
        Assert.AreEqual(6.00m, result.Invoice.TaxAmount);
        Assert.AreEqual(66.00m, result.Invoice.TotalAmount);
    }
}
