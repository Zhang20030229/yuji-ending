import type { Attachment, AttachmentAdapter, CompleteAttachment, PendingAttachment } from "@assistant-ui/react";
import { discardAsset, uploadImage } from "@/api/conversation";
import { readAssetIdFromContentUrl } from "./asset-route";
import { createId } from "@/lib/utils";

/** assistant-ui 图片附件在选中后上传，发送动作只引用已经可靠写入的私有素材。 */
export class EchoraImageAttachmentAdapter implements AttachmentAdapter {
  readonly accept = "image/png,image/jpeg,image/webp,image/heic,image/heif,.heic,.heif";

  async *add({ file }: { file: File }): AsyncGenerator<PendingAttachment, void> {
    const attachment = {
      id: createId(),
      type: "image" as const,
      name: file.name,
      contentType: file.type,
      file,
    };
    yield { ...attachment, status: { type: "running", reason: "uploading", progress: 0 } };

    if (file.size > 25 * 1024 * 1024) {
      yield { ...attachment, status: { type: "incomplete", reason: "error" } };
      return;
    }

    try {
      const receipt = await uploadImage(file);
      yield {
        ...attachment,
        contentType: receipt.mimeType,
        content: [{ type: "image", image: receipt.contentUrl, filename: receipt.fileName }],
        status: { type: "requires-action", reason: "composer-send" },
      };
    } catch {
      // 保留本地预览和输入正文，让用户可以移除后重试，不制造静默丢失。
      yield { ...attachment, status: { type: "incomplete", reason: "error" } };
    }
  }

  async send(attachment: PendingAttachment): Promise<CompleteAttachment> {
    const assetId = getAssetId(attachment);
    if (!assetId || !attachment.content) throw new Error("图片尚未上传完成。");
    return {
      id: assetId,
      type: "image",
      name: attachment.name,
      contentType: attachment.contentType,
      status: { type: "complete" },
      content: attachment.content,
    };
  }

  async remove(attachment: Attachment) {
    const assetId = getAssetId(attachment);
    if (assetId) await discardAsset(assetId);
  }
}

/** 只接受 ECHORA 自己的受保护素材路由，外部 URL 永远不会变成删除目标。 */
function getAssetId(attachment: Attachment) {
  const image = attachment.content?.find((part) => part.type === "image");
  if (image?.type !== "image") return undefined;
  return readAssetIdFromContentUrl(image.image);
}
