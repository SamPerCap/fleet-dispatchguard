# LESSONS — History of process lessons (Fleet DispatchGuard)

**Purpose**: keeps the *evidence, narrative and concrete cases* of the lessons learned in each `/orchestrate-feature`, taken out of `CLAUDE.md` to keep it light. `CLAUDE.md` keeps the **living rules** (what to do, concise); here is the **why** (the SPEC, the case, the concrete symbol) that backs each rule.

**Who reads this**: whoever writes a new SPEC or does the periodic pattern review (every 5–10 features). The living rules are in `CLAUDE.md`.

**Who writes here**: the session that closes a feature, only if there was a new lesson (evidence here; concise living rule in `CLAUDE.md`). §1–§10 are from the period of orchestration by agents (SPECs 0001–0017); §11 explains the change of workflow. The references "CLAUDE.md §4.x / §9" in those sections point to the previous version of CLAUDE.md; the current rules are summarised in CLAUDE.md §2 ("Reglas aprendidas").

---

## 1. Validating a SPEC: domain existence check (and inheritance chain)

**Living rule (CLAUDE.md §4.3 Paso 1)**: before the prompt-score, verify by grep that the classes/enums/properties cited in "Domain Entities"/"Value Objects"/"Business Rules" really exist in `src/Core`, **including the full inheritance chain** (`: BaseClass`), not only the file itself. If something does not exist, reconcile the scope in the SPEC itself (`## Reconciliación con el dominio actual`) before orchestrating.

**Evidence**:
- **SPEC 0001**: the prompt-score validates *textual* clarity/specificity, not real existence — a well-written SPEC about a non-existent field can score ≥8/10 and still need a full reconciliation. 0001 assumed `Vehicle.Status`/`ServiceTypes[]`/`ITVExpiryDate`, which did not exist; the gap had to be documented in the SPEC, not discovered halfway through orchestration.
- **SPEC 0016**: `Driver : EntityWithUnavailabilities<...> : EntityWithHomeLocation` — the grep of `Driver.cs` was NOT enough. The SPEC assumed that `Driver` had no location because `Driver.cs` did not show it; the property already existed in `EntityWithHomeLocation` (two levels up), already mapped in the DB with the migration applied. The gap was detected only in the Architect's design phase, forcing a pause to reconcile with the user → hence the rule of checking the full inheritance.

---

## 2. "Deletable" code and the real order of the backlog

**Living rule (CLAUDE.md §4.3 Paso 1b)**: if the SPEC marks existing files for deletion, verify with `grep` that they are not in `DependencyInjection.cs` nor consumed by another already integrated service before assuming they are orphans. If a research SPEC (`SPEC`, not `FEATURE`) recommends an order different from the numeric one, prioritise that recommendation but **confirm with the user** before departing from the order.

**Evidence**: **SPEC 0005** — `LegalValidationService` existed, compiled and was in DI, but the SPEC still marked it for deletion. SPEC 0003 (research) recommended jumping to 0005 instead of the ascending numeric order.

---

## 3. Pure documentation SPECs (100% text deliverable)

**Living rule (CLAUDE.md §4.3 Paso 1c)**: if the SPEC does not change code, combine Paso 1 (grep of symbols) and the Architect's first sweep in a single deep reading pass (backlog/active/completed SPECs + real tree of `src/`) before the prompt-score, instead of two agents re-reading the same thing separately. It does not replace the normal Paso 1 (quick grep) for code SPECs.

**Evidence**: **SPEC 0012** — the shallow Paso 1 (grep against `src/Core`) was not enough: the Architect found 9 additional fictitious/aspirational names (from backlog SPECs never implemented and from the aspirational structure of §3 of CLAUDE.md itself) that Paso 1 did not cover → redundancy of two agents re-reading the same tree.

---

## 4. `.gitignore` / exclusion SPECs: verify real tracking

**Living rule (CLAUDE.md §4.3 Paso 1d)**: if the SPEC proposes exclusion patterns and assumes that certain files "are not committed", run `git ls-files | grep <patrón>` before implementing. A new `.gitignore` pattern does NOT affect already tracked files; if they actually are tracked, `git rm --cached` is needed (it changes the git index → shared state) and **requires confirmation from the user**. Also, in the Review run `git status --short` on the real repo and explain each `??` (untracked), not only the examples the SPEC anticipated.

**Evidence**: **SPEC 0010** — it assumed 3 `appsettings.Development.json` were untracked when they were tracked. And the Review against the original ACs would not have found `.claude/worktrees/` (495 KB of ephemeral repo copies per agent session) — it was detected only because the user asked for a final check of `git status --short` instead of trusting the categories the SPEC had thought of beforehand.

---

## 5. Constructor signature changes: `grep "new Type("` does not prove "zero call-sites"

**Living rule (CLAUDE.md §4.3 Paso 1e)**: for a constructor signature change, `grep "new NombreClase("` is only a risk estimate, NOT proof that "it breaks nothing" — it is blind to target-typed `new(...)` of modern C#, indirect object initializers and test factories/helpers. The source of truth is applying the change and running `dotnet build`, which lists every broken call-site. Do not use the grep to discard the build gate.

**Evidence**: **SPEC 0017** — SPEC and orchestrator reproduced "zero call-sites" with that grep, but 3 test files (`ReturnToBaseServiceTests.cs`, `ScheduleConflictServiceTests.cs`, `TripRepositoryTests.cs`) built `Trip` through helpers with target-typed `new(origin: ..., ...)` (the token `Trip` does not appear at the construction site) and broke compilation after the change.

---

## 6. Knowledge System before re-reading everything (Paso 0)

**Living rule (CLAUDE.md §4.3 Paso 0)**: for questions about status/dependencies/counts/conventions, first check `docs/KNOWLEDGE_MAP.md` (question→file map) and, depending on the question, `PROJECT_METADATA.json`, `DEPENDENCY_MATRIX.md`, `REGULATORY_MATRIX.md`, `ARCHITECTURE_DIGEST.md` or `QUICK_REFERENCE.md`, before re-reading the whole CLAUDE.md or grepping widely. It does NOT replace Paso 1 (direct grep against `src/` to verify symbols cited in a new SPEC) because the Knowledge System can go stale if a feature did not update it.

**Evidence**: **SPEC 0012** created this system (see `specs/completed/0012_FEATURE_ProjectInformationRetrieval_KnowledgeSystem.md`).

---

## 7. Docs: self-check for internal contradiction

**Living rule (CLAUDE.md §4.4, Docs)**: before finishing, Docs greps the file(s) it has just edited (esp. `DEPENDENCY_MATRIX.md`, `KNOWLEDGE_MAP.md`, `index.html`) looking for OTHER mentions of the just completed SPEC number with a different status (e.g. "aún backlog") and fixes them in the same pass.

**Evidence**: **SPEC 0016** — `DEPENDENCY_MATRIX.md` had 2 stale references to "0016 (backlog/pendiente)" a few lines from the "completed" entry that Docs itself had written; it was detected by the orchestrator through independent verification, not by Docs.

---

## 8. Config-only SPECs: reduce the set of agents

**Living rule (CLAUDE.md §9, note)**: if the SPEC does not touch C# entities/services (`.gitignore`, build/CI config, infra without domain logic), there is no SOLID to design nor xUnit tests to write: merge Architect+Dev into a direct implementation, replace Tests with the manual verification the SPEC defines (`git check-ignore`, `git status`), and use Review as the only adversarial validator of the ACs. Docs and Retrospective unchanged. Analogous to Paso 1c for documentation SPECs.

**Evidence**: **SPEC 0010** (config-only, `.gitignore`).

---

## 9. SPEC template: precision > budget; estimation units

**Living rules (in the template §5)**:
- If a size/budget NFR (e.g. "< 2 kb/archivo") clashes with precision/coherence (e.g. "each exact number", "each field with its origin"), **precision wins** — do not truncate real facts because of a byte ceiling; document the trade-off in the deliverable itself.
- If the SPEC is run 100% through `/orchestrate-feature`, add a range of agent wall-clock time besides the S/M in person-hours — they are different units, not comparable.

**Evidence**: **SPEC 0012** — 2 of 8 files exceeded their byte budget because the real data weighs more than the SPEC's placeholders; and it estimated M/8-12h of manual work for a job that took ~100-120 min of agents (figures that are not comparable).

---

## 10. Web documentation (`docs/index.html`) with Mermaid — render gotchas (v11)

**Living rule (CLAUDE.md §4.4.1 / Docs)**: verify `index.html` in a real browser after editing it (not headless). When touching Mermaid, watch out for the same bugs detected in the sister project Coche (2026-07-06): the unpinned CDN resolves to Mermaid **v11**, which breaks the inherited pattern `<div class="mermaid"><script type="text/mermaid">…` (auto-run by the `.mermaid` class → "Syntax error" in all diagrams). Fixes: rename the container to `class="mermaid-diagram"` + explicit render with `mermaid.render(id, code)` on `DOMContentLoaded`; **never** write a literal script closing tag inside the inline `<script>` (not even in comments) because the HTML parser cuts the block there; and in v11 quote edge labels with special characters (`A -->|"{ x }"| B`). (Pending: verify/apply in DispatchGuard's `index.html`.)

---

## 11. Change of workflow: from orchestration by roles to a single session + real verification (2026-11-13)

**User decision**: drop `/orchestrate-feature` with 7 agents by role (Architect/Tests/Dev/Review/Docs/WebBuilder/Retrospective) as the default workflow.

**Evidence**:
- Coche 0011: the "meta" (Review+Docs+Retrospective) consumed ~58% of the tokens against ~42% of real engineering (GLOBAL_STRATEGY v1.2).
- Fleet DispatchGuard 0001–0017: 17 orchestrated SPECs, all with Review and Retrospective, and none detected that 7 of 8 repositories throw `NotImplementedException` nor that `POST /api/trips/{id}/allocate` returns `tripId = result.Driver.Id`. The tests passed with fakes. The failure was one of **verification**, not of the split of roles (`.claude/ARCHITECTURE_REVIEW.md` P0/P1, onboarding 2026-11-13).
- Each role agent starts without context and rediscovers what the previous one knew; the design degrades at each hand-over.

**Resulting living rule** (in `CLAUDE.md` §2): one session per feature (plan mode → test-first on regulatory rules → implement → build/test → integration if it touches DB/HTTP → independent `/code-review` → map → commit). Subagents only to isolate context, for independent review or to parallelise independent SPECs in worktrees. Retrospective: closing notes in the SPEC; pattern review every 5–10 features.

**What was discarded and why**: Docs/web in each feature (high fixed cost, low value between milestones); mandatory deep Retrospective (silence vs "no friction" is already solved by the closing notes); separate Architect Agent (plan mode plays the same role with the user reviewing).

---

## 12. An oracle and an example can give green without testing the rule (2026-11-23)

**Living rule**: in a limits rule, besides the green test, break each part of the rule on purpose (mutation) and check that some test fails; and the oracle shares with the engine neither code nor platform data, and compares the cause, not only the verdict.

**Evidence**: **SPEC 0064** — the example of the “service that crosses Monday” passed without the split by clock: in Madrid time the stretch fell entirely in the new week. Evaluating only the first week a trip touches did not break any test, not even the oracle (in legal schedules the verdict is the same; the figure changes). And the oracle used the same `TimeZoneInfo` as the engine.

---

## 13. Reading, deciding and saving in loose steps fails as soon as there are two at once (2026-11-23)

**Living rule**: whoever writes services, sick leave or workshop periods does it inside `IUnitOfWork.ExecuteExclusiveAsync` and reads there everything it decides on; it never receives entities already read. A concurrency test pauses one request at a known point (just before saving) and waits for the other to finish or for the server to say it is waiting (`sys.dm_tran_locks`): no sleeps and no luck. It is checked that it fails without the fix and with each piece removed.

**Evidence**: **SPEC 0065** — with the usual tests green, 480 of 480 requests that stepped on each other between two instances were accepted, and a service cancelled while being allocated was left `Assigned` and inactive at the same time: EF only writes the columns each request changed and mixes the two. The first save of a reallocation stayed done even if the second one failed.

## 14. An in-memory copy must be tested at the edge of what the rules look at (2026-11-23)

**Living rule**: if something simulates on a copy what the database then decides, one test compares the copy with the real repositories and another the simulated verdict with the applied one, with the same words; and there is a targeted case for each end of the schedule that is read.

**Evidence**: **SPEC 0062** — with 7 random schedules and parity green, trimming the copy to its own trips (without history or without later services) did not break any test: the surrounding services almost always fell inside by chance. Mutation uncovered it; now it is covered by a 4 h service right before and another right after.
