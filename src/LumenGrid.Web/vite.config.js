import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 6175,
    proxy: {
      '/api': { target: 'http://localhost:6103', changeOrigin: true },
      '/hubs': { target: 'ws://localhost:6103', changeOrigin: true, ws: true }
    }
  }
})
