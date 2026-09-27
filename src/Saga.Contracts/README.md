# Saga Contracts

Shared message contracts used by the independent service processes. Contracts describe commands (for example, `ReserveInventory`, `ChargePayment`, and `ReleaseInventory`) and result/status events (for example, `InventoryReserved`, `PaymentFailed`, and `OrderStatusChanged`).

Changing a message is a distributed contract change: preserve compatibility or version the contract when evolving it. Services must not share persistence entities or access one another's databases.