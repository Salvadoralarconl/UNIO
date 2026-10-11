import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig(({ command, mode }) => ({
  plugins: [react()],
  server: {
    host: 'localhost', port: 5174, strictPort: true,
    // Activate only after staged rollout; preserve the still-running HTTP API until then.
    // Both choices are fixed loopback targets; no certificate export or trust changes.
    proxy: { '/api': loadEnv(mode, '.', 'CLARY_LOCAL_HTTPS').CLARY_LOCAL_HTTPS === '1'
      ? { target: 'https://localhost:5081', secure: command !== 'serve' }
      : { target: 'http://127.0.0.1:5081' } },
  },
}));
