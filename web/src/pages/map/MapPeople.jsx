import { useEffect, useState } from "react";
import { IconChevronLeft, IconSearch, IconUser, IconUsers } from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { getArchiveDetail, getArchiveList } from "@/api/archive";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";

/**
 * 星图 · 人物：直接消费拾光人物 API（getArchiveList/getArchiveDetail("people")），
 * 展示姓名、关系、关键词、最近总结、对话与事件计数以及封面。
 */
export default function MapPeople() {
  const [query, setQuery] = useState("");
  const [people, setPeople] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true);
      setError("");
      getArchiveList("people", query, controller.signal)
        .then(setPeople)
        .catch((reason) => {
          if (!controller.signal.aborted && reason?.name !== "AbortError") {
            setError(reason?.message ?? "暂时无法载入人物。");
          }
        })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 180);
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [query]);

  useEffect(() => {
    if (!selectedId) { setDetail(undefined); return undefined; }
    const controller = new AbortController();
    getArchiveDetail("people", selectedId, controller.signal)
      .then(setDetail)
      .catch(() => { if (!controller.signal.aborted) setDetail(undefined); });
    return () => controller.abort();
  }, [selectedId]);

  if (selectedId && detail) {
    return <PersonDetail detail={detail} onBack={() => setSelectedId(null)} />;
  }

  return (
    <div className="mx-auto max-w-5xl">
      <label className="yuji-search flex h-11 items-center gap-2 rounded-xl bg-secondary px-3">
        <IconSearch className="size-4 text-muted-foreground" aria-hidden />
        <span className="sr-only">搜索人物</span>
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="搜索姓名或关键词"
          className="min-w-0 flex-1 bg-transparent text-sm outline-none"
        />
      </label>

      {loading ? (
        <div className="mt-5 grid gap-3 sm:grid-cols-2">
          {[1, 2, 3, 4].map((id) => <div key={id} className="h-28 animate-pulse rounded-2xl bg-secondary" />)}
        </div>
      ) : error ? (
        <EmptyState text={error} />
      ) : people.length === 0 ? (
        <EmptyState text="还没有人物记录。当对话里出现重要的人，他们会被整理在这里。" />
      ) : (
        <div className="mt-5 grid gap-3 sm:grid-cols-2">
          {people.map((person) => (
            <button
              key={person.id}
              type="button"
              onClick={() => setSelectedId(person.id)}
              className="yuji-record-card flex min-h-28 gap-3 rounded-2xl border border-border/70 bg-background p-4 text-left transition-colors hover:bg-secondary/60"
            >
              <span className="size-16 shrink-0 overflow-hidden rounded-full bg-secondary">
                {person.coverImageUrl
                  ? <ImagePreviewDialog src={person.coverImageUrl} alt={person.name} thumbnailClassName="size-16 object-cover" interactive={false} />
                  : <span className="grid size-full place-items-center text-muted-foreground"><IconUser className="size-6" aria-hidden /></span>}
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-2">
                  <strong className="truncate text-sm font-semibold">{person.name}</strong>
                  {person.secondary ? <span className="shrink-0 rounded-full bg-secondary px-2 py-0.5 text-[11px] text-muted-foreground">{person.secondary}</span> : null}
                </span>
                {person.keywords?.length > 0 ? (
                  <span className="mt-1.5 flex flex-wrap gap-1">
                    {person.keywords.slice(0, 3).map((keyword) => (
                      <span key={keyword} className="rounded-full bg-secondary px-2 py-0.5 text-[10px] text-muted-foreground">{keyword}</span>
                    ))}
                  </span>
                ) : null}
                <span className="mt-1.5 line-clamp-2 block text-xs leading-5 text-muted-foreground">{person.latestSummary || "等待更多相处记录"}</span>
                <span className="mt-1.5 block text-[11px] text-muted-foreground">{person.conversationCount} 段对话 · {person.eventCount} 个事件</span>
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function PersonDetail({ detail, onBack }) {
  const navigate = useNavigate();
  return (
    <article className="mx-auto max-w-3xl">
      <button type="button" onClick={onBack} className="mb-4 flex min-h-11 items-center gap-1 text-sm text-muted-foreground">
        <IconChevronLeft className="size-4" aria-hidden />返回人物
      </button>
      <div className="flex items-center gap-4">
        <span className="size-20 shrink-0 overflow-hidden rounded-full bg-secondary">
          {detail.coverImageUrl
            ? <ImagePreviewDialog src={detail.coverImageUrl} alt={detail.name} thumbnailClassName="size-20 object-cover" interactive={false} />
            : <span className="grid size-full place-items-center text-muted-foreground"><IconUser className="size-8" aria-hidden /></span>}
        </span>
        <div className="min-w-0">
          <h2 className="text-2xl font-semibold tracking-[-0.03em]">{detail.name}</h2>
          {detail.secondary ? <p className="mt-1 text-sm text-muted-foreground">{detail.secondary}</p> : null}
          {detail.keywords?.length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {detail.keywords.map((keyword) => <span key={keyword} className="rounded-full bg-secondary px-2.5 py-1 text-[11px] text-muted-foreground">{keyword}</span>)}
            </div>
          ) : null}
        </div>
      </div>

      {detail.records?.length > 0 ? (
        <section className="mt-7">
          <h3 className="mb-3 text-xs font-semibold text-muted-foreground">相处记录</h3>
          <div className="space-y-3">
            {detail.records.map((record) => (
              <div key={record.id} className="rounded-2xl border border-border/70 bg-background p-4">
                <p className="text-xs font-medium text-muted-foreground">{record.conversationTitle}</p>
                <p className="mt-1 text-sm leading-6 text-foreground/85">{record.summary}</p>
              </div>
            ))}
          </div>
        </section>
      ) : null}

      {detail.events?.length > 0 ? (
        <section className="mt-7">
          <h3 className="mb-3 text-xs font-semibold text-muted-foreground">相关事件</h3>
          <div className="space-y-3">
            {detail.events.map((event) => (
              <button
                type="button"
                key={event.id}
                className="w-full rounded-2xl bg-secondary/60 p-4 text-left transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
                onClick={() => navigate(`/app/events/${event.id}`)}
              >
                <h4 className="text-sm font-semibold">{event.title}</h4>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{event.summary}</p>
              </button>
            ))}
          </div>
        </section>
      ) : null}
    </article>
  );
}

function EmptyState({ text }) {
  return (
    <div className="grid min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground">
      <div><IconUsers className="mx-auto mb-3 size-7" aria-hidden />{text}</div>
    </div>
  );
}
