# ADR 0025 — Public deployment: nothing without a session, a daily rebuild and a release by hand

- **Status:** accepted (plan approved by the user, 2026-11-25) · **Origin:** SPEC 0086 · **Completes:** ADR 0014, 0021, 0022 and 0023

## Context
The demo has hardly any visits, must survive months without anyone looking after it, and is paid with the free hours of a database that sleeps. A bot that wakes it up spends them. GitHub Actions is stopped (billing) and the repository stays private, so neither the migration nor the daily reset can depend on it.

## Decision
- **Nothing is read without a session either.** The guard takes every endpoint, unless it says otherwise (`[WithoutSession]`): a new one is closed without anyone having to remember it. A test goes through the routes themselves and counts the connections. `/health/ready` is one more endpoint, with session and quota; `/health/live` stays open and never asks the database.
- **The key of the session is one, from the configuration** (`Session:Key`), served to Data Protection as if it were its repository, with the XML the framework itself exports. It does not expire, all the instances share it and no start opens a connection. Outside Development and Testing the API does not start without it. The table of keys goes away.
- **The demo is rebuilt by the API itself**, at `GET /api/admin/demo/rebuild`, because the cron of Vercel only sends a GET, with the secret it already sends (`CRON_SECRET`). The loader runs inside, against `127.0.0.1` and by HTTP: everything still comes in through the public API, with its checks. It is a library, `src/DemoData`, shared with `tools/SampleData`.
- **One rebuild at a time**, with an advisory lock of a transaction that stays open meanwhile: Vercel may call twice and another instance may answer. A failure is tried once more from the beginning, if there is time left.
- **Publishing is moving the branch `production`**, the only one Vercel deploys, and `deploy/release.ps1` is who moves it: build, tests, the image started as it will be deployed, migrations with the bundle of that same image, push, and `--check`. It is run by the user, who types the connection string of the owner: no service keeps it.
- **A migration must also work with the previous version**: both live together for a moment, and going back does not undo it.

## Discarded
- **Keys encrypted in the image or in a file**: the image is public within the host, and a container has no disk of its own.
- **Deploying each push to `master`** and migrating afterwards: for a while there is a version without its tables.
- **Migrating when the API starts**: it needs the owner's role, wakes the database up with each start and runs in several instances at once.
- **Staged deployments of Vercel** (domains not assigned by themselves): an instant rollback and its undoing change that setting by themselves.
- **Loading the demo by SQL**: it would skip the checks that make the demo worth believing.
- **Starting the loader as another process**: it would need the secret in its environment and a way to know how it ended.

## Consequences
- A bot that asks for a session can still wake the database up, within the quotas: ten sessions a minute for each address, and the rules of the firewall of Vercel.
- The quotas live in the memory of each instance: with several, they are for each of them.
- The session key cannot be changed without ending the sessions. Nothing else is lost: a session has no permissions.
- A container that builds but does not start is published: that is what `release.ps1` checks before and after.
- Update (2026-11-27): Vercel deploys the `production` branch of the **public repository**. `release.ps1` checks the private commit as before, exports it (allowlist, rewrites and the content gates of the export) as one more commit on the public history, and moves the public `master` and `production` to it; the private `production` moves too, as the record of what is migrated. It stops if the public repository has commits that no export made, so that publishing never undoes them.
- Update (2026-11-25): the script asks for the connection string only when there are migrations and, the only secret it keeps, for that of the bot protection once. It is encrypted with the Windows user: a program run by that user could read it, and it only lets through the bot protection.
- If both attempts of a night fail, the demo may stay half loaded until the next one, or until the cron is run by hand.
- The limit of 300 s of the host and the time against Neon are not measured yet.
