# Inventory Service

Consumes `ReserveInventory` and `ReleaseInventory` commands from RabbitMQ. It owns `inventory_db`, including stock and order reservations; no other service accesses this database.

The local database is seeded with 50 units of `DEMO-SKU`. Reservation records are keyed by order ID so redelivered reserve/release commands do not decrement or restore stock twice. Inventory quantity has an optimistic concurrency check to protect competing reservations. Release is the payment-failure compensation.

## Run

```sh
dotnet run --no-launch-profile --project src/Inventory.Worker
```

The worker publishes `InventoryReserved`, `InventoryRejected`, or `InventoryReleased`. Consumer retries and the EF inbox/outbox are configured in the host. See the root `README.md` for infrastructure startup.