import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  // 需与 Apple Developer / App Store Connect 中注册的 Bundle ID 完全一致。
  appId: "com.yuji.echora",
  appName: "遇己",
  webDir: "dist",
  ios: {
    // 让 WKWebView 自己处理安全区，页面内再用 env(safe-area-inset-*) 精确排版。
    contentInset: "always",
  },
  plugins: {
    SplashScreen: {
      // 首屏数据就绪后由代码手动隐藏，避免 WKWebView 第一帧白屏。
      launchAutoHide: false,
      backgroundColor: "#0b0b0f",
    },
  },
};

export default config;
