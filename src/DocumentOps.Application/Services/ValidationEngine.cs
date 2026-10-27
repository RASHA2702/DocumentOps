using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.Services;

public class ValidationEngine : IValidationEngine
{
    public ValidationResult Validate(InvoiceData invoice, DocumentRecord document)
    {
        var result = new ValidationResult();

        // 1. Mandatory invoice number
        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            result.Discrepancies.Add(new ValidationDiscrepancy
            {
                RuleCode = "INV_MISSING_NUMBER",
                Severity = DiscrepancySeverity.Error,
                FieldName = nameof(invoice.InvoiceNumber),
                Message = "Invoice number could not be extracted from document.",
                ExpectedValue = "Non-empty string",
                ActualValue = null
            });
        }

        // 2. Mandatory vendor name
        if (string.IsNullOrWhiteSpace(invoice.VendorName))
        {
            result.Discrepancies.Add(new ValidationDiscrepancy
            {
                RuleCode = "INV_MISSING_VENDOR",
                Severity = DiscrepancySeverity.Error,
                FieldName = nameof(invoice.VendorName),
                Message = "Vendor / Supplier name was not detected.",
                ExpectedValue = "Recognized vendor name",
                ActualValue = null
            });
        }

        // 3. Amount sanity
        if (invoice.TotalAmount <= 0)
        {
            result.Discrepancies.Add(new ValidationDiscrepancy
            {
                RuleCode = "INV_ZERO_TOTAL",
                Severity = DiscrepancySeverity.Error,
                FieldName = nameof(invoice.TotalAmount),
                Message = "Total amount is zero or negative.",
                ExpectedValue = "> 0.00",
                ActualValue = invoice.TotalAmount.ToString("F2")
            });
        }

        if (invoice.Subtotal < 0 || invoice.TaxAmount < 0)
        {
            result.Discrepancies.Add(new ValidationDiscrepancy
            {
                RuleCode = "INV_NEGATIVE_AMOUNTS",
                Severity = DiscrepancySeverity.Error,
                FieldName = "Amounts",
                Message = "Subtotal or tax amount cannot be negative.",
                ExpectedValue = ">= 0.00",
                ActualValue = $"Subtotal: {invoice.Subtotal:F2}, Tax: {invoice.TaxAmount:F2}"
            });
        }

        // 4. Arithmetic reconciliation: Subtotal + Tax == Total
        decimal calculatedTotal = invoice.Subtotal + invoice.TaxAmount;
        if (Math.Abs(calculatedTotal - invoice.TotalAmount) > 0.01m && invoice.TotalAmount > 0)
        {
            result.Discrepancies.Add(new ValidationDiscrepancy
            {
                RuleCode = "INV_ARITHMETIC_MISMATCH",
                Severity = DiscrepancySeverity.Error,
                FieldName = nameof(invoice.TotalAmount),
                Message = $"Cross-field arithmetic mismatch: Subtotal ({invoice.Subtotal:F2}) + Tax ({invoice.TaxAmount:F2}) = {calculatedTotal:F2}, but Total is {invoice.TotalAmount:F2}.",
                ExpectedValue = calculatedTotal.ToString("F2"),
                ActualValue = invoice.TotalAmount.ToString("F2")
            });
        }

        // 5. Line items sum check
        if (invoice.LineItems != null && invoice.LineItems.Count > 0)
        {
            decimal lineSum = invoice.LineItems.Sum(li => li.LineTotal);
            if (Math.Abs(lineSum - invoice.Subtotal) > 0.05m && invoice.Subtotal > 0)
            {
                result.Discrepancies.Add(new ValidationDiscrepancy
                {
                    RuleCode = "INV_LINE_ITEMS_SUM_MISMATCH",
                    Severity = DiscrepancySeverity.Warning,
                    FieldName = nameof(invoice.LineItems),
                    Message = $"Sum of line items ({lineSum:F2}) does not match subtotal ({invoice.Subtotal:F2}).",
                    ExpectedValue = invoice.Subtotal.ToString("F2"),
                    ActualValue = lineSum.ToString("F2")
                });
            }
        }

        // 6. Temporal sequence check: DueDate >= IssueDate
        if (invoice.IssueDate.HasValue && invoice.DueDate.HasValue)
        {
            if (invoice.DueDate.Value.Date < invoice.IssueDate.Value.Date)
            {
                result.Discrepancies.Add(new ValidationDiscrepancy
                {
                    RuleCode = "INV_DUE_BEFORE_ISSUE",
                    Severity = DiscrepancySeverity.Warning,
                    FieldName = nameof(invoice.DueDate),
                    Message = $"Due date ({invoice.DueDate.Value:yyyy-MM-dd}) is earlier than invoice issue date ({invoice.IssueDate.Value:yyyy-MM-dd}).",
                    ExpectedValue = $">= {invoice.IssueDate.Value:yyyy-MM-dd}",
                    ActualValue = invoice.DueDate.Value.ToString("yyyy-MM-dd")
                });
            }
        }

        // 7. Suspicious historical or future dates
        if (invoice.IssueDate.HasValue)
        {
            var now = DateTime.UtcNow;
            if (invoice.IssueDate.Value < now.AddYears(-5) || invoice.IssueDate.Value > now.AddMonths(3))
            {
                result.Discrepancies.Add(new ValidationDiscrepancy
                {
                    RuleCode = "INV_SUSPICIOUS_DATE",
                    Severity = DiscrepancySeverity.Warning,
                    FieldName = nameof(invoice.IssueDate),
                    Message = $"Invoice date ({invoice.IssueDate.Value:yyyy-MM-dd}) is outside expected operating timeframe.",
                    ExpectedValue = $"Within 5 years past and 3 months future",
                    ActualValue = invoice.IssueDate.Value.ToString("yyyy-MM-dd")
                });
            }
        }

        return result;
    }
}
