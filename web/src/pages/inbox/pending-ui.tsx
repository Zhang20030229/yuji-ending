import { useEffect, useState } from "react";
import { IconCheck, IconInbox, IconMapPin, IconUser, IconX } from "@tabler/icons-react";
import { ApiError } from "@/api/client";
import {
  getPendingOverview,
  resolvePending,
  type ArchiveSource,
  type EntityCard,
  type PendingItem,
} from "@/api/archive";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { useDataRevision } from "@/components/realtime/DataUpdates";

/** 一次拉取待确认项及人物、地点候选。 */
export function usePendingOverview() {
  const [items, setItems] = useState<PendingItem[]>([]);
  const [people, setPeople] = useState<EntityCard[]>([]);
  const [places, setPlaces] = useState<EntityCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [reload, setReload] = useState(0);
  const revision = useDataRevision("archive");

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);
    getPendingOverview(controller.signal)
      .then((overview) => {
        setItems(overview.items);
        setPeople(overview.people);
        setPlaces(overview.places);
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入收件箱。");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [reload, revision]);

  return { items, people, places, loading, error, refresh: () => setReload((value) => value + 1) };
}

/** 待确认列表主体：加载态、空态与逐条处理卡片。 */
export function PendingListBody({ items, loading, error, candidates, onChanged }: {
  items: PendingItem[];
  loading: boolean;
  error?: string;
  candidates: { people: EntityCard[]; places: EntityCard[] };
  onChanged: () => void;
}) {
  if (loading) return <EmptyState text="正在载入待确认项…" />;
  if (error) return <EmptyState text={error} />;
  if (items.length === 0) return <EmptyState text="没有需要确认的人物或地点。" />;
  return <div className="space-y-3">{items.map((item) => (
    <PendingCard key={item.id} item={item} candidates={item.kind === "Person" ? candidates.people : candidates.places} onChanged={onChanged} />
  ))}</div>;
}

/** 单条待确认的搜索关联 / 新建卡片。 */
export function PendingCard({ item, candidates, onChanged }: { item: PendingItem; candidates: EntityCard[]; onChanged: () => void }) {
  const [name, setName] = useState("");
  const [entityId, setEntityId] = useState("");
  const [saving, setSaving] = useState(false);

  async function submit(input: { entityId?: number; newName?: string; ignore?: boolean }) {
    setSaving(true);
    try {
      await resolvePending(item.id, input);
      onChanged();
    } finally {
      setSaving(false);
    }
  }

  return <article className="yuji-pending-card rounded-[20px] border border-border/70 bg-background p-5">
    <div className="flex items-start gap-3">
      <span className="grid size-10 shrink-0 place-items-center rounded-full bg-primary-soft text-primary">
        {item.kind === "Person" ? <IconUser className="size-5" /> : <IconMapPin className="size-5" />}
      </span>
      <div className="min-w-0">
        <h2 className="font-semibold">{item.mention}</h2>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">{item.reason}</p>
        <p className="mt-1 text-xs text-muted-foreground">来自：{item.conversationTitle}</p>
      </div>
    </div>
    <SourceQuotes sources={item.sources} />
    {candidates.length > 0 ? <label className="mt-4 block text-xs font-medium text-muted-foreground">选择已有{item.kind === "Person" ? "人物" : "地点"}
      <select value={entityId} onChange={(event) => setEntityId(event.target.value)} className="mt-2 h-11 w-full rounded-xl bg-secondary px-3 text-sm outline-none">
        <option value="">请选择</option>
        {candidates.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.name}{candidate.aliases.length ? `（${candidate.aliases.join("、")}）` : ""}</option>)}
      </select>
    </label> : null}
    <label className="mt-4 block text-xs font-medium text-muted-foreground">或新建{item.kind === "Person" ? "人物" : "地点"}
      <input value={name} onChange={(event) => setName(event.target.value)} placeholder="输入明确名称" className="mt-2 h-11 w-full rounded-xl bg-secondary px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/35" />
    </label>
    <div className="mt-3 flex flex-wrap justify-end gap-2">
      <button type="button" disabled={saving} onClick={() => void submit({ ignore: true })} className="flex min-h-11 items-center gap-1 rounded-xl px-4 text-sm text-muted-foreground"><IconX className="size-4" aria-hidden />忽略</button>
      {entityId ? <button type="button" disabled={saving} onClick={() => void submit({ entityId: Number(entityId) })} className="flex min-h-11 items-center gap-1 rounded-xl bg-secondary px-4 text-sm font-medium"><IconCheck className="size-4" aria-hidden />关联已有</button> : null}
      <button type="button" disabled={saving || !name.trim()} onClick={() => void submit({ newName: name.trim() })} className="flex min-h-11 items-center gap-1 rounded-xl bg-primary px-4 text-sm font-medium text-primary-foreground disabled:opacity-40"><IconCheck className="size-4" aria-hidden />创建并关联</button>
    </div>
  </article>;
}

export function SourceQuotes({ sources }: { sources: ArchiveSource[] }) {
  return <div className="mt-3 space-y-2">{sources.map((source) => (
    <blockquote key={`${source.sourceType}-${source.id}`} className="rounded-xl bg-secondary px-4 py-3 text-sm leading-6">
      <div className="flex items-center gap-2 text-[11px] text-muted-foreground"><span>{source.sourceType === "Moment" ? "一刻" : "对话"}</span><time>{formatTime(source.createdAt)}</time></div>
      <p className="mt-1">"{source.text || "未填写文字"}"</p>
      {source.locationName || source.locationAddress ? <p className="mt-1 flex items-start gap-1 text-[11px] text-muted-foreground"><IconMapPin className="mt-0.5 size-3 shrink-0" />{[source.locationName, source.locationAddress].filter(Boolean).join(" · ")}</p> : null}
      {source.attachments.length > 0 ? <div className="mt-3 flex gap-2">{source.attachments.map((image) => <ImagePreviewDialog key={image.id} src={image.contentUrl} alt={image.fileName} thumbnailClassName="size-20 object-cover" />)}</div> : null}
    </blockquote>
  ))}</div>;
}

export function EmptyState({ text }: { text: string }) {
  return <div className="grid h-full min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground"><div><IconInbox className="mx-auto mb-3 size-7" aria-hidden />{text}</div></div>;
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
