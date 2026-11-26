# Retrospectives — Fleet DispatchGuard

Cumulative log of the **Retrospective Agent** (see `CLAUDE.md` section 4.4.2). Each entry summarises the process of a completed feature, not the code itself — that is already covered by `docs/SKILLS.md` and the SPEC itself in `specs/completed/`.

**Purpose of this file**: detect patterns *between* features (not only within one), so that friction does not repeat in the next SPEC.

**Format per entry**: use the template in section 5.1 of `CLAUDE.md`.

---

## Index

| SPEC | Feature | Estimated | Actual | Deviation | Main friction |
|------|---------|----------|------|------------|---------------------|
| 0017 | Trip Service Type Classification | S (~3h) | S in code; ~12min agent wall-clock + ~15-20min orchestrator | None in code; minor in process | NFR1 claimed "zero call-sites" of `new Trip(...)` via grep, reproduced the same way by Paso 1 — false negative: 3 tests used helpers with target-typed `new(...)` (invisible to the grep), breaking compilation until they were fixed at the build gate |
| 0016 | Return-to-Base Tracking (Amend. 2020/1054) | S (~4h, assumed SPEC 0014 completed) | Not directly comparable (agent wall-clock, not person-hours); Dev/Tests ~100min in parallel, more than previous S SPECs | Minor in code, significant in process | Central premise of RF1/RF2 was false — `Driver` already had an operating base via `EntityWithHomeLocation` (inheritance not checked by Paso 1); reconciled with 2 user decisions halfway through orchestration. Docs left 2 stale references unfixed in the same file it touched (`DEPENDENCY_MATRIX.md`), detected by the orchestrator's independent verification |
| 0015 | Weekly Rest Violation Enforcement (CE561 Art.8) | S (~4-5h) | S, no deviation | None | 3 pre-existing tests (`LegalComplianceEvaluationSanityTests.cs`) broke because they implicitly assumed "no history = no violations" — reconciled by the orchestrator, verified as honest (they do not hide a bug) by the Review |
| 0013 | Driver/Vehicle Schedule Conflict Validation | S (~4h) | S, no deviation | None | No blocking friction — 2 cosmetic findings from the Review (reflection hack in tests for Entity.Id, minor DRY), neither blocked the merge; spawned as a separate background task |
| 0014 | Trip Repository Query Methods | S (~4h) | S, no deviation | None | No blocking friction — only finding: `Infrastructure.Tests.csproj` needed `ProjectReference`+`EntityFrameworkCore.InMemory`, not anticipated in the SPEC's "Archivos Afectados" |
| 0010 | Robust .gitignore Configuration | S (~90 min) | S + 1 extra round after the user's verification | Minor | (1) SPEC assumed `appsettings.Development.json` was not tracked in git; it was, in 3 locations (no real secrets) — solved with `git rm --cached` after the user's confirmation. (2) Review-by-ACs did not cover `.claude/worktrees/` (495KB, not considered by the SPEC) — it was only found because the user asked for an explicit final verification, not by the orchestration process itself |
| 0012 | Project Information Retrieval System (Knowledge Architecture) | M (8-12h manual) | ~100-120 min agent wall-clock (100% orchestrated) | Not comparable (different units) | No critical friction — Paso 1 (4.3) worked as designed; the Architect found 9 additional fictitious names that Paso 1 did not cover; large time disparity between the 2 parallel Devs; tension NFR2 (bytes) vs NFR3 (precision) resolved in favour of precision |
| 0005 | Enhance DrivingLimits with Continuous Driving Rule | S | S (code), significant overhead in deciding | Minor in code, significant in process | Backlog order did not match the real recommendation (SPEC 0003 research); "Files to Delete" assumed orphan code that was actually integrated in DI/DriverAllocationService |
| 0001 | Vehicle Eligibility Validator | S/M | S/M (Tests ~29min, the rest quick) | Minor | SPEC cited non-existent domain fields (Vehicle.Status/ServiceTypes/ITV) — required manual reconciliation before orchestrating |

---

## Entries

*(One entry is added per completed SPEC, most recent first)*

### SPEC 0017 — Trip Service Type Classification

See the full retrospective in `specs/completed/0017_FEATURE_TripServiceTypeClassification.md` (section `## Retrospectiva — SPEC 0017`).

**Summary**:
- **Fifth SPEC of the domain-completion round**, closes the service-type debt explicitly left pending since SPEC 0001. Paso 0 (Sync-Check) detected that §9 of this `CLAUDE.md` still cited `claude-sonnet-4-6`/`claude-haiku-4-5` even though the header claimed to be in sync with GLOBAL_STRATEGY v1.1 (which specifies Sonnet 5/Haiku 4.5) — reconciled in this same orchestration. The Sync-Check also found that the global `VERSION.json` (`E:\Code\.claude\VERSION.json`) declares `strategy v1.0` + `architect: fable-5`, violating the hard rule v1.1 (Architect = Opus, Fable forbidden) — flagged to the user, not fixed because it is a global file shared between projects.
- **Main friction — grep false negative in NFR1**: both the SPEC and the orchestrator's Paso 1 verified "zero call-sites of `new Trip(...)`" with `grep "new Trip("`, concluding that changing the constructor signature was safe. It was false: 3 test files (`ReturnToBaseServiceTests.cs`, `ScheduleConflictServiceTests.cs`, `TripRepositoryTests.cs`) built `Trip` through helpers with **target-typed `new(...)`** (`private static Trip NewTrip(...) => new(...)`), invisible to that grep pattern. They broke compilation (violating CA5) until the orchestrator fixed them at the build gate — no agent (Architect, Dev, Tests, Review) detected it before; `dotnet build` detected it.
- **Architect (Opus) without friction**: design in ~66s, constructor contract fixed to avoid divergence between Dev/Tests in parallel, and it correctly pointed out the need for an EF migration that the SPEC had omitted.
- **Docs (Haiku) did not repeat the mistake of SPEC 0001**: the orchestrator handed it the authoritative test figures beforehand, and Docs did not invent counts — mitigation already applied in previous rounds, confirmed effective again.
- **Verification in a real browser** (permanent rule since previous retrospectives): confirmed with no console errors, correct stats, 0017 card present, and the 9 Mermaid diagrams force-rendered with no syntax errors.
- **Residual inconsistency, out of the scope of this SPEC**: `docs/index.html` (~line 1419) still labels agents as "Sonnet 4.6" in a diagram, now out of date after the reconciliation of §9 in Paso 0 — flagged for the user, not fixed here.
- **Proposal applied directly**: new "Paso 1e" in CLAUDE.md §4.3 — for SPECs that change a constructor's signature, `grep "new Type("` is not enough to claim "zero call-sites" (blind to target-typed `new(...)`, object initializers, helpers/factories); the real source of truth is applying the change and running `dotnet build`, not predicting by grep before touching code.

### SPEC 0016 — Return-to-Base Tracking (Amend. 2020/1054)

See the full retrospective in `specs/completed/0016_FEATURE_ReturnToBaseTracking_2020_1054.md` (section `## Retrospectiva — SPEC 0016`).

**Summary**:
- **Fourth SPEC of the domain-completion round**, first completely new rule of the domain (unlike 0013/0014/0015, which extended existing partial code). Paso 1 (§4.3) was correctly applied on `Driver.cs` directly and found no location property — but that verification was incomplete: it did not follow the inheritance chain (`Driver : EntityWithUnavailabilities<DriverUnavailability> : EntityWithHomeLocation`), and `EntityWithHomeLocation` already had `HomeLocationId`/`HomeLocation`/`SetHomeLocation(Location)` mapped in the DB with migration and index applied.
- **The Architect Agent (Opus) detected the gap already in the design phase**, not Paso 1 nor the prompt-score (9/10, which validates textual clarity, not the real inheritance chain). This forced pausing the orchestration between Architect and Dev/Tests for 2 user decisions: reuse `Driver.HomeLocation` instead of creating new fields, and treat a non-existent `driverId` as `NotVerifiable` without an exception. Both were documented in a section `## Reconciliación con el dominio actual` in the SPEC itself before continuing — same pattern already used in SPEC 0001/0005.
- **With the reconciled contract, Tests and Dev ran in parallel without discrepancies** between them (16 tests, 140/140 in the full suite). Review re-verified on its own (without trusting the Architect's claim) that the inheritance chain was real — same pattern of targeted scepticism identified in the retrospective of SPEC 0015, now applied to a claim of the Architect instead of to a reconciliation by the orchestrator. Verdict: approved, with 2 non-blocking nitpicks (the CA6 test does not really distinguish `.Last()` from `.Max()`; an overload of `ReturnToBaseCheck.NonCompliant` is dead code from the end-to-end integration).
- **Docs left 2 stale references unfixed inside the same file it touched** (`docs/DEPENDENCY_MATRIX.md`: "0016 (aún backlog)" and "0016 (return-to-base, pendiente)", contradicting the "completed" entry that Docs itself wrote two lines below). Detected only because the orchestrator verified Docs' work independently (same pattern already used after SPEC 0015), not by the process itself.
- **Estimate not directly comparable**: the SPEC estimated S (~4h, person-hours). Real agent wall-clock: Architect ~3.6min, Dev ~103min and Tests ~104min in parallel, Review ~30.6min, Docs ~6.9min — noticeably slower than other S SPECs of this same round (0013/0014/0015, minutes), explained by the reconciliation pause halfway through design, not by code complexity.
- **Repeated test pattern**: the fake repository that ignores `from`/`to` and lets the service decide the window edge in memory already appeared in SPEC 0013 and now in 0016 — candidate for a shared test helper if it appears a third time.
- **Proposals applied directly to CLAUDE.md**: (1) extension of Paso 1 (§4.3) requiring the full inheritance chain of a cited entity to be checked, not only the file with its name; (2) note in the Docs Agent paragraph (§4.4) asking for a self-grep of the file it touched to detect internal status contradictions before finishing the pass.

### SPEC 0015 — Weekly Rest Violation Enforcement (CE 561/2006 Art. 8)

See the full retrospective in `specs/completed/0015_FEATURE_WeeklyRestViolationEnforcement.md` (section `## Retrospectiva — SPEC 0015`).

**Summary**:
- **Third SPEC of the domain-completion round**, independent of 0013/0014/0016/0017. It closed the "Partial" gap of Art. 8 (weekly rest) inherited from the old SPEC 0003: the Architect found that the gap was deeper than documented — `BuildDrivingLimits` hardcoded `lastWeeklyRestDate: null` (never derived from the real history) and `NeedsWeeklyRest()` used `DateTime.UtcNow` instead of `tripStart` (breaking the determinism of the rules engine).
- **Real regression detected after Dev+Tests**: 3 pre-existing tests in `LegalComplianceEvaluationSanityTests.cs` started failing because their fixtures implicitly assumed "no driving history = no violations" — an assumption valid only because the weekly rest rule was never evaluated before this SPEC. The orchestrator reconciled the 3 tests directly (2 gained an old isolating `DrivingPeriod`, 1 was renamed to reflect the new correct expectation according to CA4).
- **The Review specifically scrutinised the orchestrator's own reconciliation** (not only the code of Dev/Tests) to verify that it did not hide a real bug — it confirmed it was honest. This level of scepticism aimed at changes made outside the Dev/Tests diff is a pattern to repeat.
- **No process friction**: the Architect's design followed literally, Dev and Tests worked on disjoint files without conflict (Tests created a new file instead of touching the same file Dev was partially modifying).
- **RF4 (reduced-rest compensation)** honestly documented as out of scope (a "rest ledger" entity is missing in the domain) instead of being implemented with false positives — decision of the Architect, confirmed consistent by Review in code and in `REGULATORY_MATRIX.md`.

### SPEC 0013 — Driver/Vehicle Schedule Conflict Validation

See the full retrospective in `specs/completed/0013_FEATURE_DriverVehicleScheduleConflictValidation.md` (section `## Retrospectiva — SPEC 0013`).

**Summary**:
- **Second SPEC of the domain-completion round**, orchestrated after 0014 (reversing the order the user initially asked for, after pointing out the real dependency) to reuse `GetByDriverAsync`/`GetByVehicleAsync` instead of duplicating a query.
- **The Architect correctly discarded RF3 of the SPEC** (create a new query method) because SPEC 0014 already covered it — confirms that the Architect's design must prevail over the literal text when the context changed between writing the SPEC and orchestrating it.
- **Clean orchestration**: Architect → Dev+Tests in parallel (no file conflict) → Review with real build/test. PASS on the first pass, with no correction iterations.
- **2 cosmetic findings, non-blocking**: (1) the tests use reflection to set `Entity.Id` (protected setter) in a fake repository — fragile, candidate for a cross-cutting test-construction mechanism; spawned as an independent background task instead of solving it within this SPEC. (2) slight DRY duplication between `CheckDriverConflict`/`CheckVehicleConflict` — cosmetic, not fixed.
- **Reusable pattern**: "conflict check against a paged repository, filtering by status and overlap in memory via `TimeWindow.OverlapsWith`" — documented in `docs/SKILLS.md` as `schedule-conflict-check`, reusable for future resource availability validations.

### SPEC 0014 — Trip Repository Query Methods

See the full retrospective in `specs/completed/0014_FEATURE_TripRepositoryQueryMethods.md` (section `## Retrospectiva — SPEC 0014`).

**Summary**:
- **First SPEC of this domain-completion round** (0013-0017), written after the user deleted the previous backlog (0002/0003/0006/0007/0008/0009) and explicitly asked to complete domain/DDD before infrastructure.
- **Scope decision confirmed with the user before orchestrating**: the 4 new methods of `ITripRepository` were fully implemented in Core (`PagedResult<T>` + interface), but `Infrastructure/Repositories/TripRepository.cs` stayed as a `NotImplementedException` stub — consistent with ALL Infrastructure repositories already being stubs before this SPEC (verified, it was not a new gap). The integration tests were written in full but `[Fact(Skip = "...")]`, serving as an executable specification for when real EF Core is implemented.
- **Real orchestration with subagents** (not only direct work by the orchestrator, unlike SPEC 0010): Architect (design of signatures + PagedResult) → Dev and Tests in parallel (no file conflict) → adversarial Review that actually ran `dotnet build`/`dotnet test` instead of trusting the reports of the other agents. No correction iterations — Review gave PASS on the first pass.
- **Only unanticipated finding**: `Tests/Infrastructure.Tests/Infrastructure.Tests.csproj` had no `ProjectReference` to Core/Infrastructure nor the `EntityFrameworkCore.InMemory` package — needed for the tests (even if skipped) to compile against real types. The SPEC's "Archivos Afectados" section did not anticipate it.
- **Dependency order respected**: the user originally asked to orchestrate 0013 first, but the real dependency on 0014 was pointed out and the user chose to reverse the order — it avoided 0013 implementing an ad-hoc query that 0014 would have duplicated/replaced later.
- **Proposal for future SPECs**: when a SPEC adds EF Core integration tests to a test project that did not have them before, anticipate in "Archivos Afectados" that the `.csproj` may need new `PackageReference`/`ProjectReference` (not applied as a CLAUDE.md change because it is low impact — it stays as a note for the SPEC author).

### SPEC 0010 — Robust .gitignore Configuration

See the full retrospective in `specs/completed/0010_SPEC_RobustGitignoreConfiguration.md` (section `## Retrospectiva — SPEC 0010`).

**Summary**:
- **Reconciliation gap detected and solved**: the SPEC assumed `appsettings.Development.json` was not tracked; in fact it was, in 3 locations (`src/Web.Api/` and two worker stubs that are not published), without real secrets. A new `.gitignore` pattern does not affect already tracked files, so `git rm --cached` was needed — a shared-state decision, presented to the user for confirmation before running it (they chose to untrack).
- **First orchestrated config-only SPEC**: no C# entities/services, so the standard orchestration of 5 agents did not fit 1:1. It was adapted by merging Architect+Dev (direct implementation) and using a single independent Review agent to adversarially validate the 7 Acceptance Criteria via `git check-ignore`/`git status` — Review confirmed PASS on the 6 testable ACs (AC7 was correctly deferred to the docs update, done later).
- **No implementation friction**: the SPEC's patterns (sections 6/13/17) were directly usable; the only finding was the tracking gap, caught before touching code thanks to the existing Paso 1b.
- **Second gap found after closing, not by the process itself**: when the user asked for a final verification ("does it ignore everything it should?"), it was found that `.claude/` (local Claude Code directory, including `.claude/worktrees/` — 495KB of ephemeral repo copies per agent session) was not covered by any category of the SPEC nor detected by the adversarial Review, because the Review only tested the ACs the SPEC had defined beforehand. Fixed by adding a new section to `.gitignore` (`.claude/worktrees/`, `.claude/settings.local.json`, keeping `.claude/launch.json`/`settings.json` trackable). Verified with a real build (`dotnet build`) confirming that nested `bin/`/`obj/` are also correctly ignored.
- **Proposals applied directly**: (1) new Paso 1d in CLAUDE.md §4.3 — verify `git ls-files` against the proposed exclusion patterns before implementing a `.gitignore` SPEC; (2) note in the Agents table (§9) about merging Architect+Dev and using a single Review for config-only SPECs, analogous to the Paso 1c precedent for documentation SPECs; (3) extension of Paso 1d requiring the Review of a `.gitignore` SPEC to explain each `??` entry of `git status --short` on the real repo, not only the examples the SPEC anticipated.

### SPEC 0012 — Project Information Retrieval System (Knowledge Architecture)

See the full retrospective in `specs/completed/0012_FEATURE_ProjectInformationRetrieval_KnowledgeSystem.md` (section `## Retrospectiva — SPEC 0012`).

**Summary**:
- **No critical friction** — first SPEC where "Paso 1 — Chequeo de existencia de dominio" (added in the retrospective of 0001) was applied at full scale (8 deliverable files) and worked: it caught 4 families of fictitious components cited by the SPEC itself (`ComplianceValidationService`, `TripsController`/etc., `OptimizationEngine`/`TripManagementService`, `LegalValidation.IsCompliantFor(...)`) and fabricated example figures, BEFORE the prompt-score, preventing them from leaking into the real knowledge system.
- **The Architect found 9 additional fictitious/aspirational names** that Paso 1 did not cover (because they were specific to backlog SPECs never implemented and to the aspirational structure of CLAUDE.md §3 itself): `Service`/`ServiceType`, `Route`, `ContinuousDrivingValidator`/`ContinuousDrivingPeriod`, `ReturnToBaseValidator`, `ComplianceReportingService`, `ComplianceStatus`/`DriverStatus`, `DrivingRecord` (loose entity), folders `Application/DTOs`/`Application/Mappings`.
- **Large time disparity between the 2 parallel Devs** (Dev-A ~4.7min/7 tool calls vs. Dev-B ~82min/27 tool calls) for the same number of files — inferred that DEPENDENCY_MATRIX.md (RF4, SPEC-to-SPEC graph) required re-reading more SPECs transitively to verify each edge.
- **Tension NFR2 (byte budget) vs NFR3 (precision)**: 2 of the 8 files exceeded their byte budget (+75% and +25%) because the real, precise data weighs more than the SPEC's own illustrative placeholders. Review correctly prioritised precision over the byte ceiling, without truncating real facts.
- **Incidental finding out of scope**: `docs/SKILLS.md` line 10 (row of SPEC 0005) cites a non-existent test file (`DrivingLimitsTests.cs`) — the real one is `LegalValidationTests.cs`. Pre-existing debt of SPEC 0005, not of this SPEC; flagged to be spawned as an independent fix, not fixed here.
- **Estimate**: "M (8-12h)" manual vs. ~100-120 min of agent wall-clock, 100% orchestrated — units not directly comparable; a note was added to the Estimated Effort template (CLAUDE.md §5) asking also for an expected agent wall-clock range for SPECs meant for full orchestration.
- **Proposals applied directly**: (1) note in CLAUDE.md §4.3 about merging Paso 1 and the Architect's design into a single deep pass for pure documentation SPECs; (2) note about the tie-break rule "precision wins over byte budget"; (3) optional agent wall-clock field in the Estimated Effort template.
- **Flagged for the user's confirmation, not applied**: possible nuance in the model table of §9 (Docs = Haiku) for large structural edits in `docs/index.html` — it touches the model allocation policy.

### SPEC 0005 — Enhance DrivingLimits with Continuous Driving Rule

See the full retrospective in `specs/completed/0005_FEATURE_EnhanceDrivingLimitsContinuous.md` (section `## Retrospectiva — SPEC 0005`).

**Summary**:
- **Main friction**: the ascending order of the backlog (0002 next) was not the real order of work — 0002 depended on a non-existent `Route` entity and duplicated the already existing `Trip` entity with a new `Service` aggregate; SPEC 0003 (research) already explicitly recommended jumping to 0005. It was confirmed with the user before orchestrating.
- **The SPEC's "Files to Delete" section assumed orphan code that was not orphan**: `LegalValidationService` and its associated types were already registered in DI and consumed by `DriverAllocationService` (working feature). The scope was reconciled in the SPEC itself (from "delete and rebuild" to "extend and delegate") before moving it to `active/`.
- **Real regression detected by the Tests agent** during implementation (an existing `InlineData` expected a trip of 9h of continuous driving to be compliant, which the SPEC itself makes incorrect) — fixed by the orchestrator.
- **2 pre-existing bugs** in `LegalComplianceEvaluationSanityTests.cs` (arrange data unrelated to this SPEC) only appeared when running the full `dotnet test` — nobody had run it since that file was created (untracked).
- **Proposal applied**: the DI/call-sites check before qualifying a file as deletable was added to section 4.3 of `CLAUDE.md`, and a note about preferring the recommendation of a previous research SPEC over the ascending numeric order (confirming it with the user).

### SPEC 0001 — Vehicle Eligibility Validator

See the full retrospective in `specs/completed/0001_FEATURE_VehicleEligibilityValidator.md` (section `## Retrospectiva — SPEC 0001`).

**Summary**:
- **Main friction**: the backlog SPEC assumed `Vehicle` fields (Status, ServiceTypes[], ITVExpiryDate) and a `ServiceType` enum that do not exist in the real domain; it was detected only when reading the code before orchestrating, not by the prompt-score (8.8/10, which does not validate the existence of cited symbols).
- **Test bug detected and fixed by the orchestrator** (not by Dev nor Review): `VehicleUnavailability` built with `vehicleId: vehicle.Id` (0 in memory) caused 14/25 failures from an *arrange* exception, not from logic.
- **Docs left out-of-date figures** in `docs/index.html` (44/46 and 69/69 instead of 69/71) — detected only when verifying in a real browser, not by reading the HTML.
- **Estimation accuracy**: small overall deviation, but the Tests agent took ~29min against Dev's ~6min — sign of an over-specified Tests prompt.
- **Proposal applied**: "Paso 1 — Chequeo de existencia de dominio" was added to section 4.3 of `CLAUDE.md`. In addition, the user formalised that the Retrospective Agent is now **mandatory in every `/orchestrate-feature`** and has authority to edit CLAUDE.md/templates directly when it finds a process improvement, instead of only proposing it (see sections 2, 4.4, 4.4.2 and 9 of `CLAUDE.md`, updated on 2026-07-01).
