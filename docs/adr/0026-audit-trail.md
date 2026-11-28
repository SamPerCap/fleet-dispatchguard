# ADR 0026 — Audit trail: in the transaction, append only, and never on its own

- **Status:** accepted (plan approved by the user, 2026-11-25) · **Origin:** SPEC 0085 · **Completes:** ADR 0021, 0024 and 0025


> **2026-11-28 (SPEC 0103):** the notice no longer depends on the session. Opening any page (cover included) sends `POST /api/visits/opened`, which needs no session and reads no database; the server sends one notice per visitor every 30 minutes, and none from the browser where the audit key was accepted. The visit timeline below is unchanged.
## Context
The author wants to know whether anyone enters the demo, through which application and what they do. The demo is a container that stops after 5 idle minutes, perhaps with several instances, and a database that sleeps, has 1 GB and is paid with free hours. There is no login: a session identifies a browser.

## Decision
- **Changes are audited when they are saved, in their transaction** (`DispatchGuardDbContext.SaveChangesAsync`). Nothing has to remember to do it, and a change that cannot be audited is not saved. An allocation attempt goes inside the transaction of the assignment; a rejection that cannot be recorded answers 503.
- **Append only, by permissions of the database.** `lof_app` can read and add in the four tables. The migration itself takes the rest away, because the permissions by default give everything to each new table and nobody runs `application-role.sql` again after a migration.
- **What expires is deleted by a function of the owner** (`SECURITY DEFINER`, fixed search path, the 30 days inside). The application can ask for it and decides nothing: no second user, no second secret, no separate job. The daily rebuild asks for it.
- **The trace of a request is written before its answer ends.** After it, the host may stop the process. Only if the request already used the database, with a limit of 1 s, and losing it changes nothing. The web asks for it with the `sampled` flag of `traceparent`: the refreshes that nobody asked for are not kept, unless they fail.
- **The web sends its timeline after a request of its own**, or when the tab is hidden within 2 minutes of one. No timers: a forgotten tab writes nothing.
- **Only names that are in the code are recorded.** The text of a button may have somebody's name; it is recorded if it is one of a list, and as “Otro control” (Other control) if not. The same goes for paths (pattern and numbers), errors (their type) and SQL (parameters, not values). DNI, tax id, email, telephone and notes appear as changed, without their value.
- **Ceilings inside each insert**: per session by counting, and for all by the distance between ids, which is free, holds with several instances, and makes room as things expire.
- **The notice goes to an address that is itself the secret** (ntfy): one variable, no account. It is sent by whoever inserts the visit, which happens once for each session in any instance.

## Discarded
- **Storing the IP, even truncated**: the quotas already use it in memory, and for the author the session and the link are enough. What is not stored needs no protection.
- **Triggers** to audit: they would also see bulk deletes, but the session and the thread would have to travel to the database in each transaction, behind a pooler.
- **A chain of hashes**: it protects against whoever owns the database, who is the author.
- **OpenTelemetry to a service**: another account and another secret, for a handful of visits.
- **Telegram or email**: a bot, a token and a chat, or an account in a provider. The user asked for the channel with the fewest steps.
- **Batches in the background of the server**: the process may be gone.

## Consequences
- Each save that changes something costs one more `INSERT`, and one transaction if it had none.
- `ExecuteUpdate` and `ExecuteDelete` are not audited: the reset of the demo, the only one that uses them, writes its own event. Whoever adds another has to do the same.
- The rows of the daily load are not audited one by one, only the reset.
- Whoever knows the address of ntfy can read and send notices: it carries no personal data, and changing it is changing a variable.
- A script that asks for sessions without sending batches gives no notice. Whoever does send them can give 30 an hour for each instance, at most.
- A visitor who closes the tab before the first answer leaves nothing.
