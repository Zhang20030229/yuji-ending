import "@/styles/combined/index.css";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "@/api/client";
import { getArchiveList, type EventItem } from "@/api/archive";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { backwards } from "@/lib/navigation";
import MapTimeline from "./MapTimeline.jsx";

/** 首页“展开更多”的独立时间线，不再依附于星图 Map View。 */
export default function TimelinePage() {
  const navigate = useNavigate();
  const [events, setEvents] = useState<EventItem[]>([]);
  const [error, setError] = useState("");
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  useEffect(() => {
    const controller = new AbortController();
    getArchiveList<EventItem>("events", "", controller.signal)
      .then(setEvents)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof ApiError ? reason.message : "暂时无法载入时间线。");
        }
      });
    return () => controller.abort();
  }, []);

  return (
    <section className="standalone-timeline-screen" aria-label="时间线">
      <SecondaryPageHeader
        title="时间线"
        subtitle={`${events.length} 个事件`}
        onBack={() => backwards(navigate, "/app/home")}
        backLabel="返回首页"
        scrolled={scrolled}
      />
      <div className="standalone-timeline-content">
        {error ? <p className="data-error" role="alert">{error}</p> : null}
        <MapTimeline
          events={events}
          onSelectEvent={(id: number) => navigate(`/app/events/${id}`)}
          onScroll={onScroll}
        />
      </div>
    </section>
  );
}
