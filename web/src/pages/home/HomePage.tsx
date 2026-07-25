import "@/styles/combined/index.css";
import { useEffect, useMemo, useState } from "react";
import {
  IconBrain,
  IconCalendarEvent,
  IconChevronRight,
  IconFileAnalytics,
  IconHeartFilled,
  IconInbox,
  IconMapPin,
  IconPhoto,
  IconSearch,
  IconSparkles,
  IconUsers,
} from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "@/api/client";
import type { EntityCard, EventItem, FragmentItem, PendingItem } from "@/api/archive";
import type { ReportListItem } from "@/api/reports";
import type { EmotionDay, RecognitionItem } from "@/api/self";
import { getHomeOverview } from "@/api/home";
import { useUser } from "@/components/layout/user-context";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import { categoryLabel } from "@/pages/tree/vendor/categories";
import type { AnalysisStatus } from "@/api/analysis";
import { sendIMessageCare } from "@/api/imessage";

/** 「我」首页：概览 widgets、可搜索的最近事件，以及人物与认识预览列表。 */
export default function HomePage() {
  const user = useUser();
  const navigate = useNavigate();
  const [events, setEvents] = useState<EventItem[]>([]);
  const [people, setPeople] = useState<EntityCard[]>([]);
  const [places, setPlaces] = useState<EntityCard[]>([]);
  const [day, setDay] = useState<EmotionDay>();
  const [recognitions, setRecognitions] = useState<RecognitionItem[]>([]);
  const [pendingItems, setPendingItems] = useState<PendingItem[]>([]);
  const [fragments, setFragments] = useState<FragmentItem[]>([]);
  const [reports, setReports] = useState<ReportListItem[]>([]);
  const [eventQuery, setEventQuery] = useState("");
  const [error, setError] = useState("");
  const [analysisStatus, setAnalysisStatus] = useState<AnalysisStatus>();
  const [careStatus, setCareStatus] = useState<"idle" | "sending" | "sent" | "failed">("idle");
  const [careError, setCareError] = useState("");

  const revision = useDataRevision("archive", "self", "reports", "analysis");

  useEffect(() => {
    const controller = new AbortController();
    getHomeOverview(controller.signal).then((overview) => {
      setEvents(overview.events);
      setPeople(overview.people);
      setPlaces(overview.places);
      setDay(overview.emotion);
      setRecognitions(overview.recognitions);
      setPendingItems(overview.pending);
      setFragments(overview.fragments);
      setReports(overview.reports);
      setAnalysisStatus(overview.analysisStatus);
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(reason instanceof ApiError ? reason.message : "暂时无法载入首页内容。");
    });
    return () => controller.abort();
  }, [revision]);

  const dominantMood = useMemo(
    () => [...(day?.families ?? [])].sort((a, b) => b.peakIntensity - a.peakIntensity)[0],
    [day],
  );
  const currentMood = dominantMood ? `${dominantMood.family} · ${dominantMood.peakIntensity}/5` : null;
  const representativePerson = useMemo(() => pickRandom(people), [people]);
  const representativeRecognition = useMemo(() => pickRandom(recognitions), [recognitions]);
  const filteredEvents = useMemo(() => {
    const query = eventQuery.trim().toLocaleLowerCase();
    if (!query) return events;
    return events.filter((event) =>
      [event.title, event.summary, ...event.people, ...event.places]
        .some((value) => value.toLocaleLowerCase().includes(query)));
  }, [eventQuery, events]);

  async function handleGreetingClick(event: React.MouseEvent<HTMLButtonElement>) {
    if (event.detail !== 3 || careStatus === "sending") return;
    setCareStatus("sending");
    setCareError("");
    try {
      await sendIMessageCare();
      setCareStatus("sent");
    } catch (reason) {
      setCareStatus("failed");
      setCareError(reason instanceof ApiError ? reason.message : "暂时没有发送成功。");
    }
  }

  return <section className="screen dashboard-screen" aria-label="我">
    <div className="dashboard-home-header">
      <header className="dashboard-header">
        <button type="button" className="dashboard-greeting" onClick={handleGreetingClick}>
          <h1>{user.displayName}，你好</h1>
          <p>{new Intl.DateTimeFormat("zh-CN", { dateStyle: "full" }).format(new Date())}</p>
        </button>
        <div className="dashboard-header__actions">
          <button type="button" className="login-status" onClick={() => navigate("/app/settings")} aria-label="账户与设置">
            <span className="login-status__avatar" />
          </button>
        </div>
      </header>
      {careStatus !== "idle" ? (
        <p className={`dashboard-care-status dashboard-care-status--${careStatus}`} role="status">
          {careStatus === "sending"
            ? "正在准备一声问候…"
            : careStatus === "sent"
              ? "已经通过 iMessage 送出问候"
              : careError}
        </p>
      ) : null}
    </div>

    {analysisStatus && analysisStatus.lifeRecordCount + analysisStatus.recognitionCount + analysisStatus.emotionCount > 0 ? (
      <div className="yuji-analysis-banner mx-auto mt-3 flex w-[calc(100%-2rem)] max-w-[1124px] items-center gap-2 rounded-xl bg-primary-soft px-4 py-3 text-sm text-primary" role="status">
        <IconSparkles className="size-4 animate-pulse" aria-hidden />
        <span>有内容正在整理</span>
      </div>
    ) : null}

    <div className="dashboard-scroll dashboard-scroll--recent">
        <div className="widget-stack">
          <div className="dashboard-widget-grid">
            <button type="button" className="state-card state-card--mood" onClick={() => navigate("/app/insights?mode=day")} aria-label={`今日心情：${currentMood ?? "暂无记录"}，进入洞察`}>
              <img src={moodBackgroundFor(dominantMood?.family)} alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>今日心情</span><IconHeartFilled size={15} aria-hidden /></span>
                <strong>{currentMood ?? "暂无记录"}</strong>
                <small>查看心情洞察</small>
              </div>
            </button>

            <button type="button" className="state-card state-card--insight" onClick={() => navigate("/app/events")}>
              <img src="/assets/widgets/bg-insight.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>事件</span><IconCalendarEvent size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {events.length} 个事件</strong>
                <small>查看事件列表</small>
              </div>
            </button>

            <button
              type="button"
              className="state-card state-card--places"
              onClick={() => navigate("/app/places")}
            >
              <img src="/assets/widgets/bg-life-tree.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>地点</span><IconMapPin size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {places.length} 个地点</strong>
                <small>查看地点列表</small>
              </div>
            </button>

            <button
              type="button"
              className="state-card state-card--people"
              onClick={() => navigate("/app/people")}
            >
              <img src="/assets/widgets/bg-people.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>人物</span><IconUsers size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {people.length} 个人物</strong>
                <small>{representativePerson ? `最近关注 · ${representativePerson.name}` : "查看人物列表"}</small>
              </div>
            </button>

            <button
              type="button"
              className="state-card state-card--recognition"
              onClick={() => navigate("/app/recognitions")}
            >
              <img src="/assets/widgets/bg-recognition.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>认识</span><IconBrain size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {recognitions.length} 条认识</strong>
                <small>{representativeRecognition ? categoryLabel(representativeRecognition.category, "zh") : "查看认识列表"}</small>
              </div>
            </button>

            <button type="button" className="state-card state-card--pending" onClick={() => navigate("/app/inbox")}>
              <img src="/assets/widgets/bg-pending-confirmation.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>待确认</span><IconInbox size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {pendingItems.length} 条待确认</strong>
                <small>{pendingItems[0] ? `${pendingItems[0].kind === "Person" ? "人物" : "地点"} · ${pendingItems[0].mention}` : "目前都已确认"}</small>
              </div>
            </button>

            <button type="button" className="state-card state-card--fragments" onClick={() => navigate("/app/fragments")}>
              <img src="/assets/widgets/bg-time-fragment.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>时光片段</span><IconPhoto size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {fragments.length} 个片段</strong>
                <small>{fragments[0]?.title ?? "查看时光片段"}</small>
              </div>
            </button>

            <button type="button" className="state-card state-card--reports" onClick={() => navigate("/app/reports")}>
              <img src="/assets/widgets/bg-heart-report.webp" alt="" aria-hidden="true" />
              <div>
                <span className="state-card__top"><span>心迹报告</span><IconFileAnalytics size={17} aria-hidden /></span>
                <strong className="state-card__content-copy">共 {reports.length} 份报告</strong>
                <small>{reports[0]?.headline ?? "查看心迹报告"}</small>
              </div>
            </button>
          </div>

          <article className="dashboard-recent">
            <div className="section-title">
              <h2><IconCalendarEvent size={16} aria-hidden />最近事件</h2>
            </div>
            <div className="dashboard-timeline">
              <label className="dashboard-timeline-search">
                <IconSearch size={16} aria-hidden />
                <span className="sr-only">搜索最近事件</span>
                <input type="search" value={eventQuery} onChange={(event) => setEventQuery(event.target.value)} placeholder="搜索事件、人物或地点" />
              </label>
              {filteredEvents.length === 0
                ? <p className="data-empty">{eventQuery ? "没有匹配的最近事件。" : `还没有事件。去和${user.aiName}聊聊今天吧。`}</p>
                : (
                  <div className={`dashboard-timeline-events${filteredEvents.length > 2 ? " dashboard-timeline-events--collapsed" : ""}`}>
                    {filteredEvents.slice(0, 4).map((event, index) => {
                      const content = <>
                        <time dateTime={event.occurredAt}>{formatTimelineDate(event.occurredAt)}</time>
                        <span className="dashboard-timeline-marker" aria-hidden />
                        <span className="dashboard-timeline-content">
                          <strong>{event.title}</strong>
                          <small>{event.summary || [...event.people, ...event.places].join(" · ") || "查看事件详情"}</small>
                          {event.people.length + event.places.length > 0 ? <em>{[...event.people, ...event.places].slice(0, 2).join(" · ")}</em> : null}
                        </span>
                        <IconChevronRight size={16} aria-hidden />
                      </>;
                      return index < 2 ? (
                        <button type="button" className="dashboard-timeline-item" key={event.id} onClick={() => navigate(`/app/events/${event.id}`)}>
                          {content}
                        </button>
                      ) : (
                        <div className="dashboard-timeline-item dashboard-timeline-item--blurred" key={event.id} aria-hidden="true">
                          {content}
                        </div>
                      );
                    })}
                    {filteredEvents.length > 2 ? (
                      <div className="dashboard-timeline-more">
                        <button type="button" onClick={() => navigate("/app/timeline")}>
                          展开更多
                          <IconChevronRight size={14} aria-hidden />
                        </button>
                      </div>
                    ) : null}
                  </div>
                )}
            </div>
          </article>

          <RecentPreviewSection title="人物" icon={<IconUsers size={16} aria-hidden />} onAll={() => navigate("/app/people")} empty="还没有人物记录。">
            {people.slice(0, 4).map((person) => (
              <button type="button" className="dashboard-entity-row" key={person.id} onClick={() => navigate(`/app/people/${person.id}`)}>
                <span className="dashboard-entity-avatar">{person.name.slice(0, 1)}</span>
                <span><strong>{person.name}</strong><small>{person.secondary || person.latestSummary || "等待更多记录"}</small></span>
                <span className="dashboard-entity-count">{person.eventCount} 个事件</span>
                <IconChevronRight size={16} aria-hidden />
              </button>
            ))}
          </RecentPreviewSection>

          <RecentPreviewSection title="认识" icon={<IconBrain size={16} aria-hidden />} onAll={() => navigate("/app/recognitions")} empty="还没有形成认识。">
            {recognitions.slice(0, 4).map((recognition) => (
              <button type="button" className="dashboard-entity-row dashboard-recognition-row" key={recognition.id} onClick={() => navigate(`/app/recognitions/${recognition.id}`)}>
                <span className="event-icon event-icon--amber"><IconBrain size={18} aria-hidden /></span>
                <span><strong>{recognition.content}</strong><small>{categoryLabel(recognition.category, "zh")}</small></span>
                <IconChevronRight size={16} aria-hidden />
              </button>
            ))}
          </RecentPreviewSection>

          {error ? <p className="data-error" role="alert">{error}</p> : null}
        </div>
      </div>
  </section>;
}

function RecentPreviewSection({ title, icon, onAll, empty, children }: {
  title: string;
  icon: React.ReactNode;
  onAll: () => void;
  empty: string;
  children: React.ReactNode;
}) {
  const hasItems = Array.isArray(children) ? children.length > 0 : Boolean(children);
  return <article className="dashboard-recent">
    <div className="section-title">
      <h2>{icon}{title}</h2>
      <button type="button" onClick={onAll}>查看列表 <span>›</span></button>
    </div>
    <div className="event-list-card">{hasItems ? children : <p className="data-empty">{empty}</p>}</div>
  </article>;
}

function formatTimelineDate(value: string) {
  return new Intl.DateTimeFormat("zh-CN", { month: "numeric", day: "numeric" }).format(new Date(value));
}

function pickRandom<T>(items: T[]) {
  return items.length === 0 ? undefined : items[Math.floor(Math.random() * items.length)];
}

function moodBackgroundFor(family?: string) {
  if (family === "平静") return "/assets/widgets/mood-calm.webp";
  if (["愉悦", "期待", "亲近", "惊讶"].includes(family ?? "")) return "/assets/widgets/mood-joy.webp";
  if (["低落", "恐惧", "厌恶", "自责"].includes(family ?? "")) return "/assets/widgets/mood-low.webp";
  if (family) return "/assets/widgets/mood-anxious.webp";
  return "/assets/widgets/mood-calm.webp";
}

