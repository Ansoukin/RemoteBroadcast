import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "path";

// 双入口：pc.html（≥900px 独立信息架构）+ mobile.html（启动器形态）。
// 产物直落插件 wwwroot，由插件 HttpListener 本地托管，页面不引用任何外部 CDN。
export default defineConfig({
  plugins: [vue()],
  base: "./",
  build: {
    outDir: resolve(__dirname, "../src/ClassFabric.RemoteBroadcast/Assets/wwwroot"),
    emptyOutDir: true,
    rollupOptions: {
      input: {
        pc: resolve(__dirname, "pc.html"),
        mobile: resolve(__dirname, "mobile.html"),
      },
    },
  },
});
