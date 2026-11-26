# ADR 0023 — One container that serves everything, in front of a database that sleeps

- **Status:** accepted · **Origin:** SPEC 0091

> **2026-11-25:** the host is now Vercel with Neon (SPECs 0086 and 0092). The decisions stand; the figures of Azure are those of the moment. The role is written for PostgreSQL and the locks are advisory ones (ADR 0024).

## Context
The demo goes to Azure Container Apps with a free Azure SQL database: 100,000 vCore-seconds a month, about 55 h awake. The database goes to sleep after 15 minutes without use, and when the allowance ends it stays down until the next month. A page left open that polls every minute uses it up in 2.3 days.

## Decision
- **One image** for the API, the web, the migrations (an EF bundle, so without the SDK) and the demo loader. Same origin: no CORS, and the cookie of the session never leaves it. The base is chiseled (no shell, no package manager) and runs without privileges and on a read-only file system. Base images by digest.
- **The web is files, before the limits**: serving them costs nothing, and counting them would spend the quota of an address on a single visit. What is not a file, `/api` or `/health` is the index; a missing file is not found, so that an open page of the previous version fails clearly.
- **Two health questions.** `live` never touches the database, and is the only one for the probes of the host: the other would keep the database awake. `ready` connects without retries nor pool and gives up in 5 s. Both are short-circuited in the routing, before the session: reading its cookie needs the keys, which are in the database. `ready` has no quota, so the questions that coincide share one connection.
- **The page of a sleeping database** asks `ready`, not the data: nothing of the web is mounted until it answers.
- **The polling stops** with the tab hidden or after 10 minutes without activity. Coming back loads at once only if the data are older than a minute. Scrolling is not activity: the page scrolls by itself when its content changes.
- **CSP of the pages**: scripts are never inline. Styles are, because the Radix dialog inserts one whose content depends on the scrollbar, so it has no fixed hash; a nonce would need an index made for each request. It is the only medium alert of ZAP that is accepted, by its reference.
- **The user of the application** has rows and nothing else: a role with `SELECT`, `INSERT`, `UPDATE` and `DELETE` on the schema, and not on the history of migrations. `sp_getapplock` is open to every user.
- **CI tests the image and not the code next to it**: behind an HTTPS proxy, with the limited user, without database at the start; then its E2E and ZAP.

## Consequences
- The E2E in CI no longer uses Vite. Locally it still can.
- The migrations bundle builds the host of the API: it must run without `ASPNETCORE_ENVIRONMENT`, because in Production the host asks for settings that the bundle does not have.
- The web cannot load anything from another origin, not even an embedded font (`assetsInlineLimit: 0`).
- The deployment must point its probes at `live`, with a `Host` that `AllowedHosts` admits.
- What ZAP sees is the six files of the web: the baseline does not run scripts, so it does not reach the API. Its headers are tested in .NET.
