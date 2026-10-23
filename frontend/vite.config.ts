/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// In development the API runs on :5080; proxying keeps the browser on one origin,
// so no CORS or cookie configuration is needed locally.
const api = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/media': api,
    },
  },
  // three.js (3D viewer) and Recharts (admin) are only reached through lazy imports, so they load on demand.
  build: { chunkSizeWarningLimit: 1100 },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
