import { fileURLToPath, URL } from 'node:url';

// Ruta absoluta: la web también se sirve con `vite web` desde la raíz del repo.
export default { plugins: { tailwindcss: { config: fileURLToPath(new URL('./tailwind.config.js', import.meta.url)) }, autoprefixer: {} } };
