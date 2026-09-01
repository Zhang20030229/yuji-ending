import { apiFetch, apiJson } from "./client.ts";

/** 一条直接用户原话。 */
export interface SelfQuote {
  messageId: number;
  sourceType: "Message" | "Moment";
  text: string;
  createdAt: string;
  locationName?: string;
  locationAddress?: string;
}

/** 一条按会话保存的认识。 */
export interface RecognitionItem {
  id: number;
  category: string;
  content: string;
  keywords: string[];
  conversationId: number;
  conversationTitle: string;
  updatedAt: string;
  sources: SelfQuote[];
  rejectedAt?: string | null;
  rejectionNote?: string | null;
}

/** 一天内的一条情绪记录。 */
export interface EmotionItem {
  id: number;
  family: string;
  subtype: string;
  intensity: number;
  summary: string;
  occurredAt: string;
  conversationId: number;
  conversationTitle: string;
  sources: SelfQuote[];
}

/** 一条由原话支持、且可由用户修订的 CBT 自我观察。 */
export interface CbtObservation {
  id: number;
  situation: string;
  automaticThought?: string;
  bodySensation?: string;
  behavior?: string;
  immediateOutcome?: string;
  occurredAt: string;
  sources: SelfQuote[];
}

/** 一条由模型按当日记录归纳、只读的关键洞察。 */
export interface DayInsight {
  situation: string;
  appraisal?: string | null;
  emotions: string[];
  followUp?: string | null;
  quotes: string[];
  observationIds: number[];
}

/** 情绪日视图。 */
export interface EmotionDay {
  date: string;
  summary?: string;
  narrative?: string | null;
  conversationCount: number;
  sourceMessageCount: number;
  items: EmotionItem[];
  families: Array<{ family: string; momentCount: number; peakIntensity: number; subtypes: string[] }>;
  insights: DayInsight[];
  cbtObservations: CbtObservation[];
}

/** 情绪月视图。 */
export interface EmotionMonth {
  month: string;
  summary?: string;
  days: Array<{
    date: string;
    families: Array<{ family: string; peakIntensity: number; count: number; lastOccurredAt: string }>;
  }>;
  families: Array<{
    family: string;
    activeDays: number;
    averageDailyPeak: number;
    subtypes: string[];
    trend: Array<{ date: string; peakIntensity: number }>;
  }>;
  representativeQuotes: Array<{ date: string; family: string; subtype: string; text: string }>;
  cbtObservationCount: number;
  cbtCoveredDays: number;
}

/** 情绪年度热力图。 */
export interface EmotionYear {
  year: number;
  days: Array<{
    date: string;
    families: Array<{ family: string; peakIntensity: number; count: number; lastOccurredAt: string }>;
  }>;
  months: Array<{ month: number; activeDays: number; recordCount: number }>;
  families: Array<{ family: string; activeDays: number; peakIntensity: number; recordCount: number }>;
}

/** 读取认识列表；rejected 省略时只返回未被驳回的认识。 */
export function getRecognitions(
  category: string,
  query: string,
  signal?: AbortSignal,
  rejected?: "true" | "all",
) {
  const params = new URLSearchParams();
  if (category) params.set("category", category);
  if (query.trim()) params.set("q", query.trim());
  if (rejected) params.set("rejected", rejected);
  return apiJson<RecognitionItem[]>(`/self/recognitions${params.size ? `?${params}` : ""}`, { signal });
}

/** 驳回一条认识；原话不会被修改，档案、召回与后续报告都不再包含它。 */
export async function rejectRecognition(id: number, note?: string) {
  await apiFetch(`/self/recognitions/${id}/rejection`, {
    method: "POST",
    body: JSON.stringify({ note: note?.trim() ? note.trim() : null }),
  });
}

/** 撤销驳回，让这条认识重新参与档案与召回。 */
export async function restoreRecognition(id: number) {
  await apiFetch(`/self/recognitions/${id}/rejection`, { method: "DELETE" });
}

/** 读取一天的情绪。 */
export function getEmotionDay(date: string, signal?: AbortSignal) {
  return apiJson<EmotionDay>(`/self/emotions/day?date=${encodeURIComponent(date)}`, { signal });
}

/** 读取一个月的情绪。 */
export function getEmotionMonth(month: string, signal?: AbortSignal) {
  return apiJson<EmotionMonth>(`/self/emotions/month?month=${encodeURIComponent(month)}`, { signal });
}

/** 读取一年的情绪热力图。 */
export function getEmotionYear(year: number, signal?: AbortSignal) {
  return apiJson<EmotionYear>(`/self/emotions/year?year=${encodeURIComponent(year)}`, { signal });
}

/** 删除 CBT 自我观察；原消息和同源情绪不会被修改。 */
export async function deleteCbtObservation(id: number) {
  await apiFetch(`/self/cbt-observations/${id}`, { method: "DELETE" });
}
