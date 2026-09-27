namespace Inventory.Worker.Persistence;

public sealed class InventoryItem
{
    public string Sku { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }
}

public sealed class InventoryReservation
{
    public Guid OrderId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
}