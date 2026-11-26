import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

declare const process: { env: Record<string, string | undefined> };

// API_PROXY permite apuntar a otra instancia local (p. ej. una compilación aparte en :5172).
const API = process.env.API_PROXY ?? 'http://localhost:5171';

// SPEC 0037: la web llama a /api y Vite lo reenvía a la API (sin CORS).
export default defineConfig({
  plugins: [react()],
  // SPEC 0044: los specs de Playwright (e2e/) no son de Vitest.
  // css: the contrast tests read the tokens; Vitest replaces every other style sheet with an empty one.
  test: { include: ['src/**/*.test.ts'], setupFiles: ['src/testSetup.ts'], css: { include: [/index\.css/] } },
  server: {
    port: 5173,
    proxy: { '/api': API, '/health': API },
  },
  // A font inside the style sheet is not a file of the web, and its CSP only lets those in (SPEC 0091 R3).
  build: { assetsInlineLimit: 0 },
});
