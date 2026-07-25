import assert from "node:assert/strict";
import test from "node:test";
import {
  firstMonthWithData,
  mapMappablePlaces,
  selectVisiblePlaceMarkers,
  toMarkerThumbnailUrl,
  toTreeRecognitions,
} from "./map-data.ts";

test("mapMappablePlaces drops entries without finite coordinates", () => {
  const markers = mapMappablePlaces([
    { id: 1, name: "A", latitude: 39.9, longitude: 116.4 },
    { id: 2, name: "B" },
    { id: 3, name: "C", latitude: Number.NaN, longitude: 0 },
  ]);

  assert.deepEqual(markers.map((marker) => marker.id), ["place:1"]);
  assert.equal(markers[0].sourceId, 1);
});

test("mapMappablePlaces carries the Places summary into the map marker", () => {
  const [marker] = mapMappablePlaces([
    {
      id: 7,
      name: "滨江公园",
      latitude: 31.2,
      longitude: 121.5,
      latestSummary: "常去散步、整理思绪的地方。",
      secondary: "上海 浦东新区",
      conversationCount: 3,
      eventCount: 2,
    },
  ]);

  assert.equal(marker.summary, "常去散步、整理思绪的地方。");
  assert.equal(marker.secondary, "上海 浦东新区");
  assert.equal(marker.conversationCount, 3);
  assert.equal(marker.eventCount, 2);
});

test("mapMappablePlaces keeps each place's own cover image", () => {
  const places = [
    { id: 4, name: "A", latitude: 39.9, longitude: 116.4, coverImageUrl: "/a.jpg" },
    { id: 5, name: "B", latitude: 31.2, longitude: 121.5, coverImageUrl: "/b.jpg" },
    { id: 6, name: "C", latitude: 30.6, longitude: 104.0, coverImageUrl: "/c.jpg" },
    { id: 7, name: "D", latitude: 22.5, longitude: 113.9, coverImageUrl: "/d.jpg" },
    { id: 8, name: "E", latitude: 22.7, longitude: 114.8, coverImageUrl: "/e.jpg" },
  ];

  assert.deepEqual(
    mapMappablePlaces(places).map((marker) => marker.imageUrl),
    ["/a.jpg", "/b.jpg", "/c.jpg", "/d.jpg", "/e.jpg"],
  );
});

test("selectVisiblePlaceMarkers keeps coordinate-only places without events", () => {
  const markers = mapMappablePlaces([
    { id: 4, name: "白云山", latitude: 23.1847, longitude: 113.3027, eventCount: 0 },
    { id: 5, name: "天河体育中心", latitude: 23.1373, longitude: 113.3271, eventCount: 1 },
    { id: 6, name: "深圳人才公园", latitude: 22.5175, longitude: 113.9436, eventCount: 1 },
  ]);

  assert.deepEqual(
    selectVisiblePlaceMarkers(markers, new Set(["天河体育中心"])).map((marker) => marker.title),
    ["白云山", "天河体育中心"],
  );
});

test("toMarkerThumbnailUrl requests a small compressed asset", () => {
  assert.equal(
    toMarkerThumbnailUrl("/api/assets/42/content"),
    "/api/assets/42/thumbnail?size=68&quality=65",
  );
  assert.equal(toMarkerThumbnailUrl("https://example.test/photo.jpg"), "https://example.test/photo.jpg");
});

test("firstMonthWithData prefers earliest recorded month else January", () => {
  assert.equal(firstMonthWithData(2026, [7, 3, 11]), "2026-03");
  assert.equal(firstMonthWithData(2026, []), "2026-01");
});

test("toTreeRecognitions maps recognition fields for the tree", () => {
  const [leaf] = toTreeRecognitions([
    {
      id: 9,
      category: "Need",
      content: "需要明确回应",
      keywords: ["回应"],
      conversationId: 1,
      conversationTitle: "约会",
      updatedAt: "2026-07-24T10:00:00+08:00",
      sources: [],
    },
  ]);
  assert.equal(leaf.id, "9");
  assert.equal(leaf.title, "需要明确回应");
  assert.equal(leaf.category, "Need");
  assert.match(leaf.meta, /约会/);
});
