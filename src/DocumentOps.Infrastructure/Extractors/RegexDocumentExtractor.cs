using System.Globalization;
using System.Text.RegularExpressions;
using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;

namespace DocumentOps.Infrastructure.Extractors;

public class RegexDocumentExtractor : IDocumentExtractor
{
    public string ExtractorName => "DeterministicRuleBasedEngine";

    public Task<ExtractionResult> ExtractAsync(string rawText, CancellationToken ct = default)
    {
        var result = new ExtractionResult
        {
            ExtractorName = ExtractorName
        };

        var invoice = new InvoiceData();
        int matchedSignals = 0;

        // 1. Currency
        if (rawText.Contains('€') || rawText.Contains("EUR")) invoice.Currency = "EUR";
        else if (rawText.Contains('£') || rawText.Contains("GBP")) invoice.Currency = "GBP";
        else if (rawText.Contains("CAD")) invoice.Currency = "CAD";
        else if (rawText.Contains("AUD")) invoice.Currency = "AUD";
        else invoice.Currency = "USD";

        // 2. Invoice Number
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"(?:invoice|inv)\s*(?:no\.?|number|#)?\s*[:#\s]\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string val = m.Groups[1].Value.Trim();
                if (val.Length >= 2 &&
                    !val.Equals("invoice", StringComparison.OrdinalIgnoreCase) &&
                    !val.Equals("number", StringComparison.OrdinalIgnoreCase))
                {
                    invoice.InvoiceNumber = val;
                    matchedSignals++;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            var codeMatch = Regex.Match(rawText, @"\b(INV-[A-Z0-9\-]+)\b", RegexOptions.IgnoreCase);
            if (codeMatch.Success)
            {
                invoice.InvoiceNumber = codeMatch.Groups[1].Value.Trim();
                matchedSignals++;
            }
        }

        // 3. Vendor Name
        var vendorMatch = Regex.Match(rawText, @"(?:vendor|supplier|from|billed\s*by|company)[:\s]*([^\r\n]+)", RegexOptions.IgnoreCase);
        if (vendorMatch.Success && !string.IsNullOrWhiteSpace(vendorMatch.Groups[1].Value))
        {
            invoice.VendorName = vendorMatch.Groups[1].Value.Trim();
            matchedSignals++;
        }
        else
        {
            // Fallback: first non-empty line of the document
            if (lines.Length > 0 && !lines[0].Contains("INVOICE", StringComparison.OrdinalIgnoreCase))
            {
                invoice.VendorName = lines[0].Trim();
                matchedSignals++;
            }
        }

        // 4. Tax ID / VAT Number
        var taxIdMatch = Regex.Match(rawText, @"(?:tax\s*id|vat\s*no\.?|ein|gstin)[:\s]*([A-Z0-9\-_]+)", RegexOptions.IgnoreCase);
        if (taxIdMatch.Success)
        {
            invoice.VendorTaxId = taxIdMatch.Groups[1].Value.Trim();
        }

        // 5. Issue Date
        var issueDateMatch = Regex.Match(rawText, @"(?:invoice\s*date|issue\s*date|date)[:\s]*([0-9]{4}[-/][0-9]{1,2}[-/][0-9]{1,2}|[0-9]{1,2}[-/][0-9]{1,2}[-/][0-9]{4}|[A-Za-z]{3,9}\s+[0-9]{1,2},?\s+[0-9]{4})", RegexOptions.IgnoreCase);
        if (issueDateMatch.Success && TryParseDate(issueDateMatch.Groups[1].Value, out var issueDate))
        {
            invoice.IssueDate = issueDate;
            matchedSignals++;
        }

        // 6. Due Date
        var dueDateMatch = Regex.Match(rawText, @"(?:due\s*date|payment\s*due)[:\s]*([0-9]{4}[-/][0-9]{1,2}[-/][0-9]{1,2}|[0-9]{1,2}[-/][0-9]{1,2}[-/][0-9]{4}|[A-Za-z]{3,9}\s+[0-9]{1,2},?\s+[0-9]{4})", RegexOptions.IgnoreCase);
        if (dueDateMatch.Success && TryParseDate(dueDateMatch.Groups[1].Value, out var dueDate))
        {
            invoice.DueDate = dueDate;
            matchedSignals++;
        }

        // 7. Subtotal
        var subtotalMatch = Regex.Match(rawText, @"(?:subtotal|sub-total|net\s*amount)[:\s]*[\$€£]?\s*([0-9,]+\.[0-9]{2})", RegexOptions.IgnoreCase);
        if (subtotalMatch.Success && TryParseDecimal(subtotalMatch.Groups[1].Value, out var subtotal))
        {
            invoice.Subtotal = subtotal;
            matchedSignals++;
        }

        // 8. Tax Amount
        var taxMatch = Regex.Match(rawText, @"(?:tax|vat|gst|sales\s*tax)[:\s]*[\$€£]?\s*([0-9,]+\.[0-9]{2})", RegexOptions.IgnoreCase);
        if (taxMatch.Success && TryParseDecimal(taxMatch.Groups[1].Value, out var tax))
        {
            invoice.TaxAmount = tax;
            matchedSignals++;
        }

        // 9. Total Amount
        var totalMatch = Regex.Match(rawText, @"(?<!sub[- ])\btotal(?:\s*(?:amount|due))?[:\s]*[\$€£]?\s*([0-9,]+\.[0-9]{2})|\b(?:balance\s*due|amount\s*due)[:\s]*[\$€£]?\s*([0-9,]+\.[0-9]{2})", RegexOptions.IgnoreCase);
        if (totalMatch.Success)
        {
            string totalStr = totalMatch.Groups[1].Success ? totalMatch.Groups[1].Value : totalMatch.Groups[2].Value;
            if (TryParseDecimal(totalStr, out var total))
            {
                invoice.TotalAmount = total;
                matchedSignals++;
            }
        }
        else if (invoice.Subtotal > 0 && invoice.TaxAmount >= 0)
        {
            invoice.TotalAmount = invoice.Subtotal + invoice.TaxAmount;
        }

        // 10. Line Items Table Parsing
        var lineItemRegex = new Regex(@"^\s*(?<desc>[A-Za-z0-9\s\-_./()]{3,40}?)\s+(?<qty>[0-9]+(?:\.[0-9]+)?)\s+[\$€£]?(?<price>[0-9,]+\.[0-9]{2})\s+[\$€£]?(?<total>[0-9,]+\.[0-9]{2})\s*$", RegexOptions.Multiline);
        var itemMatches = lineItemRegex.Matches(rawText);
        foreach (Match m in itemMatches)
        {
            string desc = m.Groups["desc"].Value.Trim();
            if (desc.Equals("Subtotal", StringComparison.OrdinalIgnoreCase) ||
                desc.Equals("Total", StringComparison.OrdinalIgnoreCase) ||
                desc.Equals("Description", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseDecimal(m.Groups["qty"].Value, out var qty) &&
                TryParseDecimal(m.Groups["price"].Value, out var price) &&
                TryParseDecimal(m.Groups["total"].Value, out var lineTotal))
            {
                invoice.LineItems.Add(new InvoiceLineItem
                {
                    Description = desc,
                    Quantity = qty,
                    UnitPrice = price,
                    LineTotal = lineTotal
                });
            }
        }

        // If subtotal wasn't explicitly extracted but line items exist, sum them
        if (invoice.Subtotal == 0 && invoice.LineItems.Count > 0)
        {
            invoice.Subtotal = invoice.LineItems.Sum(li => li.LineTotal);
            if (invoice.TotalAmount == 0)
            {
                invoice.TotalAmount = invoice.Subtotal + invoice.TaxAmount;
            }
        }

        result.Invoice = invoice;
        result.ExtractorConfidence = Math.Clamp(matchedSignals / 7.0, 0.2, 1.0);
        return Task.FromResult(result);
    }

    private static bool TryParseDecimal(string val, out decimal result)
    {
        string cleaned = val.Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDate(string val, out DateTime date)
    {
        string[] formats = new[]
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy",
            "MMMM dd, yyyy", "MMM dd, yyyy", "dd MMMM yyyy", "dd MMM yyyy"
        };
        return DateTime.TryParseExact(val.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(val.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
