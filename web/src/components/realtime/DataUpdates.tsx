import { useEffect, useState, type ReactNode } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getAccessToken } from "@/api/client";

const EVENT_NAME = "echora:data-changed";

interface DataChanged {
  state: string;
  areas: string[];
}

/** 全应用只建立一条通知连接；通知仅让当前页面重新读取一次，不承载业务正文。 */
export function DataUpdates({ children }: { children: ReactNode }) {
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/api/updates", { accessTokenFactory: () => getAccessToken() ?? "" })
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("dataChanged", (message: DataChanged) => {
      window.dispatchEvent(new CustomEvent<DataChanged>(EVENT_NAME, { detail: message }));
    });
    connection.onreconnected(() => {
      window.dispatchEvent(new CustomEvent<DataChanged>(EVENT_NAME, {
        detail: { state: "reconnected", areas: ["all"] },
      }));
    });
    void connection.start().catch(() => {
      // 初次连接失败时页面仍可正常使用；切页和刷新都会读取数据库真实状态。
    });
    return () => { void connection.stop(); };
  }, []);

  return children;
}

/** 返回相关数据最近一次失效的版本号，供页面触发一次重新读取。 */
export function useDataRevision(...areas: string[]) {
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let timer = 0;
    const changed = (event: Event) => {
      const message = (event as CustomEvent<DataChanged>).detail;
      if (!message?.areas.some((area) => area === "all" || areas.includes(area))) return;
      window.clearTimeout(timer);
      timer = window.setTimeout(() => setRevision((value) => value + 1), 300);
    };
    window.addEventListener(EVENT_NAME, changed);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener(EVENT_NAME, changed);
    };
  }, [areas.join("|")]);
  return revision;
}
