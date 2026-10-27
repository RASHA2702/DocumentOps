using DocumentOps.Application.DTOs;
using DocumentOps.Application.Interfaces;
using DocumentOps.Application.Services;
using DocumentOps.Core.Enums;
using DocumentOps.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddInfrastructureServices();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// Global exception handling (RFC 9457 ProblemDetails)
app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-ID";
    if (!context.Request.Headers.TryGetValue(correlationHeader, out var correlationId) || string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }
    context.Response.Headers[correlationHeader] = correlationId;

    try
    {
        await next(context);
    }
    catch (KeyNotFoundException ex)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc9457#section-3",
            title = "Resource Not Found",
            status = StatusCodes.Status404NotFound,
            detail = ex.Message,
            correlationId = correlationId.ToString(),
            instance = context.Request.Path.ToString()
        });
    }
    catch (InvalidOperationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc9457#section-3",
            title = "Invalid Business Operation",
            status = StatusCodes.Status400BadRequest,
            detail = ex.Message,
            correlationId = correlationId.ToString(),
            instance = context.Request.Path.ToString()
        });
    }
    catch (ArgumentException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc9457#section-3",
            title = "Validation Error",
            status = StatusCodes.Status400BadRequest,
            detail = ex.Message,
            correlationId = correlationId.ToString(),
            instance = context.Request.Path.ToString()
        });
    }
    catch (Exception)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc9457#section-3",
            title = "Internal Server Error",
            status = StatusCodes.Status500InternalServerError,
            detail = "An unexpected error occurred processing your request. Please reference the correlation ID.",
            correlationId = correlationId.ToString(),
            instance = context.Request.Path.ToString()
        });
    }
});

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow,
    service = "DocumentOps.Api",
    version = "1.0.0"
}));

// Dashboard Metrics
app.MapGet("/api/dashboard/metrics", async (DocumentReviewService reviewService, CancellationToken ct) =>
{
    var metrics = await reviewService.GetMetricsAsync(ct);
    return Results.Ok(metrics);
});

// List Documents
app.MapGet("/api/documents", async (
    IDocumentRepository repo,
    DocumentStatus? status,
    string? search,
    int page = 1,
    int pageSize = 20,
    CancellationToken ct = default) =>
{
    if (page < 1) page = 1;
    if (pageSize < 1 || pageSize > 100) pageSize = 20;

    int skip = (page - 1) * pageSize;
    var docs = await repo.ListAsync(status, search, skip, pageSize, ct);
    int totalCount = await repo.CountAsync(status, search, ct);

    var dtos = docs.Select(d => new DocumentSummaryDto
    {
        Id = d.Id,
        FileName = d.FileName,
        FileSizeBytes = d.FileSizeBytes,
        UploadedAt = d.UploadedAt,
        Status = d.Status,
        ConfidenceScore = d.ConfidenceScore,
        InvoiceNumber = d.Invoice?.InvoiceNumber,
        VendorName = d.Invoice?.VendorName,
        TotalAmount = d.Invoice?.TotalAmount,
        Currency = d.Invoice?.Currency,
        DiscrepancyCount = d.Discrepancies.Count,
        UnresolvedErrorCount = d.Discrepancies.Count(disc => disc.Severity == DiscrepancySeverity.Error && !disc.IsResolved)
    });

    return Results.Ok(new
    {
        page,
        pageSize,
        totalCount,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        items = dtos
    });
});

// Get Document Detail
app.MapGet("/api/documents/{id:guid}", async (IDocumentRepository repo, Guid id, CancellationToken ct) =>
{
    var doc = await repo.GetByIdAsync(id, ct);
    if (doc == null) return Results.NotFound(new { detail = $"Document with ID '{id}' was not found." });

    var dto = new DocumentDetailDto
    {
        Id = doc.Id,
        FileName = doc.FileName,
        ContentType = doc.ContentType,
        FileSizeBytes = doc.FileSizeBytes,
        UploadedAt = doc.UploadedAt,
        Status = doc.Status,
        ConfidenceScore = doc.ConfidenceScore,
        RawText = doc.RawText,
        DocumentHash = doc.DocumentHash,
        ProcessedAt = doc.ProcessedAt,
        ReviewedAt = doc.ReviewedAt,
        ReviewedBy = doc.ReviewedBy,
        Invoice = doc.Invoice,
        Discrepancies = doc.Discrepancies,
        AuditTrail = doc.AuditTrail
    };

    return Results.Ok(dto);
});

// Upload & Process Document
app.MapPost("/api/documents/upload", async (DocumentProcessingService processingService, DocumentUploadRequest request, CancellationToken ct) =>
{
    var doc = await processingService.ProcessDocumentAsync(request, ct);
    return Results.Created($"/api/documents/{doc.Id}", new
    {
        id = doc.Id,
        fileName = doc.FileName,
        status = doc.Status.ToString(),
        confidenceScore = doc.ConfidenceScore,
        invoiceNumber = doc.Invoice?.InvoiceNumber,
        vendorName = doc.Invoice?.VendorName,
        totalAmount = doc.Invoice?.TotalAmount,
        currency = doc.Invoice?.Currency,
        discrepancies = doc.Discrepancies
    });
});

// Review & Edit Extracted Fields
app.MapPut("/api/documents/{id:guid}/review", async (
    DocumentReviewService reviewService,
    Guid id,
    DocumentReviewUpdateRequest request,
    CancellationToken ct) =>
{
    var updated = await reviewService.UpdateReviewAsync(id, request, ct);
    return Results.Ok(updated);
});

// Approve Document
app.MapPost("/api/documents/{id:guid}/approve", async (
    DocumentReviewService reviewService,
    Guid id,
    string? reviewerName,
    CancellationToken ct) =>
{
    var approved = await reviewService.ApproveAsync(id, reviewerName ?? "Auditor", ct);
    return Results.Ok(new
    {
        id = approved.Id,
        status = approved.Status.ToString(),
        reviewedBy = approved.ReviewedBy,
        reviewedAt = approved.ReviewedAt
    });
});

// Reject Document
app.MapPost("/api/documents/{id:guid}/reject", async (
    DocumentReviewService reviewService,
    Guid id,
    string? reviewerName,
    string? reason,
    CancellationToken ct) =>
{
    var rejected = await reviewService.RejectAsync(id, reviewerName ?? "Auditor", reason ?? "Rejected by auditor", ct);
    return Results.Ok(new
    {
        id = rejected.Id,
        status = rejected.Status.ToString(),
        reviewedBy = rejected.ReviewedBy,
        reviewedAt = rejected.ReviewedAt
    });
});

// Export to ERP
app.MapGet("/api/documents/{id:guid}/export", async (
    DocumentReviewService reviewService,
    Guid id,
    string format = "json",
    CancellationToken ct = default) =>
{
    string payload = await reviewService.ExportAsync(id, format, ct);
    string contentType = format.Equals("csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : "application/json";
    return Results.Content(payload, contentType);
});

app.Run();
