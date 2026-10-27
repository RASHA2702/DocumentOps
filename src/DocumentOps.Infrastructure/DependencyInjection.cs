using DocumentOps.Application.Interfaces;
using DocumentOps.Application.Services;
using DocumentOps.Infrastructure.Extractors;
using DocumentOps.Infrastructure.Persistence;
using DocumentOps.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Persistence
        services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();

        // Extraction Pipeline
        services.AddSingleton<IDocumentExtractor, RegexDocumentExtractor>();

        // Validation & Scoring
        services.AddSingleton<IValidationEngine, ValidationEngine>();
        services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();

        // Export Service
        services.AddSingleton<IErpExportService, ErpExportService>();

        // Application Services
        services.AddScoped<DocumentProcessingService>();
        services.AddScoped<DocumentReviewService>();

        return services;
    }
}
