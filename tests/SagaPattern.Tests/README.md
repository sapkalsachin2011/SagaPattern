# Saga Tests

Runs the orchestrator's state machine on MassTransit's in-memory test harness. Tests cover successful confirmation and payment-decline compensation without requiring RabbitMQ or PostgreSQL.

```sh
dotnet test tests/SagaPattern.Tests/SagaPattern.Tests.csproj
```