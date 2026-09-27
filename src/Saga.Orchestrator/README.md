# Saga Orchestrator

Runs the persisted MassTransit `OrderSagaStateMachine`. Saga instances are stored in the isolated `saga_db`; the service coordinates other services only through RabbitMQ messages.

## Workflow

`OrderSubmitted` -> reserve inventory -> charge payment -> publish `Confirmed`.

If payment fails, the orchestrator publishes `CompensationPending`, requests `ReleaseInventory`, and publishes `Cancelled` only after `InventoryReleased`. The Saga state and outgoing messages use PostgreSQL persistence and the EF transactional outbox. Message correlation uses the order ID.

RabbitMQ retries transient consumer errors. If compensation cannot be processed after retries, its message is routed to the endpoint error queue; the persisted Saga stays in its compensation state and the client-visible order stays `CompensationPending` until recovery.

## Run

```sh
dotnet run --no-launch-profile --project src/Saga.Orchestrator
```

This worker has no HTTP endpoint. View its logs and the `order-saga` queue in RabbitMQ management. See the root `README.md` for infrastructure startup and the full flow.