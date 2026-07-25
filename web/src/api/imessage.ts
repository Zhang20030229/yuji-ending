import { apiFetch, apiJson } from "./client";

/** 当前账号的 iMessage 连接状态。 */
export interface IMessageBinding {
  enabled: boolean;
  isBound: boolean;
  maskedSender?: string | null;
  publicPhone?: string | null;
  boundAt?: string | null;
}

/** 一次性绑定码及失效时间。 */
export interface IMessageBindingCode {
  code: string;
  publicPhone: string;
  expiresAt: string;
}

export function getIMessageBinding(signal?: AbortSignal) {
  return apiJson<IMessageBinding>("/imessage/binding", { signal });
}

export function createIMessageBindingCode() {
  return apiJson<IMessageBindingCode>("/imessage/binding/code", { method: "POST" });
}

export async function disconnectIMessage() {
  await apiFetch("/imessage/binding", { method: "DELETE" });
}

/** 让数字分身生成一条关怀，并通过当前账号已绑定的 iMessage 发出。 */
export function sendIMessageCare() {
  return apiJson<{ spaceId: string; messageId?: string | null; sentAt: string }>(
    "/imessage/care",
    { method: "POST" },
  );
}
