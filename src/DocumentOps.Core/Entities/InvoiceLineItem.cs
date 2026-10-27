namespace DocumentOps.Core.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 0;
    public decimal LineTotal { get; set; } = 0;
}
