import { defineConfig, devices } from '@playwright/test';

// SPEC 0044: E2E de la web contra la API real. La BD la pone quien lo lance (ConnectionStrings__DispatchGuardDb);
// ver web/e2e/README.md. Zona horaria fija: la web debe mostrar horas de Madrid.
// SPEC 0091: with E2E_BASE_URL the tests run against a container that is already up and seeded (deploy/test/start.sh).
const container = process.env.E2E_BASE_URL;
const baseURL = container ?? 'http://localhost:5173';

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }], ['junit', { outputFile: 'reports/playwright.xml' }]] : 'list',
  globalSetup: container ? undefined : './e2e/global-setup.ts',
  use: {
    baseURL,
    // The checks read the Spanish texts, the ones there were before the catalogue; showcase.spec.ts tries both (SPEC 0095).
    storageState: { cookies: [], origins: [{ origin: baseURL, localStorage: [{ name: 'dispatchguard.language', value: 'es' }] }] },
    // Its certificate is made for the occasion.
    ignoreHTTPSErrors: !!container,
    locale: 'es-ES',
    timezoneId: 'Europe/Madrid',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: container
    ? undefined
    : [
        {
          command: 'dotnet run --project ../src/Web.Api --no-launch-profile',
          url: 'http://localhost:5171/health/live',
          // SPEC 0053: la demo hace ~700 llamadas; el log SQL de EF (≈6 MB) llenaba la tubería de salida y colgaba la API.
          // Sin puntos en el nombre: Playwright lanza la orden con /bin/sh, que descarta esas variables.
          env: {
            ASPNETCORE_URLS: 'http://localhost:5171', ASPNETCORE_ENVIRONMENT: 'Testing',
            Logging__LogLevel__Default: 'Warning',
            // Testing turns it off by default; here it runs as in the demo (SPEC 0084).
            Session__Required: 'true',
          },
          timeout: 180_000,
          reuseExistingServer: !process.env.CI,
        },
        { command: 'npm run dev', url: 'http://localhost:5173', reuseExistingServer: !process.env.CI },
      ],
});
