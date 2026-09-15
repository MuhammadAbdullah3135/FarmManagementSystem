import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  base: '/FarmManagementSystem/',
  plugins: [react()],
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
})
