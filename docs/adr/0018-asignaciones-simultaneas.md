# ADR 0018 — Simultaneous allocations: application lock in SQL Server, in a fixed order

- **Status:** accepted (scope by the user, 2026-11-23; mechanism, technical decision) · **Origin:** SPEC 0065 · **Completes:** ADR 0001, 0015 and 0016

> **2026-11-25 (SPEC 0092, ADR 0024):** the database is PostgreSQL. The mechanism becomes `pg_advisory_xact_lock`; the order, the total
> wait, the 503 and the retries stay the same. What it says about SQL Server and the measures are from that time.

## Context
Allocating reads the driver's calendar, validates and saves. Between reading and saving there was nothing: two requests at the same time
decided with the same data and both saved. Checked with two instances: double allocation of the same driver and
of the same vehicle, 12 h in one working day, cancelled services that went back to `Assigned`, “Forced by service #T” marks
from a service that was no longer there, and a half-done reallocation if the second save failed.

## Decision
- **Exclusive `sp_getapplock` with the transaction as owner**, one per resource: `lof:trip:{id}`, `lof:driver:{id}`,
  `lof:vehicle:{id}`. It holds across instances and is released only on commit, rollback or loss of the connection.
- **Fixed order:** services → drivers → vehicles, and by id within each group. The service goes first because it says which
  driver and vehicle it has now, which are locked together with the new ones. As all follow the same order, there is no cycle.
- **A single path:** `IUnitOfWork.ExecuteExclusiveAsync(claim, work, written)`. It opens the transaction, takes the locks, clears
  the EF change tracker so that `work` reads from the database, saves and commits. The order cannot be chosen from outside. The
  caller asks for ids: `AllocateDriver` no longer accepts entities read before.
- **There is no need to lock the later services that get marked (0063):** a mark only exists between services of the same
  driver, and to change or cancel one of them you must hold the lock of its driver.
- **`READ COMMITTED`.** With the locks taken, nobody else writes what is read. It works with `READ_COMMITTED_SNAPSHOT`
  (that is how EF creates the database) and without it.
- **A wait of 5 s in total** (`Allocation:LockWaitMilliseconds`), not per resource. Measured: about 14 ms per queued allocation
  on the same driver, so 5 s are hundreds of operations ahead; more would make the dispatcher wait without knowing
  why. Exhausted: `ResourceBusyException` → **503** with `Retry-After: 1` and `retryable: true`. It is not 409 or 422, which
  here are rejections with a reason. It does not go through the attempt log, so it does not count as a block.
- **EF retries** (`EnableRetryOnFailure`): everything goes inside the strategy, which repeats the whole operation. If the
  failure arrives at commit, it checks whether the main row has the timestamp (`UpdatedAt`) that was written; if it has it,
  it is taken as done and not repeated. The exhausted wait is not transient for EF: it is not
  retried by itself. If EF repeats the operation for another cause, the wait starts again.

## Discarded
- **In-memory lock:** it does not work with several instances.
- **`SERIALIZABLE`:** it protects ranges, but two shared reads that then write always deadlock. Retrying would have to be
  the norm, and it gives neither a bounded wait nor an order.
- **`UPDLOCK, HOLDLOCK` on the driver, vehicle and service rows:** correct, but it mixes the serialisation with the
  locks that SQL Server takes on its own (foreign keys, indexes), the wait time (`SET LOCK_TIMEOUT`) affects
  every statement and, without versioned reads, the other screens would wait.
- **Optimistic concurrency (`rowversion`) and retrying:** it needs a migration and touching the driver and vehicle rows in
  each allocation. With contention the same calendar is validated several times, and whoever arrives late may always lose.
- **A constraint in the database (index or trigger):** SQL Server has no range exclusion and CE 561/2006 does not fit in it.
- **A queue per driver:** more pieces and the response stops being immediate.

## Measures
Two instances of the API published in Release against a SQL Server 2022 in a container, with the demo loaded (1112 services);
`POST /allocate` from a client on the same machine (16 cores), three runs; median of the p50 and of the p95 values, in ms.

| Case | Before p50 / p95 | After p50 / p95 | Operations/s before → after |
|---|---|---|---|
| 1 client, no contention (400) | 15.7 / 21.4 | 21.4 / 25.5 | 58 → 45 |
| 8 clients, no contention (800) | 18.2 / 24.9 | 27.0 / 40.1 | 406 → 285 |
| 8 clients, same driver (400) | 19.9 / 26.6 | 108.4 / 118.6 | 392 → 72 |

Without contention it costs between 6 and 9 ms (opening a transaction, two rounds of locks, reading the current resources and committing). With
contention it is the price sought: they go one at a time. “Before” was fast because it accepted them all: in 60 bursts of 8
requests that step on each other, it accepted 480 of 480; now 60, one per burst, and 420 get their 409.

## Consequences
- Allocations with the same driver, vehicle or service go one at a time; the others do not wait for each other.
- Each waiting request holds a connection, for 5 s at most.
- The lock is cooperative: whoever writes services, sick leaves or workshops by another route (SQL by hand, a future import worker) has
  to use `ExecuteExclusiveAsync`.
- Seeing a service “free” on screen does not reserve it: on allocating it is validated again and may be rejected.
