import "@/styles/combined/index.css";
import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { IconSparkles } from "@tabler/icons-react";
import { useSearchParams } from "react-router-dom";
import { ApiError } from "@/api/client";
import {
  getEmotionDay,
  getEmotionMonth,
  getEmotionYear,
  type EmotionDay,
  type EmotionMonth,
  type EmotionYear,
} from "@/api/self";
import {
  colorOf,
  EmotionDayView,
  EmotionMonthView,
  Empty,
  Skeleton,
  toDateInput,
} from "@/pages/self/emotion-views";
import { DatePicker, MonthPicker, YearPicker } from "@/components/ui/date-picker";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import ReportsView from "@/pages/self/ReportsView";

type InsightMode = "day" | "month" | "year" | "report";

const modeLabels: Array<{ value: InsightMode; label: string }> = [
  { value: "day", label: "日" },
  { value: "month", label: "月" },
  { value: "year", label: "年" },
  { value: "report", label: "报告" },
];

/** 独立情绪洞察页：以日、月、年三个尺度回看真实情绪记录。 */
export default function InsightsPage() {
  const today = toDateInput(new Date());
  const [searchParams] = useSearchParams();
  const requestedMode = toInsightMode(searchParams.get("mode"));
  const [mode, setMode] = useState<InsightMode>(requestedMode);
  const [date, setDate] = useState(today);
  const [month, setMonth] = useState(today.slice(0, 7));
  const [year, setYear] = useState(Number(today.slice(0, 4)));
  const [dayData, setDayData] = useState<EmotionDay>();
  const [monthData, setMonthData] = useState<EmotionMonth>();
  const [yearData, setYearData] = useState<EmotionYear>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const revision = useDataRevision("self");

  useEffect(() => setMode(requestedMode), [requestedMode]);

  useEffect(() => {
    if (mode === "report") {
      setLoading(false);
      setError("");
      return;
    }
    const controller = new AbortController();
    setLoading(true);
    setError("");
    const request = mode === "day"
      ? getEmotionDay(date, controller.signal).then(setDayData)
      : mode === "month"
        ? getEmotionMonth(month, controller.signal).then(setMonthData)
        : getEmotionYear(year, controller.signal).then(setYearData);
    request
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof ApiError ? reason.message : "暂时无法载入情绪洞察。");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [date, mode, month, revision, year]);

  const selectDay = (value: string) => {
    setDate(value);
    setMonth(value.slice(0, 7));
    setYear(Number(value.slice(0, 4)));
    setMode("day");
  };

  const selectMonth = (value: string) => {
    setMonth(value);
    setYear(Number(value.slice(0, 4)));
    setMode("month");
  };

  return (
    <section className="emotion-insights" aria-label="情绪洞察">
      <div className="emotion-insights__scroll">
        <header className="emotion-insights__header">
          <div>
            <span className="emotion-insights__eyebrow"><IconSparkles size={14} /> 洞察</span>
            <h1>情绪洞察</h1>
            <p>回看情绪在一天、一月与一年里的变化，也可以生成今日心迹。</p>
          </div>
        </header>

        <div className="emotion-insights__toolbar">
          <div className="emotion-insights__period" role="tablist" aria-label="洞察时间范围">
            {modeLabels.map((item) => (
              <button
                key={item.value}
                type="button"
                role="tab"
                aria-selected={mode === item.value}
                className={mode === item.value ? "is-active" : ""}
                onClick={() => setMode(item.value)}
              >
                {item.label}
              </button>
            ))}
          </div>
          <div className="emotion-insights__picker">
            {mode === "day" ? (
              <DatePicker aria-label="选择日期" value={date} max={today} onChange={setDate} />
            ) : mode === "month" ? (
              <MonthPicker aria-label="选择月份" value={month} max={today.slice(0, 7)} onChange={setMonth} />
            ) : mode === "year" ? (
              <YearPicker
                aria-label="选择洞察年份"
                value={year}
                min={new Date().getFullYear() - 120}
                max={Number(today.slice(0, 4))}
                onChange={setYear}
              />
            ) : null}
          </div>
        </div>

        <main className="emotion-insights__content" aria-live="polite">
          {mode === "report" ? (
            <ReportsView defaultPreset="Today" />
          ) : loading ? (
            <Skeleton />
          ) : error ? (
            <Empty text={error} />
          ) : mode === "day" && dayData ? (
            <EmotionDayView data={dayData} />
          ) : mode === "month" && monthData ? (
            <EmotionMonthView data={monthData} selectedMonth={month} onSelectDay={selectDay} />
          ) : mode === "year" && yearData ? (
            <EmotionYearView data={yearData} onSelectDay={selectDay} onSelectMonth={selectMonth} />
          ) : null}
        </main>
      </div>
    </section>
  );
}

function toInsightMode(value: string | null): InsightMode {
  return value === "month" || value === "year" || value === "report" ? value : "day";
}

function EmotionYearView({
  data,
  onSelectDay,
  onSelectMonth,
}: {
  data: EmotionYear;
  onSelectDay: (date: string) => void;
  onSelectMonth: (month: string) => void;
}) {
  const calendar = useMemo(() => buildYearCalendar(data.year), [data.year]);
  const byDate = useMemo(() => new Map(data.days.map((day) => [day.date, day])), [data.days]);
  const activeMonths = useMemo(() => new Map(data.months.map((month) => [month.month, month])), [data.months]);
  const dominantFamily = data.families[0];
  const recordCount = data.months.reduce((total, month) => total + month.recordCount, 0);

  if (data.days.length === 0) return <Empty text={`${data.year} 年还没有明确的情绪记录。`} />;

  return (
    <div className="emotion-year-view">
      <section className="emotion-year-summary">
        <div>
          <span>年度概览</span>
          <h2>{dominantFamily ? `${dominantFamily.family}是这一年最常出现的情绪` : "这一年的情绪脉络正在形成"}</h2>
          <p>共记录 {data.days.length} 天 · {recordCount} 个情绪时刻</p>
        </div>
        {dominantFamily ? (
          <div className="emotion-year-summary__badge">
            <i style={{ backgroundColor: colorOf(dominantFamily.family) }} />
            <strong>{dominantFamily.family}</strong>
            <small>{dominantFamily.activeDays} 天</small>
          </div>
        ) : null}
      </section>

      <section className="emotion-year-heatmap" aria-labelledby="emotion-year-heatmap-title">
        <header>
          <div>
            <span>情绪热力图</span>
            <h2 id="emotion-year-heatmap-title">{data.year} 年的情绪轨迹</h2>
          </div>
          <small>点击有记录的日期查看当日洞察</small>
        </header>
        <div className="emotion-year-heatmap__viewport">
          <div className="emotion-year-heatmap__grid">
            {calendar.map((date, index) => {
              if (!date) return <i className="is-padding" key={`padding-${index}`} />;
              const value = toDateInput(date);
              const day = byDate.get(value);
              const dominant = day ? [...day.families].sort((left, right) => right.peakIntensity - left.peakIntensity || right.count - left.count)[0] : undefined;
              const label = dominant
                ? `${value}，${dominant.family}，强度 ${dominant.peakIntensity}/5`
                : `${value}，无情绪记录`;
              return (
                <button
                  key={value}
                  type="button"
                  disabled={!dominant}
                  aria-label={label}
                  title={label}
                  style={dominant ? ({
                    "--heat-color": colorOf(dominant.family),
                    "--heat-opacity": `${28 + dominant.peakIntensity * 14}%`,
                  } as CSSProperties) : undefined}
                  onClick={() => onSelectDay(value)}
                />
              );
            })}
          </div>
        </div>
        <EmotionLegend families={data.families} />
      </section>

      <section className="emotion-year-months">
        <header>
          <h2>月份概览</h2>
        </header>
        <div>
          {Array.from({ length: 12 }, (_, index) => {
            const month = index + 1;
            const item = activeMonths.get(month);
            return (
              <button
                key={month}
                type="button"
                disabled={!item}
                onClick={() => onSelectMonth(`${data.year}-${String(month).padStart(2, "0")}`)}
              >
                <strong>{month}月</strong>
                <span>{item ? `${item.activeDays} 天有记录` : "暂无记录"}</span>
                <i style={{ width: `${item ? Math.max(8, item.activeDays / 31 * 100) : 0}%` }} />
              </button>
            );
          })}
        </div>
      </section>
    </div>
  );
}

function EmotionLegend({ families }: { families: EmotionYear["families"] }) {
  return (
    <div className="emotion-year-legend" aria-label="情绪颜色图例">
      {families.map((family) => (
        <span key={family.family}><i style={{ backgroundColor: colorOf(family.family) }} />{family.family}</span>
      ))}
    </div>
  );
}

function buildYearCalendar(year: number) {
  const first = new Date(year, 0, 1);
  const leading = (first.getDay() + 6) % 7;
  const dayCount = new Date(year, 1, 29).getMonth() === 1 ? 366 : 365;
  return [
    ...Array<Date | null>(leading).fill(null),
    ...Array.from({ length: dayCount }, (_, index) => new Date(year, 0, index + 1)),
  ];
}
