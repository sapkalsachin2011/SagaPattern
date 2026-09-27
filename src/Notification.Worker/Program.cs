using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.Worker.Consumers;
using Notification.Worker.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var notificationConnection = builder.Configuration.GetConnectionString("Notifications")
	?? throw new InvalidOperationException("ConnectionStrings:Notifications is required.");

builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(notificationConnection));
builder.Services.AddMassTransit(registration =>
{
	registration.AddConsumer<OrderStatusChangedConsumer>();
	registration.AddEntityFrameworkOutbox<NotificationDbContext>(options =>
	{
		options.UsePostgres();
		options.UseBusOutbox();
	});
	registration.UsingRabbitMq((context, config) =>
	{
		ConfigureRabbitMq(config, builder.Configuration);
		config.ReceiveEndpoint("notification-service", endpoint =>
		{
			endpoint.UseMessageRetry(retry => retry.Intervals(
				TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
			endpoint.UseEntityFrameworkOutbox<NotificationDbContext>(context);
			endpoint.ConfigureConsumer<OrderStatusChangedConsumer>(context);
		});
	});
});

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
	await dbContext.Database.EnsureCreatedAsync();
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
