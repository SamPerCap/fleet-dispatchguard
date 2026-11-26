> A real SPEC of this project, written in English and closed on 2026-11-25; copied without changes except an English gloss after the three Spanish texts of the <!-- sample-note -->
> interface of that date, which was in Spanish then. It describes the code of that date. <!-- sample-note -->

# [0090] SPEC: Two people editing the same card

## Goal
Nobody overwrites, without seeing it, what somebody else has just saved in the data of a driver or a vehicle.

## Context
Third of the three deliveries approved with 0084 (2026-11-24). The demo shares its data between visitors, and since 0079 the `PUT` carries every field: the last one to save won. Closes that debt of `ARCHITECTURE_REVIEW.md`.

## Rules
- R1 **Version**: `DriverDto` and `VehicleDto` carry `DetailsVersion`, a fingerprint of the fields of their `PUT` as they are stored. Same data → same version, also after being changed and put back. The agenda, the declared weekly rest and deactivating do not change it. E.g. A opens a vehicle, B adds holidays or a workshop to its agenda, A saves → 200.
- R2 **`PUT /api/{drivers,vehicles}/{id}`** requires it: missing or not a fingerprint → 400. If it is not the one of the stored data → **409** «Otra persona ha cambiado estos datos» (“Somebody else has changed this data”), without `field` and with nothing saved. A DNI or plate that is taken stays a 409 **with** `field`. A body that cannot be read aside, the order is 404, conflict, the rest: whoever sees old data is told that first.
- R3 It is compared **inside the lock** of 0065. E.g. two `PUT` at once with the same version and different data → exactly one 200, the other 409. Saving what is already there → 200 and the version does not change.
- R4 **Web**: the version sent is the one of the data seen when the first field was touched, not of the last reload: a reload in the background must not hide the conflict. If the data changed meanwhile, before or after trying to save, what was typed stays, saving is blocked and «Cargar los datos actuales» (“Load the current data”) is offered. It keeps what was typed in the fields the other person did not touch; the ones that both changed show the current value, so nothing is overwritten unseen. Whoever typed nothing just sees the new data.

## Acceptance criteria
- [x] CA-unit: every field of each `PUT` changes the version; the agenda does not; the fields cannot be mistaken for each other (C#). Version kept with the draft, conflict and merge of R4 (Vitest).
- [x] CA-integration (real SQL Server): A and B open the same card, A saves and B gets 409; unchanged save; agenda changed in between; two `PUT` at once; 400 of R2; 409 without `field`.
- [x] CA-HTTP: real API with a database of its own, with curl.
- [x] CA-E2E: the conflict in the card, in CI. CI, E2E and Security green.
- [ ] CA-visual: manual QA by the user.

## Out of scope
Bookings (the web does not edit them), agenda entries, bases and clients; `rowversion`; showing who changed what; merging one same field.

## Reconciliation with the current domain
Exist: `DriverService.UpdateAsync` / `VehicleService.UpdateAsync` through `ChangeAsync`, `UpdateDriverRequest`, `UpdateVehicleRequest`, `ValueAlreadyUsedException`, `DomainExceptionHandler.MapProblem`, `DetailsSection`, `resourceDetails.ts` (`withEdit`, `inputOf`, `detailsDrafts`), `useSave`. New: `DetailsVersion`, `DetailsChangedException`. **No migration.**

## Likely files
`src/Application/DTOs/MasterData/`, `MasterDataServices.cs`, `DomainExceptionHandler.cs`; `web/src/{DetailsSection,save}.tsx`, `resourceDetails.ts`, `types.ts`; `ResourceDetailsHttpTests.cs`, `ConcurrentAllocationTests.cs`.

## Closing notes
Closed 2026-11-25. Size S, as estimated; no migration. CI (14 E2E) and Security green in GitHub (run 45561104072). Run locally: 1242 .NET tests (679 + 563, of them 495 of integration against SQL Server) and 513 of the web. Taking the comparison out makes six of the new integration tests fail.
- Checked with curl against a real API with a database of its own, for driver and vehicle: A saves and B gets the 409 with nothing written, unchanged save, agenda changed in between, two `PUT` at once (200 and 409), no version and wrong version (400), repeated DNI (409 with `field`).
- The fingerprint is taken over the request itself: its fields are required, so a new one does not compile until it is also in the version.
- Decided here: with something typed, the form no longer follows the data that are reloaded (0079 did): it shows what was seen and typed until the current data are loaded, and those keep what was typed only in the fields the other person did not touch.
- The test of 0079 that expected the last one to win now expects only the first to be saved: the rule changed with this SPEC.
- Left, in `ARCHITECTURE_REVIEW.md`: a save whose answer is lost and is tried again says «otra persona» (“somebody else”) about one's own change.
