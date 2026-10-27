using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;
using DocumentOps.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class ErpExportServiceTests
{
    private readonly ErpExportService _exportService = new();

    [TestMethod]
    [DataRow("=1+1", "'=1+1")]
    [DataRow("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
    [DataRow("-100", "'-100")]
    [DataRow("@SUM(A1:A5)", "'@SUM(A1:A5)")]
    [DataRow("\ttab_attack", "'\ttab_attack")]
    public void EscapeCsv_SanitizesFormulaInjectionPrefixes(string input, string expected)
    {
        var result = ErpExportService.EscapeCsv(input);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task ExportAsync_CsvFormatWithFormulaPayload_NeutralizesPayload()
    {
        var doc = new DocumentRecord
        {
            Status = DocumentStatus.Approved,
            ConfidenceScore = 0.98,
            Invoice = new InvoiceData
            {
                InvoiceNumber = "=HYPERLINK(\"http://malicious.site\")",
                VendorName = "+Malicious Vendor",
                Subtotal = 100m,
                TaxAmount = 20m,
                TotalAmount = 120m,
                Currency = "USD",
                LineItems = new List<InvoiceLineItem>
                {
                    new() { Description = "@SUM(A1)", Quantity = 1, UnitPrice = 100m, LineTotal = 100m }
                }
            }
        };

        var csv = await _exportService.ExportAsync(doc, "csv");

        // Assert single-quote prefix is inserted before formula characters
        Assert.IsTrue(csv.Contains("\"'=HYPERLINK(\"\"http://malicious.site\"\")\""));
        Assert.IsTrue(csv.Contains("'+Malicious Vendor"));
        Assert.IsTrue(csv.Contains("'@SUM(A1)"));
    }

    [TestMethod]
    public async Task ExportAsync_JsonFormat_GeneratesValidErpAccountingPayload()
    {
        var doc = new DocumentRecord
        {
            Status = DocumentStatus.Approved,
            ConfidenceScore = 0.95,
            ReviewedBy = "Lead Auditor",
            ReviewedAt = DateTimeOffset.UtcNow,
            Invoice = new InvoiceData
            {
                InvoiceNumber = "INV-2026-999",
                VendorName = "Reliable Cloud LLC",
                Subtotal = 500m,
                TaxAmount = 100m,
                TotalAmount = 600m,
                Currency = "USD"
            }
        };

        var json = await _exportService.ExportAsync(doc, "json");

        Assert.IsTrue(json.Contains("\"schemaVersion\": \"2026-03-UBL-ERP\""));
        Assert.IsTrue(json.Contains("\"invoiceNumber\": \"INV-2026-999\""));
        Assert.IsTrue(json.Contains("\"vendor\": {"));
        Assert.IsTrue(json.Contains("\"totalAmount\": 600"));
        Assert.IsTrue(json.Contains("\"approvedBy\": \"Lead Auditor\""));
    }
}
