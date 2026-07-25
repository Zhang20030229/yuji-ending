import { useMemo } from "react";
import { CalendarBlank, MapPin, UsersThree } from "@phosphor-icons/react";

/**
 * 星图 · 时间线：将拾光事件按年、月组织为纵向时间轴。
 */
export default function MapTimeline({ events, onSelectEvent, onScroll }) {
  const yearGroups = useMemo(() => {
    const groups = new Map();
    [...events]
      .filter((event) => Number.isFinite(new Date(event.occurredAt).getTime()))
      .sort((left, right) => new Date(right.occurredAt) - new Date(left.occurredAt))
      .forEach((event) => {
        const date = new Date(event.occurredAt);
        const year = date.getFullYear();
        const month = date.getMonth() + 1;
        if (!groups.has(year)) groups.set(year, new Map());
        const months = groups.get(year);
        if (!months.has(month)) months.set(month, []);
        months.get(month).push(event);
      });
    return [...groups.entries()].map(([year, months]) => ({
      year,
      months: [...months.entries()].map(([month, items]) => ({ month, items })),
    }));
  }, [events]);

  return (
    <section className="timeline-screen">
      <div className="timeline-scroll" onScroll={onScroll}>
        <div className="event-chronicle">
          {yearGroups.length === 0 ? (
            <div className="event-chronicle__empty">
              <CalendarBlank size={28} weight="duotone" />
              <p>还没有可以放进时间线的事件。</p>
            </div>
          ) : yearGroups.map(({ year, months }) => (
            <section className="event-year" key={year}>
              <header className="event-year__header">
                <strong>{year}</strong>
                <span>{months.reduce((total, month) => total + month.items.length, 0)} 个事件</span>
              </header>
              <div className="event-year__months">
                {months.map(({ month, items }) => (
                  <section className="event-month" key={`${year}-${month}`}>
                    <h3>{month}月</h3>
                    <div className="event-month__items">
                      {items.map((event) => (
                        <button
                          type="button"
                          className="event-timeline-card"
                          key={event.id}
                          onClick={() => onSelectEvent?.(event.id)}
                        >
                          <time dateTime={event.occurredAt}>
                            {new Intl.DateTimeFormat("zh-CN", {
                              month: "2-digit",
                              day: "2-digit",
                              weekday: "short",
                            }).format(new Date(event.occurredAt))}
                          </time>
                          <h4>{event.title}</h4>
                          {event.summary ? <p>{event.summary}</p> : null}
                          {(event.people?.length > 0 || event.places?.length > 0) ? (
                            <div className="event-timeline-card__meta">
                              {event.people?.length > 0 ? (
                                <span><UsersThree size={13} />{event.people.join("、")}</span>
                              ) : null}
                              {event.places?.length > 0 ? (
                                <span><MapPin size={13} />{event.places.join("、")}</span>
                              ) : null}
                            </div>
                          ) : null}
                        </button>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            </section>
          ))}
        </div>
      </div>
    </section>
  );
}
