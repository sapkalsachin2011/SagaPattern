namespace Payment.Worker.Persistence;

public sealed class PaymentEntity
{
    public Guid OrderId { get; set; }
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}