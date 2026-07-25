import "@/styles/combined/index.css";
import { useEffect, useMemo, useState } from "react";
import {
  IconChevronRight,
  IconSearch,
} from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "@/api/client";
import { getArchiveList, type EntityCard } from "@/api/archive";
import { getRecognitions, type RecognitionItem } from "@/api/self";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { categoryLabel } from "@/pages/tree/vendor/categories";
import { backwards } from "@/lib/navigation";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { useDataRevision } from "@/components/realtime/DataUpdates";

/** 首页人物 widget 的完整列表目标。 */
export function PeopleListPage() {
  const navigate = useNavigate();
  const revision = useDataRevision("archive");
  const [items, setItems] = useState<EntityCard[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getArchiveList<EntityCard>("people", "", controller.signal)
      .then(setItems)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入人物列表。");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);

  const filtered = useMemo(() => {
    const value = query.trim().toLocaleLowerCase();
    if (!value) return items;
    return items.filter((item) =>
      [item.name, item.secondary, item.latestSummary, ...item.keywords, ...item.aliases]
        .some((text) => text.toLocaleLowerCase().includes(value)));
  }, [items, query]);

  return <ListShell
    title="人物"
    count={items.length}
    query={query}
    setQuery={setQuery}
    placeholder="搜索人物、关系或一段记录"
    onBack={() => backwards(navigate, "/app/home")}
  >
    {loading ? <ListSkeleton /> : null}
    {error ? <p className="data-error" role="alert">{error}</p> : null}
    {!loading && !error && filtered.length === 0 ? <Empty text={query ? "没有匹配的人物。" : "还没有人物记录。"} /> : null}
    {filtered.map((person) => (
      <button type="button" className="home-list-card home-person-list-card" key={person.id} onClick={() => navigate(`/app/people/${person.id}`)}>
        <span className="home-list-avatar">{person.name.slice(0, 1)}</span>
        <span className="home-list-card__content">
          <strong>{person.name}</strong>
          <small>{person.secondary || "关系待确认"} · {person.conversationCount} 段记录 · {person.eventCount} 个事件</small>
          <p>{person.latestSummary || "等待更多相处记录。"}</p>
        </span>
        <IconChevronRight size={18} aria-hidden />
      </button>
    ))}
  </ListShell>;
}

/** 首页地点 widget 的完整列表目标。 */
export function PlacesListPage() {
  const revision = useDataRevision("archive");
  const navigate = useNavigate();
  const [items, setItems] = useState<EntityCard[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getArchiveList<EntityCard>("places", "", controller.signal)
      .then(setItems)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入地点列表。");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);

  const filtered = useMemo(() => {
    const value = query.trim().toLocaleLowerCase();
    if (!value) return items;
    return items.filter((item) =>
      [item.name, item.secondary, item.latestSummary, ...item.aliases]
        .some((text) => text.toLocaleLowerCase().includes(value)));
  }, [items, query]);

  return <ListShell
    title="地点"
    count={items.length}
    query={query}
    setQuery={setQuery}
    placeholder="搜索地点、城市或一段记录"
    onBack={() => backwards(navigate, "/app/home")}
  >
    {loading ? <ListSkeleton /> : null}
    {error ? <p className="data-error" role="alert">{error}</p> : null}
    {!loading && !error && filtered.length === 0 ? <Empty text={query ? "没有匹配的地点。" : "还没有地点记录。"} /> : null}
    {filtered.map((place) => (
      <button type="button" className="home-list-card home-person-list-card" key={place.id} onClick={() => navigate(`/app/places/${place.id}`)}>
        {place.coverImageUrl
          ? <ImagePreviewDialog src={place.coverImageUrl} alt={place.name} thumbnailClassName="home-list-avatar object-cover" interactive={false} />
          : <span className="home-list-avatar">{place.name.slice(0, 1)}</span>}
        <span className="home-list-card__content">
          <strong>{place.name}</strong>
          <small>{place.secondary || "地区待补充"} · {place.conversationCount} 段记录 · {place.eventCount} 个事件</small>
          <p>{place.latestSummary || "等待更多地点记录。"}</p>
        </span>
        <IconChevronRight size={18} aria-hidden />
      </button>
    ))}
  </ListShell>;
}

/** 首页认识 widget 的完整列表目标。 */
export function RecognitionsListPage() {
  const revision = useDataRevision("self");
  const navigate = useNavigate();
  const [items, setItems] = useState<RecognitionItem[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getRecognitions("", "", controller.signal)
      .then(setItems)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入认识列表。");
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);

  const filtered = useMemo(() => {
    const value = query.trim().toLocaleLowerCase();
    if (!value) return items;
    return items.filter((item) =>
      [item.content, item.conversationTitle, categoryLabel(item.category, "zh"), ...item.keywords]
        .some((text) => text.toLocaleLowerCase().includes(value)));
  }, [items, query]);

  return <ListShell
    title="认识"
    count={items.length}
    query={query}
    setQuery={setQuery}
    placeholder="搜索认识、分类或关键词"
    onBack={() => backwards(navigate, "/app/home")}
  >
    {loading ? <ListSkeleton /> : null}
    {error ? <p className="data-error" role="alert">{error}</p> : null}
    {!loading && !error && filtered.length === 0 ? <Empty text={query ? "没有匹配的认识。" : "还没有形成认识。"} /> : null}
    {filtered.map((recognition) => (
      <button type="button" className="home-list-card home-recognition-list-card" key={recognition.id} onClick={() => navigate(`/app/recognitions/${recognition.id}`)}>
        <span className="home-list-category">{categoryLabel(recognition.category, "zh")}</span>
        <span className="home-list-card__content">
          <strong>{recognition.content}</strong>
          <small>{recognition.conversationTitle} · {formatDate(recognition.updatedAt)}</small>
          {recognition.keywords.length > 0 ? <span className="home-list-keywords">{recognition.keywords.slice(0, 3).join(" · ")}</span> : null}
        </span>
        <IconChevronRight size={18} aria-hidden />
      </button>
    ))}
  </ListShell>;
}

function ListShell({ title, count, query, setQuery, placeholder, onBack, children }: {
  title: string;
  count: number;
  query: string;
  setQuery: (value: string) => void;
  placeholder: string;
  onBack: () => void;
  children: React.ReactNode;
}) {
  const { scrolled, onScroll } = useSecondaryHeaderScroll();
  return <section className="home-list-screen">
    <SecondaryPageHeader title={title} subtitle={`共 ${count} 条`} onBack={onBack} backLabel="返回首页" scrolled={scrolled} />
    <div className="home-list-scroll" onScroll={onScroll}>
      <label className="home-list-search">
        <IconSearch size={17} aria-hidden />
        <span className="sr-only">搜索{title}</span>
        <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={placeholder} />
      </label>
      <div className="home-list-results">{children}</div>
    </div>
  </section>;
}

function ListSkeleton() {
  return <div className="home-list-skeleton" aria-label="正在载入列表">{[1, 2, 3].map((item) => <span key={item} />)}</div>;
}

function Empty({ text }: { text: string }) {
  return <p className="data-empty">{text}</p>;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "short", day: "numeric" }).format(new Date(value));
}
