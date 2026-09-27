using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;
using Saga.Contracts;

namespace Order.Api.Consumers;

public sealed class OrderStatusChangedConsumer(OrderDbContext dbContext) : IConsumer<OrderStatusChanged>
{
    public async Task Consume(ConsumeContext<OrderStatusChanged> context)
    {
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
    }
}