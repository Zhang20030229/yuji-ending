import { useEffect, useState } from "react";
import { IconMapPin, IconMoodSmile, IconTrash } from "@tabler/icons-react";
import {
  deleteCbtObservation,
  type CbtObservation,
  type DayInsight,
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

/** 情绪日视图：当日总结、情绪占比、关键洞察与逐条记录。供遇己页与洞察页复用。 */
export function EmotionDayView({ data }: { data: EmotionDay }) {
  const [cards, setCards] = useState(() => buildInsightViews(data));
  useEffect(() => setCards(buildInsightViews(data)), [data]);

  if (data.items.length === 0 && data.cbtObservations.length === 0)
    return <Empty text="这一天没有明确的情绪记录。" />;

  return <div className="mt-5 space-y-4">
    {data.items.length > 0 && <section className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5">
      <p className="text-xs font-medium text-muted-foreground">当日总结</p>
      <h2 className="mt-2 text-lg font-semibold leading-7">{data.narrative || data.summary}</h2>
      <p className="mt-2 text-xs text-muted-foreground">来自 {data.conversationCount} 次会话 · {data.sourceMessageCount} 条原话</p>
      <EmotionLanes items={data.items} />
    </section>}
    {data.families.length > 0 && <EmotionShare families={data.families} />}
    {cards.length > 0 && <section>
      <h2 className="mb-1 text-sm font-semibold">关键洞察</h2>
      <p className="mb-3 text-xs text-muted-foreground">{data.insights.length > 0 ? "当天最值得回看的几件事，由原话归纳。" : "从原话中梳理情境、想法与结果。"}</p>
      <div className="space-y-3">{cards.map((card) => <InsightCard
        key={card.key}
        item={card}
        onDelete={() => setCards((current) => current.filter((value) => value.key !== card.key))}
      />)}</div>
    </section>}
    {data.items.length > 0 && <section>
      <h2 className="mb-3 text-sm font-semibold">情绪记录</h2>
      <div className="space-y-3">{data.items.map((item) => <article key={item.id} className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5"><div className="flex items-center gap-2"><span className="size-3 rounded-full" style={{ backgroundColor: colorOf(item.family) }} /><strong className="text-sm">{item.family} · {item.subtype}</strong><span className="ml-auto text-xs text-muted-foreground">{new Date(item.occurredAt).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })} · 强度 {item.intensity}</span></div><p className="mt-3 text-sm leading-6 text-foreground/85">{item.summary}</p>{item.sources.map((source) => <Quote key={`${source.sourceType}-${source.messageId}`} source={source} />)}<p className="mt-2 text-[11px] text-muted-foreground">{item.conversationTitle}</p></article>)}</div>
    </section>}
  </div>;
}

/** 一张关键洞察卡的内容；模型摘要与原始 CBT 观察都先转成这个形状再渲染。 */
interface InsightView {
  key: string;
  occurredAt?: string;
  emotions: string[];
  situation: string;
  appraisal?: string | null;
  followUp?: string | null;
  quotes: Array<SelfQuote | string>;
  observationIds: number[];
}

/** 摘要还没生成时回退到原始 CBT 观察，页面不会空着。 */
function buildInsightViews(data: EmotionDay): InsightView[] {
  if (data.insights.length > 0) return data.insights.map((insight, index) => toInsightView(data, insight, index));
  return data.cbtObservations.map((observation) => ({
    key: `observation-${observation.id}`,
    occurredAt: observation.occurredAt,
    emotions: emotionsForSource(data, observation),
    situation: observation.situation,
    appraisal: observation.automaticThought,
    followUp: [observation.behavior, observation.immediateOutcome].filter(Boolean).join("，"),
    quotes: observation.sources,
    observationIds: [observation.id],
  }));
}

/** 把模型给的原话文本对回当天的来源，能对上就按对话或一刻展示出处。 */
function toInsightView(data: EmotionDay, insight: DayInsight, index: number): InsightView {
  const sources = [...data.items.flatMap((item) => item.sources), ...data.cbtObservations.flatMap((item) => item.sources)];
  const quotes = insight.quotes.map((text) => sources.find((source) => source.text.includes(text)) ?? text);
  const anchor = data.cbtObservations.find((item) => insight.observationIds.includes(item.id));
  const matched = quotes.find((quote): quote is SelfQuote => typeof quote !== "string");
  return {
    key: `insight-${index}`,
    occurredAt: anchor?.occurredAt ?? matched?.createdAt,
    emotions: insight.emotions,
    situation: insight.situation,
    appraisal: insight.appraisal,
    followUp: insight.followUp,
    quotes,
    observationIds: insight.observationIds,
  };
}

/** 情绪月视图：本月总结、日历、家族统计、单类趋势与代表原话。供遇己页与未来的时间线复用。 */
export function EmotionMonthView({ data, selectedMonth, onSelectDay }: { data: EmotionMonth; selectedMonth: string; onSelectDay: (date: string) => void }) {
  if (data.days.length === 0) return <Empty text="这个月没有明确的情绪记录。" />;
  const calendar = buildCalendar(selectedMonth);
  const byDate = new Map(data.days.map((day) => [day.date, day]));
  const defaultFamily = data.families[0];
  return <div className="mt-5 space-y-4"><section className="rounded-2xl border border-border/70 bg-background p-5"><p className="text-xs font-medium text-muted-foreground">本月总结</p><h2 className="mt-2 text-lg font-semibold leading-7">{data.summary}</h2>{data.cbtObservationCount > 0 ? <p className="mt-3 text-xs font-medium text-primary">CBT 自我观察 · 共 {data.cbtObservationCount} 次 · 覆盖 {data.cbtCoveredDays} 天</p> : null}</section><section className="rounded-2xl border border-border/70 bg-background p-4 sm:p-5"><div className="grid grid-cols-7 text-center text-[11px] text-muted-foreground">{"一二三四五六日".split("").map((day) => <span key={day} className="py-2">周{day}</span>)}</div><div className="grid grid-cols-7">{calendar.map((date, index) => { const value = date ? toDateInput(date) : ""; const item = value ? byDate.get(value) : undefined; return <button key={`${value}-${index}`} type="button" disabled={!item} onClick={() => item && onSelectDay(value)} className="aspect-square min-h-12 border-t border-border/50 p-1 text-left text-xs disabled:text-muted-foreground/35"><span>{date?.getDate()}</span>{item ? <span className="mt-1 flex flex-wrap gap-1">{item.families.slice(0, 3).map((family) => <span key={family.family} className="rounded-full" title={family.family} style={{ width: 5 + family.peakIntensity * 2, height: 5 + family.peakIntensity * 2, backgroundColor: colorOf(family.family) }} />)}{item.families.length > 3 ? <small className="text-[9px]">+{item.families.length - 3}</small> : null}</span> : null}</button>; })}</div></section><section><h2 className="mb-3 text-sm font-semibold">本月出现的情绪</h2><div className="grid gap-3 sm:grid-cols-2">{data.families.map((family) => <div key={family.family} className="rounded-2xl bg-secondary/70 p-4"><div className="flex items-center gap-2"><span className="size-3 rounded-full" style={{ backgroundColor: colorOf(family.family) }} /><strong className="text-sm">{family.family}</strong></div><p className="mt-2 text-xs text-muted-foreground">出现 {family.activeDays} 天 · 平均日峰值 {family.averageDailyPeak}</p><p className="mt-1 text-xs text-muted-foreground">{family.subtypes.join("、")}</p></div>)}</div></section>{defaultFamily ? <section className="rounded-2xl border border-border/70 bg-background p-5"><h2 className="text-sm font-semibold">{defaultFamily.family} · 单类趋势</h2><Trend family={defaultFamily} month={selectedMonth} /></section> : null}<section><h2 className="mb-3 text-sm font-semibold">代表原话</h2><div className="space-y-2">{data.representativeQuotes.map((item, index) => <button key={`${item.date}-${index}`} type="button" onClick={() => onSelectDay(item.date)} className="w-full rounded-2xl bg-secondary/70 p-4 text-left"><span className="text-xs font-medium" style={{ color: colorOf(item.family) }}>{item.date} · {item.family}/{item.subtype}</span><blockquote className="mt-2 text-sm leading-6">“{item.text}”</blockquote></button>)}</div></section></div>;
}

/** 四段式关键洞察卡。内容由模型按指纹重算，就地编辑会被静默覆盖，所以只读；删除仍然作用在原始观察上。 */
function InsightCard({ item, onDelete }: { item: InsightView; onDelete: () => void }) {
  const [deleting, setDeleting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const sections = [
    ["发生的事情", item.situation],
    ["你怎么看这件事", item.appraisal],
    ["后续进展", item.followUp],
  ].filter((section): section is [string, string] => Boolean(section[1]));

  async function remove() {
    setBusy(true);
    setError("");
    try {
      // 洞察是归纳结果，删除要落到它引用的每条原始观察上；指纹随之变化，摘要下一轮自动重算。
      for (const id of item.observationIds) await deleteCbtObservation(id);
      setDeleting(false);
      onDelete();
    } catch (reason) {
      setError(reason instanceof ApiError || reason instanceof Error ? reason.message : "暂时无法删除。");
      setDeleting(false);
    } finally {
      setBusy(false);
    }
  }

  return <article className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5">
    <div className="flex flex-wrap items-center gap-2">
      {item.occurredAt ? <time className="text-xs text-muted-foreground">{new Date(item.occurredAt).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })}</time> : null}
      {item.emotions.map((label) => <span key={label} className="rounded-full px-2 py-0.5 text-[11px] font-medium" style={{ backgroundColor: `${colorOf(familyOf(label))}22`, color: colorOf(familyOf(label)) }}>{label}</span>)}
      {item.observationIds.length > 0 ? <button type="button" onClick={() => setDeleting(true)} className="ml-auto grid size-9 place-items-center rounded-full text-muted-foreground hover:bg-secondary hover:text-destructive" aria-label="删除这条洞察"><IconTrash className="size-4" aria-hidden /></button> : null}
    </div>
    <ol className="mt-4 space-y-3">{sections.map(([label, value]) => <li key={label} className="rounded-xl bg-secondary/65 px-4 py-3"><span className="block text-[11px] text-muted-foreground">{label}</span><p className="mt-1 text-sm leading-6">{value}</p></li>)}</ol>
    {item.quotes.length > 0 ? <details className="mt-3"><summary className="cursor-pointer text-xs text-muted-foreground">原话</summary>{item.quotes.map((quote, index) => typeof quote === "string"
      ? <blockquote key={`text-${index}`} className="mt-3 rounded-xl bg-secondary/70 px-4 py-3 text-sm leading-6">“{quote}”</blockquote>
      : <Quote key={`${quote.sourceType}-${quote.messageId}`} source={quote} />)}</details> : null}
    {error ? <p className="mt-3 text-xs text-destructive" role="alert">{error}</p> : null}
    <ConfirmDialog open={deleting} onOpenChange={setDeleting} title="删除这条洞察？" description="它引用的情境与反应会被删除，原聊天和情绪记录仍会保留。" busy={busy} onConfirm={() => void remove()} />
  </article>;
}

/** 回退卡片的情绪标签：取同一条原话上记录到的情绪，格式与模型摘要保持一致。 */
function emotionsForSource(data: EmotionDay, observation: CbtObservation) {
  const source = observation.sources[0];
  if (!source) return [];
  return data.items
    .filter((item) => item.sources.some((candidate) => candidate.sourceType === source.sourceType && candidate.messageId === source.messageId))
    .map((item) => `${item.family}·${item.subtype}`);
}

export function Quote({ source }: { source: SelfQuote }) {
  return <blockquote className="mt-3 rounded-xl bg-secondary/70 px-4 py-3 text-sm leading-6"><span className="mb-1 block text-[11px] text-muted-foreground">{source.sourceType === "Moment" ? "一刻" : "对话"}</span>“{source.text || "未填写文字"}”{source.locationName || source.locationAddress ? <span className="mt-1 flex items-start gap-1 text-[11px] text-muted-foreground"><IconMapPin className="mt-0.5 size-3 shrink-0" />{[source.locationName, source.locationAddress].filter(Boolean).join(" · ")}</span> : null}</blockquote>;
}

/** 今日情绪占比：排行 + 环形图，一眼看出各类情绪各占多少。 */
export function EmotionShare({ families }: { families: EmotionDay["families"] }) {
  const total = families.reduce((sum, item) => sum + item.momentCount, 0);
  if (total === 0) return null;
  const ranked = [...families].sort((left, right) => right.momentCount - left.momentCount);
  const percents = ranked.map((item) => Math.round(item.momentCount / total * 100));
  // 取整误差交给占比最大的那一项吸收，页面上的百分比加起来始终是 100。
  percents[0] = 100 - percents.slice(1).reduce((sum, value) => sum + value, 0);
  const circumference = 2 * Math.PI * 42;
  let covered = 0;
  return <section className="yuji-insight-card rounded-2xl border border-border/70 bg-background p-5">
    <h2 className="text-sm font-semibold">今日情绪占比</h2>
    <p className="mt-1 text-xs text-muted-foreground">今天共记录 {total} 个情绪时刻，分布在 {ranked.length} 类情绪中</p>
    <ol className="mt-4 space-y-2.5">{ranked.map((item, index) => <li key={item.family}>
      <div className="flex items-center gap-2 text-xs">
        <span className="size-2.5 rounded-full" style={{ backgroundColor: colorOf(item.family) }} />
        <strong className="font-medium">{item.family}</strong>
        <span className="ml-auto text-muted-foreground">{percents[index]}% · {item.momentCount} 个时刻</span>
      </div>
      <div className="mt-1.5 h-1.5 rounded-full bg-secondary"><span className="block h-full rounded-full" style={{ width: `${percents[index]}%`, backgroundColor: colorOf(item.family) }} /></div>
    </li>)}</ol>
    <div className="relative mx-auto mt-6 size-36">
      <svg viewBox="0 0 100 100" className="size-full -rotate-90" role="img" aria-label={ranked.map((item, index) => `${item.family} ${percents[index]}%`).join("，")}>
        <circle cx="50" cy="50" r="42" fill="none" stroke="currentColor" className="text-secondary" strokeWidth="12" />
        {ranked.map((item, index) => {
          const dash = percents[index] / 100 * circumference;
          const offset = -covered / 100 * circumference;
          covered += percents[index];
          return <circle key={item.family} cx="50" cy="50" r="42" fill="none" stroke={colorOf(item.family)} strokeWidth="12" strokeDasharray={`${dash} ${circumference - dash}`} strokeDashoffset={offset} />;
        })}
      </svg>
      <div className="pointer-events-none absolute inset-0 grid place-content-center text-center">
        <strong className="text-2xl font-semibold leading-none">{ranked.length}</strong>
        <span className="mt-1 text-[11px] text-muted-foreground">类情绪</span>
      </div>
    </div>
  </section>;
}

/** 情绪泳道：一个家族一条，跨家族不再重叠；同族相邻时刻间隔小于 20 分钟合并成一段。 */
export function EmotionLanes({ items }: { items: EmotionDay["items"] }) {
  const lanes = buildLanes(items);
  return <div className="mt-6">
    <div className="relative overflow-hidden rounded-xl border border-border/50">
      {[25, 50, 75].map((line) => <span key={line} className="absolute inset-y-0 z-0 w-px bg-border/50" style={{ left: `${line}%` }} />)}
      {lanes.map((lane, index) => <div key={lane.family} className={`relative h-8 ${index > 0 ? "border-t border-border/40" : ""}`}>
        {lane.bars.map((bar, barIndex) => <span
          key={barIndex}
          role="img"
          aria-label={`${lane.family}·${bar.subtypes.join("、")} ${minuteLabel(bar.start)}–${minuteLabel(bar.end)} 强度 ${bar.intensity}`}
          title={`${lane.family}·${bar.subtypes.join("、")} ${minuteLabel(bar.start)}–${minuteLabel(bar.end)} 强度 ${bar.intensity}`}
          className="absolute inset-y-1.5 rounded-lg"
          style={{ left: `${bar.start / 14.4}%`, width: `max(${(bar.end - bar.start) / 14.4}%, 20px)`, backgroundColor: colorOf(lane.family), opacity: 0.45 + (bar.intensity - 1) / 4 * 0.55 }}
        />)}
      </div>)}
    </div>
    <div className="mt-2 flex justify-between text-[10px] text-muted-foreground"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>
    <ul className="mt-3 flex flex-wrap justify-center gap-x-4 gap-y-1.5">{lanes.map((lane) => <li key={lane.family} className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
      <span className="size-2.5 rounded-full" style={{ backgroundColor: colorOf(lane.family) }} />{lane.family}
    </li>)}</ul>
  </div>;
}

/** 把当天时刻按家族分组并合并成时段；间隔小于 20 分钟视为同一段。 */
function buildLanes(items: EmotionDay["items"]) {
  const byFamily = new Map<string, Array<{ minute: number; intensity: number; subtype: string }>>();
  for (const item of items) {
    const local = new Date(item.occurredAt);
    const entry = { minute: local.getHours() * 60 + local.getMinutes(), intensity: item.intensity, subtype: item.subtype };
    byFamily.set(item.family, [...byFamily.get(item.family) ?? [], entry]);
  }
  return [...byFamily].map(([family, entries]) => {
    const sorted = [...entries].sort((left, right) => left.minute - right.minute);
    const bars: Array<{ start: number; end: number; intensity: number; subtypes: string[] }> = [];
    for (const entry of sorted) {
      const last = bars.at(-1);
      if (last && entry.minute - last.end < 20) {
        last.end = entry.minute;
        last.intensity = Math.max(last.intensity, entry.intensity);
        if (!last.subtypes.includes(entry.subtype)) last.subtypes.push(entry.subtype);
        continue;
      }
      bars.push({ start: entry.minute, end: entry.minute, intensity: entry.intensity, subtypes: [entry.subtype] });
    }
    return { family, bars };
  });
}

function minuteLabel(minute: number) {
  return `${String(Math.floor(minute / 60)).padStart(2, "0")}:${String(minute % 60).padStart(2, "0")}`;
}

export function Trend({ family, month }: { family: EmotionMonth["families"][number]; month: string }) {
  const days = new Date(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0).getDate();
  const points = family.trend.map((item) => ({ x: (Number(item.date.slice(-2)) - 1) / Math.max(1, days - 1) * 100, y: 100 - (item.peakIntensity - 1) / 4 * 100 }));
  return <svg viewBox="0 0 100 36" className="mt-4 h-32 w-full overflow-visible" role="img" aria-label={`${family.family}本月强度趋势`}><g stroke="currentColor" className="text-border" strokeWidth="0.35">{[0, 1, 2, 3, 4].map((line) => <line key={line} x1="0" x2="100" y1={line * 8} y2={line * 8} />)}</g>{points.map((point, index) => { const previous = points[index - 1]; const currentDay = Number(family.trend[index].date.slice(-2)); const previousDay = index ? Number(family.trend[index - 1].date.slice(-2)) : 0; return <g key={index}>{previous && currentDay === previousDay + 1 ? <line x1={previous.x} y1={previous.y * .32} x2={point.x} y2={point.y * .32} stroke={colorOf(family.family)} strokeWidth="1" /> : null}<circle cx={point.x} cy={point.y * .32} r="1.7" fill={colorOf(family.family)} /></g>; })}</svg>;
}

export function Empty({ text }: { text: string }) { return <div className="grid min-h-72 place-items-center px-8 text-center text-sm text-muted-foreground"><div><IconMoodSmile className="mx-auto mb-3 size-7" />{text}</div></div>; }
export function Skeleton() { return <div className="mt-5 grid gap-3 md:grid-cols-2">{[1, 2, 3, 4].map((id) => <div key={id} className="h-40 animate-pulse rounded-2xl bg-secondary" />)}</div>; }
export function colorOf(family: string) { return emotionColors[family] ?? "#8B73E8"; }
/** 情绪标签形如「愤怒·烦躁」，取家族部分决定颜色。 */
export function familyOf(label: string) { return label.split("·")[0].trim(); }
export function toDateInput(date: Date) { const offset = date.getTimezoneOffset(); return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 10); }
export function buildCalendar(month: string) { const [year, value] = month.split("-").map(Number); const first = new Date(year, value - 1, 1); const count = new Date(year, value, 0).getDate(); const leading = (first.getDay() + 6) % 7; return [...Array<Date | null>(leading).fill(null), ...Array.from({ length: count }, (_, index) => new Date(year, value - 1, index + 1))]; }
