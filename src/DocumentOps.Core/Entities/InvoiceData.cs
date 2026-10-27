namespace DocumentOps.Core.Entities;

public class InvoiceData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? VendorTaxId { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; } = 0;
    public decimal TaxAmount { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0;
    public string? Notes { get; set; }
    public List<InvoiceLineItem> LineItems { get; set; } = new();
}
