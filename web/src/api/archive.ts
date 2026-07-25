import { apiFetch, apiJson } from "./client";
import type { MomentItem } from "./moments";

/** 私有附件的安全页面引用。 */
export interface ArchiveAttachment {
  id: number;
  fileName: string;
  mimeType: string;
  contentUrl: string;
  aiDescription?: string;
}

/** 一条可直接展示的用户原话。 */
export interface ArchiveSource {
  id: number;
  sourceType: "Message" | "Moment";
  text: string;
  createdAt: string;
  locationName?: string;
  locationAddress?: string;
  attachments: ArchiveAttachment[];
}

/** 片段列表卡。 */
export interface FragmentItem {
  id: number;
  title: string;
  summary: string;
  occurredAt: string;
  sourceCount: number;
  personCount: number;
  placeCount: number;
  eventCount: number;
  coverImageUrl?: string;
  latitude?: number;
  longitude?: number;
}

/** 片段详情。 */
export interface FragmentDetail extends FragmentItem {
  sources: ArchiveSource[];
  people: ArchiveEntityLink[];
  places: ArchiveEntityLink[];
  events: Array<{ id: number; title: string; summary: string }>;
  latitude?: number;
  longitude?: number;
}

/** 可从片段跳转到详情的人物或地点。 */
export interface ArchiveEntityLink {
  id: number;
  name: string;
}

/** 人物或地点列表卡。 */
export interface EntityCard {
  id: number;
  name: string;
  secondary: string;
  keywords: string[];
  aliases: string[];
  latestSummary: string;
  conversationCount: number;
  eventCount: number;
  coverImageUrl?: string;
  latitude?: number;
  longitude?: number;
}

/** 人物或地点详情。 */
export interface EntityDetail {
  id: number;
  name: string;
  secondary: string;
  keywords: string[];
  aliases: string[];
  coverImageUrl?: string;
  images: ArchiveAttachment[];
  records: Array<{
    id: number;
    conversationId: number;
    conversationTitle: string;
    summary: string;
    sources: ArchiveSource[];
  }>;
  events: Array<{ id: number; title: string; summary: string }>;
  latitude?: number;
  longitude?: number;
}

/** 事件列表卡。 */
export interface EventItem {
  id: number;
  title: string;
  summary: string;
  occurredAt: string;
  people: string[];
  places: string[];
  locations: Array<{
    id: number;
    name: string;
    region: string;
    latitude?: number;
    longitude?: number;
  }>;
  coverImageUrl?: string;
}

/** 事件详情。 */
export interface EventDetail extends EventItem {
  conversationId: number;
  sources: ArchiveSource[];
  images: ArchiveAttachment[];
}

/** 一条待确认的人物或地点。 */
export interface PendingItem {
  id: number;
  kind: "Person" | "Place";
  mention: string;
  reason: string;
  conversationId: number;
  conversationTitle: string;
  createdAt: string;
  sources: ArchiveSource[];
}

/** 地图一次请求返回地点和事件。 */
export interface ArchiveMapOverview {
  places: EntityCard[];
  events: EventItem[];
}

/** 待确认页一次请求返回卡片和全部候选。 */
export interface PendingOverview {
  items: PendingItem[];
  people: EntityCard[];
  places: EntityCard[];
}

export interface FragmentsOverview {
  fragments: FragmentItem[];
  moments: MomentItem[];
}

/** 读取指定拾光列表。 */
export function getArchiveList<T>(view: "fragments" | "people" | "places" | "events", query: string, signal?: AbortSignal) {
  const params = new URLSearchParams();
  if (query.trim()) params.set("q", query.trim());
  return apiJson<T[]>(`/archive/${view}${params.size ? `?${params}` : ""}`, { signal });
}

/** 读取指定拾光详情。 */
export function getArchiveDetail<T>(view: "fragments" | "people" | "places" | "events", id: number, signal?: AbortSignal) {
  return apiJson<T>(`/archive/${view}/${id}`, { signal });
}

/** 读取全部待确认项。 */
export function getPending(signal?: AbortSignal) {
  return apiJson<PendingItem[]>("/archive/pending", { signal });
}

/** 一次读取地图所需数据。 */
export function getArchiveMapOverview(signal?: AbortSignal) {
  return apiJson<ArchiveMapOverview>("/archive/map/overview", { signal });
}

/** 一次读取待确认项和候选。 */
export function getPendingOverview(signal?: AbortSignal) {
  return apiJson<PendingOverview>("/archive/pending/overview", { signal });
}

/** 一次读取片段及一刻整理状态。 */
export function getFragmentsOverview(signal?: AbortSignal) {
  return apiJson<FragmentsOverview>("/archive/fragments/overview", { signal });
}

/** 解决、新建或忽略一条待确认项。 */
export async function resolvePending(id: number, input: { entityId?: number; newName?: string; ignore?: boolean }) {
  await apiFetch(`/archive/pending/${id}/resolve`, { method: "POST", body: JSON.stringify(input) });
}

/** 保存人物或地点的一条 AI 记录总结。 */
export async function updateEntityRecordSummary(view: "people" | "places", id: number, summary: string) {
  await apiFetch(`/archive/${view}/records/${id}/summary`, {
    method: "PUT",
    body: JSON.stringify({ summary }),
  });
}

/** 保存事件的 AI 总结。 */
export async function updateEventSummary(id: number, summary: string) {
  await apiFetch(`/archive/events/${id}/summary`, {
    method: "PUT",
    body: JSON.stringify({ summary }),
  });
}

/** 删除一条事件，不影响原聊天及关联人物、地点和图片。 */
export async function deleteLifeEvent(id: number) {
  await apiFetch(`/archive/events/${id}`, { method: "DELETE" });
}
