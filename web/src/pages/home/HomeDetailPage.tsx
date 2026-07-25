import "@/styles/combined/index.css";
import { useEffect, useState } from "react";
import {
  IconCalendarEvent,
  IconCheck,
  IconMapPin,
  IconMessageCircle,
  IconPencil,
  IconPhoto,
  IconQuote,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  deleteLifeEvent,
  getArchiveDetail,
  updateEntityRecordSummary,
  type EntityDetail,
} from "@/api/archive";
import { getRecognitions, type RecognitionItem } from "@/api/self";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { categoryLabel } from "@/pages/tree/vendor/categories";
import { backwards } from "@/lib/navigation";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { useDataRevision } from "@/components/realtime/DataUpdates";

export function PersonDetailPage() {
  return <EntityDetailPage view="people" />;
}

export function PlaceDetailPage() {
  return <EntityDetailPage view="places" />;
}

function EntityDetailPage({ view }: { view: "people" | "places" }) {
  const navigate = useNavigate();
  const { itemId } = useParams<{ itemId: string }>();
  const [detail, setDetail] = useState<EntityDetail>();
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  const [deleting, setDeleting] = useState<number>();
  const revision = useDataRevision("archive");
  const isPlace = view === "places";

  useEffect(() => {
    const controller = new AbortController();
    getArchiveDetail<EntityDetail>(view, Number(itemId), controller.signal)
      .then(setDetail)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : `暂时无法载入${isPlace ? "地点" : "人物"}详情。`);
        }
      });
    return () => controller.abort();
  }, [isPlace, itemId, reload, revision, view]);

  async function removeEvent(id: number) {
    if (!window.confirm("删除后，这条事件会从人物、地点和事件列表中移除，但原聊天和图片仍会保留。确定删除吗？")) return;
    setDeleting(id);
    try {
      await deleteLifeEvent(id);
      setReload((value) => value + 1);
    } finally {
      setDeleting(undefined);
    }
  }

  return <DetailShell
    title={detail?.name ?? (isPlace ? "地点" : "人物")}
    subtitle={detail?.secondary || (isPlace ? "地点详情" : "人物详情")}
    onBack={() => backwards(navigate, isPlace ? "/app/places" : "/app/people")}
  >
    {error ? <p className="data-error" role="alert">{error}</p> : null}
    {detail ? <>
      {detail.images.length > 0 ? <DetailSection title="图片" icon={<IconPhoto size={17} aria-hidden />}>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">{detail.images.map((image) => (
          <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.fileName} thumbnailClassName="aspect-square size-full rounded-xl object-cover" />
        ))}</div>
      </DetailSection> : null}
      {isPlace && detail.latitude != null && detail.longitude != null ? <p className="home-detail-coordinate"><IconMapPin size={15} aria-hidden />{detail.latitude.toFixed(6)}, {detail.longitude.toFixed(6)}</p> : null}
      {detail.keywords.length > 0 ? <div className="home-detail-tags">{detail.keywords.map((keyword) => <span key={keyword}>{keyword}</span>)}</div> : null}
      <DetailSection title="最近记录" icon={<IconMessageCircle size={17} aria-hidden />}>
        {detail.records.length === 0 ? <Empty /> : detail.records.map((record) => <article className="home-evidence-card" key={record.id}>
          <h3>{record.conversationTitle}</h3>
          <EditableSummary value={record.summary} onSave={async (summary) => {
            await updateEntityRecordSummary(view, record.id, summary);
            setReload((value) => value + 1);
          }} />
          {record.sources.map((source) => <blockquote key={`${source.sourceType}-${source.id}`}>
            <IconQuote size={15} aria-hidden />{source.text}
            {source.attachments.length > 0 ? <span className="mt-3 flex flex-wrap gap-2">{source.attachments.map((image) => (
              <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.fileName} thumbnailClassName="size-20 rounded-lg object-cover" />
            ))}</span> : null}
          </blockquote>)}
        </article>)}
      </DetailSection>
      <DetailSection title="相关事件" icon={<IconCalendarEvent size={17} aria-hidden />}>
        {detail.events.length === 0 ? <Empty /> : detail.events.map((event) => <div className="home-related-event flex items-start gap-2" key={event.id}>
          <button type="button" className="min-w-0 flex-1 text-left" onClick={() => navigate(`/app/events/${event.id}`)}>
            <strong>{event.title}</strong><span>{event.summary}</span>
          </button>
          <button type="button" disabled={deleting === event.id} onClick={() => void removeEvent(event.id)} className="grid size-10 shrink-0 place-items-center rounded-full text-muted-foreground hover:bg-secondary hover:text-destructive" aria-label={`删除事件“${event.title}”`}>
            <IconTrash size={16} aria-hidden />
          </button>
        </div>)}
      </DetailSection>
    </> : !error ? <p className="data-empty">正在载入…</p> : null}
  </DetailShell>;
}

function EditableSummary({ value, onSave }: { value: string; onSave: (value: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => { if (!editing) setDraft(value); }, [editing, value]);

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

  if (!editing) return <div className="mt-2 flex items-start gap-2"><p className="min-w-0 flex-1">{value}</p><button type="button" onClick={() => setEditing(true)} className="grid size-9 shrink-0 place-items-center rounded-full text-muted-foreground hover:bg-secondary" aria-label="编辑 AI 总结"><IconPencil size={16} aria-hidden /></button></div>;
  return <div className="mt-2">
    <textarea value={draft} onChange={(event) => setDraft(event.target.value)} maxLength={2000} rows={3} className="w-full resize-y rounded-xl border border-border bg-background px-3 py-2 text-sm leading-6 outline-none focus-visible:ring-2 focus-visible:ring-ring/35" aria-label="编辑 AI 总结" />
    {error ? <p className="mt-1 text-xs text-destructive" role="alert">{error}</p> : null}
    <div className="mt-2 flex justify-end gap-2">
      <button type="button" disabled={saving} onClick={() => setEditing(false)} className="flex min-h-10 items-center gap-1 rounded-xl px-3 text-xs text-muted-foreground"><IconX size={15} aria-hidden />取消</button>
      <button type="button" disabled={saving} onClick={() => void save()} className="flex min-h-10 items-center gap-1 rounded-xl bg-primary px-3 text-xs font-medium text-primary-foreground disabled:opacity-50"><IconCheck size={15} aria-hidden />保存</button>
    </div>
  </div>;
}

export function RecognitionDetailPage() {
  const navigate = useNavigate();
  const revision = useDataRevision("self");
  const { itemId } = useParams<{ itemId: string }>();
  const [detail, setDetail] = useState<RecognitionItem>();
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getRecognitions("", "", controller.signal)
      .then((items) => {
        // API 为避免浏览器丢失 64 位精度，会把标识符序列化为字符串。
        const found = items.find((item) => String(item.id) === itemId);
        if (!found) throw new Error("没有找到这条认识。");
        setDetail(found);
      })
      .catch((reason: unknown) => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "暂时无法载入认识详情。"); });
    return () => controller.abort();
  }, [itemId, revision]);

  return <DetailShell title="一条认识" subtitle={detail ? categoryLabel(detail.category, "zh") : "认识详情"} onBack={() => backwards(navigate, "/app/recognitions")}>
    {error ? <p className="data-error" role="alert">{error}</p> : null}
    {detail ? <>
      <article className="home-recognition-hero">
        <span>{categoryLabel(detail.category, "zh")}</span>
        <h2>{detail.content}</h2>
        <time dateTime={detail.updatedAt}>{formatDateTime(detail.updatedAt)}更新</time>
      </article>
      {detail.keywords.length > 0 ? <div className="home-detail-tags">{detail.keywords.map((keyword) => <span key={keyword}>{keyword}</span>)}</div> : null}
      <DetailSection title="来自这段对话" icon={<IconMessageCircle size={17} aria-hidden />}>
        <article className="home-evidence-card">
          <h3>{detail.conversationTitle}</h3>
          {detail.sources.length === 0 ? <Empty /> : detail.sources.map((source) => <blockquote key={`${source.sourceType}-${source.messageId}`}><IconQuote size={15} aria-hidden />{source.text}</blockquote>)}
        </article>
      </DetailSection>
    </> : !error ? <p className="data-empty">正在载入…</p> : null}
  </DetailShell>;
}

function DetailShell({ title, subtitle, onBack, children }: { title: string; subtitle: string; onBack: () => void; children: React.ReactNode }) {
  const { scrolled, onScroll } = useSecondaryHeaderScroll();
  return <section className="home-detail-screen">
    <SecondaryPageHeader title={title} subtitle={subtitle} onBack={onBack} scrolled={scrolled} />
    <div className="home-detail-scroll" onScroll={onScroll}>{children}</div>
  </section>;
}

function DetailSection({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return <section className="home-detail-section"><h2>{icon}{title}</h2>{children}</section>;
}

function Empty() {
  return <p className="data-empty">还没有相关记录。</p>;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "long", day: "numeric" }).format(new Date(value));
}
