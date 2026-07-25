import assert from "node:assert/strict";
import test from "node:test";
import {
  mergeCreatedConversationSession,
  pickActiveConversationSession,
} from "./conversation-session-state.ts";

const historical = {
  id: "10",
  title: "最近的历史会话",
  createdAt: "2026-07-25T04:00:00Z",
  updatedAt: "2026-07-25T04:30:00Z",
  status: "Current" as const,
  channel: "Web",
  isWritable: true,
};

const created = {
  id: "11",
  title: "新对话",
  createdAt: "2026-07-25T05:00:00Z",
  updatedAt: "2026-07-25T05:00:00Z",
  status: "Current" as const,
  channel: "Web",
  isWritable: true,
};

test("新会话路由尚未出现在旧列表时不回退渲染历史会话", () => {
  assert.equal(pickActiveConversationSession([historical], created.id), undefined);
});

test("加入新会话时保留历史会话并只归档旧 Current", () => {
  assert.deepEqual(mergeCreatedConversationSession([historical], created), [
    created,
    { ...historical, status: "Archived" },
  ]);
});
