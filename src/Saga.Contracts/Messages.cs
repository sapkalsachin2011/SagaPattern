namespace Saga.Contracts;

public sealed record OrderSubmitted(
    Guid CorrelationId,
    Guid OrderId,
    string CustomerEmail,
    decimal TotalAmount,
    bool SimulatePaymentFailure,
    bool SimulateInventoryFailure);

public sealed record ReserveInventory(Guid CorrelationId, Guid OrderId, string Sku, int Quantity);
public sealed record InventoryReserved(Guid CorrelationId, Guid OrderId);
public sealed record InventoryRejected(Guid CorrelationId, Guid OrderId, string Reason);
public sealed record ReleaseInventory(Guid CorrelationId, Guid OrderId);
public sealed record InventoryReleased(Guid CorrelationId, Guid OrderId);
public sealed record InventoryReleaseFailed(Guid CorrelationId, Guid OrderId, string Reason);

public sealed record ChargePayment(Guid CorrelationId, Guid OrderId, decimal Amount, bool SimulateFailure);
public sealed record PaymentCaptured(Guid CorrelationId, Guid OrderId);
public sealed record PaymentFailed(Guid CorrelationId, Guid OrderId, string Reason);

public sealed record OrderStatusChanged(
    Guid CorrelationId,
    Guid OrderId,
    string Status,
    string CustomerEmail,
    string? Reason);