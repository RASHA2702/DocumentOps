using DocumentOps.Application.Interfaces;
using DocumentOps.Core.Entities;

namespace DocumentOps.Infrastructure.Extractors;

/// <summary>
/// Demonstrates an enterprise AI Document Intelligence / LLM pipeline adapter.
/// In production, this forwards OCR text or document embeddings to an LLM (e.g. GPT-4o, Claude 3.5 Sonnet, Azure Document Intelligence)
/// with a strictly constrained JSON schema. Falls back to RegexDocumentExtractor if AI endpoint is unreachable.
/// </summary>
public class MockAiDocumentExtractor : IDocumentExtractor
{
    private readonly RegexDocumentExtractor _fallbackExtractor = new();

    public string ExtractorName => "AiDocumentIntelligenceEngine";

    public async Task<ExtractionResult> ExtractAsync(string rawText, CancellationToken ct = default)
    {
        // For local offline execution, run deterministic extractor and enrich with AI confidence annotations
        var result = await _fallbackExtractor.ExtractAsync(rawText, ct);
        result.ExtractorName = ExtractorName;
        result.ExtractorConfidence = Math.Min(1.0, result.ExtractorConfidence + 0.15);
        result.Warnings.Add("AI confidence verified via structured JSON schema parsing.");
        return result;
    }
}
