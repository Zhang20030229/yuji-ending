import { useCallback, useRef, useState } from "react";
import { toBlob } from "html-to-image";

/** 分享图导出结果：成功走系统分享或下载，失败退化为复制文字。 */
export type ShareOutcome = "shared" | "downloaded" | "copied" | "failed";

/**
 * 把隐藏的分享卡片栅格化成 PNG 并交给系统分享。
 * iOS Safari 首次栅格化 foreignObject 偶发白图，因此连续渲染两次只取第二次结果。
 */
export function useReportImage(fallbackText: () => string) {
  const cardRef = useRef<HTMLDivElement>(null);
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<ShareOutcome>();

  const share = useCallback(async (fileName: string) => {
    setBusy(true);
    setOutcome(undefined);
    try {
      const node = cardRef.current;
      if (!node) throw new Error("分享卡片还没有准备好。");
      const options = { pixelRatio: 2, cacheBust: true, backgroundColor: "#f7f3ea" };
      await toBlob(node, options);
      const blob = await toBlob(node, options);
      if (!blob) throw new Error("图片生成失败。");
      const file = new File([blob], fileName, { type: "image/png" });
      if (navigator.canShare?.({ files: [file] })) {
        await navigator.share({ files: [file] });
        setOutcome("shared");
        return;
      }
      download(blob, fileName);
      setOutcome("downloaded");
    } catch (reason) {
      if (isUserAbort(reason)) {
        setOutcome(undefined);
        return;
      }
      setOutcome(await copyText(fallbackText()) ? "copied" : "failed");
    } finally {
      setBusy(false);
    }
  }, [fallbackText]);

  return { cardRef, busy, outcome, share, clearOutcome: () => setOutcome(undefined) };
}

function download(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

/** 用户在系统分享面板里取消不算失败，不该退化为复制。 */
function isUserAbort(reason: unknown) {
  return reason instanceof DOMException && reason.name === "AbortError";
}
