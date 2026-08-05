import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Standalone build of the "Projeto" landing page with mocked data, published
// to the repository's GitHub Pages — separate from the production dashboard
// build (vite.config.ts), which is embedded in the .NET backend instead.
export default defineConfig({
  root: path.resolve(import.meta.dirname, 'pages'),
  base: '/doctor-api-mcp/',
  publicDir: path.resolve(import.meta.dirname, 'public'),
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  build: {
    outDir: path.resolve(import.meta.dirname, 'dist-pages'),
    emptyOutDir: true,
  },
})
