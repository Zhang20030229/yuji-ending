import { apiFetch, apiJson } from "./client";

/** 一条独立于聊天会话的照片记录。 */
export interface MomentItem {
  id: number;
  attachmentId: number;
  text?: string;
  title?: string;
  summary?: string;
  keywords: string[];
  capturedAt: string;
  publishedAt: string;
  locationName?: string;
  locationAddress?: string;
  province?: string;
  city?: string;
  status: "Pending" | "Running" | "Succeeded" | "Failed";
  errorMessage?: string;
  imageUrl: string;
  imageDescription?: string;
}

/** 发布一张已经上传的照片；整理在后台独立执行。 */
export function createMoment(attachmentId: string, text: string) {
  return apiJson<MomentItem>("/moments", {
    method: "POST",
    body: JSON.stringify({ attachmentId: Number(attachmentId), text: text.trim() || null }),
  });
}

/** 读取全部一刻，按发布时间倒序排列。 */
export function getMoments(signal?: AbortSignal) {
  return apiJson<MomentItem[]>("/moments", { signal });
}

/** 重试一条失败的一刻。 */
export async function retryMoment(id: number) {
  await apiFetch(`/moments/${id}/retry`, { method: "POST" });
}

/** 删除原始一刻和仅由它产生的派生记录。 */
export async function deleteMoment(id: number) {
  await apiFetch(`/moments/${id}`, { method: "DELETE" });
}
