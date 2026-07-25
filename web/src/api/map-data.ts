import type { RecognitionItem } from "./self.ts";

/** 地图上的一个标记点：目前仅地点入图，事件不带经纬度。 */
export type MapMarker = {
  id: string;
  sourceId: number;
  kind: "place";
  title: string;
  summary?: string;
  secondary?: string;
  conversationCount?: number;
  eventCount?: number;
  latitude: number;
  longitude: number;
  occurredAt?: string;
  imageUrl?: string;
};

/** 过滤出拥有有效经纬度的地点，映射为地图标记。 */
export function mapMappablePlaces(
  places: Array<{
    id: number;
    name: string;
    latitude?: number;
    longitude?: number;
    coverImageUrl?: string;
    latestSummary?: string;
    secondary?: string;
    conversationCount?: number;
    eventCount?: number;
  }>,
): MapMarker[] {
  return places
    .filter((p) => Number.isFinite(p.latitude) && Number.isFinite(p.longitude))
    .map((p) => ({
      id: `place:${p.id}`,
      sourceId: p.id,
      kind: "place" as const,
      title: p.name,
      summary: p.latestSummary,
      secondary: p.secondary,
      conversationCount: p.conversationCount,
      eventCount: p.eventCount,
      latitude: p.latitude as number,
      longitude: p.longitude as number,
      imageUrl: p.coverImageUrl,
    }));
}

/** 时间筛选时保留当期事件地点，同时始终展示只有坐标、尚未关联事件的地点档案。 */
export function selectVisiblePlaceMarkers(
  markers: MapMarker[],
  visiblePlaceNames: ReadonlySet<string>,
): MapMarker[] {
  if (visiblePlaceNames.size === 0) return markers;
  return markers.filter((marker) =>
    marker.eventCount === 0 || visiblePlaceNames.has(marker.title));
}

/** 把私有原图 URL 改为地图专用的小尺寸压缩 WebP URL。 */
export function toMarkerThumbnailUrl(url?: string): string | undefined {
  if (!url) return undefined;
  const match = url.match(/^\/api\/assets\/(\d+)\/(?:content|thumbnail)(?:\?.*)?$/);
  return match
    ? `/api/assets/${match[1]}/thumbnail?size=68&quality=65`
    : url;
}

/** 给定一年内有数据的月份，返回最早月份，否则回退到当年一月。 */
export function firstMonthWithData(year: number, monthsWithData: number[]): string {
  if (!monthsWithData.length) return `${year}-01`;
  const month = Math.min(...monthsWithData);
  return `${year}-${String(month).padStart(2, "0")}`;
}

/** 生命树上的一片认识叶子。 */
export type TreeRecognition = {
  id: string;
  category: string;
  title: string;
  content: string;
  meta: string;
  updatedAt: string;
  keywords: string[];
  quote: string;
};

/** 将认识列表映射为生命树可渲染的叶子数据；quote 取自真实原话来源，没有来源时留空。 */
export function toTreeRecognitions(items: RecognitionItem[]): TreeRecognition[] {
  return items.map((item) => ({
    id: String(item.id),
    category: item.category,
    title: item.content,
    content: item.content,
    meta: item.conversationTitle,
    updatedAt: item.updatedAt,
    keywords: item.keywords,
    quote: item.sources[0]?.text ?? "",
  }));
}
