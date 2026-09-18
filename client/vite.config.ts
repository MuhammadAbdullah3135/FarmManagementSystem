import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  base: '/FarmManagementSystem/',
  plugins: [react()],
  // Stamped into the bundle so the running build is identifiable from the UI: the APK loads a
  // deployed bundle, and "which build is my phone actually on?" has been pure guesswork. The Pages
  // workflow passes VITE_BUILD_SHA; local runs fall back to 'dev'.
  define: {
    __BUILD_SHA__: JSON.stringify(process.env.VITE_BUILD_SHA ?? 'dev'),
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
  build: {
    // Older Android WebViews (the APK shell) must be able to parse the bundle.
    target: ['chrome87', 'edge88', 'firefox78', 'safari14'],
  },
  server: {
    host: '127.0.0.1',
    port: 3000,
    // Temporary public quick-tunnel testing; do not use this setting for production.
    allowedHosts: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5200',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
