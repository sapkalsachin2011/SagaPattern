using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.Worker.Persistence;
using Saga.Contracts;

namespace Notification.Worker.Consumers;

public sealed class OrderStatusChangedConsumer(
    NotificationDbContext dbContext,
    ILogger<OrderStatusChangedConsumer> logger) : IConsumer<OrderStatusChanged>
{
    public async Task Consume(ConsumeContext<OrderStatusChanged> context)
    {
        if (context.Message.Status != "Confirmed")
        {
            return;
        }

        if (await dbContext.Notifications.AnyAsync(
                notification => notification.OrderId == context.Message.OrderId,
                context.CancellationToken))
        {
            return;
        }

        dbContext.Notifications.Add(new NotificationEntity
        {
            OrderId = context.Message.OrderId,
            SentAt = DateTimeOffset.UtcNow,
            Status = "Logged"
        });
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Confirmation notification recorded for order {OrderId} ({Email})",
            context.Message.OrderId, context.Message.CustomerEmail);
    }
}