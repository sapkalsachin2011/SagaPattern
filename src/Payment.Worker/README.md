# Payment Service

Consumes `ChargePayment` and owns payment records in `payments_db`. It responds with `PaymentCaptured` or `PaymentFailed`; the orchestrator, not this service, decides the next Saga action.

This is intentionally a local simulator, not a real payment gateway. Set `simulatePaymentFailure` to `true` when creating an order to exercise the compensation path. Repeated commands use the order ID as an idempotency key and return the existing payment outcome.

## Run

```sh
dotnet run --no-launch-profile --project src/Payment.Worker
```

See the root `README.md` for infrastructure startup and API examples.