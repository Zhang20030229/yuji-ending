import assert from "node:assert/strict";
import test from "node:test";
import { firstMonthWithData } from "../../api/map-data.ts";

test("timeline defaults to first month with data for selected year", () => {
  assert.equal(firstMonthWithData(2025, [12, 2]), "2025-02");
});

test("timeline falls back to January when the year has no data", () => {
  assert.equal(firstMonthWithData(2024, []), "2024-01");
});
