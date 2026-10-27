using System.Text;
using System.Text.Json;
using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;

namespace DocumentOps.Infrastructure.Services;

public class ErpExportService : IErpExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task<string> ExportAsync(DocumentRecord document, string format = "json", CancellationToken ct = default)
    {
        var inv = document.Invoice ?? new InvoiceData();

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("DocumentId,InvoiceNumber,VendorName,VendorTaxId,IssueDate,DueDate,Currency,Subtotal,TaxAmount,TotalAmount,LineItemCount,Status,ConfidenceScore,ExportedAt");
            sb.AppendLine(string.Join(",",
                EscapeCsv(document.Id.ToString()),
                EscapeCsv(inv.InvoiceNumber),
                EscapeCsv(inv.VendorName),
                EscapeCsv(inv.VendorTaxId ?? ""),
                EscapeCsv(inv.IssueDate?.ToString("yyyy-MM-dd") ?? ""),
                EscapeCsv(inv.DueDate?.ToString("yyyy-MM-dd") ?? ""),
                EscapeCsv(inv.Currency),
                inv.Subtotal.ToString("F2"),
                inv.TaxAmount.ToString("F2"),
                inv.TotalAmount.ToString("F2"),
                inv.LineItems.Count,
                document.Status.ToString(),
                document.ConfidenceScore.ToString("F2"),
                DateTimeOffset.UtcNow.ToString("O")
            ));

            if (inv.LineItems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("LineItemNumber,Description,Quantity,UnitPrice,LineTotal");
                int lineIndex = 1;
                foreach (var item in inv.LineItems)
                {
                    sb.AppendLine(string.Join(",",
                        lineIndex++,
                        EscapeCsv(item.Description),
                        item.Quantity.ToString("F2"),
                        item.UnitPrice.ToString("F2"),
                        item.LineTotal.ToString("F2")
                    ));
                }
            }

            return Task.FromResult(sb.ToString());
        }

        // Standard accounting JSON export structure
        var exportPayload = new
        {
            exportMetadata = new
            {
                exportedAt = DateTimeOffset.UtcNow,
                sourceEngine = "DocumentOps v1.0",
                schemaVersion = "2026-03-UBL-ERP",
                documentId = document.Id,
                documentHash = document.DocumentHash
            },
            accountingDocument = new
            {
                invoiceNumber = inv.InvoiceNumber,
                vendor = new
                {
                    name = inv.VendorName,
                    taxId = inv.VendorTaxId
                },
                dates = new
                {
                    issueDate = inv.IssueDate?.ToString("yyyy-MM-dd"),
                    dueDate = inv.DueDate?.ToString("yyyy-MM-dd")
                },
                financials = new
                {
                    currency = inv.Currency,
                    subtotal = inv.Subtotal,
                    taxAmount = inv.TaxAmount,
                    totalAmount = inv.TotalAmount
                },
                lineItems = inv.LineItems.Select((li, idx) => new
                {
                    lineNumber = idx + 1,
                    description = li.Description,
                    quantity = li.Quantity,
                    unitPrice = li.UnitPrice,
                    lineTotal = li.LineTotal
                }).ToList(),
                verification = new
                {
                    confidenceScore = document.ConfidenceScore,
                    approvedBy = document.ReviewedBy ?? "System",
                    approvedAt = document.ReviewedAt ?? DateTimeOffset.UtcNow,
                    auditCount = document.AuditTrail.Count
                }
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(exportPayload, JsonOptions));
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
