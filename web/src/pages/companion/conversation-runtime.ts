import { HttpAgent, type RunAgentInput } from "@ag-ui/client";
import type {
  ExportedMessageRepository,
  ThreadAssistantMessagePart,
  ThreadHistoryAdapter,
  ThreadMessage,
} from "@assistant-ui/react";
import {
  captureSessionTurn,
  getSessionConversationPage,
  streamResponse,
  type ConversationPart,
  type ConversationTurn,
  type ConversationTurnPage,
} from "@/api/conversation";
import { readAssetIdFromContentUrl } from "./asset-route";
import { apiUrl } from "@/api/base-url";
import { createId } from "@/lib/utils";

/** 创建只承载 ECHORA 唯一连续对话的 AG-UI Agent。 */
export function createConversationAgent(
  sessionId: string,
  onResponseCommitted?: () => void,
) {
  return new HttpAgent({
    url: apiUrl("/api/conversation/ag-ui"),
    threadId: `conversation-${sessionId}`,
    fetch: (url, request) => runPersistedTurn(sessionId, url, request, onResponseCommitted),
  });
}

/** 从 ECHORA Turn API 水合 assistant-ui；新增消息仍由服务端事务持久化。 */
export function createConversationHistory(sessionId: string): ThreadHistoryAdapter {
  return {
    async load() {
      return toMessageRepository(await getSessionConversationPage(sessionId));
    },
    // ponytail: assistant-ui 只水合显示，持久化由服务端 Turn 事务负责，避免双写。
    append: () => Promise.resolve(),
  };
}

/** 把更早的一页接到 assistant-ui 当前仓库前面，保留当前分支头。 */
export function prependConversationPage(
  current: ExportedMessageRepository,
  page: ConversationTurnPage,
): ExportedMessageRepository {
  const older = toMessageRepository(page);
  if (older.messages.length === 0) return current;
  if (current.messages.length === 0) return older;

  return {
    headId: current.headId,
    messages: [
      ...older.messages,
      { ...current.messages[0], parentId: older.headId ?? older.messages.at(-1)?.message.id ?? null },
      ...current.messages.slice(1),
    ],
  };
}

/**
 * 用服务端最新页替换当前临时尾部，同时保留用户已经向上加载的更早消息。
 * 没有 sequence 的 AG-UI 临时消息会在服务端提交后被稳定 Turn 替换。
 */
export function mergeLatestConversationPage(
  current: ExportedMessageRepository,
  page: ConversationTurnPage,
): ExportedMessageRepository {
  const latest = toMessageRepository(page);
  const firstSequence = page.items[0]?.sequence;
  if (firstSequence === undefined) return current;

  const prefix = current.messages.filter((item) => {
    const sequence = item.message.metadata.custom?.sequence;
    return typeof sequence === "number" && sequence < firstSequence;
  });
  const parentId = prefix.at(-1)?.message.id ?? null;
  const tail = latest.messages.map((item, index) => (
    index === 0 ? { ...item, parentId } : item
  ));
  return {
    headId: latest.headId ?? prefix.at(-1)?.message.id ?? null,
    messages: [...prefix, ...tail],
  };
}

/** 将一页服务端 Turn 转成 assistant-ui 的单分支消息仓库。 */
function toMessageRepository(page: ConversationTurnPage): ExportedMessageRepository {
  const turns = page.items;
  return {
    headId: turns.at(-1)?.id ?? null,
    messages: turns.map((turn, index) => ({
      parentId: turns[index - 1]?.id ?? null,
      message: toThreadMessage(turn, index === 0 && page.hasMore),
    })),
  };
}

/**
 * 新消息先保存 User Turn，再把 ConversationAgent SSE 转换为 assistant-ui 消费的 AG-UI SSE。
 */
async function runPersistedTurn(
  sessionId: string,
  _url: string,
  request: RequestInit,
  onResponseCommitted?: () => void,
) {
  if (typeof request.body !== "string") throw new Error("对话请求缺少可读取的消息。");

  const input = JSON.parse(request.body) as RunAgentInput;
  const ownerInput = getLatestOwnerInput(input);
  if (!ownerInput.text && ownerInput.assetIds.length === 0) throw new Error("消息内容不能为空。");
  const receipt = await captureSessionTurn(sessionId, ownerInput.text, ownerInput.assetIds);
  const sourceTurnId = receipt.turnId;
  const encoder = new TextEncoder();
  const messageId = createId();
  const reasoningMessageId = createId();
  // Tool 挂到本轮 Assistant 消息；reasoning 按 AG-UI 协议使用独立且稳定的消息 ID。
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      let finished = false;
      let reasoningStarted = false;
      let textStarted = false;
      const write = (event: object) => controller.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`));
      const endReasoning = () => {
        if (!reasoningStarted) return;
        write({ type: "REASONING_MESSAGE_END", messageId: reasoningMessageId });
        reasoningStarted = false;
      };
      const endText = () => {
        if (!textStarted) return;
        write({ type: "TEXT_MESSAGE_END", messageId });
        textStarted = false;
      };
      const finish = () => {
        if (finished) return;
        finished = true;
        controller.close();
      };

      write({ type: "RUN_STARTED", threadId: input.threadId, runId: input.runId });

      void streamResponse(
        sourceTurnId,
        (event) => {
          if (finished) return;
          if (event.kind === "reasoning_started" && !reasoningStarted) {
            reasoningStarted = true;
            write({ type: "REASONING_MESSAGE_START", messageId: reasoningMessageId, role: "reasoning" });
          }
          if (event.kind === "reasoning_delta" && event.text) {
            if (!reasoningStarted) {
              reasoningStarted = true;
              write({ type: "REASONING_MESSAGE_START", messageId: reasoningMessageId, role: "reasoning" });
            }
            write({ type: "REASONING_MESSAGE_CONTENT", messageId: reasoningMessageId, delta: event.text });
          }
          if (event.kind === "reasoning_completed") endReasoning();
          if (event.kind === "tool_started" && event.toolCallId) {
            write({
              type: "TOOL_CALL_START",
              toolCallId: event.toolCallId,
              toolCallName: event.toolName,
              parentMessageId: messageId,
            });
            write({ type: "TOOL_CALL_ARGS", toolCallId: event.toolCallId, delta: "{}" });
            write({ type: "TOOL_CALL_END", toolCallId: event.toolCallId });
          }
          if (event.kind === "tool_completed" && event.toolCallId) {
            write({ type: "TOOL_CALL_RESULT", messageId: `tool-result-${event.toolCallId}`, toolCallId: event.toolCallId, content: "" });
          }
          if (event.kind === "text_delta" && event.text) {
            endReasoning();
            if (!textStarted) {
              textStarted = true;
              write({ type: "TEXT_MESSAGE_START", messageId, role: "assistant" });
            }
            write({ type: "TEXT_MESSAGE_CONTENT", messageId, delta: event.text });
          }
          if (event.kind === "completed") {
            endReasoning();
            endText();
            write({ type: "RUN_FINISHED", threadId: input.threadId, runId: input.runId });
            onResponseCommitted?.();
            finish();
          }
          if (event.kind === "failed") {
            endReasoning();
            endText();
            write({
              type: "RUN_ERROR",
              message: event.errorMessage ?? "模型回复失败。",
              code: event.errorCode,
            });
            finish();
          }
        },
        request.signal ?? undefined,
      ).then(() => {
        if (!finished) controller.error(new Error("模型回复流意外结束。"));
      }).catch((error: unknown) => {
        if (!finished) controller.error(error);
      });
    },
  });

  return new Response(stream, {
    headers: {
      "Cache-Control": "no-cache",
      "Content-Type": "text/event-stream; charset=utf-8",
    },
  });
}

/** 只读取本次新输入；客户端携带的旧消息不是服务端可信历史。 */
function getLatestOwnerInput(input: RunAgentInput) {
  const message = input.messages.findLast((item) => item.role === "user");
  if (!message) return { text: "", assetIds: [] };
  if (typeof message.content === "string") return { text: message.content.trim(), assetIds: [] };

  const text = message.content
    .filter((part) => part.type === "text")
    .map((part) => part.text)
    .join("\n")
    .trim();
  const assetIds = message.content.flatMap((part) => {
    if (part.type !== "image") return [];
    // assistant-ui 会把相对受保护 URL 归一化为 data source；这里只识别 ECHORA 自己的路由。
    const assetId = readAssetIdFromContentUrl(part.source.value);
    return assetId ? [assetId] : [];
  });
  return { text, assetIds: [...new Set(assetIds)] };
}

/** 将服务端存档消息转换成 assistant-ui 的稳定消息结构。 */
function toThreadMessage(turn: ConversationTurn, hasEarlier = false): ThreadMessage {
  // 服务端 ordinal 已保证顺序；可见思考和最终回答必须按原顺序恢复。
  const content: ThreadAssistantMessagePart[] = [];
  for (const part of turn.parts) {
    if (part.kind === "reasoning" && part.text) content.push({ type: "reasoning", text: part.text });
    if (part.kind === "text" && part.text) content.push({ type: "text", text: part.text });
    if (part.kind === "tool" && part.toolCallId && part.toolName) content.push({
      type: "tool-call",
      toolCallId: part.toolCallId,
      toolName: part.toolName,
      args: {},
      argsText: "{}",
      result: part.toolStatus === "completed" ? "" : undefined,
      isError: part.toolStatus === "failed",
    });
  }
  const common = {
    id: turn.id,
    createdAt: new Date(turn.createdAt),
    content,
    metadata: { custom: { sequence: turn.sequence, hasEarlier } },
  };

  if (turn.role === "owner") {
    return {
      ...common,
      role: "user",
      content: content.filter((part) => part.type === "text"),
      attachments: turn.parts.flatMap(toAttachment),
    };
  }

  return {
    ...common,
    role: "assistant",
    status: { type: "complete", reason: "stop" },
    metadata: {
      unstable_state: null,
      unstable_annotations: [],
      unstable_data: [],
      steps: [],
      custom: { sequence: turn.sequence, hasEarlier },
    },
  };
}

/** 将已有的受保护图片素材呈现为 assistant-ui 附件。 */
function toAttachment(part: ConversationPart) {
  if (part.kind !== "asset" || !part.assetId || !part.contentUrl || !part.mediaType?.startsWith("image/")) return [];
  return [{
    id: part.assetId,
    type: "image" as const,
    name: "图片",
    contentType: part.mediaType,
    status: { type: "complete" as const },
    content: [{ type: "image" as const, image: part.contentUrl, filename: "图片" }],
  }];
}
