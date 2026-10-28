using DocumentOps.Application.Services;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class ValidationEngineTests
{
    private readonly ValidationEngine _engine = new();

    [TestMethod]
    public void Validate_ValidInvoice_ReturnsNoDiscrepancies()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-001",
            VendorName = "Acme Corp",
            IssueDate = DateTime.UtcNow.AddDays(-5),
            DueDate = DateTime.UtcNow.AddDays(25),
            Subtotal = 100.00m,
            TaxAmount = 20.00m,
            TotalAmount = 120.00m
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Discrepancies.Count);
    }

    [TestMethod]
    public void Validate_ArithmeticMismatch_FlagsErrorDiscrepancy()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-002",
            VendorName = "Acme Corp",
            IssueDate = DateTime.UtcNow.AddDays(-2),
            DueDate = DateTime.UtcNow.AddDays(10),
            Subtotal = 100.00m,
            TaxAmount = 20.00m,
            TotalAmount = 150.00m // Mismatch: 100 + 20 != 150
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsFalse(result.IsValid);
        var disc = result.Discrepancies.FirstOrDefault(d => d.RuleCode == "INV_ARITHMETIC_MISMATCH");
        Assert.IsNotNull(disc);
        Assert.AreEqual(DiscrepancySeverity.Error, disc.Severity);
        Assert.AreEqual("120.00", disc.ExpectedValue);
        Assert.AreEqual("150.00", disc.ActualValue);
    }

    [TestMethod]
    public void Validate_DueDateBeforeIssueDate_FlagsWarningDiscrepancy()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-003",
            VendorName = "Acme Corp",
            IssueDate = new DateTime(2026, 10, 20),
            DueDate = new DateTime(2026, 10, 10), // Inverted
            Subtotal = 50.00m,
            TaxAmount = 0.00m,
            TotalAmount = 50.00m
        };

        var result = _engine.Validate(invoice, doc);

        var disc = result.Discrepancies.FirstOrDefault(d => d.RuleCode == "INV_DUE_BEFORE_ISSUE");
        Assert.IsNotNull(disc);
        Assert.AreEqual(DiscrepancySeverity.Warning, disc.Severity);
    }

    [TestMethod]
    public void Validate_MissingVendorAndInvoiceNumber_FlagsErrors()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "",
            VendorName = "",
            Subtotal = 10.00m,
            TaxAmount = 1.00m,
            TotalAmount = 11.00m
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_MISSING_NUMBER"));
        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_MISSING_VENDOR"));
    }

    [TestMethod]
    public void Validate_LineItemsSumMismatch_FlagsWarning()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-004",
            VendorName = "Acme Corp",
            Subtotal = 100.00m,
            TaxAmount = 0.00m,
            TotalAmount = 100.00m,
            LineItems = new List<InvoiceLineItem>
            {
                new() { Description = "Item 1", Quantity = 1, UnitPrice = 40.00m, LineTotal = 40.00m } // Sum is 40 != 100
            }
        };

        var result = _engine.Validate(invoice, doc);

        var disc = result.Discrepancies.FirstOrDefault(d => d.RuleCode == "INV_LINE_ITEMS_SUM_MISMATCH");
        Assert.IsNotNull(disc);
        Assert.AreEqual(DiscrepancySeverity.Warning, disc.Severity);
    }

    [TestMethod]
    public void Validate_CurrencyPrecision_WhenJpyHasCents_FlagsPrecisionWarning()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-JPY-01",
            VendorName = "Tokyo Telecom",
            Currency = "JPY",
            Subtotal = 1000.50m, // JPY cannot have fractional decimal places
            TaxAmount = 0.00m,
            TotalAmount = 1000.50m
        };

        var result = _engine.Validate(invoice, doc);

        var disc = result.Discrepancies.FirstOrDefault(d => d.RuleCode == "INV_CURRENCY_PRECISION_MISMATCH");
        Assert.IsNotNull(disc);
        Assert.AreEqual(DiscrepancySeverity.Warning, disc.Severity);
        Assert.IsTrue(disc.Message.Contains("0 decimals"));
    }

    [TestMethod]
    public void Validate_LineItems_WithInvalidQuantityAndNegativePrice_FlagsErrors()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-005",
            VendorName = "Acme Corp",
            Subtotal = 100.00m,
            TaxAmount = 0.00m,
            TotalAmount = 100.00m,
            LineItems = new List<InvoiceLineItem>
            {
                new() { Description = "Invalid Item", Quantity = 0m, UnitPrice = -10m, LineTotal = 0m }
            }
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_LINE_ITEM_INVALID_QUANTITY"));
        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_LINE_ITEM_NEGATIVE_PRICE"));
    }

    [TestMethod]
    public void Validate_LineItems_WithDuplicateDescription_FlagsWarning()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-006",
            VendorName = "Acme Corp",
            Subtotal = 200.00m,
            TaxAmount = 0.00m,
            TotalAmount = 200.00m,
            LineItems = new List<InvoiceLineItem>
            {
                new() { Description = "Cloud Hosting", Quantity = 1m, UnitPrice = 100m, LineTotal = 100m },
                new() { Description = "Cloud Hosting", Quantity = 1m, UnitPrice = 100m, LineTotal = 100m }
            }
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_DUPLICATE_LINE_ITEM"));
    }

    [TestMethod]
    public void Validate_VendorTaxId_WithMalformedSyntax_FlagsWarning()
    {
        var doc = new DocumentRecord();
        var invoice = new InvoiceData
        {
            InvoiceNumber = "INV-007",
            VendorName = "Global Vendor",
            VendorTaxId = "INVALID_TAX_ID_123!",
            Subtotal = 100m,
            TaxAmount = 20m,
            TotalAmount = 120m
        };

        var result = _engine.Validate(invoice, doc);

        Assert.IsTrue(result.Discrepancies.Any(d => d.RuleCode == "INV_TAX_ID_SYNTAX_INVALID"));
    }
}
