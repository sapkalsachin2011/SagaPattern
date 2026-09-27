# Order Processing Saga

A local .NET 10 microservices example of an orchestrated Saga using RabbitMQ, MassTransit 8.5.10, and PostgreSQL. It runs without an Azure subscription or MassTransit license key. MassTransit 8.5.10 is the Apache-2.0 version used by this learning project.

## Project Contents

| Project | README | Entry point / implementation |
| --- | --- | --- |
| Shared message contracts | [Saga.Contracts README](src/Saga.Contracts/README.md) | [Messages.cs](src/Saga.Contracts/Messages.cs) |
| Order HTTP API | [Order API README](src/Order.Api/README.md) | [Program.cs](src/Order.Api/Program.cs), [status consumer](src/Order.Api/Consumers/OrderStatusChangedConsumer.cs) |
| Saga orchestrator | [Orchestrator README](src/Saga.Orchestrator/README.md) | [state machine](src/Saga.Orchestrator/OrderSagaStateMachine.cs), [Saga state](src/Saga.Orchestrator/Persistence/OrderSagaState.cs) |
| Inventory worker | [Inventory README](src/Inventory.Worker/README.md) | [reserve consumer](src/Inventory.Worker/Consumers/ReserveInventoryConsumer.cs), [release consumer](src/Inventory.Worker/Consumers/ReleaseInventoryConsumer.cs) |
| Payment worker | [Payment README](src/Payment.Worker/README.md) | [charge consumer](src/Payment.Worker/Consumers/ChargePaymentConsumer.cs) |
| Notification worker | [Notification README](src/Notification.Worker/README.md) | [status consumer](src/Notification.Worker/Consumers/OrderStatusChangedConsumer.cs) |
| Saga tests | [Tests README](tests/SagaPattern.Tests/README.md) | [state machine tests](tests/SagaPattern.Tests/UnitTest1.cs) |

Solution: [SagaPattern.sln](SagaPattern.sln). Local dependencies: [compose.yaml](compose.yaml) and PostgreSQL database initialization at [infra/postgres/init/01-create-databases.sql](infra/postgres/init/01-create-databases.sql).

API scenario runners: [VS Code HTTP requests](src/Order.Api/Order.Api.http) and [Postman collection](SagaPattern.postman_collection.json).

## Architecture

```text
Client -> Order API -> RabbitMQ -> Saga Orchestrator
                                  |             |
                         ReserveInventory   ChargePayment
                                  v             v
                            Inventory       Payment
                                  \             /
                                   result events
                                        |
                          Confirm order or compensate
                                        |
                         Order API + Notification
```

Each service owns its own database. Locally, five separate PostgreSQL databases share one PostgreSQL container to keep setup lightweight. The services use separate connection strings and do not read or write one another's databases. A production deployment would normally provision each service's database independently.

## Prerequisites

- .NET 10 SDK
- Docker Desktop running
- Ports `5080` (Order API), `5432` (PostgreSQL), `5672` (RabbitMQ AMQP), and `15672` (RabbitMQ management UI) available

## Local Credentials

These values are checked in for local development only. Do not reuse them outside this machine or commit real credentials.

| Service | Host / port | Username | Password | Database / notes |
| --- | --- | --- | --- | --- |
| RabbitMQ AMQP | `localhost:5672` | `saga` | `saga_local_only` | Used by the .NET processes |
| RabbitMQ management | [http://localhost:15672](http://localhost:15672) | `saga` | `saga_local_only` | Browser UI for queues, exchanges, and error queues |
| PostgreSQL | `localhost:5432` | `saga` | `saga_local_only` | Databases listed below; default maintenance DB is `postgres` |

PostgreSQL databases: `orders_db`, `inventory_db`, `payments_db`, `notifications_db`, and `saga_db`. Each service has its own database and connection string; they share a single PostgreSQL container only to keep the local setup small. Container-to-container applications would use hostnames `rabbitmq` and `postgres`; the .NET processes in this example run on the host and use `localhost`.

## Start infrastructure

Open a terminal in this directory (`/Users/sachinsapkal/projects/Microservice-Projects/SagaPattern`), then start the broker and database:

```sh
docker compose up -d --wait
```

The PostgreSQL init script creates the five databases when the data volume is first initialized. The services create their own tables at startup using EF Core `EnsureCreated`; no separate migration command is needed for this demo.

Start the API and each worker in its own terminal, from the solution directory:

```sh
dotnet run --no-launch-profile --project src/Order.Api --urls http://localhost:5080
dotnet run --no-launch-profile --project src/Saga.Orchestrator
dotnet run --no-launch-profile --project src/Inventory.Worker
dotnet run --no-launch-profile --project src/Payment.Worker
dotnet run --no-launch-profile --project src/Notification.Worker
```

The apps create their local tables on startup. This uses EF Core `EnsureCreated` for the demo; use reviewed EF migrations for production schema changes.

## Try the workflow

Create an order with a successful simulated payment:

```sh
curl -i -X POST http://localhost:5080/orders \
  -H 'Content-Type: application/json' \
  -d '{"customerEmail":"buyer@example.com","totalAmount":49.95}'
```

The API returns `202 Accepted` and an order ID. Poll `GET http://localhost:5080/orders/{orderId}`; the status moves from `Pending` to `Confirmed`.

The response includes the generated `id`. Use that value in the status URL, for example `curl http://localhost:5080/orders/<id>`. `GET http://localhost:5080/health` checks that the API process is listening.

Exercise compensation by simulating a payment decline:

```sh
curl -i -X POST http://localhost:5080/orders \
  -H 'Content-Type: application/json' \
  -d '{"customerEmail":"buyer@example.com","totalAmount":49.95,"simulatePaymentFailure":true}'
```

The order moves to `CompensationPending` while the Saga requests inventory release, then to `Cancelled` after Inventory confirms the release. Poll the returned order ID to see each persisted status.

Payment is simulated locally. A declined charge occurs before capture, so this demonstration compensates by releasing inventory; it does not integrate with a payment provider or issue refunds.

## Test API Scenarios

Start Docker infrastructure and all five .NET processes as described above before running these requests.

### VS Code REST Client

Install the **REST Client** extension (`humao.rest-client`), open [Order.Api.http](src/Order.Api/Order.Api.http), and select **Send Request** above a request. Run each create request before its paired status request. The status GET can be sent repeatedly while asynchronous messages are being processed.

### Postman

Import [SagaPattern.postman_collection.json](SagaPattern.postman_collection.json) into Postman. The collection uses `http://localhost:5080` as its `baseUrl`, saves order IDs from create responses, and polls each order status. Run it with **Collection Runner** and set the request delay to **500 ms**; each status check retries up to 20 times.

If Node.js is installed, the same collection can also run from a terminal with Newman:

```sh
npx --yes newman run SagaPattern.postman_collection.json --delay-request 500
```

The collection covers:

| Scenario | Request switch | Expected terminal status |
| --- | --- | --- |
| Successful inventory and payment | No simulation flags | `Confirmed` |
| Payment decline and inventory compensation | `simulatePaymentFailure: true` | `Cancelled` after the temporary `CompensationPending` state |
| Inventory rejection | `simulateInventoryFailure: true` | `Cancelled` with an unknown-SKU reason; payment is not attempted |

The `.http` format is for VS Code REST Client and cannot be imported directly into Postman. Use the Postman collection when working in Postman. The automated Saga unit tests remain available with `dotnet test SagaPattern.sln` and do not require these services to be running.

## Message flow

1. Order API stores the order and publishes `OrderSubmitted` through its EF transactional bus outbox.
2. The orchestrator persists a Saga instance, sends `ReserveInventory`, then sends `ChargePayment` after `InventoryReserved`.
3. A successful payment publishes `OrderStatusChanged(Confirmed)`. Order API updates its read status and Notification records a local confirmation log.
4. A declined payment publishes `OrderStatusChanged(CompensationPending)` and sends `ReleaseInventory`. Only after `InventoryReleased` does the Saga publish `OrderStatusChanged(Cancelled)`.
5. Inventory release is idempotent. Transient consumer failures use retries; if the retry policy is exhausted, inspect RabbitMQ's error queue and the Saga remains pending for operator recovery.

Messages include a correlation ID (the order ID). Each service has its own EF inbox/outbox tables for duplicate detection and reliable message publication. RabbitMQ queues and Saga state are durable.

The main receive queues are `order-saga`, `order-status`, `inventory-service`, `payment-service`, and `notification-service`. Failed messages after retries appear in the corresponding RabbitMQ error queue (for example, `inventory-service_error`). The orchestrator persists `CompensatingInventory`; an operator must repair the cause and replay/recover the failed message before the order can leave `CompensationPending`.

## Service databases

| Owner | Local database | Data |
| --- | --- | --- |
| Order API | `orders_db` | Order and client-visible status |
| Inventory | `inventory_db` | Demo stock and reservations |
| Payment | `payments_db` | Simulated payment records |
| Notification | `notifications_db` | Local confirmation log |
| Orchestrator | `saga_db` | Persisted Saga state |

## Inspect PostgreSQL Data

Make sure Docker Compose is running, then open an interactive PostgreSQL prompt from the solution directory:

```sh
docker compose exec postgres psql -U saga -d postgres
```

Useful `psql` commands:

```text
\l                    list databases
\c orders_db          connect to a service database
\dt                   list tables in the selected database
\d "Orders"           show columns and indexes for a table
\q                    exit psql
```

Run the following example queries from the `psql` prompt. Each `\c` switches to that service's database; table and column names are quoted because EF Core creates case-sensitive PostgreSQL identifiers.

```sql
\c orders_db
SELECT "Id", "CustomerEmail", "TotalAmount", "Status", "FailureReason", "CreatedAt"
FROM "Orders"
ORDER BY "CreatedAt" DESC
LIMIT 20;

\c inventory_db
SELECT * FROM "Items";
SELECT * FROM "Reservations" ORDER BY "OrderId";

\c payments_db
SELECT * FROM "Payments" ORDER BY "UpdatedAt" DESC;

\c notifications_db
SELECT * FROM "Notifications" ORDER BY "SentAt" DESC;

\c saga_db
SELECT "CorrelationId", "OrderId", "CurrentState", "FailureReason", "SubmittedAt"
FROM "OrderSagas"
ORDER BY "SubmittedAt" DESC;
```

The service databases also contain MassTransit inbox/outbox tables (`InboxState`, `OutboxMessage`, and `OutboxState`); use `\dt` to inspect them. The orchestrator calls `SetCompletedWhenFinalized`, so completed Saga rows are deleted. Check final order status in `orders_db`; `saga_db` is for in-progress workflows such as `AwaitingPayment` or `CompensatingInventory`.

For a GUI such as DBeaver or pgAdmin, connect to host `localhost`, port `5432`, username `saga`, password `saga_local_only`, and select the database you want to inspect (`orders_db`, `inventory_db`, `payments_db`, `notifications_db`, or `saga_db`). These are local-only credentials.

## Watch Database Changes During a Test

Postman and the `.http` requests call the Order API; they do not connect directly to PostgreSQL. Keep the service processes running, then open another terminal in the solution directory and start a query that refreshes once per second:

```sh
docker compose exec postgres psql -U saga -d orders_db
```

At the `psql` prompt, run this query to watch new orders and status updates while sending requests from Postman or VS Code:

```sql
SELECT "Id", "Status", "FailureReason", "UpdatedAt"
FROM "Orders"
ORDER BY "CreatedAt" DESC
LIMIT 10;
\watch 1
```

For Inventory, open a second terminal and connect to its database:

```sh
docker compose exec postgres psql -U saga -d inventory_db
```

Then watch stock and reservations:

```sql
SELECT item."Sku", item."AvailableQuantity", reservation."OrderId", reservation."Status"
FROM "Items" AS item
LEFT JOIN "Reservations" AS reservation ON item."Sku" = reservation."Sku"
ORDER BY reservation."OrderId" DESC NULLS LAST
LIMIT 20;
\watch 1
```

After each query, `\watch 1` refreshes the results once per second. Use `Ctrl+C` to stop watching, then `\q` to exit `psql`.

### Payment Service: payment status

```sh
docker compose exec postgres psql -U saga -d payments_db
```

```sql
SELECT "OrderId", "Amount", "Status", "UpdatedAt"
FROM "Payments"
ORDER BY "UpdatedAt" DESC
LIMIT 10;
\watch 1
```

Successful orders show `Captured`; payment-decline orders show `Failed`. Inventory rejection should not create a payment row.

### Notification Service: confirmation status

```sh
docker compose exec postgres psql -U saga -d notifications_db
```

```sql
SELECT "OrderId", "Status", "SentAt"
FROM "Notifications"
ORDER BY "SentAt" DESC
LIMIT 10;
\watch 1
```

Only confirmed orders are recorded by the local notification simulator.

### Saga Orchestrator: active workflow status

```sh
docker compose exec postgres psql -U saga -d saga_db
```

```sql
SELECT "CorrelationId", "OrderId", "CurrentState", "FailureReason", "SubmittedAt"
FROM "OrderSagas"
ORDER BY "SubmittedAt" DESC
LIMIT 10;
\watch 1
```

This shows active states such as `AwaitingInventory`, `AwaitingPayment`, and `CompensatingInventory`. Finalized Saga rows are deleted by design; check `orders_db` for completed order status. The `\watch` views show committed row changes, not every SQL statement.

To see EF Core SQL statements in a service terminal, restart that service with command logging enabled. For example:

```sh
env Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information \
  dotnet run --no-launch-profile --project src/Order.Api --urls http://localhost:5080
```

Use the corresponding project path for another worker. This can produce verbose logs, so it is intended for local debugging only. No database credentials need to be put in Postman.

## Configuration and safety

Connection strings and RabbitMQ settings are in each service's `appsettings.json` and can be overridden with environment variables. Examples: `ConnectionStrings__Orders`, `ConnectionStrings__Saga`, `RabbitMq__Host`, `RabbitMq__Username`, and `RabbitMq__Password`. The checked-in credentials are for local development only. The payment failure flag is a demo switch, not a production payment API.

## Stop and Reset

Stop the containers while keeping their data volumes:

```sh
docker compose down
```

Reset all local RabbitMQ/PostgreSQL data and recreate the initial databases on the next start:

```sh
docker compose down -v
```

**Warning:** `down -v` permanently deletes the local service databases, Saga records, and broker data. It is not needed for normal shutdown. Stop the five `dotnet run` processes separately with Ctrl+C in their terminals.

## Troubleshooting

- Check container health and logs with `docker compose ps` and `docker compose logs -f rabbitmq postgres`.
- If a port is occupied, stop the other process or change the host port mapping in `compose.yaml` and update the matching local connection string / API URL.
- If a database is missing after changing the init SQL, remember that init scripts only run for a new PostgreSQL data volume. Create the database manually or intentionally reset the local volume as described above.
- If an order remains `Pending`, make sure all five .NET processes are running and inspect their logs and RabbitMQ queues.
- If an order remains `CompensationPending`, inspect the Inventory worker and `inventory-service_error`, correct the cause, then recover the failed release message. The status is intentionally not changed to `Cancelled` before inventory release succeeds.
- Run `dotnet test SagaPattern.sln` to verify the orchestrator's success and compensation transitions without Docker.

## Build and tests

```sh
dotnet build SagaPattern.sln
dotnet test SagaPattern.sln
```

The Saga state-machine tests use MassTransit's in-memory test harness and do not require Docker.
