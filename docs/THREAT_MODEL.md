# Threat model

STRIDE over what exists today: a public demo with **no user accounts**, one container serving the API and the web, one PostgreSQL
database, a daily reset. Each mitigation names the test or the configuration that holds it; a threat without one is listed as a
residual risk, not hidden. Updated 2026-11-26 (SPEC 0099).

## Assets and trust boundaries

| Asset | Why it matters |
|---|---|
| Allocation decisions and the attempt log | The product's claim: nothing illegal is allocated, and every attempt is recorded |
| The audit trail and the visit log | Evidence of what happened; must not be rewritten or read by visitors |
| Session key, audit key, demo reset key, cron secret, connection string | Whoever holds them can forge sessions, read the audit or wipe the demo |
| Availability of the demo | A portfolio that does not answer proves nothing |

Boundaries: browser ↔ API (anonymous, untrusted) · hosting proxy ↔ API (trusted only for the networks configured) ·
API ↔ PostgreSQL (application role with row permissions only) · CI ↔ repository (no secrets, no deployment).

## STRIDE

| Threat | Where | Mitigation | Held by |
|---|---|---|---|
| **S**poofing a session | Session cookie | Cookie sealed with `Session:Key` (Data Protection); a forged one is replaced and cannot write; `Secure`, `HttpOnly`, `SameSite=Strict`; the key is mandatory outside Development and Testing | `AnonymousSessionHttpTests` (`Session_ForgedCookie_GetsAnotherOneAndCannotWrite`, `Session_InProduction_CookieIsSecureStrictAndHttpOnly`, `Start_NotRequiredOutsideTesting_Fails`); ADR 0021 |
| **S**poofing a cross-site write (CSRF) | Every write | 401 without a session, 403 without the `X-DispatchGuard-Request` header, which a cross-site form cannot send | `AnonymousSessionHttpTests` (`Write_WithSessionButWithoutHeader_IsForbidden`, `AnyWrite_WithoutSession_NeverReachesTheOperation`) |
| **S**poofing the client IP | Limits per IP | `X-Forwarded-For` is honoured only from `ForwardedHeaders:TrustedNetworks` | `RequestLimitsHttpTests`; `src/Web.Api/Perimeter/TrustedProxies.cs` |
| **T**ampering with an allocation by racing it | Allocate, cancel, sick leave, workshop | Read, validate and save inside one transaction holding a lock per service, driver and vehicle | `ConcurrentAllocationTests`, `ConcurrentLoadTests`; ADR 0018 |
| **T**ampering with the audit trail | Audit and visit tables | Written in the same transaction as the change; the application role can only insert (no update or delete) | `AuditTrailTests`, `LeastPrivilegeTests`; `deploy/sql/application-role.sql`; ADR 0026 |
| **T**ampering with the X-ray | `GET /api/xray` | The report is sealed into a token with the session key; altered or guessed tokens get 404 | `XRayHttpTests` (`Read_GuessedOrAlteredOrMissing_Returns404`) |
| **R**epudiation of an allocation | Attempt log | Every human attempt is recorded with its session, rules, result and, when forced, the reason and the declared name; if it cannot be recorded nothing is allocated (503) | `AllocationAttemptLogIntegrationTests`, `ForcedAllocationIntegrationTests`, `AnonymousSessionHttpTests` (`Allocate_KeepsTheSessionThatMadeTheAttempt`) |
| **I**nformation disclosure through the X-ray | X-ray report | Readable only by the session that asked, for 5 minutes; a field allowlist: no SQL, no lock or conflict ids, no names, no force reason | `XRayHttpTests` (`Read_FromAnotherSession_Returns404`, `Read_AfterItsLifetime_Returns404`, `Read_ForcedOverAnotherDriver_HoldsOnlyAllowedFieldsAndNothingSensitive`) |
| **I**nformation disclosure of the audit | Audit screen and API | 404 without `X-Audit-Key`; no menu entry | `VisitsHttpTests`; `src/Web.Api/Perimeter/AuditKey.cs` |
| **I**nformation disclosure through reading without a session | Every read | 401 without a session, so a crawler neither reads data nor wakes the database | `AnonymousSessionHttpTests` (`Read_WithoutSession_IsUnauthorizedAndGetsNoCookie`), `DatabaseStaysAsleepTests` |
| **I**nformation disclosure of secrets in the code | Repository | No secret in the repository; gitleaks over the whole history in CI; keys come from the host's configuration | `.github/workflows/security.yml`; `.gitignore` |
| **I**nformation disclosure in the browser | Pages | CSP without inline scripts or other origins, security headers, `AllowedHosts` | `PerimeterHeadersHttpTests`, `WebPagesHttpTests`; ADR 0022 |
| **D**enial of service by volume | Every endpoint | Rate limits per session and per IP, a tighter quota for heavy calculations and for visits | `RequestLimitsHttpTests`, `VisitsHttpTests`; `src/Web.Api/Perimeter/RequestLimits.cs` |
| **D**enial of service by payload | Text fields, dates | Any text longer than its column → 400; a null character → 400; offsets normalised | `LongTextHttpTests`, `HardeningHttpTests`, `OffsetDatesHttpTests` |
| **D**enial of service by keeping the database awake | Health probes | `/health/live` never touches the database | `HealthHttpTests`, `DatabaseStaysAsleepTests` |
| **E**levation to wipe or rebuild the demo | Reset and rebuild endpoints | 404 without `Demo:ResetKey`; the rebuild checks the cron secret (hashed comparison) before opening the database; one at a time | `AnonymousSessionHttpTests` (`DemoReset_NeedsItsKeyAndNoSession`), `DemoRebuildHttpTests` |
| **E**levation to delete master data | Delete driver, vehicle, location | Disabled by configuration in the demo (403) | `MasterDataDeletionLockHttpTests`; `Demo:LockMasterDataDeletion` |
| **E**levation inside the database | Application role | Row permissions only; no DDL, no access to the migrations history | `LeastPrivilegeTests`; `deploy/sql/application-role.sql` |
| Supply chain | Dependencies, images, actions | NuGet and npm audit fail CI; images pinned by digest; gitleaks pinned by checksum; Dependabot; CycloneDX SBOM per build | `.github/workflows/ci.yml`, `security.yml`, `.github/dependabot.yml` |

## Residual risks (accepted)

- **No authentication.** Anyone can open a session and change the demo data; that is the point of a public demo. The daily reset
  bounds the damage. The name given when forcing an allocation is declared, not verified.
- **Rate limits per IP** depend on the hosting proxy being the only trusted network; a misconfiguration would let a client choose
  its IP.
- **An X-ray token can be replayed by its own session** during its 5 minutes; it shows only what that session already received.
- The cookie keeps the former prefix `lof` and the locks `lof:driver:`; renaming them needs a release of its own.
- Not tested here: the hosting platform and the managed database (their own controls apply), and anything requiring the private
  keys. OWASP ZAP runs against the image in CI, not against the public deployment.

Reporting a vulnerability: [`SECURITY.md`](../SECURITY.md).
