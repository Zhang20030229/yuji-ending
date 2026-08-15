import path from "path"
import { defineConfig } from "vite"
import react from "@vitejs/plugin-react"
import tailwindcss from "@tailwindcss/vite"

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
    },
  },
  server: {
    // 绑定所有网卡，便于同一局域网内的手机直接访问开发页面。
    host: true,
    proxy: {
      "/api": {
        target: "http://localhost:5237",
        ws: true,
      },
    },
  },
})
