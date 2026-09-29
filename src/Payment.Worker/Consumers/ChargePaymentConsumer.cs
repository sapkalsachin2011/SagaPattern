using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Worker.Persistence;
using Saga.Contracts;

namespace Payment.Worker.Consumers;

public sealed class ChargePaymentConsumer(
    PaymentDbContext dbContext,
    ILogger<ChargePaymentConsumer> logger) : IConsumer<ChargePayment>
{
    public async Task Consume(ConsumeContext<ChargePayment> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "[FLOW] RECEIVE service=Payment.Worker consumer=ChargePaymentConsumer message=ChargePayment orderId={OrderId} correlationId={CorrelationId} amount={Amount} simulateFailure={SimulateFailure}",
            message.OrderId, message.CorrelationId, message.Amount, message.SimulateFailure);
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
            logger.LogInformation(
                "[FLOW] DB COMMIT service=Payment.Worker database=payments_db orderId={OrderId} amount={Amount} paymentStatus={PaymentStatus}",
                message.OrderId, payment.Amount, payment.Status);
        }

        if (payment.Status == "Captured")
        {
            logger.LogInformation(
                "[FLOW] OUTBOX ADD service=Payment.Worker destination=Saga.Orchestrator message=PaymentCaptured orderId={OrderId}",
                message.OrderId);
            await context.Publish(new PaymentCaptured(message.CorrelationId, message.OrderId));
        }
        else
        {
            logger.LogInformation(
                "[FLOW] OUTBOX ADD service=Payment.Worker destination=Saga.Orchestrator message=PaymentFailed orderId={OrderId} paymentStatus={PaymentStatus}",
                message.OrderId, payment.Status);
            await context.Publish(new PaymentFailed(message.CorrelationId, message.OrderId,
                "Payment was declined by the local payment simulator."));
        }
    }
}