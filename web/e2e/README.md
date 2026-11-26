# Web E2E tests

Playwright starts the API (`dotnet run`, port 5171) and Vite (5173), seeds the demo week of the next
Monday (`tools/SampleData`) and tests: leave → candidates → assign → ⓘ (panel and bar), the "Ahorro de km"
report and times in Europe/Madrid.

In CI (`e2e` job) it does not start anything: `E2E_BASE_URL` points at the container that `deploy/test/start.sh`
started and seeded (SPEC 0091). `container.spec.ts` only runs there.

Locally it needs a migrated database and its connection string:

```bash
export ConnectionStrings__DispatchGuardDb="Host=localhost;Database=DispatchGuardE2E;Username=postgres;Password=…"
dotnet ef database update --project src/Infrastructure --startup-project src/Web.Api --connection "$ConnectionStrings__DispatchGuardDb"
cd web && npx playwright install chromium && npm run e2e
```

Use a database of its own: the tests reassign services.
