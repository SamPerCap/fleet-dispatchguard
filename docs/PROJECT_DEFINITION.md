# Fleet DispatchGuard — Project definition

> **Current source** of what Fleet DispatchGuard is, where it is going and what is decided. Updated 2026-11-15.
> Detailed original vision (January 2026): a private document, not published. Legal rules: `docs/RULES_OF_BUSINESS.md`.

## 1. What it is and for whom

Fleet DispatchGuard is a backend (plus a web app for the dispatcher) that **allocates a driver and a vehicle to each service of a passenger transport company** —regular lines, discretionary services, multi-day circuits, airport transfers, school transport— **guaranteeing compliance with CE 561/2006** before each allocation.

Target clients: bus companies. Product designed to serve several companies from a single backend (multi-company, from M2).

## 2. Problem and value proposition

Today the dispatcher checks by hand the availability, driving hours and rest periods of each driver, and shares out limited buses and drivers among many services, with last-minute changes and data spread across older systems.

Fleet DispatchGuard provides:
- **Legal validation before allocating**, never after (zero tolerance for infringements of CE 561/2006 and of return to base of UE 2020/1054).
- **Candidate search**: drivers and vehicles eligible for a service, sorted by suitability.
- **Assisted manual allocation** with a justified override for special cases.
- **Traceability**: each allocation stores the validation that justified it.
- Later on: import from each company's existing system and automatic optimisation of the planning.

## 3. Current architecture principles

- **Clean Architecture + DDD**: Core (pure domain) ← Application ← Web.Api; Infrastructure implements Core interfaces.
- **A single backend for all companies.** The code is never forked per company; the differences are data (rules, parameters).
- **Data isolated per company: one database per tenant.** That is why **there is no `TenantId` in the entities** nor global filters: the isolation is the database itself. The tenant will be resolved per request (claim / header) and will route `DispatchGuardDbContext`. The connection string is an Infrastructure secret, it never reaches Core.
- **Behaviour configurable by data**: the state legal floor (CE 561/2006, "the constitution") is immutable; each company adds `TenantRule`s ("regional laws") that **can only make it stricter, never relax it**. If a company needed logic that cannot be expressed as a rule: Strategy/plugin per tenant inside the same code base (`IComplianceRule`).
- **Import through an anti-corruption layer**: external data is rebuilt under the canonical model before persisting; one-way import (Fleet DispatchGuard never writes to the source system).
- **The frontend contains no business logic**: it consumes the API and, from M2, identifies itself with its tenant.

## 4. Current decisions

| Decision | Date | Status | Note |
|---|---|---|---|
| Pooled app + DB-per-tenant + behaviour by data | 2026-07-09 | firm | §3. SPECs 0018–0019, 0027–0029 |
| Rules per company = **thresholds that only make stricter + catalogue of templates** (no DSL) | 2026-07-09 | firm | Formerly D1. DSL as a future extension |
| Real road distances: **a road-routing provider (to be chosen)**, with cache (`CachedRoute`) and Euclidean prefilter | 2026-07-09 | firm | Formerly D2. M2 |
| `Trip.ServiceType` is subsumed into `Booking.BookingType` + `TripRole` | 2026-07-09 | proposal | Postponed: 0045 does not add `BookingType` (it stays in 0023) |
| Approximate availability from driving + unavailabilities (no "presence/other work") | 2026-07-09 | firm | SPEC 0030 |
| No. of suggested candidates: 3 by default, configurable | 2026-07-09 | firm | SPECs 0031/0032 |
| **First milestone = dispatcher tool for a single company** | 2026-11-13 | firm | Multi-company in M2 |
| **Data in M1 by direct entry in the API** (no import from other systems) | 2026-11-13 | firm | Import in M3 |
| **Dispatcher web app in React + TypeScript** | 2026-11-13 | firm | SPEC 0037 |
| **No authentication in M1** (local/demo use) | 2026-11-13 | firm | Arrives in M2 with the tenant in the claim |
| **Public demo without login, only with an anonymous session per browser**: it identifies a browser, not a person, and grants no permissions; the name is still declared | 2026-11-24 | firm | SPECs 0084 and 0085 (user decision) |
| **Hosting of the demo: Vercel (the container) + Neon (PostgreSQL)**, instead of Azure: almost no visits, each one is a cold start and Azure SQL takes around a minute to wake up; nothing that expires must switch it off. Conditional on a prior test on Vercel. Daily data reset. Replaces the Azure decision of the same day | 2026-11-25 | firm | SPECs 0086 and 0092 (user decision) |
| **PostgreSQL replaces SQL Server** in the product (the two are not kept) | 2026-11-25 | firm | SPEC 0092, ADR 0024; the test on Vercel passed |
| **The demo is shown to recruiters: cover, short tour and engineering page**, no video; the trace and the notice of each visit are only for the author. Since SPEC 0097, X-ray only of the answer just asked for by that session (sealed in the answer, 5 min, allow list). No name or links of the author; nothing is known about someone who only reads the cover, and it says that it is programmed with Claude Code | 2026-11-25 | firm | SPECs 0085 and 0093 |
| **Limits per minute**: per session 300 reads, 150 writes and 20 heavy calculations; per IP 600 in total, 60 heavy and 10 new sessions | 2026-11-24 | firm | SPEC 0089, ADR 0022 (plan approved by the user) |
| **In the public demo drivers, vehicles and locations are not deleted: the function stays, but it is switched off by configuration** (the API answers 403; the web app has no buttons to delete them; if added, they will come out disabled with the reason) | 2026-11-24 | firm | SPEC 0088 (user decision) |
| Workflow: one feature = one session, real verification, gates (hook + CI) | 2026-11-13 | firm | `docs/LESSONS.md §11` |
| **Goal: portfolio to present to companies**, not a product for sale | 2026-11-14 | firm | Prioritises a finished, demonstrable use case |
| **Private repo**; option of an interactive demo on a free cloud (Azure, Vercel…) | 2026-11-14 | firm | Decided when closing M1 |
| **M1 revolves around the star case "last-minute absence"**; `Booking` (0023–0026) moves to M2 | 2026-11-14 | firm | In M1 services are created directly as `Trip` |
| **Driver without enough history: weekly rest period "not verifiable" → warning, not block**; it is only a critical violation if the known history covers 6×24 h without a rest period | 2026-11-15 | firm | Revokes 0015 CA4. SPEC 0040 |
| **Position of the vehicle for the ranking = destination of its last trip (7 days) → base → unknown (at the end)**; schedule overlap = hard filter | 2026-11-16 | firm | SPEC 0032. Arriving on time (needs 0033) and chaining trips / "always returns to base" (M4 optimiser) deferred |
| **Radius 5–40 km of the ranking (own design decision) does not exclude**: drivers at > 40 km and then without a base go to the end, with their reason | 2026-11-16 | firm | SPEC 0031 |
| **Sample data: real stops of the Madrid interurban network (CRTM) transcribed by hand with the source cited, without downloading GTFS; fictitious company; idempotent load through the API** | 2026-11-17 | firm | SPEC 0038 |
| **The gap between the last driving period and the start of the service counts as a weekly rest period if ≥ 24h** | 2026-11-17 | firm | SPEC 0039 (closes the pending item of 0040) |
| **Closing of the product (after M1): 0042 explanations → reduced M4 (heuristic optimiser) → hardening → public deployment**; M3, multi-company, auth, `TenantRule`, Google Maps and Bookings move to future work | 2026-11-17 | firm | Public demo only when everything is robust |
| **Each allocation, rejection and ranking carries an explanation in two layers (business / technical) generated from what the engine calculated**, never from fixed texts | 2026-11-17 | firm | SPEC 0042 |
| **Bookings (0045): every trip always belongs to a booking (even if it has a single trip); the client is only a name; the same driver is preferred across the whole booking, never mandatory**; CE 561/2006 is still validated trip by trip | 2026-11-18 | firm | User decision. Booking status derived from its trips, not stored |
| **Recommend allocation: heuristic proposal (not a global optimum) for a booking or the queue, reviewable and not saved until “Apply”; tiers of 5/15/30 km and, within them, the order of the panel; in a booking the team is kept even beyond 30 km; it never forces nor touches what is allocated** | 2026-11-23 | firm | SPEC 0062, ADR 0020 |
| **Allocate week (or day, or month): main button of Planning that proposes and, after a confirmation, applies at once what is recommendable in the period; one failure does not stop the rest and the summary gives the reason for each trip left unallocated. It adds to Recommend, allocating by hand and Km saving** | 2026-11-24 | firm | SPEC 0068 |
| **“Optimise week” becomes the “Km saving” report: read only, below “Allocate week”; it explains what it looks at, what it does not do and that it is not an optimum. Making it applicable is left out** | 2026-11-24 | firm | SPEC 0069 |
| **Km saving: a service that has already departed keeps its bus and counts in both figures; the saving comes only from what can still be changed (also in the Control Centre)** | 2026-11-24 | firm | SPEC 0070, ADR 0013 |
| **Km saving: no bus departs before now; the one that already has the service is kept as an option even if the estimate says it does not arrive, and that never invalidates the report. Its week and its days are those of Madrid** | 2026-11-24 | firm | SPEC 0071, ADR 0013 |
| **Km saving: the number of each service opens its booking and there only the bus can be changed (manual allocation, same checks); the proposed one is offered marked, without being chosen automatically, and the report is recalculated. It still does not apply the list** | 2026-11-24 | firm | SPEC 0072 |
| **New booking: origin and destination are searched while typing (name, address, city or province), with 20 options at most. The search is in memory over the places already loaded; search on the server only if they reach tens of thousands** | 2026-11-24 | firm | SPEC 0077 |
| **Agenda of driver and vehicle in their record (annual leave, sick leave, ITV, workshop…): saving over an allocated service is allowed, warns and removes nothing; the conflict is shown in the record and in Planning until it is resolved. The engine does not change; when the ITV is due is not calculated** | 2026-11-24 | firm | SPEC 0078, ADR 0019 |
| **Driver and vehicle data editable in their record (DNI, name, registration plate, seats, type, automatic proposal, base, reference location): lowering seats warns of the services that no longer fit and lets you save; nothing allocated is removed or revalidated** | 2026-11-24 | firm | SPEC 0079, ADR 0019 |
| **Two people editing the same driver or vehicle record: the second one receives a 409 and saves nothing; it keeps what was typed and loads the current data before saving. Changing the agenda is not a conflict** | 2026-11-24 | firm | SPEC 0090 (plan approved by the user) |
| **The Control Centre warns (“Not enough seats”) of today's unfinished services that need more seats than their vehicle has; it goes away by itself once fixed** | 2026-11-24 | firm | SPEC 0081 (user decision) |
| **Booking detail: one block per trip, sorted by departure and numbered (“Trip 2 of 4”, cancelled ones too so that the number does not change), with all the data labelled; allocation happens inside its block and several forms can be open** | 2026-11-24 | firm | SPEC 0080 |
| **When allocating, driver and bus are searched while typing: all the engine's candidates can be reached (before, only three buses) and whoever cannot be chosen appears with their reason, without being selectable. Also in “Last-minute absence”, where the full list of substitutes is calculated only if asked for** | 2026-11-24 | firm | SPEC 0082, 0083 |
| Other fleet projects (SAE control tower, predictive maintenance) **outside Fleet DispatchGuard**: separate repos connected by contracts/events | 2026-11-14 | firm | Only after finishing M1 |

## 5. Roadmap

### M1 — Dispatcher tool (one company)
Data through the API, React + TS web app, no auth. It revolves around the **star case "last-minute absence"**: a driver drops out shortly before departure and the system proposes the 3 best **legal** replacements (CE 561/2006), explaining why each one is valid, to allocate it with one click. Order:

| # | SPEC | What | Size |
|---|---|---|---|
| 1 | 0034 ✅ | Real EF persistence: generic repository + the 7 stub repositories + `TripRepository` queries. Closes the P0 (closed 2026-11-15) | M |
| 2 | 0035 ✅ | Robust allocation endpoint: correct `tripId`, schedule conflict → 409, global error handling (`ProblemDetails`) | S |
| 3 | 0036 ✅ | Master data API: drivers (with the declared date of the last weekly rest period, optional), vehicles, locations, services (`Trip`) and unavailabilities; remove `ClientsController` (closed 2026-11-15) | M |
| 3b | 0040 ✅ | Weekly rest period **not verifiable** with insufficient history → warning, not block (otherwise every new driver is blocked) | S |
| 4 | 0030 ✅ | Driver availability (closed 2026-11-16) | S |
| 5 | 0031 ✅ | Search and ranking of candidate drivers (ranking by Euclidean distance in M1; closed 2026-11-16) | M |
| 6 | 0032 ✅ | Search and ranking of candidate vehicles: position = destination of the last trip → base → unknown (closed 2026-11-16) | S |
| 7 | 0037 ✅ | Dispatcher web app (React + TS) in `web/`: **planning Gantt** per driver/vehicle and **incident flow** (put on leave → 3 legal replacements with their explanation → allocate) (closed 2026-11-16) | M |
| 8 | 0038 ✅ | Credible sample data: fictitious company with 15 real CRTM stops (transcribed by hand, no GTFS), drivers, fleet and a week of services; idempotent seeder through the API (closed 2026-11-17) | S |
| 9 | 0039 ✅ | Portfolio presentation: ADRs in `docs/adr/`, business README without invented figures, benchmark of the legal validation (BenchmarkDotNet); fixes the weekly rest period right before the service (closed 2026-11-17, **M1 closed**) | S |

When closing M1: update `docs/index.html` and decide whether the interactive demo is deployed.

### Closing of the product (user decision, 2026-11-17)
Goal: a public demo in which anyone can see how allocation works and, with an ⓘ button, what has happened behind the scenes (business and technical). Order:

| # | SPEC | What | Size |
|---|---|---|---|
| 0 | 0041 ✅ | Daily rest period by window (CE 561/2006 Art. 8.2): split shift evaluated independently of the order of the records (closed 2026-11-17) | S |
| 1 | 0042 ✅ | **"Explain to me what happened"**: each allocation, rejection and ranking returns an explanation in two layers (business with the calculated figures; technical with the real flow through the code and the ADR), and the web app shows it in an ⓘ pop-up | M |
| 2 | 0043 ✅ | **Reduced M4 — heuristic optimiser** (read-only API; and web 0043b, since 0069 the “Km saving” report; closed 2026-11-17): chains trips to reduce empty km and respects "the vehicle returns to base"; straight-line distance and average speed of 70 km/h configurable in a single place | M |
| 3 | 0044 ✅ | **Hardening** (closed 2026-11-17): E2E of the web app, input validation, reset of the demo data, request limit, ⓘ in the Gantt and times in Europe/Madrid | M |
| 4 | 0087 ✅ | **Quality and dependencies**: warnings as errors, web linter, dependencies and secrets watched in CI | S |
| 5a | 0088 ✅ | **Blocking the deletion** of master data in the demo, by configuration | S |
| 5 | 0084 ✅ | **Anonymous session**: nothing is written without a session that the API recognises | M |
| 5b | 0089 ✅ | **Perimeter**: limits per session and IP, headers, own fonts and data out of the URL | M |
| 5c | 0090 ✅ | **Simultaneous edits** of a record: the second one receives 409 | S |
| 6 | 0091 ✅ | **Ready for the cloud**: one container, the web app served by the API, cold start and saving of the free quota | M |
| 6b | 0086 (step 0) ✅ | **Test on Vercel** of the current container, without a database | S |
| 6c | 0092 ✅ | **From SQL Server to PostgreSQL**, with the same behaviour | M |
| 6d | 0086 | **Deployment on Vercel + Neon** and bot blocking: published on 2026-11-25. Still missing: CA3, the first nightly cron, E2E, ZAP and moving the cron to Schedules | M |
| 7 | 0085 | **Traceability of each visit**, notice to the author and link per application (published on 2026-11-25; screen only for the author) | M |
| 8 | 0093 | **Showcase and “open the bonnet”** (done in its branch on 2026-11-25; visual QA and E2E missing); after that, final scan and publish the link | M |

### Extension: Bookings and redesign of the web app (user decision, 2026-11-18)
A booking is a client's order that groups one or several trips (e.g. an outbound and return excursion). Mobile is ruled out for now.

| # | SPEC | What | Size |
|---|---|---|---|
| 1 | 0045 ✅ | **Bookings (backend)**: `Booking` with client (name), description, notes and its trips; migration that gives each existing trip its booking; derived status (unallocated / partial / complete); API `/api/bookings`; the driver who already covers another trip of the booking goes up to #1 of the candidates (preference, never a filter) and it is explained; demo week grouped into bookings (closed 2026-11-18) | M |
| 2 | 0046 ✅ | Wireframes of the web app centred on bookings | S |
| 3 | 0047 ✅ | Redesign of the web app centred on bookings, with a detail panel | M |
| 4 | 0048 ✅ | Driver and vehicle records | S |

### Future work (outside the closing of the product)
- **Bookings (rest):** 0023 (`BookingType`, client as an entity), 0024 (absorb `Circuit`), 0025 (invariants per `BookingType`); 0026 is covered by 0045 except what depends on `BookingType`.
- **Multi-company and authentication:** 0018 (tenant catalogue), 0019 (DB routing per request), authentication with the tenant in the claim, 0027 (rules pipeline), 0028 (`TenantRule`), 0029 (composition and precedence).
- **Real distances:** a backlog SPEC, not published.
- **Import from existing systems (former M3):** backlog SPECs 0020–0022, not published.
- **Full optimiser:** global optimum of the whole planning, beyond the heuristics done (chaining vehicles, 0043; recommending driver and vehicle, 0062).

## 6. Out of scope of M1

Bookings (`Booking`, it came later in 0045), multi-company, authentication, import from existing systems, automatic optimiser, real road distances and companies' own rules.

## 7. Glossary

- **Booking** — what a client orders; groups 1..N `Trip`; every trip belongs to one (0045). The `BookingType` is still future (0023).
- **Trip (service/trip)** — operational unit with a time window and required seats; it receives a driver and a vehicle.
- **BookingType** — regular line, discretionary, school excursion, circuit, multi-stage route, airport transfer.
- **Dispatcher** — user who plans and allocates resources in the company.
- **Candidate** — driver or vehicle eligible for a `Trip`, sorted by suitability.
- **Legal floor** — rules of CE 561/2006 and UE 2020/1054; immutable (`DrivingLimits`).
- **Tenant** — client company with its own database (from M2).
- **TenantRule** — a company's own rule that only makes the legal floor stricter (from M2).
- **Anti-corruption layer (ACL)** — translates external data to the canonical model before persisting (M3).
