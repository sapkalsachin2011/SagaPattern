# Order API

ASP.NET Core entry point for order creation and status queries. The API owns `orders_db`; it does not read the Inventory, Payment, or Saga databases.

## Endpoints

- `POST /orders`: validates the request, stores a `Pending` order, and publishes `OrderSubmitted` in the same PostgreSQL transaction using the EF bus outbox. Returns `202 Accepted` and the order ID.
- `GET /orders/{orderId}`: returns the persisted order and its asynchronously updated status.
- `GET /health`: process liveness check.

The API also consumes `OrderStatusChanged` to update the order's status and reason. Its RabbitMQ endpoint uses an EF inbox/outbox and retries transient failures.

## Run

Start Docker infrastructure from the solution root, then run:

```sh
dotnet run --no-launch-profile --project src/Order.Api --urls http://localhost:5080
```

## Test Scenarios

- For VS Code, open [Order.Api.http](Order.Api.http) with the REST Client extension and send the create request before its paired status request.
- For Postman, import the root [SagaPattern.postman_collection.json](../../SagaPattern.postman_collection.json) and run it with a 500 ms delay. It covers success, payment-failure compensation, and inventory rejection.

See the root [README.md](../../README.md) for service startup, local credentials, database inspection, and troubleshooting.
