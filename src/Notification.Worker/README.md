# Notification Service

Consumes `OrderStatusChanged` events and records a local notification entry in its own `notifications_db` when an order is confirmed. It logs the customer email to the console; no email/SMS provider is configured.

Notification delivery is deliberately outside the order's critical success path. A notification failure must not trigger a payment refund or inventory compensation.

## Run

```sh
dotnet run --no-launch-profile --project src/Notification.Worker
```

See the root `README.md` for infrastructure startup.