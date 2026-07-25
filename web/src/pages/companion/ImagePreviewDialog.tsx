import { useEffect, useState } from "react";
import { IconX } from "@tabler/icons-react";
import { Dialog } from "radix-ui";
import { apiFetch } from "@/api/client";

/** 图片缩略图与可访问的大图预览；Radix 负责焦点圈定、Esc 和焦点归还。 */
export default function ImagePreviewDialog({
  src,
  alt,
  thumbnailClassName,
  interactive = true,
}: {
  src: string;
  alt: string;
  thumbnailClassName: string;
  interactive?: boolean;
}) {
  const { previewSource, failed } = useAuthorizedImageSource(src);
  if (!previewSource) {
    return (
      <span
        className={`grid place-items-center rounded-xl bg-secondary text-[10px] text-muted-foreground ${thumbnailClassName}`}
        role={failed ? "alert" : "status"}
      >
        {failed ? "图片载入失败" : "正在载入…"}
      </span>
    );
  }

  if (!interactive) return <img src={previewSource} alt={alt} className={thumbnailClassName} loading="lazy" />;

  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>
        <button
          type="button"
          className="flex min-h-11 min-w-11 items-center justify-center overflow-hidden rounded-xl bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
          aria-label={`放大查看${alt}`}
        >
          <img src={previewSource} alt={alt} className={thumbnailClassName} loading="lazy" />
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm data-[state=closed]:animate-out data-[state=open]:animate-in" />
        <Dialog.Content className="fixed inset-0 z-50 grid place-items-center p-4 outline-none sm:p-8">
          <Dialog.Title className="sr-only">图片预览</Dialog.Title>
          <img src={previewSource} alt={alt} className="max-h-[calc(100dvh-32px)] max-w-full rounded-xl object-contain sm:max-h-[calc(100dvh-64px)]" />
          <Dialog.Close className="absolute right-4 top-4 grid size-11 place-items-center rounded-full bg-black/55 text-white transition-colors hover:bg-black/75 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/80 sm:right-7 sm:top-7" aria-label="关闭图片预览">
            <IconX className="size-5" aria-hidden />
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** 受保护 API 图片先带 JWT 读取为 Blob；本地 object URL 则直接复用。 */
function useAuthorizedImageSource(source: string) {
  const protectedSource = source.startsWith("/api/");
  const [previewSource, setPreviewSource] = useState(protectedSource ? "" : source);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!source.startsWith("/api/")) {
      setPreviewSource(source);
      setFailed(false);
      return;
    }

    let cancelled = false;
    let objectUrl = "";
    setPreviewSource("");
    setFailed(false);
    void apiFetch(source.slice("/api".length))
      .then((response) => response.blob())
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setPreviewSource(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [source]);

  return { previewSource, failed };
}
