import { useState } from "react";
import { useI18n } from "@/prototype/i18n.jsx";

export function EventSlider({
  timelineNodes,
  timelineIndex,
  granularity,
  years,
  selectedYear,
  onYearChange,
  onSelectEvent,
}) {
  const { language, t } = useI18n();
  const [isExpanded, setIsExpanded] = useState(false);
  const currentEvent = timelineNodes[timelineIndex];
  if (!currentEvent) return null;

  const formatPeriod = (event) => {
    const date = new Date(event.occurredAt);
    return granularity === "year"
      ? `${date.getFullYear()}`
      : t("map.month", {
        month: language === "zh"
          ? date.getMonth() + 1
          : new Intl.DateTimeFormat("en", { month: "short" }).format(date),
      });
  };
  const sliderProgress =
    timelineNodes.length > 1 ? (timelineIndex / (timelineNodes.length - 1)) * 100 : 0;
  const periodLabels = timelineNodes.map(formatPeriod);
  const activePeriod = formatPeriod(currentEvent);
  const periodPosition = (index) =>
    periodLabels.length > 1 ? (index / (periodLabels.length - 1)) * 100 : 50;

  return (
    <div className={`event-slider ${isExpanded ? "is-expanded" : ""}`}>
      <div className="event-slider__years" role="group" aria-label="选择地图年份">
        {["all", ...years.map(String)].map((year) => (
          <button
            type="button"
            key={year}
            className={selectedYear === year ? "is-active" : ""}
            aria-pressed={selectedYear === year}
            onClick={() => onYearChange(year)}
          >
            {year === "all" ? "全部" : year}
          </button>
        ))}
      </div>
      <div className="event-slider__body">
        <div className="event-slider__timeline">
          <span className="event-slider__current" aria-hidden="true">
            {activePeriod}
          </span>
          <div
            className={`event-slider__periods ${periodLabels.length > 7 ? "is-dense" : ""}`}
            aria-hidden="true"
          >
            {periodLabels.map((label, index) => (
              <span
                key={label}
                className={label === activePeriod ? "is-active" : ""}
                style={{ "--period-position": `${periodPosition(index)}%` }}
              >
                {label}
              </span>
            ))}
          </div>

          <div className="event-slider__control">
            <input
              id="event-timeline"
              type="range"
              min="0"
              max={timelineNodes.length - 1}
              step="1"
              value={timelineIndex}
              aria-label={granularity === "year" ? t("map.yearSlider") : t("map.monthSlider")}
              aria-valuetext={t("map.valueText", {
                period: formatPeriod(currentEvent),
                label: currentEvent.label ?? currentEvent.title ?? "",
              })}
              style={{ "--slider-progress": `${sliderProgress}%` }}
              onPointerDown={() => setIsExpanded(true)}
              onPointerUp={() => setIsExpanded(false)}
              onPointerCancel={() => setIsExpanded(false)}
              onKeyDown={(event) => {
                if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
                  setIsExpanded(true);
                }
              }}
              onKeyUp={(event) => {
                if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
                  setIsExpanded(false);
                }
              }}
              onBlur={() => setIsExpanded(false)}
              onChange={(event) => onSelectEvent(Number(event.target.value))}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
