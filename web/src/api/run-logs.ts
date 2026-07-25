import { apiFetch, apiJson } from "./client";

/** 设置页可展示的一条安全失败记录。 */
export interface RunLogItem {
  id: string;
  kind: "moment" | "processing_run" | "report";
  branch: string;
  stage: string;
  state: string;
  errorCode?: string;
  summary: string;
  occurredAt: string;
  attemptCount: number;
  conversationSessionId?: string;
  canRetry: boolean;
}

/** 读取最近失败的知识项和后台运行。 */
export function getRunLogs(signal?: AbortSignal) {
  return apiJson<RunLogItem[]>("/run-logs", { signal });
}

/** 只重试选中的失败记录。 */
export async function retryRunLog(item: RunLogItem) {
  const path = item.kind === "moment"
    ? `/moments/${item.id}/retry`
    : item.kind === "report"
      ? `/reports/${item.id}/retry?kind=${encodeURIComponent(item.branch)}`
    : `/run-logs/processing-runs/${item.id}/retry`;
  await apiFetch(path, { method: "POST" });
}
