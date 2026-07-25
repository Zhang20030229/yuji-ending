import { apiJson } from "./client";
import type { AnalysisStatus } from "./analysis";
import type { EntityCard, EventItem, FragmentItem, PendingItem } from "./archive";
import type { ReportListItem } from "./reports";
import type { EmotionDay, RecognitionItem } from "./self";

/** 首页一次请求返回全部可见模块，避免客户端并发拼装。 */
export interface HomeOverview {
  events: EventItem[];
  people: EntityCard[];
  places: EntityCard[];
  emotion: EmotionDay;
  recognitions: RecognitionItem[];
  pending: PendingItem[];
  fragments: FragmentItem[];
  reports: ReportListItem[];
  analysisStatus: AnalysisStatus;
}

export function getHomeOverview(signal?: AbortSignal) {
  return apiJson<HomeOverview>("/home/overview", { signal });
}
