import { apiFetch, apiJson } from "./client";
import { createId } from "@/lib/utils";

/** 一条聊天消息中可渲染的文字、思考、Tool 或图片。 */
export interface ConversationPart {
  id: string;
  kind: "text" | "reasoning" | "tool" | "asset";
  text?: string;
  assetId?: string;
  contentUrl?: string;
  mediaType?: string;
  toolCallId?: string;
  toolName?: string;
  toolStatus?: "completed" | "failed";
}

/** 服务端按一次 User/Assistant Turn 聚合后的聊天消息。 */
export interface ConversationTurn {
  id: string;
  sequence: number;
  role: "owner" | "assistant";
  createdAt: string;
  parts: ConversationPart[];
}

export interface ConversationTurnPage {
  items: ConversationTurn[];
  nextBeforeSequence?: number | null;
  hasMore: boolean;
}

/** 一段独立会话；是否可继续发送由服务端按会话创建日期判断。 */
export interface ConversationSession {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  status: "Current" | "Archived";
  channel: "Web" | "IMessage";
  isWritable: boolean;
}

export interface TurnReceipt {
  turnId: string;
  conversationId: string;
  savedAt: string;
  isDuplicate: boolean;
}

export interface ConversationDeletionImpact {
  conversationId: string;
  title: string;
  messageCount: number;
  fragmentCount: number;
  eventCount: number;
  recognitionCount: number;
  emotionCount: number;
  personRecordCount: number;
  placeRecordCount: number;
}

/** 一张已保存到私有附件存储的图片。 */
export interface AttachmentReceipt {
  id: string;
  fileName: string;
  mimeType: string;
  sizeBytes: number;
  contentUrl: string;
}

export function getSessionConversationPage(
  sessionId: string,
  limit = 10,
  beforeSequence?: number,
  signal?: AbortSignal,
) {
  const query = new URLSearchParams({ limit: String(limit) });
  if (beforeSequence !== undefined) query.set("beforeSequence", String(beforeSequence));
  return apiJson<ConversationTurnPage>(`/conversation/sessions/${sessionId}/turns?${query}`, { signal });
}

export function getConversationSessions(signal?: AbortSignal) {
  return apiJson<ConversationSession[]>("/conversation/sessions", { signal });
}

export function createConversationSession(continuedFromConversationId?: string) {
  return apiJson<ConversationSession>("/conversation/sessions", {
    method: "POST",
    body: JSON.stringify({ continuedFromConversationId }),
  });
}

export function renameConversationSession(id: string, title: string) {
  return apiJson<ConversationSession>(`/conversation/sessions/${id}`, {
    method: "PATCH",
    body: JSON.stringify({ title }),
  });
}

export async function deleteConversationSession(id: string) {
  await apiFetch(`/conversation/sessions/${id}`, { method: "DELETE" });
}

export function getConversationDeletionImpact(id: string) {
  return apiJson<ConversationDeletionImpact>(`/conversation/sessions/${id}/deletion-impact`);
}

/** 原文先可靠落库；服务端随后使用保存的完整会话历史调用 Agent。 */
export function captureSessionTurn(sessionId: string, text: string, assetIds: string[] = []) {
  return apiJson<TurnReceipt>(`/conversation/sessions/${sessionId}/turns`, {
    method: "POST",
    headers: { "Idempotency-Key": createId() },
    body: JSON.stringify({ text: text || undefined, assetIds }),
  });
}

export async function uploadImage(file: File) {
  const body = new FormData();
  body.append("file", file);
  return (await apiFetch("/assets", { method: "POST", body })).json() as Promise<AttachmentReceipt>;
}

export async function discardAsset(assetId: string) {
  await apiFetch(`/assets/${assetId}`, { method: "DELETE" });
}

/** 读取后端 SSE；SSE event 名用于调试，稳定业务类型由 data.kind 提供。 */
export async function streamResponse(
  turnId: string,
  onEvent: (event: ConversationStreamEvent) => void,
  signal?: AbortSignal,
) {
  const response = await apiFetch(`/conversation/turns/${turnId}/responses`, {
    method: "POST",
    signal,
  });
  if (!response.body) throw new Error("浏览器未提供响应流。");

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  while (true) {
    const { value, done } = await reader.read();
    buffer += value ?? "";
    const blocks = buffer.split("\n\n");
    buffer = blocks.pop() ?? "";
    for (const block of blocks) {
      const data = block.split("\n").find((line) => line.startsWith("data: "))?.slice(6);
      if (data) onEvent(JSON.parse(data) as ConversationStreamEvent);
    }
    if (done) break;
  }
}

export interface ConversationStreamEvent {
  kind:
    | "reasoning_started"
    | "reasoning_delta"
    | "reasoning_completed"
    | "tool_started"
    | "tool_completed"
    | "text_delta"
    | "completed"
    | "failed";
  text?: string;
  toolCallId?: string;
  toolName?: string;
  assistantTurnId?: string;
  errorCode?: string;
  errorMessage?: string;
}
