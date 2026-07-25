import { useEffect, useState } from "react";
import { IconSearch } from "@tabler/icons-react";
import { useNavigate, useParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import {
  getEmotionDay,
  getEmotionMonth,
  getRecognitions,
  type EmotionDay,
  type EmotionMonth,
  type RecognitionItem,
} from "@/api/self";
import AnalysisBanner from "@/components/analysis/AnalysisBanner";
import { DatePicker, MonthPicker } from "@/components/ui/date-picker";
import { EmotionDayView, EmotionMonthView, Empty, Quote, Skeleton } from "./emotion-views";
import ReportsView from "./ReportsView";

type SelfView = "recognitions" | "emotions-day" | "emotions-month" | "reports";

const categories = [
  ["", "全部"], ["Identity", "身份"], ["Trait", "特质"], ["Value", "价值"], ["Preference", "偏好"],
  ["Habit", "习惯"], ["Ability", "能力"], ["Need", "需求"], ["Goal", "目标"], ["Relationship", "关系"],
] as const;

/** 0.3 遇己：认识按会话保留依据，情绪只提供日/月两种确定性视图。 */
export default function SelfPage() {
  const { view: routeView, itemId } = useParams<{ view?: string; itemId?: string }>();
  const navigate = useNavigate();
  const view = isView(routeView) ? routeView : "recognitions";
  const emotionSelected = view === "emotions-day" || view === "emotions-month";
  return <section className="yuji-self flex h-full min-h-0 flex-col bg-background">
    <header className="yuji-page-header mx-auto grid w-full max-w-[1180px] shrink-0 grid-cols-1 px-4 pt-5 md:grid-cols-[1fr_auto_1fr] md:items-center md:px-7">
      <h1 className="text-xl font-semibold tracking-[-0.03em]">遇己</h1>
      <div role="tablist" aria-label="遇己分类" className="yuji-segmented mt-4 grid grid-cols-3 rounded-xl bg-secondary p-1 md:col-start-2 md:mt-0 md:w-[390px]">
        <button type="button" role="tab" aria-selected={view === "recognitions"} onClick={() => navigate("/app/self/recognitions")} className={tabClass(view === "recognitions")}>认识</button>
        <button type="button" role="tab" aria-selected={emotionSelected} onClick={() => navigate("/app/self/emotions-day")} className={tabClass(emotionSelected)}>情绪</button>
        <button type="button" role="tab" aria-selected={view === "reports"} onClick={() => navigate("/app/self/reports")} className={tabClass(view === "reports")}>报告</button>
      </div>
    </header>
    <AnalysisBanner scope="self" />
    <div className="yuji-self-content mx-auto min-h-0 w-full max-w-[1180px] flex-1 overflow-y-auto px-4 pb-8 pt-4 md:px-7 md:pt-5">
      {view === "recognitions"
        ? <RecognitionView />
        : emotionSelected
          ? <EmotionView mode={view === "emotions-month" ? "month" : "day"} onMode={(mode) => navigate(`/app/self/emotions-${mode}`)} />
          : <ReportsView reportId={itemId} />}
    </div>
  </section>;
}

function RecognitionView() {
  const [category, setCategory] = useState("");
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<RecognitionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true); setError(undefined);
      getRecognitions(category, query, controller.signal).then(setItems).catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入认识。");
      }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 180);
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [category, query]);
  return <div className="mx-auto max-w-5xl">
    <div className="flex flex-col gap-3 md:flex-row md:items-center"><label className="yuji-search flex h-11 flex-1 items-center gap-2 rounded-xl bg-secondary px-3"><IconSearch className="size-4 text-muted-foreground" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索认识或关键词" className="min-w-0 flex-1 bg-transparent text-sm outline-none" /></label><div className="flex gap-2 overflow-x-auto pb-1">{categories.map(([id, label]) => <button key={id || "all"} type="button" onClick={() => setCategory(id)} className={`min-h-10 shrink-0 rounded-full px-4 text-xs font-medium ${category === id ? "bg-primary-soft text-primary" : "bg-secondary text-muted-foreground"}`}>{label}</button>)}</div></div>
    {loading ? <Skeleton /> : error ? <Empty text={error} /> : items.length === 0 ? <Empty text="这段时间还没有足够依据形成认识。" /> : <div className="mt-5 grid gap-3 lg:grid-cols-2">{items.map((item) => <article key={item.id} className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5"><div className="flex items-center justify-between gap-3"><span className="rounded-full bg-primary-soft px-3 py-1 text-xs font-medium text-primary">{categoryLabel(item.category)}</span><time className="text-[11px] text-muted-foreground">{formatDate(item.updatedAt)}</time></div><h2 className="mt-4 text-[15px] font-semibold leading-6">{item.content}</h2>{item.keywords.length > 0 ? <div className="mt-3 flex flex-wrap gap-1.5">{item.keywords.map((keyword) => <span key={keyword} className="rounded-full bg-secondary px-2.5 py-1 text-[11px] text-muted-foreground">{keyword}</span>)}</div> : null}<div className="mt-4 border-t border-border/60 pt-3"><p className="text-xs font-medium text-muted-foreground">{item.conversationTitle}</p>{item.sources.map((source) => <Quote key={`${source.sourceType}-${source.messageId}`} source={source} />)}</div></article>)}</div>}
  </div>;
}

function EmotionView({ mode, onMode }: { mode: "day" | "month"; onMode: (mode: "day" | "month") => void }) {
  const today = new Date();
  const [date, setDate] = useState(toDateInput(today));
  const [month, setMonth] = useState(toDateInput(today).slice(0, 7));
  const [dayData, setDayData] = useState<EmotionDay>();
  const [monthData, setMonthData] = useState<EmotionMonth>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(undefined);
    const request = mode === "day" ? getEmotionDay(date, controller.signal).then(setDayData) : getEmotionMonth(month, controller.signal).then(setMonthData);
    request.catch((reason: unknown) => { if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入情绪。") }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [date, mode, month]);
  return <div className="mx-auto max-w-5xl">
    <div className="flex items-center justify-between gap-4"><div className="grid grid-cols-2 rounded-xl bg-secondary p-1"><button type="button" onClick={() => onMode("day")} className={smallTabClass(mode === "day")}>日</button><button type="button" onClick={() => onMode("month")} className={smallTabClass(mode === "month")}>月</button></div><div className="w-[190px]">{mode === "day" ? <DatePicker aria-label="选择日期" value={date} max={toDateInput(today)} onChange={setDate} className="h-10 bg-secondary" /> : <MonthPicker aria-label="选择月份" value={month} max={toDateInput(today).slice(0, 7)} onChange={setMonth} className="h-10 bg-secondary" />}</div></div>
    {loading ? <Skeleton /> : error ? <Empty text={error} /> : mode === "day" && dayData ? <EmotionDayView data={dayData} /> : mode === "month" && monthData ? <EmotionMonthView data={monthData} selectedMonth={month} onSelectDay={(value) => { setDate(value); onMode("day"); }} /> : null}
  </div>;
}

function tabClass(active: boolean) { return `min-h-10 rounded-[9px] text-xs font-medium ${active ? "bg-background text-foreground shadow-sm" : "text-muted-foreground"}`; }
function smallTabClass(active: boolean) { return `min-h-9 rounded-[9px] px-5 text-xs font-medium ${active ? "bg-background text-foreground shadow-sm" : "text-muted-foreground"}`; }
function isView(value?: string): value is SelfView { return value === "recognitions" || value === "emotions-day" || value === "emotions-month" || value === "reports"; }
function categoryLabel(value: string) { return categories.find(([id]) => id === value)?.[1] ?? value; }
function formatDate(value: string) { return new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium" }).format(new Date(value)); }
function toDateInput(date: Date) { const offset = date.getTimezoneOffset(); return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 10); }
