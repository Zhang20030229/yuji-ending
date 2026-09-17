import {
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import {
  ArrowRight,
  HouseLine,
  MapTrifold,
  X,
} from "@phosphor-icons/react";
import Map, {
  AttributionControl,
  Marker,
  NavigationControl,
  Popup,
} from "react-map-gl/maplibre";
import "maplibre-gl/dist/maplibre-gl.css";
import { useI18n } from "@/prototype/i18n.jsx";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { MAPTILER_API_KEY, MAPTILER_STYLE } from "@/config/map";

const mapTilerApiKey = MAPTILER_API_KEY;
const mapTilerStyle = MAPTILER_STYLE;

export const FALLBACK_LOCATION = {
  longitude: 116.4074,
  latitude: 39.9042,
};

export const NEARBY_EVENT_RADIUS_KM = 8;

export const MAP_CAMERA = {
  ...FALLBACK_LOCATION,
  zoom: 12.5,
  pitch: 0,
  bearing: 0,
};

const BUILDINGS_3D_LAYER_ID = "yuji-3d-buildings";
const MAP_LANGUAGE_CODES = {
  zh: "zh-Hans",
  en: "en",
};
const WIDE_MAP_QUERY = "(min-width: 1024px)";
const MAP_NAME_PROPERTIES = ["name", "name:en", "name:zh", "name:zh-Hans"];
const REGION_NAME_OVERRIDES_ZH_HANS = {
  CN: "中国",
  HK: "中国香港",
  MO: "中国澳门",
  TW: "中国台湾",
};

function createSimplifiedChineseRegionNameExpression() {
  const regionNames = typeof Intl.DisplayNames === "function"
    ? new Intl.DisplayNames(["zh-Hans"], { type: "region", fallback: "none" })
    : null;
  const matches = [];

  for (let first = 65; first <= 90; first += 1) {
    for (let second = 65; second <= 90; second += 1) {
      const code = String.fromCharCode(first, second);
      const label = REGION_NAME_OVERRIDES_ZH_HANS[code] ?? regionNames?.of(code);

      if (
        label
        && label !== code
        && label !== "未知地区"
        && label !== "未知区域"
      ) {
        matches.push(code, label);
      }
    }
  }

  return [
    "match",
    ["upcase", ["coalesce", ["get", "iso_a2"], ""]],
    ...matches,
    "",
  ];
}

const SIMPLIFIED_CHINESE_REGION_NAME_EXPRESSION =
  createSimplifiedChineseRegionNameExpression();

/* ── Taiwan country-label compliance ──────────────────────────────── */
const TAIWAN_LABEL_SOURCE_ID = "yuji-taiwan-country-label-source";
const TAIWAN_LABEL_LAYER_ID = "yuji-taiwan-country-label";
const TAIWAN_LABEL_COORDINATES = [120.9605, 23.6978];
const TAIWAN_NAMES = [
  "Taiwan",
  "Taiwan, China",
  "Republic of China",
  "中华民国",
  "中華民國",
  "中国台湾",
  "中國台灣",
  "台湾",
  "臺灣",
];

/* ── South China Sea islands compliance ───────────────────────────── */
const SCS_LABEL_SOURCE_ID = "yuji-scs-islands-label-source";
const SCS_LABEL_LAYER_ID = "yuji-scs-islands-label";
const SCS_LABEL_COORDINATES = [112.27, 15.37];
const SCS_DISPUTED_NAMES = [
  "Spratly Islands",
  "Paracel Islands",
  "Scarborough Shoal",
  "Second Thomas Shoal",
  "Mischief Reef",
  "Fiery Cross Reef",
  "Subi Reef",
  "南沙群岛",
  "西沙群岛",
  "中沙群岛",
  "东沙群岛",
  "黄岩岛",
  "美济礁",
  "永暑礁",
  "渚碧礁",
  "仁爱礁",
];

/* ── Disputed region / border label compliance ────────────────────── */
const DISPUTED_REGION_NAMES = [
  "Aksai Chin",
  "South Tibet",
  "Arunachal Pradesh",
  "阿克赛钦",
  "藏南",
  "麦克马洪线",
];

function isCountryLabelLayer(layer) {
  if (
    layer.type !== "symbol"
    || !layer.layout?.["text-field"]
    || layer.id === TAIWAN_LABEL_LAYER_ID
    || layer.id === SCS_LABEL_LAYER_ID
  ) {
    return false;
  }

  const id = layer.id.toLowerCase();
  const sourceLayer = (layer["source-layer"] ?? "").toLowerCase();
  const filter = JSON.stringify(layer.filter ?? "").toLowerCase();

  return (
    id.includes("country")
    || ((sourceLayer === "place" || sourceLayer === "boundary") && filter.includes("country"))
  );
}

function isPlaceLabelLayer(layer) {
  if (
    layer.type !== "symbol"
    || !layer.layout?.["text-field"]
    || layer.id === TAIWAN_LABEL_LAYER_ID
    || layer.id === SCS_LABEL_LAYER_ID
  ) {
    return false;
  }

  const id = layer.id.toLowerCase();
  const sourceLayer = (layer["source-layer"] ?? "").toLowerCase();

  return (
    id.includes("place")
    || id.includes("poi")
    || id.includes("label")
    || sourceLayer === "place"
    || sourceLayer === "poi"
  );
}

function containsNameProperty(value) {
  if (typeof value === "string") {
    return MAP_NAME_PROPERTIES.some((property) => (
      value === property || value.includes(`{${property}}`)
    ));
  }

  return Array.isArray(value) && value.some(containsNameProperty);
}

function firstNameProperty(value) {
  if (!Array.isArray(value)) return null;

  if (
    value[0] === "get"
    && typeof value[1] === "string"
    && MAP_NAME_PROPERTIES.includes(value[1])
  ) {
    return value[1];
  }

  for (const item of value) {
    const property = firstNameProperty(item);
    if (property) return property;
  }

  return null;
}

function localizedNameExpression(language, layer) {
  if (language === "zh") {
    const fallbacks = isCountryLabelLayer(layer)
      ? [
        SIMPLIFIED_CHINESE_REGION_NAME_EXPRESSION,
        ["get", "name:zh"],
        ["get", "name"],
        ["get", "name:en"],
      ]
      : [
        ["get", "name:zh"],
        ["get", "name"],
        ["get", "name:en"],
      ];

    return [
      "coalesce",
      ["get", "name:zh-Hans"],
      ...fallbacks,
    ];
  }

  return ["coalesce", ["get", "name:en"], ["get", "name"]];
}

function applyMapLabelLanguage(map, language) {
  if (!map?.isStyleLoaded()) return;

  const preferredNameProperty = language === "zh" ? "name:zh-Hans" : "name:en";
  const layers = map.getStyle().layers ?? [];

  layers.forEach((layer) => {
    if (
      layer.type !== "symbol"
      || layer.id === TAIWAN_LABEL_LAYER_ID
      || layer.id === SCS_LABEL_LAYER_ID
      || !containsNameProperty(layer.layout?.["text-field"])
      || firstNameProperty(layer.layout["text-field"]) === preferredNameProperty
    ) {
      return;
    }

    map.setLayoutProperty(layer.id, "text-field", localizedNameExpression(language, layer));
  });
}

function createTaiwanExclusionFilter(originalFilter) {
  const codeProperties = [
    "iso_a2",
    "iso_3166_1",
    "iso_3166_1_alpha_2",
    "country_code",
  ];
  const excluded = [
    "any",
    ...codeProperties.map((property) => [
      "==",
      ["upcase", ["coalesce", ["get", property], ""]],
      "TW",
    ]),
    ...MAP_NAME_PROPERTIES.map((property) => [
      "in",
      ["coalesce", ["get", property], ""],
      ["literal", TAIWAN_NAMES],
    ]),
  ];
  const exclusion = ["!", excluded];

  return originalFilter ? ["all", originalFilter, exclusion] : exclusion;
}

function createSouthChinaSeaExclusionFilter(originalFilter) {
  const excluded = [
    "any",
    ...MAP_NAME_PROPERTIES.map((property) => [
      "in",
      ["coalesce", ["get", property], ""],
      ["literal", SCS_DISPUTED_NAMES],
    ]),
  ];
  const exclusion = ["!", excluded];

  return originalFilter ? ["all", originalFilter, exclusion] : exclusion;
}

function ensureComplianceLabel(map, {
  sourceId,
  layerId,
  coordinates,
  labelProperties,
  language,
  minzoom = 0,
  maxzoom = 9,
  textSizeStops = [2, 11, 6, 15],
}) {
  const sourceData = {
    type: "FeatureCollection",
    features: [
      {
        type: "Feature",
        properties: labelProperties,
        geometry: { type: "Point", coordinates },
      },
    ],
  };
  const existingSource = map.getSource(sourceId);

  if (existingSource) {
    existingSource.setData(sourceData);
  } else {
    map.addSource(sourceId, { type: "geojson", data: sourceData });
  }

  const textField = [
    "coalesce",
    ["get", language === "zh" ? "name:zh-Hans" : "name:en"],
    ["get", "name"],
  ];

  if (!map.getLayer(layerId)) {
    map.addLayer({
      id: layerId,
      type: "symbol",
      source: sourceId,
      minzoom,
      maxzoom,
      layout: {
        "text-field": textField,
        "text-size": ["interpolate", ["linear"], ["zoom"], ...textSizeStops],
        "text-letter-spacing": 0.02,
        "text-allow-overlap": true,
        "text-ignore-placement": false,
      },
      paint: {
        "text-color": "#4d5c60",
        "text-halo-color": "rgba(250, 247, 238, 0.94)",
        "text-halo-width": 1.4,
        "text-halo-blur": 0.3,
      },
    });
  } else {
    const currentTextField = map.getLayoutProperty(layerId, "text-field");
    if (JSON.stringify(currentTextField) !== JSON.stringify(textField)) {
      map.setLayoutProperty(layerId, "text-field", textField);
    }
  }

  // Language-driven style reloads recreate the base style. Keep app-owned
  // compliance labels above every MapTiler symbol layer after each rebuild.
  map.moveLayer(layerId);
}

function applyTaiwanLabelCompliance(map, language) {
  const layers = map.getStyle().layers ?? [];

  /* Filter out original Taiwan country labels */
  layers.filter(isCountryLabelLayer).forEach((layer) => {
    if (JSON.stringify(layer.filter ?? "").includes("Republic of China")) return;
    map.setFilter(layer.id, createTaiwanExclusionFilter(layer.filter));
  });

  /* Add compliant replacement label */
  ensureComplianceLabel(map, {
    sourceId: TAIWAN_LABEL_SOURCE_ID,
    layerId: TAIWAN_LABEL_LAYER_ID,
    coordinates: TAIWAN_LABEL_COORDINATES,
    language,
    labelProperties: {
      name: language === "zh" ? "中国台湾" : "Taiwan, China",
      "name:zh-Hans": "中国台湾",
      "name:zh": "中国台湾",
      "name:en": "Taiwan, China",
    },
  });
}

function applySouthChinaSeaCompliance(map, language) {
  const layers = map.getStyle().layers ?? [];

  /* Filter out disputed island / reef labels in the South China Sea */
  layers.filter(isPlaceLabelLayer).forEach((layer) => {
    const filterJson = JSON.stringify(layer.filter ?? "");
    if (filterJson.includes("Second Thomas Shoal")) return;
    map.setFilter(layer.id, createSouthChinaSeaExclusionFilter(layer.filter));
  });

  /* Add compliant unified label "南海诸岛" / "South China Sea Islands" */
  ensureComplianceLabel(map, {
    sourceId: SCS_LABEL_SOURCE_ID,
    layerId: SCS_LABEL_LAYER_ID,
    coordinates: SCS_LABEL_COORDINATES,
    language,
    minzoom: 2,
    maxzoom: 8,
    textSizeStops: [2, 9, 5, 13],
    labelProperties: {
      name: language === "zh" ? "南海诸岛" : "South China Sea Islands",
      "name:zh-Hans": "南海诸岛",
      "name:zh": "南海诸岛",
      "name:en": "South China Sea Islands",
    },
  });
}

function applyDisputedRegionCompliance(map) {
  const layers = map.getStyle().layers ?? [];

  /*
   * Hide labels for disputed border regions (e.g. Aksai Chin, South Tibet /
   * "Arunachal Pradesh"). These names are contested and should not appear
   * in the app's map view.
  */
  layers.filter(isPlaceLabelLayer).forEach((layer) => {
    const filterJson = JSON.stringify(layer.filter ?? "");
    if (filterJson.includes("South Tibet")) return;

    const excluded = [
      "any",
      ...MAP_NAME_PROPERTIES.map((property) => [
        "in",
        ["coalesce", ["get", property], ""],
        ["literal", DISPUTED_REGION_NAMES],
      ]),
    ];
    const exclusion = ["!", excluded];
    map.setFilter(
      layer.id,
      layer.filter ? ["all", layer.filter, exclusion] : exclusion,
    );
  });
}

function applyMapCompliance(map, language) {
  if (!map?.isStyleLoaded()) return;
  applyMapLabelLanguage(map, language);
  applyTaiwanLabelCompliance(map, language);
  applySouthChinaSeaCompliance(map, language);
  applyDisputedRegionCompliance(map);
}

function add3DBuildings({ target: map }) {
  const layers = map.getStyle().layers ?? [];
  const existing3DBuildings = layers.find(
    (layer) => layer.type === "fill-extrusion" && layer["source-layer"] === "building",
  );
  if (existing3DBuildings || map.getLayer(BUILDINGS_3D_LAYER_ID)) return;

  const buildingFootprint = layers.find(
    (layer) => layer.type === "fill" && layer["source-layer"] === "building",
  );
  if (!buildingFootprint?.source) return;

  const firstLabelLayer = layers.find(
    (layer) => layer.type === "symbol" && layer.layout?.["text-field"],
  );

  map.addLayer(
    {
      id: BUILDINGS_3D_LAYER_ID,
      source: buildingFootprint.source,
      "source-layer": buildingFootprint["source-layer"],
      type: "fill-extrusion",
      minzoom: 13.5,
      paint: {
        "fill-extrusion-color": "#d9cbb8",
        "fill-extrusion-height": [
          "interpolate",
          ["linear"],
          ["zoom"],
          13.5,
          0,
          14.5,
          ["coalesce", ["get", "height"], 12],
        ],
        "fill-extrusion-base": ["coalesce", ["get", "height_min"], 0],
        "fill-extrusion-opacity": 0.78,
        "fill-extrusion-vertical-gradient": true,
      },
    },
    firstLabelLayer?.id,
  );
}

function distanceInKilometers(origin, destination) {
  const toRadians = (degrees) => (degrees * Math.PI) / 180;
  const latitudeDelta = toRadians(destination.latitude - origin.latitude);
  const longitudeDelta = toRadians(destination.longitude - origin.longitude);
  const originLatitude = toRadians(origin.latitude);
  const destinationLatitude = toRadians(destination.latitude);
  const haversine = (
    Math.sin(latitudeDelta / 2) ** 2
    + Math.cos(originLatitude)
      * Math.cos(destinationLatitude)
      * Math.sin(longitudeDelta / 2) ** 2
  );

  return 6371 * 2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine));
}

function fitNearbyEvents(map, events, currentLocation) {
  const nearbyEvents = events.filter(
    (event) => distanceInKilometers(currentLocation, event) <= NEARBY_EVENT_RADIUS_KM,
  );

  if (!nearbyEvents.length) {
    map.jumpTo({
      center: [currentLocation.longitude, currentLocation.latitude],
      zoom: MAP_CAMERA.zoom,
      pitch: MAP_CAMERA.pitch,
      bearing: MAP_CAMERA.bearing,
    });
    return;
  }

  const pointsInView = [currentLocation, ...nearbyEvents];
  const longitudes = pointsInView.map((event) => event.longitude);
  const latitudes = pointsInView.map((event) => event.latitude);
  const west = Math.min(...longitudes);
  const east = Math.max(...longitudes);
  const south = Math.min(...latitudes);
  const north = Math.max(...latitudes);

  map.fitBounds(
    [
      [west, south],
      [east, north],
    ],
    {
      padding: { top: 96, right: 48, bottom: 178, left: 48 },
      duration: 0,
      maxZoom: 14,
    },
  );
}

function focusEvents(map, events, fallbackLocation) {
  if (!events.length) {
    map.jumpTo({
      center: [fallbackLocation.longitude, fallbackLocation.latitude],
      zoom: MAP_CAMERA.zoom,
      pitch: MAP_CAMERA.pitch,
      bearing: MAP_CAMERA.bearing,
    });
    return;
  }

  if (events.length === 1) {
    map.jumpTo({
      center: [events[0].longitude, events[0].latitude],
      zoom: 13.5,
      pitch: 0,
      bearing: 0,
    });
    return;
  }

  const longitudes = events.map((event) => event.longitude);
  const latitudes = events.map((event) => event.latitude);
  map.fitBounds(
    [
      [Math.min(...longitudes), Math.min(...latitudes)],
      [Math.max(...longitudes), Math.max(...latitudes)],
    ],
    {
      padding: 54,
      duration: 0,
      maxZoom: 13.5,
    },
  );
}

function useWideMapLayout() {
  const [isWide, setIsWide] = useState(() => (
    typeof window !== "undefined" && window.matchMedia(WIDE_MAP_QUERY).matches
  ));

  useEffect(() => {
    const mediaQuery = window.matchMedia(WIDE_MAP_QUERY);
    const update = () => setIsWide(mediaQuery.matches);
    mediaQuery.addEventListener("change", update);
    update();
    return () => mediaQuery.removeEventListener("change", update);
  }, []);

  return isWide;
}

export function MapCanvas({
  mapRef,
  layer,
  allEvents,
  visibleMarkers,
  onSelectMarker,
  focusLocation,
  showCurrentLocation = true,
}) {
  const { language, t } = useI18n();
  const mapStyle = useMemo(() => {
    const query = new URLSearchParams({
      key: mapTilerApiKey,
      language: MAP_LANGUAGE_CODES[language] ?? MAP_LANGUAGE_CODES.en,
    });
    return `https://api.maptiler.com/maps/${mapTilerStyle}/style.json?${query}`;
  }, [language]);
  const [currentLocation, setCurrentLocation] = useState(
    focusLocation ?? FALLBACK_LOCATION,
  );
  const [isAwayFromCurrent, setIsAwayFromCurrent] = useState(false);
  const [isCompassVisible, setIsCompassVisible] = useState(
    Math.abs(MAP_CAMERA.bearing) > 0.5,
  );
  const [navigationControlGroup, setNavigationControlGroup] = useState(null);
  const [mapInstance, setMapInstance] = useState(null);
  const [selectedMarker, setSelectedMarker] = useState(null);
  const isWideMapLayout = useWideMapLayout();
  const markerTimeRange = useMemo(() => {
    const timestamps = visibleMarkers
      .map((item) => new Date(item.occurredAt).getTime())
      .filter(Number.isFinite);

    if (timestamps.length < 2) {
      return { latest: timestamps[0] ?? 0, span: 0 };
    }

    const oldest = Math.min(...timestamps);
    const latest = Math.max(...timestamps);
    return { latest, span: latest - oldest };
  }, [visibleMarkers]);
  const languageRef = useRef(language);
  languageRef.current = language;

  useEffect(() => {
    if (focusLocation) {
      setCurrentLocation(focusLocation);
      return undefined;
    }
    if (!navigator.geolocation) return undefined;

    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        setCurrentLocation({
          longitude: coords.longitude,
          latitude: coords.latitude,
        });
        setIsAwayFromCurrent(false);
      },
      () => {},
      {
        enableHighAccuracy: true,
        timeout: 10000,
        maximumAge: 300000,
      },
    );

    return undefined;
  }, [focusLocation]);

  /*
   * A language change makes react-map-gl replace the complete MapTiler style,
   * including app-owned sources and layers. Restore customizations only after
   * the replacement style has emitted style.load, so the Chinese style cannot
   * overwrite the compliance layers added to the previous style. The listener
   * is stable for the map lifetime and reads the current language from a ref;
   * an old English closure must not win a Chinese style-load race.
   */
  useEffect(() => {
    if (!mapInstance) return undefined;

    const restoreStyleCustomizations = () => {
      if (!mapInstance.isStyleLoaded()) return;
      applyMapCompliance(mapInstance, languageRef.current);
    };

    mapInstance.on("style.load", restoreStyleCustomizations);
    restoreStyleCustomizations();

    return () => {
      mapInstance.off("style.load", restoreStyleCustomizations);
    };
  }, [mapInstance]);

  useEffect(() => {
    const map = mapRef.current?.getMap?.() ?? mapRef.current;
    if (!map?.isStyleLoaded()) return;
    if (focusLocation) focusEvents(map, allEvents, focusLocation);
    else fitNearbyEvents(map, allEvents, currentLocation);
  }, [allEvents, currentLocation, focusLocation, mapRef]);

  useEffect(() => {
    setSelectedMarker((current) => {
      if (!isWideMapLayout || !current) return null;
      const isStillVisible = visibleMarkers.some(
        (marker) => (marker.markerKey ?? marker.key)
          === (current.markerKey ?? current.key),
      );
      return isStillVisible ? current : null;
    });
  }, [isWideMapLayout, visibleMarkers]);

  if (!mapTilerApiKey) {
    return (
      <div className={`map-canvas map-canvas--${layer}`}>
        <div className="map-config-notice" role="status">
          <span><MapTrifold size={24} weight="duotone" /></span>
          <strong>{t("map.missingTitle")}</strong>
          <small>{t("map.missingDetail")}</small>
        </div>
      </div>
    );
  }

  const returnToCurrentLocation = () => {
    const map = mapRef.current?.getMap?.() ?? mapRef.current;
    if (!map) return;

    map.flyTo({
      center: [currentLocation.longitude, currentLocation.latitude],
      zoom: Math.max(map.getZoom(), 13),
      pitch: map.getPitch(),
      bearing: map.getBearing(),
      duration: 1100,
      curve: 1.35,
      essential: true,
    });
    setIsAwayFromCurrent(false);
  };

  return (
    <div
      className={`map-canvas map-canvas--${layer} ${
        isCompassVisible ? "" : "map-canvas--compass-hidden"
      }`}
      onClick={(event) => {
        if (!event.target.closest?.(".map-marker, .map-place-popup")) {
          setSelectedMarker(null);
        }
      }}
    >
      <Map
        ref={mapRef}
        initialViewState={MAP_CAMERA}
        mapStyle={mapStyle}
        minZoom={0}
        maxZoom={17}
        maxPitch={60}
        dragRotate
        touchPitch
        antialias
        renderWorldCopies={false}
        attributionControl={false}
        onDragStart={() => setIsAwayFromCurrent(true)}
        onMoveEnd={(event) => {
          const map = event.target;
          setIsCompassVisible(Math.abs(map.getBearing()) > 0.5);
        }}
        onLoad={(event) => {
          setMapInstance(event.target);
          applyMapCompliance(event.target, language);
          if (focusLocation) focusEvents(event.target, allEvents, focusLocation);
          else fitNearbyEvents(event.target, allEvents, currentLocation);
          window.requestAnimationFrame(() => {
            setNavigationControlGroup(
              event.target
                .getContainer()
                .querySelector(".maplibregl-ctrl-bottom-right .maplibregl-ctrl-group"),
            );
          });
        }}
      >
        <NavigationControl position="bottom-right" showCompass />
        <AttributionControl position="bottom-left" compact={false} />
        {showCurrentLocation ? (
          <Marker
            longitude={currentLocation.longitude}
            latitude={currentLocation.latitude}
            anchor="center"
          >
            <div className="current-location" aria-label={t("map.currentLocation")}>
              <HouseLine size={14} weight="fill" />
            </div>
          </Marker>
        ) : null}
        {visibleMarkers.map((item) => {
          const {
            markerKey, key, longitude, latitude, icon: Icon,
          } = item;
          const label = item.label;
          const eventTime = new Date(item.occurredAt).getTime();
          const eventAge = Number.isFinite(eventTime) && markerTimeRange.span > 0
            ? Math.min(Math.max((markerTimeRange.latest - eventTime) / markerTimeRange.span, 0), 1)
            : 0;
          const identity = markerKey ?? key;
          const isSelected = (selectedMarker?.markerKey ?? selectedMarker?.key) === identity;
          return (
          <Marker
            key={identity}
            longitude={longitude}
            latitude={latitude}
            anchor="bottom"
          >
            <button
              type="button"
              className={`map-marker ${isSelected ? "is-selected" : ""}`}
              style={{
                "--event-grayscale": `${Math.round(eventAge * 92)}%`,
                "--event-saturation": `${Math.round(100 - eventAge * 72)}%`,
                "--event-opacity": (1 - eventAge * 0.42).toFixed(2),
                "--event-brightness": (1 + eventAge * 0.3).toFixed(2),
              }}
              onClickCapture={(event) => {
                event.stopPropagation();
                if (!isWideMapLayout || isSelected) {
                  onSelectMarker(key);
                  return;
                }
                setSelectedMarker(item);
              }}
              aria-pressed={isWideMapLayout ? isSelected : undefined}
              aria-label={t("map.viewEvent", { label })}
            >
              <span>
                <Icon size={17} weight="fill" />
                {item.imageUrl ? (
                  <ImagePreviewDialog
                    src={item.imageUrl}
                    alt=""
                    thumbnailClassName="map-marker-image"
                    interactive={false}
                  />
                ) : null}
              </span>
              <strong>{label}</strong>
            </button>
          </Marker>
          );
        })}
        {isWideMapLayout && selectedMarker ? (
          <Popup
            longitude={selectedMarker.longitude}
            latitude={selectedMarker.latitude}
            anchor="bottom"
            offset={54}
            closeButton={false}
            closeOnClick={false}
            className="map-place-popup"
            onClose={() => setSelectedMarker(null)}
          >
            <article
              className="map-place-summary"
              aria-label={`${selectedMarker.label}摘要`}
              onPointerDown={(event) => event.stopPropagation()}
              onClick={(event) => event.stopPropagation()}
            >
              <header>
                <div>
                  <h2>{selectedMarker.label}</h2>
                  {selectedMarker.secondary ? (
                    <p>{selectedMarker.secondary}</p>
                  ) : null}
                </div>
                <button
                  type="button"
                  className="map-place-summary__close"
                  onClick={() => setSelectedMarker(null)}
                  aria-label="关闭地点摘要"
                >
                  <X size={14} weight="bold" />
                </button>
              </header>
              <p className="map-place-summary__text">
                {selectedMarker.summary || "这个地点还在等待更多回忆。"}
              </p>
              <footer>
                <span>
                  {selectedMarker.conversationCount ?? 0} 段对话
                  <i aria-hidden="true">·</i>
                  {selectedMarker.eventCount ?? 0} 个事件
                </span>
                <button
                  type="button"
                  onClick={() => onSelectMarker(selectedMarker.key)}
                >
                  查看详情
                  <ArrowRight size={14} weight="bold" />
                </button>
              </footer>
            </article>
          </Popup>
        ) : null}
      </Map>
      {navigationControlGroup && isAwayFromCurrent && createPortal(
        <button
          type="button"
          className={`maplibregl-ctrl-geolocate return-to-location-control ${
            isAwayFromCurrent ? "is-away" : ""
          }`}
          onClick={returnToCurrentLocation}
          title={t("map.returnToLocation")}
          aria-label={t("map.returnToLocation")}
        >
          <span className="maplibregl-ctrl-icon" aria-hidden="true" />
        </button>,
        navigationControlGroup,
      )}
    </div>
  );
}
