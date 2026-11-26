# Mutation testing of the EC 561/2006 engine

Mutation testing changes the engine on purpose (a `>` into `>=`, a `+` into `-`, a line removed) and runs the tests against each
change. A change the tests notice is **killed**; one they do not notice **survives** and marks a place where a bug could slip
through. The score says how many changes the tests would have caught, which a line-coverage figure cannot say.

## Result (2026-11-26)

| | Mutants |
|---|---:|
| Killed by a failing test | 357 |
| Timeout (counted as killed by Stryker) | 5 |
| Survived | 71 |
| No test reaches them | 8 |
| **Mutation score** = (killed + timeout) / (killed + timeout + survived + no coverage) | **82.09 %** (362 / 441) |
| Score counting only mutants killed by a test | 80.95 % (357 / 441) |
| Not evaluated: the mutant does not compile | 87 |
| Not evaluated: ignored by Stryker (block already covered, or outside the scope) | 46 |

- **Tool:** Stryker.NET 5.1.0 (`dotnet-tools.json`), .NET SDK 10.0.301, Windows; configuration in
  [`Tests/Core.Tests/stryker-config.json`](../Tests/Core.Tests/stryker-config.json), with an additional timeout of 30 s per mutant.
- **Scope:** `LegalComplianceEvaluation.cs`, `LaterServicesEvaluation.cs`, `DrivingWeek.cs`, `DrivingPeriod.cs` and
  `DriverAvailability.cs` in `src/Core/ValueObjects` (the EC 561/2006 rules, the later services, the week of Art. 6 and the daily
  availability), against the unit tests of `Tests/Core.Tests`. The figures of `DrivingLimits.cs` are constants and are checked by
  [`RegulatoryMatrixTests`](../Tests/Core.Tests/Docs/RegulatoryMatrixTests.cs) instead.
- **Per file:** `DrivingWeek` 13 of 13 killed · `DriverAvailability` 16 of 17 · `DrivingPeriod` 11 of 13 · `LaterServicesEvaluation`
  54 of 66 (3 by timeout) · `LegalComplianceEvaluation` 268 of 332 (2 by timeout).

Run it again (Windows needs the SDK's MSBuild, not the one of Visual Studio):

```bash
cd Tests/Core.Tests
dotnet tool restore
dotnet tool run dotnet-stryker --msbuild-path "<dotnet sdk folder>/MSBuild.dll"
```

## How the figure was reached, and why the timeouts matter

Stryker counts a timeout as killed. With its default timeout the first run said **93.20 %**, with 116 of 423 mutants in timeout. They
were not endless loops: [`Article6Tests`](../Tests/Core.Tests/ValueObjects/Article6Tests.cs) checks 10,102 random calendars against an
independent oracle, and a mutant makes it slower than the default limit. With 30 s more per mutant, most of those timeouts finish:

| Run | Tests | Additional timeout | Killed | Timeout | Survived | No coverage | Score |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | before this work | default | 295 | 116 | 12 | 18 | 93.20 % |
| 2 | + first new tests | default | 326 | 95 | 12 | 8 | 95.46 % |
| 3 | same as run 2 | 30 s | 346 | 2 | 85 | 8 | 78.91 % |
| 4 | + second block of new tests | 30 s | 357 | 5 | 71 | 8 | **82.09 %** |

Of the 95 timeouts of run 2, 20 were real kills, 73 were survivors and 2 stayed in timeout. The published figure is the one with
the longer timeout.

## What the surviving mutants found

The new tests that killed them are marked "Mutation testing (SPEC 0099)" in the test classes:

- the remaining driving time (day, week, two weeks) that `Validate` returns was not checked by any test, nor that it never goes
  below zero, nor that it counts from 9 h once both extended days are used;
- a gap of exactly 24 h between two periods is a weekly rest period, one minute less is not;
- a period that ends exactly at the instant asked for counts as driven;
- the messages of a later service: the daily rest period is a minimum and fails below the reduced one, reduced rest periods are
  counted in rests, an unknown service is shown as `#?`;
- impacts come by departure whatever the order of the calendar, and only affected services keep their result;
- the guards name the missing argument; `DrivingPeriod` rejects an end that is not after its start, and its equality ignores the
  trip.

No surviving mutant required a change in a rule.

## Mutants still alive or not reached

Grouped by line. "Not analysed yet" means that no test has been written and that it has not been shown to be equivalent: it is
pending work, not a claim.

| Where | Status | Mutation | Reason |
|---|---|---|---|
| `DriverAvailability.cs:34` | 1 Survived | Equality | Equivalent: when the span equals the window both branches give zero. |
| `DrivingPeriod.cs:19` | 1 Survived | String | Text of an exception message; the type and parameter are tested. |
| `DrivingPeriod.cs:44` | 1 NoCoverage | String | `ToString` for debugging only. |
| `LaterServicesEvaluation.cs:80` | 1 Survived | Linq method (ThenBy() to ThenByDescending()) | Tie-break of two services of one driver that start at the same instant, which the overlap check rejects before; not shown equivalent by a test. |
| `LaterServicesEvaluation.cs:81` | 2 Timeout | Logical | Not analysed yet. |
| `LaterServicesEvaluation.cs:102` | 1 Survived | Linq method (FirstOrDefault() to First()) | Not analysed yet. |
| `LaterServicesEvaluation.cs:158` | 2 Survived | Arithmetic; Conditional (true) | Not analysed yet. |
| `LaterServicesEvaluation.cs:172` | 1 Survived | Boolean | Not analysed yet. |
| `LaterServicesEvaluation.cs:173` | 1 Survived | Boolean | Not analysed yet. |
| `LaterServicesEvaluation.cs:174` | 3 Survived | Arithmetic; Equality | Not analysed yet. |
| `LaterServicesEvaluation.cs:175` | 1 Survived | Equality | Not analysed yet. |
| `LaterServicesEvaluation.cs:184` | 1 Timeout, 2 Survived | Equality; Logical | Not analysed yet. |
| `LegalComplianceEvaluation.cs:65` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:66` | 1 Survived | Arithmetic | Not analysed yet. |
| `LegalComplianceEvaluation.cs:81` | 1 Survived | Conditional (false) | Not analysed yet. |
| `LegalComplianceEvaluation.cs:143` | 1 Survived | Conditional (true) | Not analysed yet. |
| `LegalComplianceEvaluation.cs:157` | 1 Survived | String | Not analysed yet. |
| `LegalComplianceEvaluation.cs:187` | 1 Survived | Conditional (false) | Not analysed yet. |
| `LegalComplianceEvaluation.cs:197` | 2 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:200` | 3 NoCoverage | Boolean; Statement; String | Unreachable: the working days are built from blocks that include the trip, so its day is always found. |
| `LegalComplianceEvaluation.cs:201` | 3 NoCoverage | Boolean; Statement; String | Unreachable, same reason as line 200. |
| `LegalComplianceEvaluation.cs:207` | 2 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:230` | 1 Timeout | Statement | Not analysed yet. |
| `LegalComplianceEvaluation.cs:232` | 3 Survived | Equality; LogicalNotExpression to un-LogicalNotExpression | Not analysed yet. |
| `LegalComplianceEvaluation.cs:233` | 1 Timeout | Statement | Not analysed yet. |
| `LegalComplianceEvaluation.cs:235` | 1 Survived | Arithmetic | Not analysed yet. |
| `LegalComplianceEvaluation.cs:262` | 3 Survived | Conditional (false); Conditional (true); Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:287` | 2 Survived | Statement; String | Not analysed yet. |
| `LegalComplianceEvaluation.cs:337` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:339` | 2 Survived | Equality; Linq method (Min() to Max()) | `Min` to `Max` needs two periods of one driver under way at once (overlap, rejected before); the equality one is not analysed yet. |
| `LegalComplianceEvaluation.cs:378` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:379` | 3 Survived | Equality; Linq method (Any() to All()) | Not analysed yet. |
| `LegalComplianceEvaluation.cs:384` | 4 Survived | Conditional (false); Conditional (true); Equality | Regular vs reduced weekly rest type: no rule reads it today (compensation, Art. 8.6, is out of scope). |
| `LegalComplianceEvaluation.cs:393` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:395` | 1 NoCoverage | Statement | Unreachable through the public methods: both filter the history to periods that start before the instant. |
| `LegalComplianceEvaluation.cs:401` | 4 Survived | Conditional (false); Conditional (true); Equality | Same as line 384. |
| `LegalComplianceEvaluation.cs:443` | 5 Survived | Equality; Logical | Not analysed yet. |
| `LegalComplianceEvaluation.cs:444` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:447` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:466` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:476` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:477` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:486` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:487` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:488` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:500` | 1 Survived | Linq method (Append() to Prepend()) | Not analysed yet. |
| `LegalComplianceEvaluation.cs:503` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:504` | 2 Survived | Conditional (true); Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:532` | 1 Survived | OrAssignmentExpression to ExclusiveOrAssignmentExpression | Not analysed yet. |
| `LegalComplianceEvaluation.cs:535` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:537` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:538` | 1 Survived | Equality | Not analysed yet. |
| `LegalComplianceEvaluation.cs:555` | 1 Survived | Equality | `>` to `>=` is equivalent in a maximum; the other mutant of the line is killed. |
