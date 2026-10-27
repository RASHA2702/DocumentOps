using DocumentOps.Core.Entities;

namespace DocumentOps.Application.Interfaces;

public class ExtractionResult
{
    public bool Success { get; set; } = true;
    public InvoiceData Invoice { get; set; } = new();
    public double ExtractorConfidence { get; set; } = 1.0;
    public string ExtractorName { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public interface IDocumentExtractor
{
    string ExtractorName { get; }
    Task<ExtractionResult> ExtractAsync(string rawText, CancellationToken ct = default);
}
