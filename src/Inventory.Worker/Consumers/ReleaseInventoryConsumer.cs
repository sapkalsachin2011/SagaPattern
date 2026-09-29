using Inventory.Worker.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Saga.Contracts;

namespace Inventory.Worker.Consumers;

public sealed class ReleaseInventoryConsumer(
    InventoryDbContext dbContext,
    ILogger<ReleaseInventoryConsumer> logger) : IConsumer<ReleaseInventory>
{
    public async Task Consume(ConsumeContext<ReleaseInventory> context)
    {
        logger.LogInformation(
            "[FLOW] RECEIVE service=Inventory.Worker consumer=ReleaseInventoryConsumer message=ReleaseInventory orderId={OrderId} correlationId={CorrelationId}",
            context.Message.OrderId, context.Message.CorrelationId);
        var reservation = await dbContext.Reservations.FindAsync(
            [context.Message.OrderId], context.CancellationToken);

        if (reservation is null || reservation.Status == "Released")
        {
            logger.LogInformation(
                "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryReleased orderId={OrderId} alreadyReleased={AlreadyReleased}",
                context.Message.OrderId, reservation?.Status == "Released");
            await context.Publish(new InventoryReleased(context.Message.CorrelationId, context.Message.OrderId));
            return;
        }

        if (reservation.Status == "Reserved")
        {
            var item = await dbContext.Items.SingleOrDefaultAsync(
                candidate => candidate.Sku == reservation.Sku,
                context.CancellationToken);

            if (item is null)
            {
                throw new InvalidOperationException($"Cannot compensate reservation; SKU '{reservation.Sku}' is missing.");
            }

            item.AvailableQuantity += reservation.Quantity;
            reservation.Status = "Released";
            await dbContext.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation(
                "[FLOW] DB COMMIT service=Inventory.Worker database=inventory_db orderId={OrderId} sku={Sku} availableQuantity={AvailableQuantity} reservationStatus=Released",
                context.Message.OrderId, item.Sku, item.AvailableQuantity);
        }

        logger.LogInformation(
            "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryReleased orderId={OrderId}",
            context.Message.OrderId);
        await context.Publish(new InventoryReleased(context.Message.CorrelationId, context.Message.OrderId));
    }
}