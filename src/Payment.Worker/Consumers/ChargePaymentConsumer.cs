using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Worker.Persistence;
using Saga.Contracts;

namespace Payment.Worker.Consumers;

public sealed class ChargePaymentConsumer(PaymentDbContext dbContext) : IConsumer<ChargePayment>
{
    public async Task Consume(ConsumeContext<ChargePayment> context)
    {
        var message = context.Message;
        var payment = await dbContext.Payments.FindAsync([message.OrderId], context.CancellationToken);
        if (payment is null)
        {
            payment = new PaymentEntity
            {
                OrderId = message.OrderId,
                CorrelationId = message.CorrelationId,
                Amount = message.Amount,
                Status = message.SimulateFailure ? "Failed" : "Captured",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }

        if (payment.Status == "Captured")
        {
            await context.Publish(new PaymentCaptured(message.CorrelationId, message.OrderId));
        }
        else
        {
            await context.Publish(new PaymentFailed(message.CorrelationId, message.OrderId,
                "Payment was declined by the local payment simulator."));
        }
    }
}