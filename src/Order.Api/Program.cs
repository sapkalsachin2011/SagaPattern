using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Api.Consumers;
using Order.Api.Persistence;
using Saga.Contracts;

var builder = WebApplication.CreateBuilder(args);
var orderConnection = builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException("ConnectionStrings:Orders is required.");

builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(orderConnection));
builder.Services.AddMassTransit(registration =>
{
    registration.AddConsumer<OrderStatusChangedConsumer>();
    registration.AddEntityFrameworkOutbox<OrderDbContext>(options =>
    {
        options.UsePostgres();
        options.UseBusOutbox();
    });
    registration.UsingRabbitMq((context, config) =>
    {
        ConfigureRabbitMq(config, builder.Configuration);
        config.ReceiveEndpoint("order-status", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
            endpoint.UseEntityFrameworkOutbox<OrderDbContext>(context);
            endpoint.ConfigureConsumer<OrderStatusChangedConsumer>(context);
        });
    });
});

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrderDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerEmail) ||
        request.CustomerEmail.Length > 320 ||
        !request.CustomerEmail.Contains('@') ||
        request.TotalAmount <= 0)
    {
        return Results.BadRequest(new { error = "Provide a valid customerEmail and a totalAmount greater than zero." });
    }

    var now = DateTimeOffset.UtcNow;
    var order = new OrderEntity
    {
        Id = Guid.NewGuid(),
        CustomerEmail = request.CustomerEmail,
        TotalAmount = request.TotalAmount,
        Status = "Pending",
        CreatedAt = now,
        UpdatedAt = now
    };

    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    dbContext.Orders.Add(order);
    await publishEndpoint.Publish(new OrderSubmitted(
        order.Id,
        order.Id,
        order.CustomerEmail,
        order.TotalAmount,
        request.SimulatePaymentFailure,
        request.SimulateInventoryFailure), cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return Results.Accepted($"/orders/{order.Id}", new { order.Id, order.Status });
});

app.MapGet("/orders/{orderId:guid}", async (
    Guid orderId,
    OrderDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders.AsNoTracking()
        .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

    return order is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            order.Id,
            order.CustomerEmail,
            order.TotalAmount,
            order.Status,
            order.FailureReason,
            order.CreatedAt,
            order.UpdatedAt
        });
});

await app.RunAsync();

static void ConfigureRabbitMq(IRabbitMqBusFactoryConfigurator config, IConfiguration configuration)
{
    config.Host(configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
    {
        host.Username(configuration["RabbitMq:Username"] ?? "saga");
        host.Password(configuration["RabbitMq:Password"] ?? "saga_local_only");
    });
}

public sealed record CreateOrderRequest(
    string CustomerEmail,
    decimal TotalAmount,
    bool SimulatePaymentFailure = false,
    bool SimulateInventoryFailure = false);
