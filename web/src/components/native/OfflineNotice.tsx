import { useEffect, useState } from "react";

/**
 * 断网提示条：原生壳里页面本身来自本地包，断网时不会白屏而是请求全失败，
 * 因此需要一条明确的状态提示，而不是让用户面对空页面。
 */
export default function OfflineNotice() {
  const [offline, setOffline] = useState(() => typeof navigator !== "undefined" && !navigator.onLine);

  useEffect(() => {
    const online = () => setOffline(false);
    const lost = () => setOffline(true);
    window.addEventListener("online", online);
    window.addEventListener("offline", lost);
    return () => {
      window.removeEventListener("online", online);
      window.removeEventListener("offline", lost);
    };
  }, []);

  if (!offline) return null;

  return (
    <div
      role="status"
      className="fixed inset-x-0 top-0 z-50 bg-destructive px-4 py-2 text-center text-xs text-white"
      style={{ paddingTop: "calc(env(safe-area-inset-top, 0px) + 8px)" }}
    >
      当前网络不可用，遇己会在恢复后继续同步。
    </div>
  );
}
