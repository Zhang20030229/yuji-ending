import type { NavigateFunction, To } from "react-router-dom";

/**
 * 返回当前入口的上一级；直接打开页面、没有站内历史时使用指定的安全落点。
 */
export function backwards(navigate: NavigateFunction, fallback: To) {
  const historyIndex = window.history.state?.idx;
  if (typeof historyIndex === "number" && historyIndex > 0) {
    navigate(-1);
    return;
  }

  navigate(fallback, { replace: true });
}
