import { useEffect, useRef, useState } from "react";
import {
  IconArchive,
  IconCalendar,
  IconCalendarEvent,
  IconCheck,
  IconExternalLink,
  IconMapPin,
  IconMessageCircle,
  IconPencil,
  IconPhoto,
  IconSparkles,
  IconTrash,
  IconSearch,
  IconUsers,
  IconX,
} from "@tabler/icons-react";
import { MapPin as MapMarkerIcon } from "@phosphor-icons/react";
import { useNavigate, useParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import {
  deleteLifeEvent,
  getArchiveDetail,
  getArchiveList,
  updateEventSummary,
  type ArchiveSource,
  type EventDetail,
  type EventItem,
} from "@/api/archive";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { MapCanvas } from "@/pages/map/vendor/MapCanvas";
import { backwards } from "@/lib/navigation";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import AnalysisBanner from "@/components/analysis/AnalysisBanner";

/** 「事件」列表与详情：从原拾光页拆出，仅展示事件视图。 */
export default function EventsPage() {
  const { itemId } = useParams<{ itemId?: string }>();
  const navigate = useNavigate();
  const selectedId = itemId ? Number(itemId) : undefined;
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<EventItem[]>([]);
  const [detail, setDetail] = useState<EventDetail>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [reload, setReload] = useState(0);
  const [deleting, setDeleting] = useState(false);
  const revision = useDataRevision("archive");
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError(undefined);
      getArchiveList<EventItem>("events", query, controller.signal)
        .then(setItems)
        .catch((reason: unknown) => {
          if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入事件。");
        })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 180);
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [query, reload, revision]);

  useEffect(() => {
    if (!selectedId) { setDetail(undefined); return; }
    const controller = new AbortController();
    getArchiveDetail<EventDetail>("events", selectedId, controller.signal)
      .then(setDetail)
      .catch(() => { if (!controller.signal.aborted) setDetail(undefined); });
    return () => controller.abort();
  }, [selectedId, reload, revision]);
  const resolvedDetail = detail ? {
    ...detail,
    people: detail.people ?? [],
    places: detail.places ?? [],
    sources: detail.sources ?? [],
    images: detail.images ?? [],
    locations: detail.locations ?? [],
  } : undefined;

  async function removeEvent() {
    if (!selectedId) return;
    if (!window.confirm("删除后，这条事件将从事件列表及人物、地点的相关事件中移除，但原聊天和图片仍会保留。确定删除吗？")) return;
    setDeleting(true);
    try {
      await deleteLifeEvent(selectedId);
      navigate("/app/map?view=timeline");
    } finally {
      setDeleting(false);
    }
  }

  return <section className="yuji-archive flex h-full min-h-0 flex-col bg-background">
    <SecondaryPageHeader
      title={selectedId ? "事件详情" : "事件"}
      subtitle={selectedId ? resolvedDetail?.title : `${items.length} 个事件`}
      onBack={() => backwards(navigate, selectedId ? "/app/map?view=timeline" : "/app/home")}
      backLabel={selectedId ? "返回时间线" : "返回首页"}
      scrolled={scrolled}
      actions={
        selectedId ? (
          <button
            type="button"
            disabled={deleting}
            onClick={() => void removeEvent()}
            className="secondary-page-header__action-button"
            aria-label="删除事件"
          >
            <IconTrash aria-hidden />
          </button>
        ) : null
      }
    />
    <AnalysisBanner scope="archive" />
    <div className={`mx-auto min-h-0 w-full max-w-[1180px] flex-1 overflow-hidden ${selectedId ? "pt-0" : "pt-4"} md:px-7 md:pb-6`} onScrollCapture={onScroll}>
      <div className="yuji-master-detail grid h-full min-h-0 xl:grid-cols-[380px_minmax(0,1fr)] xl:overflow-hidden xl:rounded-[22px] xl:border xl:border-border/70">
        <div className={`yuji-list-pane ${selectedId ? "hidden xl:flex" : "flex"} min-h-0 flex-col bg-background`}>
          <SearchBox value={query} onChange={setQuery} />
          <EventList items={items} loading={loading} error={error} selectedId={selectedId} onSelect={(id) => navigate(`/app/events/${id}`)} />
        </div>
        <div className={`yuji-detail-pane ${selectedId ? "block" : "hidden xl:block"} min-h-0 overflow-y-auto border-l border-border/65 bg-canvas/45`}>
          {resolvedDetail ? <EventDetailView
            detail={resolvedDetail}
            onOpenMap={() => navigate("/app/map?view=map")}
            onOpenPlace={(id) => navigate(`/app/places/${id}`)}
            onChanged={() => setReload((value) => value + 1)}
          /> : <Empty text="选择一个事件查看时间、地点与证据。" />}
        </div>
      </div>
    </div>
  </section>;
}

function SearchBox({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return <div className="shrink-0 p-4"><label className="yuji-search flex h-11 items-center gap-2 rounded-xl bg-secondary px-3"><IconSearch className="size-4 text-muted-foreground" aria-hidden /><span className="sr-only">搜索</span><input type="search" value={value} onChange={(event) => onChange(event.target.value)} placeholder="搜索事件标题或一段线索" className="min-w-0 flex-1 bg-transparent text-sm outline-none" /></label></div>;
}

function EventList({ items, loading, error, selectedId, onSelect }: { items: EventItem[]; loading: boolean; error?: string; selectedId?: number; onSelect: (id: number) => void }) {
  if (loading) return <div className="space-y-2 px-4">{[1, 2, 3].map((id) => <div key={id} className="h-24 animate-pulse rounded-xl bg-secondary" />)}</div>;
  if (error) return <Empty text={error} />;
  if (items.length === 0) return <Empty text="还没有事件。对话完成后会在后台自动整理。" />;
  return <div className="yuji-card-list min-h-0 flex-1 overflow-y-auto px-2 pb-6">{items.map((item) => (
    <button key={item.id} type="button" onClick={() => onSelect(item.id)} className={`yuji-record-card flex min-h-24 w-full gap-3 rounded-2xl p-3 text-left transition-colors ${selectedId === item.id ? "bg-primary-soft/80" : "hover:bg-secondary/70"}`}>
      <span className="size-16 shrink-0 overflow-hidden rounded-xl bg-secondary">{item.coverImageUrl ? <ImagePreviewDialog src={item.coverImageUrl} alt={item.title} thumbnailClassName="size-16 object-cover" interactive={false} /> : <span className="grid size-full place-items-center text-muted-foreground"><IconCalendarEvent className="size-5" aria-hidden /></span>}</span>
      <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{item.title}</span><span className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">{item.summary || "等待更多记录"}</span><span className="mt-1.5 block text-[11px] text-muted-foreground">{[...item.people, ...item.places].join(" · ") || formatTime(item.occurredAt)}</span></span>
    </button>
  ))}</div>;
}

function EventDetailView({
  detail,
  onOpenMap,
  onOpenPlace,
  onChanged,
}: {
  detail: EventDetail;
  onOpenMap: () => void;
  onOpenPlace: (id: number) => void;
  onChanged: () => void;
}) {
  const evidenceCount = detail.sources.length + detail.images.length;
  const primaryLocation = detail.locations.find((location) => location.latitude != null && location.longitude != null);

  return <article className="archive-event-detail mx-auto max-w-3xl">
    <header className="archive-event-detail__hero">
      <span className="archive-event-detail__kicker">人生事件</span>
      <h2>{detail.title}</h2>
      <div className="archive-event-detail__meta">
        <span><IconCalendar aria-hidden />{formatEventDate(detail.occurredAt)}</span>
        {detail.places.length > 0 ? <span><IconMapPin aria-hidden />{detail.places.join("、")}</span> : null}
      </div>
      <div className="archive-event-detail__evidence">
        <span><IconSparkles aria-hidden />AI 从 {evidenceCount} 条证据中整理</span>
        {detail.images.length > 0 ? <span><IconPhoto aria-hidden />{detail.images.length} 张照片</span> : null}
        {detail.people.length > 0 ? <span><IconUsers aria-hidden />{detail.people.length} 位人物</span> : null}
      </div>
    </header>

    {detail.images.length > 0 ? <section className="archive-event-gallery" aria-label="事件照片">
      {detail.images.map((image) => <figure key={image.id}>
        <ImagePreviewDialog src={image.contentUrl} alt={image.aiDescription || image.fileName} thumbnailClassName="archive-event-gallery__image" />
        {image.aiDescription ? <figcaption>{image.aiDescription}</figcaption> : null}
      </figure>)}
    </section> : null}

    <DetailSection icon={<IconSparkles />} title="事件摘要" aside="可由你修正">
      <EditableSummary value={detail.summary} onSave={async (value) => { await updateEventSummary(detail.id, value); onChanged(); }} />
    </DetailSection>

    {primaryLocation ? <DetailSection
      icon={<IconMapPin />}
      title="地点"
      action={<button type="button" onClick={onOpenMap}>在星图中查看<IconExternalLink aria-hidden /></button>}
    >
      <EventLocationMap detail={detail} />
      <div className="archive-event-locations">
        {detail.locations.map((location) => (
          <button type="button" key={location.id} onClick={() => onOpenPlace(location.id)}>
            <IconMapPin aria-hidden />
            <span><strong>{location.name}</strong>{location.region ? <small>{location.region}</small> : null}</span>
            {location.latitude != null && location.longitude != null ? <code>{location.latitude.toFixed(4)}, {location.longitude.toFixed(4)}</code> : null}
          </button>
        ))}
      </div>
    </DetailSection> : detail.places.length > 0 ? <DetailSection icon={<IconMapPin />} title="地点" aside="暂无可靠坐标">
      <p className="archive-event-detail__plain">{detail.places.join("、")}</p>
    </DetailSection> : null}

    {detail.people.length > 0 ? <DetailSection icon={<IconUsers />} title="相关人物" aside={`${detail.people.length} 位`}>
      <div className="archive-event-people">{detail.people.map((person) => <span className="archive-event-person" key={person}>{person.slice(0, 1)}<strong>{person}</strong></span>)}</div>
    </DetailSection> : null}

    <DetailSection icon={<IconMessageCircle />} title="证据原话" aside={`${detail.sources.length} 条来源`}>
      <SourceQuotes sources={detail.sources} />
    </DetailSection>
  </article>;
}

function EventLocationMap({ detail }: { detail: EventDetail }) {
  const mapRef = useRef(null);
  const markers = detail.locations
    .filter((location) => location.latitude != null && location.longitude != null)
    .map((location) => ({
      key: `event-location:${location.id}`,
      markerKey: `event-location:${location.id}`,
      label: location.name,
      longitude: location.longitude as number,
      latitude: location.latitude as number,
      occurredAt: detail.occurredAt,
      icon: MapMarkerIcon,
    }));
  const focusLocation = markers[0] ? { longitude: markers[0].longitude, latitude: markers[0].latitude } : undefined;
  if (!focusLocation) return null;
  return <div className="archive-event-map" aria-label={`事件地点地图：${markers.map((marker) => marker.label).join("、")}`}>
    <MapCanvas mapRef={mapRef} layer="event-detail" allEvents={markers} visibleMarkers={markers} onSelectMarker={() => undefined} focusLocation={focusLocation} showCurrentLocation={false} />
  </div>;
}

function EditableSummary({ value, onSave }: { value: string; onSave: (value: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!editing) setDraft(value);
  }, [editing, value]);

  async function save() {
    const summary = draft.trim();
    if (!summary) { setError("总结不能为空。"); return; }
    setSaving(true);
    setError("");
    try {
      await onSave(summary);
      setEditing(false);
    } catch {
      setError("保存失败，请重试。");
    } finally {
      setSaving(false);
    }
  }

  if (!editing) return <div className="mt-4 flex items-start gap-2 group"><p className="min-w-0 flex-1 text-[15px] leading-7 text-foreground/85">{value}</p><button type="button" onClick={() => setEditing(true)} className="grid size-9 shrink-0 place-items-center rounded-full text-muted-foreground opacity-70 hover:bg-secondary hover:opacity-100" aria-label="编辑 AI 总结"><IconPencil className="size-4" aria-hidden /></button></div>;

  return <div className="mt-4"><textarea value={draft} onChange={(event) => setDraft(event.target.value)} maxLength={2000} rows={5} className="block w-full resize-none rounded-2xl border border-border bg-background px-4 py-3 text-[15px] leading-7 outline-none transition-[border-color,box-shadow] focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary/15" aria-label="编辑 AI 总结" />{error ? <p role="alert" className="mt-2 text-xs text-destructive">{error}</p> : null}<div className="mt-3 flex justify-end gap-2"><button type="button" disabled={saving} onClick={() => setEditing(false)} className="flex min-h-10 items-center gap-1.5 rounded-xl px-4 text-sm text-muted-foreground transition-colors hover:bg-secondary"><IconX className="size-4" aria-hidden />取消</button><button type="button" disabled={saving} onClick={() => void save()} className="flex min-h-10 items-center gap-1.5 rounded-xl bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90 disabled:opacity-50"><IconCheck className="size-4" aria-hidden />{saving ? "保存中…" : "保存"}</button></div></div>;
}

function SourceQuotes({ sources }: { sources: ArchiveSource[] }) {
  if (sources.length === 0) return <p className="archive-event-detail__plain">这条事件暂时没有可展示的原话。</p>;
  return <div className="archive-event-sources">{sources.map((source) => <blockquote key={`${source.sourceType}-${source.id}`}>
    <header>
      <span>{source.sourceType === "Moment" ? "一刻" : "对话"}</span>
      <time dateTime={source.createdAt}>{formatTime(source.createdAt)}</time>
    </header>
    <p>“{source.text || "未填写文字"}”</p>
    {source.locationName || source.locationAddress ? <div className="archive-event-source-location"><IconMapPin aria-hidden />{source.locationName || source.locationAddress}</div> : null}
    {source.attachments.length > 0 ? <div className="archive-event-source-images">{source.attachments.map((image) => <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.aiDescription || image.fileName} thumbnailClassName="size-20 object-cover" />)}</div> : null}
  </blockquote>)}</div>;
}

function DetailSection({ icon, title, aside, action, children }: { icon: React.ReactNode; title: string; aside?: string; action?: React.ReactNode; children: React.ReactNode }) {
  return <section className="archive-event-section">
    <header className="archive-event-section__heading"><h3>{icon}{title}</h3>{action ?? (aside ? <span>{aside}</span> : null)}</header>
    {children}
  </section>;
}
function Empty({ text }: { text: string }) { return <div className="grid h-full min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground"><div><IconArchive className="mx-auto mb-3 size-7" aria-hidden />{text}</div></div>; }
function formatTime(value: string) { return new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)); }
function formatEventDate(value: string) { return new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "long", day: "numeric", weekday: "short", hour: "2-digit", minute: "2-digit" }).format(new Date(value)); }
