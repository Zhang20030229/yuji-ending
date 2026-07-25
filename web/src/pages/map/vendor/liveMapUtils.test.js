import assert from "node:assert/strict";
import test from "node:test";
import {
  buildCalendarDays,
  buildMonthCalendarDays,
  dominantEmotionFamily,
  getPeriodNodes,
  isInOrBeforePeriod,
} from "./liveMapUtils.js";

const events = [
  { id: 3, occurredAt: "2025-03-20T10:00:00+08:00" },
  { id: 1, occurredAt: "2024-01-10T10:00:00+08:00" },
  { id: 2, occurredAt: "2024-07-11T10:00:00+08:00" },
];

test("timeline period nodes keep the latest event in each period", () => {
  assert.deepEqual(getPeriodNodes(events, "year").map((event) => event.id), [2, 3]);
  assert.deepEqual(getPeriodNodes(events, "month").map((event) => event.id), [1, 2, 3]);
});

test("timeline visibility is cumulative within the selected granularity", () => {
  assert.equal(isInOrBeforePeriod(events[1], events[2], "year"), true);
  assert.equal(isInOrBeforePeriod(events[0], events[2], "year"), false);
  assert.equal(isInOrBeforePeriod(events[1], events[2], "month"), true);
  assert.equal(isInOrBeforePeriod(events[0], events[2], "month"), false);
});

test("dominant emotion uses backend intensity before frequency", () => {
  assert.equal(dominantEmotionFamily([
    { family: "平静", peakIntensity: 3, count: 8 },
    { family: "愉悦", peakIntensity: 5, count: 1 },
  ]).family, "愉悦");
});

test("calendar days preserve backend emotion families and empty dates", () => {
  const days = buildCalendarDays(2025, 0, [{
    date: "2025-01-02",
    families: [{ family: "期待", peakIntensity: 4, count: 2 }],
  }]);
  const januarySecond = days.find((day) => day.date === "2025-01-02");
  const januaryThird = days.find((day) => day.date === "2025-01-03");

  assert.equal(januarySecond.families[0].family, "期待");
  assert.deepEqual(januaryThird.families, []);
});

test("month calendar starts on Monday and fills the final week", () => {
  const days = buildMonthCalendarDays(2025, 6, [{
    date: "2025-07-20",
    families: [
      { family: "焦虑", peakIntensity: 4, count: 1 },
      { family: "平静", peakIntensity: 2, count: 1 },
    ],
  }]);

  assert.equal(days.length, 35);
  assert.equal(days[0].date, "2025-06-30");
  assert.equal(days.at(-1).date, "2025-08-03");
  assert.equal(days.find((day) => day.date === "2025-07-20").families.length, 2);
});
