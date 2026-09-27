using Inventory.Worker.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Saga.Contracts;

namespace Inventory.Worker.Consumers;

public sealed class ReserveInventoryConsumer(InventoryDbContext dbContext) : IConsumer<ReserveInventory>
{
    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        var message = context.Message;
        var existing = await dbContext.Reservations.FindAsync([message.OrderId], context.CancellationToken);
        if (existing is not null)
        {
            if (existing.Status == "Reserved")
            {
                await context.Publish(new InventoryReserved(message.CorrelationId, message.OrderId));
            }
            else
            {
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
        await context.Publish(new InventoryReserved(message.CorrelationId, message.OrderId));
    }
}