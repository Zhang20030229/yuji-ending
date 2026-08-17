/**
 * API 基址的唯一出口。
 *
 * 浏览器同源部署时 `VITE_API_BASE_URL` 留空，所有请求继续走相对路径；
 * Capacitor 打包后页面来自 `capacitor://localhost`，相对路径无法命中服务器，
 * 因此构建时注入绝对基址（例如 `https://yuji.example.com`）。
 */
// 必须写成 import.meta.env.VITE_* 的字面形式，Vite 只对这种写法做构建期静态替换。
export const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

/** 把以 `/` 开头的应用内路径拼成实际请求地址。 */
export function apiUrl(path: string) {
  return `${API_BASE}${path}`;
}
