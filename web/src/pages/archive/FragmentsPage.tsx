import { useEffect, useMemo, useState } from "react";
import { IconArchive, IconChevronRight, IconMapPin, IconPhoto, IconRefresh, IconSearch, IconTrash } from "@tabler/icons-react";
import { useNavigate, useParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import {
  getArchiveDetail,
  getFragmentsOverview,
  type ArchiveSource,
  type FragmentDetail,
  type FragmentItem,
} from "@/api/archive";
import { deleteMoment, retryMoment, type MomentItem } from "@/api/moments";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { backwards } from "@/lib/navigation";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import AnalysisBanner from "@/components/analysis/AnalysisBanner";

/** 「片段」列表与详情：从原拾光页拆出，仅展示片段视图，含正在整理中的「一刻」。 */
export default function FragmentsPage() {
  const { itemId } = useParams<{ itemId?: string }>();
  const navigate = useNavigate();
  const selectedId = itemId ? Number(itemId) : undefined;
  const [query, setQuery] = useState("");
  const [allItems, setAllItems] = useState<FragmentItem[]>([]);
  const [moments, setMoments] = useState<MomentItem[]>([]);
  const [detail, setDetail] = useState<FragmentDetail>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [reload, setReload] = useState(0);
  const revision = useDataRevision("archive", "moments");
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  useEffect(() => {
    const created = () => setReload((value) => value + 1);
    window.addEventListener("echora:moment-created", created);
    return () => window.removeEventListener("echora:moment-created", created);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);
    getFragmentsOverview(controller.signal)
        .then((overview) => {
          setAllItems(overview.fragments);
          setMoments(overview.moments);
        })
        .catch((reason: unknown) => {
          if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入拾光。");
        })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload, revision]);

  const items = useMemo(() => {
    const value = query.trim().toLocaleLowerCase();
    return value
      ? allItems.filter((item) => `${item.title} ${item.summary}`.toLocaleLowerCase().includes(value))
      : allItems;
  }, [allItems, query]);

  useEffect(() => {
    if (!selectedId) { setDetail(undefined); return; }
    const controller = new AbortController();
    getArchiveDetail<FragmentDetail>("fragments", selectedId, controller.signal)
      .then(setDetail)
      .catch(() => { if (!controller.signal.aborted) setDetail(undefined); });
    return () => controller.abort();
  }, [selectedId, reload, revision]);

  const selected = selectedId ?? items[0]?.id;
  useEffect(() => {
    if (!selectedId && selected && window.matchMedia("(min-width: 1280px)").matches)
      navigate(`/app/fragments/${selected}`, { replace: true });
  }, [navigate, selected, selectedId]);

  return <section className="yuji-archive flex h-full min-h-0 flex-col bg-background">
    <SecondaryPageHeader
      title={selectedId ? "片段详情" : "拾光片段"}
      subtitle={selectedId ? detail?.title : `${items.length} 个片段`}
      onBack={() => backwards(navigate, selectedId ? "/app/fragments" : "/app/home")}
      backLabel={selectedId ? "返回片段列表" : "返回首页"}
      scrolled={scrolled}
    />
    <AnalysisBanner scope="archive" />
    <div className="mx-auto min-h-0 w-full max-w-[1180px] flex-1 overflow-hidden pt-4 md:px-7 md:pb-6" onScrollCapture={onScroll}>
      <div className="yuji-master-detail grid h-full min-h-0 xl:grid-cols-[380px_minmax(0,1fr)] xl:overflow-hidden xl:rounded-[22px] xl:border xl:border-border/70">
        <div className={`yuji-list-pane ${selectedId ? "hidden xl:flex" : "flex"} min-h-0 flex-col bg-background`}>
          <SearchBox value={query} onChange={setQuery} />
          <FragmentList items={items} moments={moments} loading={loading} error={error} selectedId={selectedId} onSelect={(id) => navigate(`/app/fragments/${id}`)} onChanged={() => setReload((value) => value + 1)} />
        </div>
        <div className={`yuji-detail-pane ${selectedId ? "block" : "hidden xl:block"} min-h-0 overflow-y-auto border-l border-border/65 bg-canvas/45`}>
          {detail ? <FragmentDetailView
            detail={detail}
            onOpenEvent={(id) => navigate(`/app/events/${id}`)}
            onOpenPerson={(id) => navigate(`/app/people/${id}`)}
            onOpenPlace={(id) => navigate(`/app/places/${id}`)}
          /> : <Empty text="选择一张卡片查看原话与关联。" />}
        </div>
      </div>
    </div>
  </section>;
}

function SearchBox({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return <div className="shrink-0 p-4"><label className="yuji-search flex h-11 items-center gap-2 rounded-xl bg-secondary px-3"><IconSearch className="size-4 text-muted-foreground" aria-hidden /><span className="sr-only">搜索</span><input type="search" value={value} onChange={(event) => onChange(event.target.value)} placeholder="搜索片段标题或一段线索" className="min-w-0 flex-1 bg-transparent text-sm outline-none" /></label></div>;
}

function FragmentList({ items, moments, loading, error, selectedId, onSelect, onChanged }: { items: FragmentItem[]; moments: MomentItem[]; loading: boolean; error?: string; selectedId?: number; onSelect: (id: number) => void; onChanged: () => void }) {
  if (loading) return <div className="space-y-2 px-4">{[1, 2, 3].map((id) => <div key={id} className="h-24 animate-pulse rounded-xl bg-secondary" />)}</div>;
  if (error) return <Empty text={error} />;
  if (items.length === 0 && moments.length === 0) return <Empty text="还没有片段。对话完成后会在后台自动整理。" />;
  return <div className="yuji-card-list min-h-0 flex-1 overflow-y-auto px-2 pb-6">
    {moments.map((moment) => <MomentCard key={moment.id} moment={moment} onChanged={onChanged} />)}
    {items.map((item) => (
      <button key={item.id} type="button" onClick={() => onSelect(item.id)} className={`yuji-record-card flex min-h-24 w-full gap-3 rounded-2xl p-3 text-left transition-colors ${selectedId === item.id ? "bg-primary-soft/80" : "hover:bg-secondary/70"}`}>
        <span className="size-16 shrink-0 overflow-hidden rounded-xl bg-secondary">{item.coverImageUrl ? <ImagePreviewDialog src={item.coverImageUrl} alt={item.title} thumbnailClassName="size-16 object-cover" interactive={false} /> : <span className="grid size-full place-items-center text-muted-foreground"><IconPhoto className="size-5" aria-hidden /></span>}</span>
        <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{item.title}</span><span className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">{item.summary || "等待更多记录"}</span><span className="mt-1.5 block text-[11px] text-muted-foreground">{item.sourceCount} 条原话 · {item.eventCount} 个事件</span></span>
      </button>
    ))}
  </div>;
}

function MomentCard({ moment, onChanged }: { moment: MomentItem; onChanged: () => void }) {
  const [busy, setBusy] = useState(false);
  async function retry() { setBusy(true); try { await retryMoment(moment.id); onChanged(); } finally { setBusy(false); } }
  async function remove() {
    if (!window.confirm("删除后，这一刻及仅由它生成的记录都会被移除。确定删除吗？")) return;
    setBusy(true); try { await deleteMoment(moment.id); onChanged(); } finally { setBusy(false); }
  }
  const status = moment.status === "Failed" ? "整理失败" : moment.status === "Succeeded" ? "一刻" : "正在整理";
  return <article className="yuji-record-card group flex min-h-28 gap-3 rounded-2xl p-3 hover:bg-secondary/70">
    <ImagePreviewDialog src={moment.imageUrl} alt={moment.title || "一刻照片"} thumbnailClassName="size-20 rounded-xl object-cover" />
    <div className="min-w-0 flex-1"><div className="flex items-center gap-2"><span className="rounded-full bg-primary-soft px-2 py-0.5 text-[10px] font-semibold text-primary">{status}</span><time className="text-[10px] text-muted-foreground">{formatTime(moment.publishedAt)}</time></div><h3 className="mt-1 truncate text-sm font-semibold">{moment.title || "刚刚记录的一刻"}</h3><p className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">{moment.summary || moment.text || "照片已经保存，正在为你整理。"}</p>{moment.locationName || moment.locationAddress ? <p className="mt-1 truncate text-[10px] text-muted-foreground">{[moment.locationName, moment.locationAddress].filter(Boolean).join(" · ")}</p> : null}</div>
    <div className="flex shrink-0 flex-col">{moment.status === "Failed" ? <button type="button" disabled={busy} onClick={() => void retry()} className="grid size-9 place-items-center rounded-full text-muted-foreground hover:bg-background" aria-label="重试整理"><IconRefresh className="size-4" /></button> : null}<button type="button" disabled={busy} onClick={() => void remove()} className="grid size-9 place-items-center rounded-full text-muted-foreground hover:bg-background hover:text-destructive" aria-label="删除一刻"><IconTrash className="size-4" /></button></div>
  </article>;
}

function FragmentDetailView({
  detail,
  onOpenEvent,
  onOpenPerson,
  onOpenPlace,
}: {
  detail: FragmentDetail;
  onOpenEvent: (id: number) => void;
  onOpenPerson: (id: number) => void;
  onOpenPlace: (id: number) => void;
}) {
  const images = detail.sources.flatMap((source) => source.attachments);
  return <article className="yuji-detail-content mx-auto max-w-3xl px-5 pb-10 pt-4 md:px-8 md:pt-7">
    <h2 className="text-2xl font-semibold tracking-[-0.03em]">{detail.title}</h2>
    <p className="mt-4 text-[15px] leading-7 text-foreground/85">{detail.summary}</p>
    {detail.latitude != null && detail.longitude != null ? <p className="mt-2 flex items-center gap-1 text-xs text-muted-foreground"><IconMapPin className="size-3.5" aria-hidden />{detail.latitude.toFixed(6)}, {detail.longitude.toFixed(6)}</p> : null}
    {images.length > 0 ? <Section title="图片"><div className="grid grid-cols-2 gap-2 sm:grid-cols-3">{images.map((image) => <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.fileName} thumbnailClassName="aspect-square size-full object-cover" />)}</div></Section> : null}
    {detail.people.length > 0 ? <Section title="相关人物"><div className="archive-event-people">{detail.people.map((person) => (
      <button type="button" className="archive-event-person" key={person.id} onClick={() => onOpenPerson(person.id)} aria-label={`查看${person.name}的详情`}>
        {person.name.slice(0, 1)}<strong>{person.name}</strong>
      </button>
    ))}</div></Section> : null}
    {detail.places.length > 0 ? <Section title="相关地点"><div className="flex flex-wrap gap-2">{detail.places.map((place) => (
      <RelatedEntityButton key={place.id} icon={<IconMapPin aria-hidden />} name={place.name} onClick={() => onOpenPlace(place.id)} />
    ))}</div></Section> : null}
    <Section title="原话"><SourceQuotes sources={detail.sources} /></Section>
    {detail.events.length > 0 ? <Section title="相关事件">{detail.events.map((event) => (
      <button
        type="button"
        key={event.id}
        className="mb-3 w-full rounded-xl bg-background p-4 text-left transition-colors hover:bg-secondary/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
        onClick={() => onOpenEvent(event.id)}
      >
        <h4 className="text-sm font-semibold">{event.title}</h4>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{event.summary}</p>
      </button>
    ))}</Section> : null}
  </article>;
}

function RelatedEntityButton({ icon, name, onClick }: { icon: React.ReactNode; name: string; onClick: () => void }) {
  return <button
    type="button"
    onClick={onClick}
    className="inline-flex min-h-10 items-center gap-2 rounded-xl bg-background px-3 text-sm font-medium transition-colors hover:bg-secondary/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
    aria-label={`查看${name}的详情`}
  >
    <span className="size-4 text-muted-foreground [&>svg]:size-4">{icon}</span>
    <span>{name}</span>
    <IconChevronRight className="size-3.5 text-muted-foreground" aria-hidden />
  </button>;
}

function SourceQuotes({ sources }: { sources: ArchiveSource[] }) {
  return <div className="mt-3 space-y-2">{sources.map((source) => <blockquote key={`${source.sourceType}-${source.id}`} className="rounded-xl bg-background px-4 py-3 text-sm leading-6"><div className="flex items-center gap-2 text-[11px] text-muted-foreground"><span>{source.sourceType === "Moment" ? "一刻" : "对话"}</span><time>{formatTime(source.createdAt)}</time></div><p className="mt-1">"{source.text || "未填写文字"}"</p>{source.locationName || source.locationAddress ? <p className="mt-1 flex items-start gap-1 text-[11px] text-muted-foreground"><IconMapPin className="mt-0.5 size-3 shrink-0" />{[source.locationName, source.locationAddress].filter(Boolean).join(" · ")}</p> : null}{source.attachments.length > 0 ? <div className="mt-3 flex gap-2">{source.attachments.map((image) => <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.fileName} thumbnailClassName="size-20 object-cover" />)}</div> : null}</blockquote>)}</div>;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) { return <section className="yuji-detail-section mt-7"><h3 className="mb-3 text-xs font-semibold text-muted-foreground">{title}</h3>{children}</section>; }
function Empty({ text }: { text: string }) { return <div className="grid h-full min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground"><div><IconArchive className="mx-auto mb-3 size-7" aria-hidden />{text}</div></div>; }
function formatTime(value: string) { return new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)); }
