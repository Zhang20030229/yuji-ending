import { Capacitor } from "@capacitor/core";
import { Haptics, ImpactStyle } from "@capacitor/haptics";
import { SplashScreen } from "@capacitor/splash-screen";
import { StatusBar, Style } from "@capacitor/status-bar";

/** 当前是否运行在 iOS / Android 原生壳内；浏览器访问时为 false。 */
export const isNativeShell = Capacitor.isNativePlatform();

/**
 * 原生壳启动收尾：statusBar 跟随浅色主题，首屏就绪后隐藏启动图。
 * 浏览器环境直接返回，所有插件调用都不会执行。
 */
export async function initNativeShell() {
  if (!isNativeShell) return;
  // 插件失败不能挡住应用启动，因此逐个吞掉异常。
  await StatusBar.setStyle({ style: Style.Light }).catch(() => {});
  await SplashScreen.hide().catch(() => {});
}

/** 关键交互的轻触感反馈；浏览器环境静默跳过。 */
export function tapFeedback() {
  if (!isNativeShell) return;
  void Haptics.impact({ style: ImpactStyle.Light }).catch(() => {});
}
