using DocumentOps.Core.Entities;

namespace DocumentOps.Application.Interfaces;

public class ValidationResult
{
    public bool IsValid => Discrepancies.All(d => d.Severity != Core.Enums.DiscrepancySeverity.Error);
    public List<ValidationDiscrepancy> Discrepancies { get; set; } = new();
}

public interface IValidationEngine
{
    ValidationResult Validate(InvoiceData invoice, DocumentRecord document);
}

public interface IConfidenceScorer
{
    double CalculateScore(InvoiceData invoice, IReadOnlyList<ValidationDiscrepancy> discrepancies);
}

public interface IErpExportService
{
    Task<string> ExportAsync(DocumentRecord document, string format = "json", CancellationToken ct = default);
}
