using Inventory.Worker.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Saga.Contracts;

namespace Inventory.Worker.Consumers;

public sealed class ReleaseInventoryConsumer(InventoryDbContext dbContext) : IConsumer<ReleaseInventory>
{
    public async Task Consume(ConsumeContext<ReleaseInventory> context)
    {
        var reservation = await dbContext.Reservations.FindAsync(
            [context.Message.OrderId], context.CancellationToken);

        if (reservation is null || reservation.Status == "Released")
        {
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
        }

        await context.Publish(new InventoryReleased(context.Message.CorrelationId, context.Message.OrderId));
    }
}