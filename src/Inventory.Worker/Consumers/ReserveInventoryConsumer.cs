using Inventory.Worker.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Saga.Contracts;

namespace Inventory.Worker.Consumers;

public sealed class ReserveInventoryConsumer(
    InventoryDbContext dbContext,
    ILogger<ReserveInventoryConsumer> logger) : IConsumer<ReserveInventory>
{
    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "[FLOW] RECEIVE service=Inventory.Worker consumer=ReserveInventoryConsumer message=ReserveInventory orderId={OrderId} correlationId={CorrelationId} sku={Sku} quantity={Quantity}",
            message.OrderId, message.CorrelationId, message.Sku, message.Quantity);
        var existing = await dbContext.Reservations.FindAsync([message.OrderId], context.CancellationToken);
        if (existing is not null)
        {
            if (existing.Status == "Reserved")
            {
                logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryReserved orderId={OrderId} reservationStatus={ReservationStatus} duplicateRequest=true",
                    message.OrderId, existing.Status);
                await context.Publish(new InventoryReserved(message.CorrelationId, message.OrderId));
            }
            else
            {
                logger.LogInformation(
                    "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryRejected orderId={OrderId} reservationStatus={ReservationStatus} duplicateRequest=true",
                    message.OrderId, existing.Status);
                await context.Publish(new InventoryRejected(message.CorrelationId, message.OrderId,
                    "An inventory reservation for this order was already completed or rejected."));
            }

            return;
        }

        var item = await dbContext.Items.SingleOrDefaultAsync(
            candidate => candidate.Sku == message.Sku,
            context.CancellationToken);

        if (item is null || item.AvailableQuantity < message.Quantity)
        {
            dbContext.Reservations.Add(new InventoryReservation
            {
                OrderId = message.OrderId,
                CorrelationId = message.CorrelationId,
                Sku = message.Sku,
                Quantity = message.Quantity,
                Status = "Rejected"
            });
            await dbContext.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation(
                "[FLOW] DB COMMIT service=Inventory.Worker database=inventory_db orderId={OrderId} reservationStatus=Rejected reason={Reason}",
                message.OrderId, item is null ? "Unknown SKU" : "Insufficient inventory");
            logger.LogInformation(
                "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryRejected orderId={OrderId}",
                message.OrderId);
            await context.Publish(new InventoryRejected(message.CorrelationId, message.OrderId,
                item is null ? $"Unknown SKU '{message.Sku}'." : "Insufficient inventory."));
            return;
        }

        item.AvailableQuantity -= message.Quantity;
        dbContext.Reservations.Add(new InventoryReservation
        {
            OrderId = message.OrderId,
            CorrelationId = message.CorrelationId,
            Sku = message.Sku,
            Quantity = message.Quantity,
            Status = "Reserved"
        });
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "[FLOW] DB COMMIT service=Inventory.Worker database=inventory_db orderId={OrderId} sku={Sku} availableQuantity={AvailableQuantity} reservationStatus=Reserved",
            message.OrderId, item.Sku, item.AvailableQuantity);
        logger.LogInformation(
            "[FLOW] OUTBOX ADD service=Inventory.Worker destination=Saga.Orchestrator message=InventoryReserved orderId={OrderId}",
            message.OrderId);
        await context.Publish(new InventoryReserved(message.CorrelationId, message.OrderId));
    }
}