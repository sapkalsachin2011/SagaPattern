using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Worker.Consumers;
using Payment.Worker.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var paymentConnection = builder.Configuration.GetConnectionString("Payments")
	?? throw new InvalidOperationException("ConnectionStrings:Payments is required.");

builder.Services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(paymentConnection));
builder.Services.AddMassTransit(registration =>
{
	registration.AddConsumer<ChargePaymentConsumer>();
	registration.AddEntityFrameworkOutbox<PaymentDbContext>(options =>
	{
		options.UsePostgres();
		options.UseBusOutbox();
	});
	registration.UsingRabbitMq((context, config) =>
	{
		ConfigureRabbitMq(config, builder.Configuration);
		config.ReceiveEndpoint("payment-service", endpoint =>
		{
			endpoint.UseMessageRetry(retry => retry.Intervals(
				TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
			endpoint.UseEntityFrameworkOutbox<PaymentDbContext>(context);
			endpoint.ConfigureConsumer<ChargePaymentConsumer>(context);
		});
	});
});

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
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
