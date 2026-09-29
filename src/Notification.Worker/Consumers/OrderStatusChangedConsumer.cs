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
        logger.LogInformation(
            "[FLOW] RECEIVE service=Notification.Worker consumer=OrderStatusChangedConsumer message=OrderStatusChanged orderId={OrderId} correlationId={CorrelationId} status={Status}",
            context.Message.OrderId, context.Message.CorrelationId, context.Message.Status);

        if (context.Message.Status != "Confirmed")
        {
            logger.LogInformation(
                "[FLOW] SKIP service=Notification.Worker orderId={OrderId} reason=OnlyConfirmedOrdersAreRecorded status={Status}",
                context.Message.OrderId, context.Message.Status);
            return;
        }

        if (await dbContext.Notifications.AnyAsync(
                notification => notification.OrderId == context.Message.OrderId,
                context.CancellationToken))
        {
            logger.LogInformation(
            "[FLOW] SKIP service=Notification.Worker orderId={OrderId} reason=NotificationAlreadyExists",
            context.Message.OrderId);
            return;
        }

        dbContext.Notifications.Add(new NotificationEntity
        {
            OrderId = context.Message.OrderId,
            SentAt = DateTimeOffset.UtcNow,
            Status = "Logged"
        });
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "[FLOW] DB COMMIT service=Notification.Worker database=notifications_db orderId={OrderId} notificationStatus=Logged",
            context.Message.OrderId);
    }
}