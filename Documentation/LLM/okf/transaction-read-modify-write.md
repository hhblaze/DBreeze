---
type: Concurrency Pitfall
title: Read-modify-write synchronization
description: Reserve tables before dependent reads to prevent lost updates, even when a transaction modifies only one table.
tags: [transactions, concurrency, synchronization, lost-updates]
sources:
  - id: shared-transaction
    resource: ../../../DBreeze/Transactions/Transaction.cs
  - id: modern-transaction
    resource: ../../../DBreeze.Net8/Transactions/Transaction.cs
  - id: shared-coordinator
    resource: ../../../DBreeze/Transactions/TransactionsCoordinator.cs
  - id: standard-coordinator
    resource: ../../../DBreeze.NetStandard/Transactions/TransactionsCoordinator.cs
  - id: modern-coordinator
    resource: ../../../DBreeze.Net8/Transactions/TransactionsCoordinator.cs
  - id: configuration
    resource: ../../../DBreeze/Engine/DBreezeConfiguration.cs
  - id: regression-checks
    resource: ../../../DBreeze.Net8.Tests/Program.cs
---
# Production rule

Call `tran.SynchronizeTables(...)` once with the complete write set before any
read used to compute or decide a write, even for a single table. All competing
read-modify-write paths must reserve before reading. Use the default
`AsReadVisibilityScope: false` for these reads. See the compact
[production skill](../DBreeze_skill.md#33-gettransaction-table-locks-versus-synchronizetables)
for the balance-increment example.[^shared-transaction][^modern-transaction]

# Lost-update mechanism

`Select` obtains a read table without acquiring a write reservation. The first
modification, such as `Insert`, obtains a write reservation through
`GetTable_WRITE` and waits for competing writers when needed. A value already
computed by application code is not recomputed after that wait.
Two transactions can therefore read `1000`, each calculate `1100`, then commit
`1100` in sequence, losing one increment. Both reads can observe committed data;
this is a lost update, distinct from reading uncommitted data.
[^shared-coordinator][^standard-coordinator][^modern-coordinator]

`SynchronizeTables` uses the same coordinator to reserve the supplied table set
before returning. A competing read-modify-write transaction requesting the same
reservation waits before its read and therefore calculates from the preceding writer's
committed result. Reservations are at table level and last until transaction
disposal; `Commit()` and `Rollback()` keep them. Ordinary reads remain possible.
[^shared-transaction][^modern-transaction][^shared-coordinator][^standard-coordinator][^modern-coordinator]

# Early-warning boundary

`NotifyAhead_WhenWriteTablePossibleDeadlock` defaults to `true`. It rejects
acquisition of an additional, unreserved write table when the transaction already
holds a write reservation. The failure unregisters the transaction, rolls back
uncommitted writes, and releases its reservations. This check does not require
concurrent activity and does not detect single-table read-modify-write races.
Single-table writes independent of existing data rely on the automatic
reservation.[^configuration][^shared-coordinator][^standard-coordinator][^modern-coordinator]

# Existing regression coverage

`CoordinatorDoesNotLoseWakeups` reserves one counter table before reading and
incrementing, then checks the total across concurrent workers.
`CoordinatorCleansUpNotifyAheadFailure` checks that a second unreserved write
table throws and that cleanup allows the next writer to proceed. These checks
were inspected for this documentation correction; they were not run.
[^regression-checks]

[^shared-transaction]: Shared SynchronizeTables, read routing, Commit/Rollback, and Dispose implementations.
[^modern-transaction]: Net8 equivalents, including reserved-table read routing.
[^shared-coordinator]: Framework read/write acquisition, reservation waiting, and failure cleanup.
[^standard-coordinator]: NetStandard coordinator, also linked by Net6, NetCoreApp, and Portable projects.
[^modern-coordinator]: Net8 read/write acquisition, reservation waiting, and failure cleanup.
[^configuration]: Default early-warning configuration.
[^regression-checks]: Existing synchronized counter and early-warning cleanup checks.
