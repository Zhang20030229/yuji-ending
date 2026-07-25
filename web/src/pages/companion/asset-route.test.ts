import assert from "node:assert/strict";
import test from "node:test";
import { readAssetIdFromContentUrl } from "./asset-route.ts";

test("reads a bigint asset id from the protected content route", () => {
  assert.equal(readAssetIdFromContentUrl("/api/assets/9007199254740993/content"), "9007199254740993");
});

test("rejects UUID, external and look-alike asset routes", () => {
  assert.equal(readAssetIdFromContentUrl("/api/assets/65bff31f-4a75-47d8-a7b6-f08d673b214e/content"), undefined);
  assert.equal(readAssetIdFromContentUrl("https://example.test/api/assets/12/content"), undefined);
  assert.equal(readAssetIdFromContentUrl("/api/assets/12/content/extra"), undefined);
});
