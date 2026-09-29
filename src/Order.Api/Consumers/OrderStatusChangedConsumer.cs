using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;
using Saga.Contracts;

namespace Order.Api.Consumers;

public sealed class OrderStatusChangedConsumer(
    OrderDbContext dbContext,
    ILogger<OrderStatusChangedConsumer> logger) : IConsumer<OrderStatusChanged>
{
    public async Task Consume(ConsumeContext<OrderStatusChanged> context)
    {
        logger.LogInformation(
            "[FLOW] RECEIVE service=Order.Api consumer=OrderStatusChangedConsumer message=OrderStatusChanged orderId={OrderId} correlationId={CorrelationId} status={Status} reason={Reason}",
            context.Message.OrderId, context.Message.CorrelationId, context.Message.Status, context.Message.Reason);

        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == context.Message.OrderId,
            context.CancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"Order '{context.Message.OrderId}' does not exist.");
        }

        order.Status = context.Message.Status;
        order.FailureReason = context.Message.Reason;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "[FLOW] DB COMMIT service=Order.Api database=orders_db orderId={OrderId} status={Status}",
            order.Id, order.Status);
    }
}