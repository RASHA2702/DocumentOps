using DocumentOps.Core.Entities;
using DocumentOps.Core.Enums;

namespace DocumentOps.Application.Interfaces;

public interface IDocumentRepository
{
    Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DocumentRecord?> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<DocumentRecord?> FindDuplicateInvoiceAsync(string vendorName, string invoiceNumber, Guid excludeId, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentRecord>> ListAsync(DocumentStatus? status = null, string? search = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<int> CountAsync(DocumentStatus? status = null, string? search = null, CancellationToken ct = default);
    Task AddAsync(DocumentRecord doc, CancellationToken ct = default);
    Task UpdateAsync(DocumentRecord doc, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
