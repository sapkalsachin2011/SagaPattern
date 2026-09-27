using MassTransit;

namespace Saga.Orchestrator.Persistence;

public sealed class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool SimulatePaymentFailure { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}