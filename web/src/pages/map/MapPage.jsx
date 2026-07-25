import "@/styles/combined/index.css";
import { useEffect, useMemo, useRef, useState } from "react";
import { MapPin } from "@phosphor-icons/react";
import { useNavigate } from "react-router-dom";
import { getArchiveMapOverview } from "@/api/archive";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import { mapMappablePlaces, selectVisiblePlaceMarkers, toMarkerThumbnailUrl } from "@/api/map-data";
import { EventSlider } from "./vendor/EventSlider.jsx";
import { MapCanvas } from "./vendor/MapCanvas.jsx";
import {
  getPeriodNodes,
  isInOrBeforePeriod,
} from "./vendor/liveMapUtils.js";
import { useTimelineSelection } from "./vendor/useTimelineSelection.js";

/** 「星图」只保留地图，并用事件时间滑块回看地点轨迹。 */
export default function MapPage() {
  const navigate = useNavigate();
  const mapRef = useRef(null);
  const [places, setPlaces] = useState([]);
  const [events, setEvents] = useState([]);
  const [selectedYear, setSelectedYear] = useState("all");
  const [isScrolled, setIsScrolled] = useState(false);
  const revision = useDataRevision("archive");

  // 监听页面滚动，为 header 添加渐进模糊效果
  useEffect(() => {
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 10);
    };

    window.addEventListener("scroll", handleScroll, { passive: true });
    return () => window.removeEventListener("scroll", handleScroll);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getArchiveMapOverview(controller.signal)
      .then((overview) => {
        setPlaces(overview.places);
        setEvents(overview.events);
      })
      .catch((error) => {
        if (error?.name !== "AbortError") {
          setPlaces([]);
          setEvents([]);
        }
      })
    return () => controller.abort();
  }, [revision]);

  const mapPlaces = useMemo(() => mapMappablePlaces(places), [places]);
  const markers = useMemo(
    () => mapPlaces.map((place) => ({
      key: place.id,
      markerKey: place.id,
      label: place.title,
      longitude: place.longitude,
      latitude: place.latitude,
      occurredAt: place.occurredAt,
      icon: MapPin,
      imageUrl: toMarkerThumbnailUrl(place.imageUrl),
      summary: place.summary,
      secondary: place.secondary,
      conversationCount: place.conversationCount,
      eventCount: place.eventCount,
      href: `/app/places/${place.sourceId}`,
    })),
    [mapPlaces],
  );

  const sortedEvents = useMemo(
    () => [...events]
      .filter((event) => Number.isFinite(new Date(event.occurredAt).getTime()))
      .sort((left, right) => new Date(left.occurredAt) - new Date(right.occurredAt)),
    [events],
  );
  const years = useMemo(
    () => [...new Set(
      sortedEvents.map((event) => new Date(event.occurredAt).getFullYear()),
    )].sort((left, right) => right - left),
    [sortedEvents],
  );
  const granularity = selectedYear === "all" ? "year" : "month";
  const scopedEvents = useMemo(
    () => selectedYear === "all"
      ? sortedEvents
      : sortedEvents.filter(
        (event) => new Date(event.occurredAt).getFullYear() === Number(selectedYear),
      ),
    [selectedYear, sortedEvents],
  );
  const timelineNodes = useMemo(
    () => getPeriodNodes(scopedEvents, granularity),
    [granularity, scopedEvents],
  );
  const { timelineIndex, selectPeriod } = useTimelineSelection(
    timelineNodes,
    selectedYear,
  );
  const activePeriod = timelineNodes[timelineIndex] ?? timelineNodes.at(-1);
  const visibleEvents = useMemo(
    () => scopedEvents.filter(
      (event) => isInOrBeforePeriod(event, activePeriod, granularity),
    ),
    [activePeriod, granularity, scopedEvents],
  );
  const visibleMarkers = useMemo(() => {
    const visiblePlaceNames = new Set(
      visibleEvents.flatMap((event) => event.places ?? []),
    );
    const visibleIds = new Set(
      selectVisiblePlaceMarkers(mapPlaces, visiblePlaceNames).map((place) => place.id),
    );
    const visible = markers.filter((marker) => visibleIds.has(marker.key));
    return visible.length > 0 ? visible : markers;
  }, [mapPlaces, markers, visibleEvents]);

  return (
    <section
      className={`screen map-screen life-explorer life-explorer--map ${isScrolled ? "is-scrolled" : ""}`}
      aria-label="人生星图"
    >
      <MapCanvas
        mapRef={mapRef}
        layer="map"
        allEvents={markers}
        visibleMarkers={visibleMarkers}
        onSelectMarker={(markerId) => {
          const place = markers.find((marker) => marker.key === markerId);
          if (place) navigate(place.href);
        }}
      />
      {timelineNodes.length > 0 ? (
        <aside
          className="map-sheet"
          aria-label="地图时间滑块"
        >
          <EventSlider
            timelineNodes={timelineNodes}
            timelineIndex={timelineIndex}
            granularity={granularity}
            years={years}
            selectedYear={selectedYear}
            onYearChange={setSelectedYear}
            onSelectEvent={selectPeriod}
          />
        </aside>
      ) : null}
    </section>
  );
}
