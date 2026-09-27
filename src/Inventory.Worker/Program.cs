using Inventory.Worker.Consumers;
using Inventory.Worker.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var inventoryConnection = builder.Configuration.GetConnectionString("Inventory")
	?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.");

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(inventoryConnection));
builder.Services.AddMassTransit(registration =>
{
	registration.AddConsumer<ReserveInventoryConsumer>();
	registration.AddConsumer<ReleaseInventoryConsumer>();
	registration.AddEntityFrameworkOutbox<InventoryDbContext>(options =>
	{
		options.UsePostgres();
		options.UseBusOutbox();
	});
	registration.UsingRabbitMq((context, config) =>
	{
		ConfigureRabbitMq(config, builder.Configuration);
		config.ReceiveEndpoint("inventory-service", endpoint =>
		{
			endpoint.UseMessageRetry(retry => retry.Intervals(
				TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
			endpoint.UseEntityFrameworkOutbox<InventoryDbContext>(context);
			endpoint.ConfigureConsumer<ReserveInventoryConsumer>(context);
			endpoint.ConfigureConsumer<ReleaseInventoryConsumer>(context);
		});
	});
});

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
	await dbContext.Database.EnsureCreatedAsync();
	if (!await dbContext.Items.AnyAsync())
	{
		dbContext.Items.Add(new InventoryItem { Sku = "DEMO-SKU", AvailableQuantity = 50 });
		await dbContext.SaveChangesAsync();
	}
}

await host.RunAsync();

static void ConfigureRabbitMq(IRabbitMqBusFactoryConfigurator config, IConfiguration configuration)
{
	config.Host(configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
	{
		host.Username(configuration["RabbitMq:Username"] ?? "saga");
		host.Password(configuration["RabbitMq:Password"] ?? "saga_local_only");
	});
}
