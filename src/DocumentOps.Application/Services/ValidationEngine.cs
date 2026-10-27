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

        // 8. Currency Subunit Precision Check (ISO 4217 standard)
        if (!string.IsNullOrWhiteSpace(invoice.Currency))
        {
            var currency = invoice.Currency.Trim().ToUpperInvariant();
            int maxDecimals = GetExpectedDecimalPlaces(currency);
            if (HasExcessDecimals(invoice.TotalAmount, maxDecimals) ||
                HasExcessDecimals(invoice.Subtotal, maxDecimals) ||
                HasExcessDecimals(invoice.TaxAmount, maxDecimals))
            {
                result.Discrepancies.Add(new ValidationDiscrepancy
                {
                    RuleCode = "INV_CURRENCY_PRECISION_MISMATCH",
                    Severity = DiscrepancySeverity.Warning,
                    FieldName = nameof(invoice.Currency),
                    Message = $"Monetary amounts exceed standard ISO 4217 decimal precision ({maxDecimals} decimals) for currency '{currency}'.",
                    ExpectedValue = $"Up to {maxDecimals} decimal places",
                    ActualValue = $"Total: {invoice.TotalAmount}, Subtotal: {invoice.Subtotal}, Tax: {invoice.TaxAmount}"
                });
            }
        }

        // 9. Individual Line Item Integrity & Duplicate Detection
        if (invoice.LineItems != null && invoice.LineItems.Count > 0)
        {
            var seenDescriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < invoice.LineItems.Count; i++)
            {
                var item = invoice.LineItems[i];
                int lineNum = i + 1;

                if (item.Quantity <= 0)
                {
                    result.Discrepancies.Add(new ValidationDiscrepancy
                    {
                        RuleCode = "INV_LINE_ITEM_INVALID_QUANTITY",
                        Severity = DiscrepancySeverity.Error,
                        FieldName = $"LineItem[{lineNum}].Quantity",
                        Message = $"Line item {lineNum} ('{item.Description}') has non-positive quantity: {item.Quantity}.",
                        ExpectedValue = "> 0",
                        ActualValue = item.Quantity.ToString("F2")
                    });
                }

                if (item.UnitPrice < 0)
                {
                    result.Discrepancies.Add(new ValidationDiscrepancy
                    {
                        RuleCode = "INV_LINE_ITEM_NEGATIVE_PRICE",
                        Severity = DiscrepancySeverity.Error,
                        FieldName = $"LineItem[{lineNum}].UnitPrice",
                        Message = $"Line item {lineNum} ('{item.Description}') has negative unit price: {item.UnitPrice}.",
                        ExpectedValue = ">= 0.00",
                        ActualValue = item.UnitPrice.ToString("F2")
                    });
                }

                decimal expectedLineTotal = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(expectedLineTotal - item.LineTotal) > 0.02m)
                {
                    result.Discrepancies.Add(new ValidationDiscrepancy
                    {
                        RuleCode = "INV_LINE_ITEM_ARITHMETIC_MISMATCH",
                        Severity = DiscrepancySeverity.Warning,
                        FieldName = $"LineItem[{lineNum}].LineTotal",
                        Message = $"Line item {lineNum} calculation mismatch: {item.Quantity} * {item.UnitPrice:F2} = {expectedLineTotal:F2}, but LineTotal is {item.LineTotal:F2}.",
                        ExpectedValue = expectedLineTotal.ToString("F2"),
                        ActualValue = item.LineTotal.ToString("F2")
                    });
                }

                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    var normalizedDesc = item.Description.Trim();
                    if (!seenDescriptions.Add(normalizedDesc))
                    {
                        result.Discrepancies.Add(new ValidationDiscrepancy
                        {
                            RuleCode = "INV_DUPLICATE_LINE_ITEM",
                            Severity = DiscrepancySeverity.Warning,
                            FieldName = $"LineItem[{lineNum}].Description",
                            Message = $"Potential duplicate line item detected: '{normalizedDesc}'.",
                            ExpectedValue = "Unique item description",
                            ActualValue = normalizedDesc
                        });
                    }
                }
            }
        }

        return result;
    }

    public static int GetExpectedDecimalPlaces(string currency)
    {
        return currency switch
        {
            "JPY" or "KRW" or "VND" or "CLP" or "PYG" or "ISK" => 0,
            "BHD" or "KWD" or "OMR" or "TND" or "JOD" => 3,
            _ => 2 // Standard for USD, EUR, GBP, CAD, AUD, INR, etc.
        };
    }

    public static bool HasExcessDecimals(decimal value, int maxDecimals)
    {
        decimal scaled = value * (decimal)Math.Pow(10, maxDecimals);
        return scaled != decimal.Truncate(scaled);
    }
}
