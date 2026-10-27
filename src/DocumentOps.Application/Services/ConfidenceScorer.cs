using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.Services;

public class ConfidenceScorer : IConfidenceScorer
{
    public double CalculateScore(InvoiceData invoice, IReadOnlyList<ValidationDiscrepancy> discrepancies)
    {
        double score = 0.0;

        if (!string.IsNullOrWhiteSpace(invoice.InvoiceNumber)) score += 0.20;
        if (!string.IsNullOrWhiteSpace(invoice.VendorName)) score += 0.20;
        if (invoice.IssueDate.HasValue) score += 0.15;
        if (invoice.TotalAmount > 0) score += 0.20;

        // Arithmetic check
        decimal expectedTotal = invoice.Subtotal + invoice.TaxAmount;
        if (Math.Abs(expectedTotal - invoice.TotalAmount) <= 0.01m && invoice.TotalAmount > 0)
        {
            score += 0.15;
        }

        // Line items check
        if (invoice.LineItems != null && invoice.LineItems.Count > 0)
        {
            decimal lineTotalSum = invoice.LineItems.Sum(li => li.LineTotal);
            if (Math.Abs(lineTotalSum - invoice.Subtotal) <= 0.05m)
            {
                score += 0.10;
            }
        }
        else
        {
            // If no line items, grant partial credit if subtotal matches total - tax
            score += 0.05;
        }

        // Deduct for discrepancies
        foreach (var disc in discrepancies.Where(d => !d.IsResolved))
        {
            if (disc.Severity == DiscrepancySeverity.Error)
            {
                score -= 0.35;
            }
            else if (disc.Severity == DiscrepancySeverity.Warning)
            {
                score -= 0.10;
            }
        }

        return Math.Clamp(Math.Round(score, 2), 0.0, 1.0);
    }
}
