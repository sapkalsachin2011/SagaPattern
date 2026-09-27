using MassTransit;
using Saga.Contracts;
using Saga.Orchestrator.Persistence;

namespace Saga.Orchestrator;

public sealed class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public State AwaitingInventory { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State CompensatingInventory { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReserved { get; private set; } = null!;
    public Event<InventoryRejected> InventoryRejected { get; private set; } = null!;
    public Event<PaymentCaptured> PaymentCaptured { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;
    public Event<InventoryReleased> InventoryReleased { get; private set; } = null!;

    public OrderSagaStateMachine()
    {
        InstanceState(state => state.CurrentState);

        Event(() => OrderSubmitted, eventConfig =>
        {
            eventConfig.CorrelateById(context => context.Message.CorrelationId);
            eventConfig.SelectId(context => context.Message.CorrelationId);
        });
        Event(() => InventoryReserved, eventConfig => eventConfig.CorrelateById(context => context.Message.CorrelationId));
        Event(() => InventoryRejected, eventConfig => eventConfig.CorrelateById(context => context.Message.CorrelationId));
        Event(() => PaymentCaptured, eventConfig => eventConfig.CorrelateById(context => context.Message.CorrelationId));
        Event(() => PaymentFailed, eventConfig => eventConfig.CorrelateById(context => context.Message.CorrelationId));
        Event(() => InventoryReleased, eventConfig => eventConfig.CorrelateById(context => context.Message.CorrelationId));

        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.CustomerEmail = context.Message.CustomerEmail;
                    context.Saga.TotalAmount = context.Message.TotalAmount;
                    context.Saga.SimulatePaymentFailure = context.Message.SimulatePaymentFailure;
                    context.Saga.SubmittedAt = DateTimeOffset.UtcNow;
                })
                .Publish(context => new ReserveInventory(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    context.Message.SimulateInventoryFailure ? "UNKNOWN-SKU" : "DEMO-SKU",
                    1))
                .TransitionTo(AwaitingInventory));

        During(AwaitingInventory,
            When(InventoryReserved)
                .Publish(context => new ChargePayment(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    context.Saga.TotalAmount,
                    context.Saga.SimulatePaymentFailure))
                .TransitionTo(AwaitingPayment),
            When(InventoryRejected)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "Cancelled",
                    context.Saga.CustomerEmail,
                    context.Message.Reason))
                .Finalize());

        During(AwaitingPayment,
            When(PaymentCaptured)
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "Confirmed",
                    context.Saga.CustomerEmail,
                    null))
                .Finalize(),
            When(PaymentFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "CompensationPending",
                    context.Saga.CustomerEmail,
                    context.Message.Reason))
                .Publish(context => new ReleaseInventory(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId))
                .TransitionTo(CompensatingInventory));

        During(CompensatingInventory,
            When(InventoryReleased)
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "Cancelled",
                    context.Saga.CustomerEmail,
                    context.Saga.FailureReason))
                .Finalize());

        SetCompletedWhenFinalized();
    }
}