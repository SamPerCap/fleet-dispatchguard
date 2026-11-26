# ADR 0024 — PostgreSQL in the place of SQL Server

- **Status:** accepted (user decision, 2026-11-25; mechanisms, technical decision) · **Origin:** SPEC 0092 · **Completes:** ADR 0018 and 0023

## Context
The demo goes to Vercel with Neon, which is PostgreSQL. The product does not keep both databases. What the dispatcher sees must not change, and PostgreSQL differs in four things that nobody writes down: what an instant is, when two texts are equal, how a lock is waited for, and what it refuses to store.

## Decision
- **Instants are `timestamptz`, through one converter of the model** (`UtcInstantConverter`). An instant without kind is UTC, which is what the API already said (SPEC 0067); a local one is converted. They are still read without kind, so that the dates of the answers keep their text, without Z.
- **Texts carry an ICU collation, `es-u-ks-level2`, not deterministic**: it ignores case and tells accents apart, like `Modern_Spanish_CI_AS`, in comparisons, `ORDER BY`, `DISTINCT` and unique indexes. By convention for every `string`, because PostgreSQL does not admit it as the collation of a database. Enumerations stored as text do not have it: the code writes them.
- **Locks are `pg_advisory_xact_lock(kind, id)`**: a number for each kind of resource and the whole id, so no two resources share one, which a hash could not promise. Owned by the transaction: they survive a pooler in transaction mode, where a lock of the session would be left in a connection that someone else gets.
- **The total wait** (ADR 0018) is a deadline that the server keeps in a setting of the transaction. Before each lock, `lock_timeout` is what is left until it, and afterwards it goes back to its value, so that it does not limit the rows. All in one batch, so in one round trip. `55P03` is the 503 of always: it is caught before the retries of EF, which take it as transient.
- **The null character is refused (400)** in the bodies and in the filter by name: PostgreSQL does not store it.
- **`RuleIds`, violations and warnings are `text[]`; `ForcedByTrips` is `jsonb`.** The limit of 1000 characters of the JSON of warnings goes away with the JSON.
- **One initial migration.** The ones of SQL Server stay in the history of git; the demo is seeded, so there are no data to carry.
- **The role** has the rows of the schema, also of the tables of the next migrations (`ALTER DEFAULT PRIVILEGES`, which only counts for those of whoever runs the script: the owner, as in the migrations).
- **`PGGSSENCMODE=disable` in the image**: the driver would try Kerberos before TLS, and the chiseled base does not have it.

## Discarded
- **`citext`**: it compares by lowering the text with the collation of the database, so it would order in another way; it is an extension; and it does not tell the reader what it does with accents.
- **`lower()` in indexes and queries**: each query would have to remember it.
- **`pg_advisory_xact_lock(hashtext(name))`**: 32 bits, so two resources could wait for each other without reason, or cross and deadlock.
- **`statement_timeout`** for the wait: it would also count the time that is not waiting, and its error is the one of a cancelled request.
- **Reading instants as UTC**: more exact, and it would add a Z to every date of the answers.

## Measures
Demo of the seeder test (1,113 services, 1,121 attempts), PostgreSQL 17 in a container, `EXPLAIN ANALYZE` while the rest of the tests ran. There is no “before” to compare with: it was left out on purpose.

| Query | Execution |
|---|---|
| Services of a driver (the validation) | 0.2 ms |
| Services of a week with their resources | 0.6 ms |
| Page of attempts with a rule | 0.2 ms |
| People who attempted something | 1.1 ms |

## Consequences
- Instants keep microseconds, not tenths of them. The check of a doubtful commit (`SavedCheck`) compares through the database, so it does not notice.
- A collation that is not deterministic does not admit `LIKE` in PostgreSQL 17. Nothing uses it today.
- A change of ICU version between servers may ask for `REINDEX` of the indexes of text.
- The order of the locks, the wait and the answers of ADR 0018 stand; its measures are of SQL Server.
