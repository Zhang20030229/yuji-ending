import { useEffect } from "react";
import { RouterProvider } from "react-router-dom";
import { router } from "./router";
import OfflineNotice from "@/components/native/OfflineNotice";
import { initNativeShell } from "@/native/shell";

/** ECHORA Web 应用入口。 */
export default function App() {
  useEffect(() => {
    // 原生壳内需要在首帧挂载后隐藏启动图；浏览器环境此调用为空操作。
    void initNativeShell();
  }, []);

  return (
    <>
      <OfflineNotice />
      <RouterProvider router={router} />
    </>
  );
}
