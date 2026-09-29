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

    public OrderSagaStateMachine(ILogger<OrderSagaStateMachine> logger)
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
                    logger.LogInformation(
                        "[FLOW] RECEIVE service=Saga.Orchestrator message=OrderSubmitted orderId={OrderId} correlationId={CorrelationId} amount={Amount}",
                        context.Message.OrderId, context.Message.CorrelationId, context.Message.TotalAmount);
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.CustomerEmail = context.Message.CustomerEmail;
                    context.Saga.TotalAmount = context.Message.TotalAmount;
                    context.Saga.SimulatePaymentFailure = context.Message.SimulatePaymentFailure;
                    context.Saga.SubmittedAt = DateTimeOffset.UtcNow;
                })
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Inventory.Worker message=ReserveInventory orderId={OrderId} correlationId={CorrelationId} sku={Sku} quantity=1",
                    context.Saga.OrderId, context.Saga.CorrelationId,
                    context.Message.SimulateInventoryFailure ? "UNKNOWN-SKU" : "DEMO-SKU"))
                .Publish(context => new ReserveInventory(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    context.Message.SimulateInventoryFailure ? "UNKNOWN-SKU" : "DEMO-SKU",
                    1))
                .TransitionTo(AwaitingInventory));

        During(AwaitingInventory,
            When(InventoryReserved)
                .Then(context => logger.LogInformation(
                    "[FLOW] RECEIVE service=Saga.Orchestrator message=InventoryReserved orderId={OrderId} correlationId={CorrelationId}",
                    context.Message.OrderId, context.Message.CorrelationId))
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Payment.Worker message=ChargePayment orderId={OrderId} correlationId={CorrelationId} amount={Amount}",
                    context.Saga.OrderId, context.Saga.CorrelationId, context.Saga.TotalAmount))
                .Publish(context => new ChargePayment(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    context.Saga.TotalAmount,
                    context.Saga.SimulatePaymentFailure))
                .TransitionTo(AwaitingPayment),
            When(InventoryRejected)
                .Then(context =>
                {
                    logger.LogInformation(
                        "[FLOW] RECEIVE service=Saga.Orchestrator message=InventoryRejected orderId={OrderId} correlationId={CorrelationId} reason={Reason}",
                        context.Message.OrderId, context.Message.CorrelationId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                })
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Order.Api,Notification.Worker message=OrderStatusChanged orderId={OrderId} status=Cancelled",
                    context.Saga.OrderId))
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "Cancelled",
                    context.Saga.CustomerEmail,
                    context.Message.Reason))
                .Finalize());

        During(AwaitingPayment,
            When(PaymentCaptured)
                .Then(context => logger.LogInformation(
                    "[FLOW] RECEIVE service=Saga.Orchestrator message=PaymentCaptured orderId={OrderId} correlationId={CorrelationId}",
                    context.Message.OrderId, context.Message.CorrelationId))
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Order.Api,Notification.Worker message=OrderStatusChanged orderId={OrderId} status=Confirmed",
                    context.Saga.OrderId))
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "Confirmed",
                    context.Saga.CustomerEmail,
                    null))
                .Finalize(),
            When(PaymentFailed)
                .Then(context =>
                {
                    logger.LogInformation(
                        "[FLOW] RECEIVE service=Saga.Orchestrator message=PaymentFailed orderId={OrderId} correlationId={CorrelationId} reason={Reason}",
                        context.Message.OrderId, context.Message.CorrelationId, context.Message.Reason);
                    context.Saga.FailureReason = context.Message.Reason;
                })
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Order.Api,Notification.Worker message=OrderStatusChanged orderId={OrderId} status=CompensationPending",
                    context.Saga.OrderId))
                .Publish(context => new OrderStatusChanged(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId,
                    "CompensationPending",
                    context.Saga.CustomerEmail,
                    context.Message.Reason))
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Inventory.Worker message=ReleaseInventory orderId={OrderId} correlationId={CorrelationId}",
                    context.Saga.OrderId, context.Saga.CorrelationId))
                .Publish(context => new ReleaseInventory(
                    context.Saga.CorrelationId,
                    context.Saga.OrderId))
                .TransitionTo(CompensatingInventory));

        During(CompensatingInventory,
            When(InventoryReleased)
                .Then(context => logger.LogInformation(
                    "[FLOW] RECEIVE service=Saga.Orchestrator message=InventoryReleased orderId={OrderId} correlationId={CorrelationId}",
                    context.Message.OrderId, context.Message.CorrelationId))
                .Then(context => logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Saga.Orchestrator destination=Order.Api,Notification.Worker message=OrderStatusChanged orderId={OrderId} status=Cancelled",
                    context.Saga.OrderId))
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