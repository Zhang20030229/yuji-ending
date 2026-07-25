import { apiJson } from "./client";

/** 拾光和遇己尚未完成的后台分支数量。 */
export interface AnalysisStatus {
  lifeRecordCount: number;
  recognitionCount: number;
  emotionCount: number;
}

export function getAnalysisStatus(signal?: AbortSignal) {
  return apiJson<AnalysisStatus>("/run-logs/status", { signal });
}
