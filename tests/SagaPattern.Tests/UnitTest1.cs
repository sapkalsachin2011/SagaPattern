using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Saga.Contracts;
using Saga.Orchestrator;
using Saga.Orchestrator.Persistence;

namespace SagaPattern.Tests;

public sealed class OrderSagaStateMachineTests
{
    [Fact]
    public async Task Payment_failure_releases_inventory_and_cancels_order()
    {
        await using var provider = CreateProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderSubmitted(
            correlationId, correlationId, "buyer@example.com", 49.95m, true, false));
        Assert.True(await harness.Published.Any<ReserveInventory>());

        await harness.Bus.Publish(new InventoryReserved(correlationId, correlationId));
        Assert.True(await harness.Published.Any<ChargePayment>());

        await harness.Bus.Publish(new PaymentFailed(correlationId, correlationId, "declined"));
        Assert.True(await harness.Published.Any<ReleaseInventory>());
        Assert.True(await harness.Published.Any<OrderStatusChanged>(published =>
            published.Context?.Message.Status == "CompensationPending"));

        await harness.Bus.Publish(new InventoryReleased(correlationId, correlationId));
        Assert.True(await harness.Published.Any<OrderStatusChanged>(published =>
            published.Context?.Message.Status == "Cancelled"));

        await harness.Stop();
    }

    [Fact]
    public async Task Successful_payment_confirms_order()
    {
        await using var provider = CreateProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderSubmitted(
            correlationId, correlationId, "buyer@example.com", 49.95m, false, false));
        await harness.Bus.Publish(new InventoryReserved(correlationId, correlationId));
        await harness.Bus.Publish(new PaymentCaptured(correlationId, correlationId));

        Assert.True(await harness.Published.Any<OrderStatusChanged>(published =>
            published.Context?.Message.Status == "Confirmed"));

        await harness.Stop();
    }

    [Fact]
    public async Task Inventory_rejection_cancels_order_without_charging_payment()
    {
        await using var provider = CreateProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderSubmitted(
            correlationId, correlationId, "buyer@example.com", 49.95m, false, true));

        Assert.True(await harness.Published.Any<ReserveInventory>(published =>
            published.Context?.Message.Sku == "UNKNOWN-SKU"));

        await harness.Bus.Publish(new InventoryRejected(correlationId, correlationId, "Unknown SKU 'UNKNOWN-SKU'."));

        Assert.True(await harness.Published.Any<OrderStatusChanged>(published =>
            published.Context?.Message.Status == "Cancelled" &&
            published.Context.Message.Reason == "Unknown SKU 'UNKNOWN-SKU'."));
        Assert.False(await harness.Published.Any<ChargePayment>());

        await harness.Stop();
    }

    private static ServiceProvider CreateProvider() => new ServiceCollection()
        .AddMassTransitTestHarness(registration =>
            registration.AddSagaStateMachine<OrderSagaStateMachine, OrderSagaState>())
        .BuildServiceProvider(true);
}
