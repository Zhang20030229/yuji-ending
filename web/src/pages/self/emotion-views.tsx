import { useEffect, useState } from "react";
import {
  IconCheck,
  IconMapPin,
  IconMoodSmile,
  IconPencil,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import {
  deleteCbtObservation,
  updateCbtObservation,
  type CbtObservation,
  type EmotionDay,
  type EmotionMonth,
  type SelfQuote,
} from "@/api/self";
import { ApiError } from "@/api/client";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";

const emotionColors: Record<string, string> = {
  愉悦: "#F3B94F", 期待: "#8B73E8", 平静: "#68A7A1", 亲近: "#E889A5", 惊讶: "#D59A55",
  焦虑: "#E07A5F", 恐惧: "#8466A8", 愤怒: "#D95B58", 低落: "#6279A5", 厌恶: "#71906B", 自责: "#9A6D7B",
};

/** 情绪日视图：当日总结、时间轴、家族分布与逐条记录。供遇己页与未来的时间线复用。 */
export function EmotionDayView({ data }: { data: EmotionDay }) {
  const [observations, setObservations] = useState(data.cbtObservations);
  useEffect(() => setObservations(data.cbtObservations), [data]);

  if (data.items.length === 0 && observations.length === 0)
    return <Empty text="这一天没有明确的情绪记录。" />;

  return <div className="mt-5 space-y-4">
    {data.items.length > 0 && <section className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5">
      <p className="text-xs font-medium text-muted-foreground">当日总结</p>
      <h2 className="mt-2 text-lg font-semibold leading-7">{data.summary}</h2>
      <p className="mt-2 text-xs text-muted-foreground">来自 {data.conversationCount} 次会话 · {data.sourceMessageCount} 条原话</p>
      <EmotionTimeline items={data.items} />
    </section>}
    {data.families.length > 0 && <section>
      <h2 className="mb-3 text-sm font-semibold">当天出现过的情绪</h2>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{data.families.map((family) => <div key={family.family} className="rounded-2xl bg-secondary/70 p-4"><div className="flex items-center gap-2"><span className="size-3 rounded-full" style={{ backgroundColor: colorOf(family.family) }} /><strong className="text-sm">{family.family}</strong></div><p className="mt-2 text-xs text-muted-foreground">{family.momentCount} 个时刻 · 最高强度 {family.peakIntensity}</p><p className="mt-1 text-xs text-muted-foreground">{family.subtypes.join("、")}</p></div>)}</div>
    </section>}
    {observations.length > 0 && <section>
      <h2 className="mb-1 text-sm font-semibold">CBT 自我观察</h2>
      <p className="mb-3 text-xs text-muted-foreground">从原话中梳理情境、想法、感受、行为与结果。</p>
      <div className="space-y-3">{observations.map((observation) => <CbtObservationCard
        key={observation.id}
        item={observation}
        emotionText={emotionsForSource(data, observation)}
        onChange={(next) => setObservations((current) => next ? current.map((item) => item.id === next.id ? next : item) : current.filter((item) => item.id !== observation.id))}
      />)}</div>
    </section>}
    {data.items.length > 0 && <section>
      <h2 className="mb-3 text-sm font-semibold">情绪记录</h2>
      <div className="space-y-3">{data.items.map((item) => <article key={item.id} className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5"><div className="flex items-center gap-2"><span className="size-3 rounded-full" style={{ backgroundColor: colorOf(item.family) }} /><strong className="text-sm">{item.family} · {item.subtype}</strong><span className="ml-auto text-xs text-muted-foreground">{new Date(item.occurredAt).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })} · 强度 {item.intensity}</span></div><p className="mt-3 text-sm leading-6 text-foreground/85">{item.summary}</p>{item.sources.map((source) => <Quote key={`${source.sourceType}-${source.messageId}`} source={source} />)}<p className="mt-2 text-[11px] text-muted-foreground">{item.conversationTitle}</p></article>)}</div>
    </section>}
  </div>;
}

/** 情绪月视图：本月总结、日历、家族统计、单类趋势与代表原话。供遇己页与未来的时间线复用。 */
export function EmotionMonthView({ data, selectedMonth, onSelectDay }: { data: EmotionMonth; selectedMonth: string; onSelectDay: (date: string) => void }) {
  if (data.days.length === 0) return <Empty text="这个月没有明确的情绪记录。" />;
  const calendar = buildCalendar(selectedMonth);
  const byDate = new Map(data.days.map((day) => [day.date, day]));
  const defaultFamily = data.families[0];
  return <div className="mt-5 space-y-4"><section className="rounded-2xl border border-border/70 bg-background p-5"><p className="text-xs font-medium text-muted-foreground">本月总结</p><h2 className="mt-2 text-lg font-semibold leading-7">{data.summary}</h2>{data.cbtObservationCount > 0 ? <p className="mt-3 text-xs font-medium text-primary">CBT 自我观察 · 共 {data.cbtObservationCount} 次 · 覆盖 {data.cbtCoveredDays} 天</p> : null}</section><section className="rounded-2xl border border-border/70 bg-background p-4 sm:p-5"><div className="grid grid-cols-7 text-center text-[11px] text-muted-foreground">{"一二三四五六日".split("").map((day) => <span key={day} className="py-2">周{day}</span>)}</div><div className="grid grid-cols-7">{calendar.map((date, index) => { const value = date ? toDateInput(date) : ""; const item = value ? byDate.get(value) : undefined; return <button key={`${value}-${index}`} type="button" disabled={!item} onClick={() => item && onSelectDay(value)} className="aspect-square min-h-12 border-t border-border/50 p-1 text-left text-xs disabled:text-muted-foreground/35"><span>{date?.getDate()}</span>{item ? <span className="mt-1 flex flex-wrap gap-1">{item.families.slice(0, 3).map((family) => <span key={family.family} className="rounded-full" title={family.family} style={{ width: 5 + family.peakIntensity * 2, height: 5 + family.peakIntensity * 2, backgroundColor: colorOf(family.family) }} />)}{item.families.length > 3 ? <small className="text-[9px]">+{item.families.length - 3}</small> : null}</span> : null}</button>; })}</div></section><section><h2 className="mb-3 text-sm font-semibold">本月出现的情绪</h2><div className="grid gap-3 sm:grid-cols-2">{data.families.map((family) => <div key={family.family} className="rounded-2xl bg-secondary/70 p-4"><div className="flex items-center gap-2"><span className="size-3 rounded-full" style={{ backgroundColor: colorOf(family.family) }} /><strong className="text-sm">{family.family}</strong></div><p className="mt-2 text-xs text-muted-foreground">出现 {family.activeDays} 天 · 平均日峰值 {family.averageDailyPeak}</p><p className="mt-1 text-xs text-muted-foreground">{family.subtypes.join("、")}</p></div>)}</div></section>{defaultFamily ? <section className="rounded-2xl border border-border/70 bg-background p-5"><h2 className="text-sm font-semibold">{defaultFamily.family} · 单类趋势</h2><Trend family={defaultFamily} month={selectedMonth} /></section> : null}<section><h2 className="mb-3 text-sm font-semibold">代表原话</h2><div className="space-y-2">{data.representativeQuotes.map((item, index) => <button key={`${item.date}-${index}`} type="button" onClick={() => onSelectDay(item.date)} className="w-full rounded-2xl bg-secondary/70 p-4 text-left"><span className="text-xs font-medium" style={{ color: colorOf(item.family) }}>{item.date} · {item.family}/{item.subtype}</span><blockquote className="mt-2 text-sm leading-6">“{item.text}”</blockquote></button>)}</div></section></div>;
}

function CbtObservationCard({ item, emotionText, onChange }: { item: CbtObservation; emotionText: string; onChange: (item?: CbtObservation) => void }) {
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [draft, setDraft] = useState(item);
  useEffect(() => setDraft(item), [item]);
  const fields = [
    ["发生了什么", item.situation],
    ["当时想到什么", item.automaticThought],
    ["情绪", emotionText],
    ["身体感受", item.bodySensation],
    ["做了什么", item.behavior],
    ["直接结果", item.immediateOutcome],
  ].filter((field): field is [string, string] => Boolean(field[1]));

  async function save() {
    setBusy(true);
    setError("");
    try {
      await updateCbtObservation(item.id, draft);
      onChange({ ...draft, id: item.id, occurredAt: item.occurredAt, sources: item.sources });
      setEditing(false);
    } catch (reason) {
      setError(reason instanceof ApiError || reason instanceof Error ? reason.message : "暂时无法保存修改。");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true);
    setError("");
    try {
      await deleteCbtObservation(item.id);
      setDeleting(false);
      onChange(undefined);
    } catch (reason) {
      setError(reason instanceof ApiError || reason instanceof Error ? reason.message : "暂时无法删除。");
      setDeleting(false);
    } finally {
      setBusy(false);
    }
  }

  return <article className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5">
    <div className="flex items-center gap-2">
      <strong className="text-sm">一次情境与反应</strong>
      <time className="ml-auto text-xs text-muted-foreground">{new Date(item.occurredAt).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })}</time>
      <button type="button" onClick={() => setEditing(true)} className="grid size-9 place-items-center rounded-full text-muted-foreground hover:bg-secondary" aria-label="编辑情境与反应"><IconPencil className="size-4" aria-hidden /></button>
      <button type="button" onClick={() => setDeleting(true)} className="grid size-9 place-items-center rounded-full text-muted-foreground hover:bg-secondary hover:text-destructive" aria-label="删除情境与反应"><IconTrash className="size-4" aria-hidden /></button>
    </div>
    {editing ? <div className="mt-4 space-y-3">
      {([
        ["发生了什么", "situation"],
        ["当时想到什么", "automaticThought"],
        ["身体感受", "bodySensation"],
        ["做了什么", "behavior"],
        ["直接结果", "immediateOutcome"],
      ] as const).map(([label, key]) => <label key={key} className="grid gap-1.5 text-xs font-medium text-muted-foreground">{label}<textarea rows={2} value={draft[key] ?? ""} onChange={(event) => setDraft((current) => ({ ...current, [key]: event.target.value }))} className="resize-none rounded-xl border border-border bg-background px-3 py-2 text-sm leading-6 text-foreground outline-none focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary/15" /></label>)}
      {error ? <p className="text-xs text-destructive" role="alert">{error}</p> : null}
      <div className="flex justify-end gap-2"><button type="button" disabled={busy} onClick={() => { setEditing(false); setDraft(item); setError(""); }} className="flex min-h-10 items-center gap-1.5 rounded-xl px-4 text-sm text-muted-foreground hover:bg-secondary"><IconX className="size-4" aria-hidden />取消</button><button type="button" disabled={busy} onClick={() => void save()} className="flex min-h-10 items-center gap-1.5 rounded-xl bg-primary px-4 text-sm font-medium text-primary-foreground disabled:opacity-50"><IconCheck className="size-4" aria-hidden />{busy ? "保存中…" : "保存"}</button></div>
    </div> : <><ol className="mt-4 grid gap-3 sm:grid-cols-2">{fields.map(([label, value]) => <li key={label} className="rounded-xl bg-secondary/65 px-4 py-3"><span className="block text-[11px] text-muted-foreground">{label}</span><p className="mt-1 text-sm leading-6">{value}</p></li>)}</ol>{item.sources.map((source) => <Quote key={`${source.sourceType}-${source.messageId}`} source={source} />)}</>}
    {error && !editing ? <p className="mt-3 text-xs text-destructive" role="alert">{error}</p> : null}
    <ConfirmDialog open={deleting} onOpenChange={setDeleting} title="删除这条情境与反应？" description="原聊天和同一时间的情绪记录仍会保留。" busy={busy} onConfirm={() => void remove()} />
  </article>;
}

function emotionsForSource(data: EmotionDay, observation: CbtObservation) {
  const source = observation.sources[0];
  if (!source) return "";
  return data.items
    .filter((item) => item.sources.some((candidate) => candidate.sourceType === source.sourceType && candidate.messageId === source.messageId))
    .map((item) => `${item.family} · ${item.subtype}（强度 ${item.intensity}）`)
    .join("、");
}

export function Quote({ source }: { source: SelfQuote }) {
  return <blockquote className="mt-3 rounded-xl bg-secondary/70 px-4 py-3 text-sm leading-6"><span className="mb-1 block text-[11px] text-muted-foreground">{source.sourceType === "Moment" ? "一刻" : "对话"}</span>“{source.text || "未填写文字"}”{source.locationName || source.locationAddress ? <span className="mt-1 flex items-start gap-1 text-[11px] text-muted-foreground"><IconMapPin className="mt-0.5 size-3 shrink-0" />{[source.locationName, source.locationAddress].filter(Boolean).join(" · ")}</span> : null}</blockquote>;
}

export function EmotionTimeline({ items }: { items: EmotionDay["items"] }) {
  return <div className="mt-7"><div className="relative h-20 border-b border-border"><div className="absolute inset-x-0 bottom-0 flex justify-between text-[10px] text-muted-foreground"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>{items.map((item, index) => { const local = new Date(item.occurredAt); const minute = local.getHours() * 60 + local.getMinutes(); return <span key={item.id} title={`${item.family} · ${item.subtype}`} className="absolute top-2 -translate-x-1/2 rounded-full ring-4 ring-background" style={{ left: `${minute / 14.4}%`, width: 8 + item.intensity * 3, height: 8 + item.intensity * 3, backgroundColor: colorOf(item.family), top: 6 + (index % 2) * 20 }} />; })}</div></div>;
}

export function Trend({ family, month }: { family: EmotionMonth["families"][number]; month: string }) {
  const days = new Date(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0).getDate();
  const points = family.trend.map((item) => ({ x: (Number(item.date.slice(-2)) - 1) / Math.max(1, days - 1) * 100, y: 100 - (item.peakIntensity - 1) / 4 * 100 }));
  return <svg viewBox="0 0 100 36" className="mt-4 h-32 w-full overflow-visible" role="img" aria-label={`${family.family}本月强度趋势`}><g stroke="currentColor" className="text-border" strokeWidth="0.35">{[0, 1, 2, 3, 4].map((line) => <line key={line} x1="0" x2="100" y1={line * 8} y2={line * 8} />)}</g>{points.map((point, index) => { const previous = points[index - 1]; const currentDay = Number(family.trend[index].date.slice(-2)); const previousDay = index ? Number(family.trend[index - 1].date.slice(-2)) : 0; return <g key={index}>{previous && currentDay === previousDay + 1 ? <line x1={previous.x} y1={previous.y * .32} x2={point.x} y2={point.y * .32} stroke={colorOf(family.family)} strokeWidth="1" /> : null}<circle cx={point.x} cy={point.y * .32} r="1.7" fill={colorOf(family.family)} /></g>; })}</svg>;
}

export function Empty({ text }: { text: string }) { return <div className="grid min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground"><div><IconMoodSmile className="mx-auto mb-3 size-7" />{text}</div></div>; }
export function Skeleton() { return <div className="mt-5 grid gap-3 md:grid-cols-2">{[1, 2, 3, 4].map((id) => <div key={id} className="h-40 animate-pulse rounded-2xl bg-secondary" />)}</div>; }
export function colorOf(family: string) { return emotionColors[family] ?? "#8B73E8"; }
export function toDateInput(date: Date) { const offset = date.getTimezoneOffset(); return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 10); }
export function buildCalendar(month: string) { const [year, value] = month.split("-").map(Number); const first = new Date(year, value - 1, 1); const count = new Date(year, value, 0).getDate(); const leading = (first.getDay() + 6) % 7; return [...Array<Date | null>(leading).fill(null), ...Array.from({ length: count }, (_, index) => new Date(year, value - 1, index + 1))]; }
