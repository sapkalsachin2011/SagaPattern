using Saga.Orchestrator;
using Saga.Orchestrator.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var sagaConnection = builder.Configuration.GetConnectionString("Saga")
	?? throw new InvalidOperationException("ConnectionStrings:Saga is required.");

builder.Services.AddDbContext<SagaDbContext>(options => options.UseNpgsql(sagaConnection));
builder.Services.AddMassTransit(registration =>
{
	registration.AddSagaStateMachine<OrderSagaStateMachine, OrderSagaState>()
		.EntityFrameworkRepository(options =>
		{
			options.ConcurrencyMode = ConcurrencyMode.Pessimistic;
			options.ExistingDbContext<SagaDbContext>();
			options.UsePostgres();
		});
	registration.AddEntityFrameworkOutbox<SagaDbContext>(options =>
	{
		options.UsePostgres();
		options.UseBusOutbox();
	});
	registration.UsingRabbitMq((context, config) =>
	{
		ConfigureRabbitMq(config, builder.Configuration);
		config.ReceiveEndpoint("order-saga", endpoint =>
		{
			endpoint.UseMessageRetry(retry => retry.Intervals(
				TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
			endpoint.UseEntityFrameworkOutbox<SagaDbContext>(context);
			endpoint.ConfigureSaga<OrderSagaState>(context);
		});
	});
});

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
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
