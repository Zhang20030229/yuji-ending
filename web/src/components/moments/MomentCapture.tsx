import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { IconCamera, IconCheck, IconPhoto, IconPlus, IconX } from "@tabler/icons-react";
import { createMoment } from "@/api/moments";
import { discardAsset, uploadImage } from "@/api/conversation";

interface MomentCaptureProps {
  /** 移动端只显示圆形快捷按钮；桌面端显示带文字按钮。 */
  compact?: boolean;
}

/** “一刻”的全局入口：选择或拍摄一张照片后发布，不创建聊天会话。 */
export default function MomentCapture({ compact = false }: MomentCaptureProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File>();
  const [preview, setPreview] = useState<string>();
  const [text, setText] = useState("");
  const [publishing, setPublishing] = useState(false);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState(false);

  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  function selectFile(next?: File) {
    if (!next) return;
    if (!next.type.startsWith("image/")) { setError("请选择一张图片。"); return; }
    if (preview) URL.revokeObjectURL(preview);
    setFile(next);
    setPreview(URL.createObjectURL(next));
    setError(undefined);
  }

  function close() {
    if (publishing) return;
    if (preview) URL.revokeObjectURL(preview);
    setFile(undefined);
    setPreview(undefined);
    setText("");
    setError(undefined);
    if (inputRef.current) inputRef.current.value = "";
  }

  async function publish() {
    if (!file) return;
    setPublishing(true);
    setError(undefined);
    let assetId: string | undefined;
    try {
      const asset = await uploadImage(file);
      assetId = asset.id;
      await createMoment(asset.id, text);
      closeAfterPublish();
      setNotice(true);
      window.setTimeout(() => setNotice(false), 3200);
      window.dispatchEvent(new CustomEvent("echora:moment-created"));
    } catch (reason) {
      if (assetId) await discardAsset(assetId).catch(() => undefined);
      setError(reason instanceof Error ? reason.message : "暂时无法发布这一刻。");
    } finally {
      setPublishing(false);
    }
  }

  function closeAfterPublish() {
    if (preview) URL.revokeObjectURL(preview);
    setFile(undefined);
    setPreview(undefined);
    setText("");
    if (inputRef.current) inputRef.current.value = "";
  }

  return <>
    <input ref={inputRef} type="file" accept="image/*" capture="environment" className="sr-only" onChange={(event) => selectFile(event.target.files?.[0])} />
    <button type="button" onClick={() => inputRef.current?.click()} className={compact
      ? "yuji-moment-button grid size-12 shrink-0 place-items-center rounded-full bg-primary text-primary-foreground transition-transform active:scale-95"
      : "yuji-moment-button flex h-12 w-full items-center justify-center gap-2 rounded-2xl bg-primary text-sm font-semibold text-primary-foreground transition-colors hover:bg-primary/90"} aria-label="记录一刻">
      {compact ? <IconPlus className="size-[25px]" aria-hidden /> : <><IconCamera className="size-5" aria-hidden />记录一刻</>}
    </button>

    {file && preview ? createPortal(<div className="fixed inset-0 z-[70] bg-black/45 p-0 backdrop-blur-sm md:grid md:place-items-center md:p-6" role="dialog" aria-modal="true" aria-labelledby="moment-title">
      <section className="yuji-moment-dialog flex h-full w-full flex-col bg-background md:h-auto md:max-h-[90dvh] md:max-w-[560px] md:overflow-hidden md:rounded-[24px] md:shadow-2xl">
        <header className="flex h-16 shrink-0 items-center justify-between px-4 md:px-5">
          <button type="button" onClick={close} disabled={publishing} className="grid size-11 place-items-center rounded-full hover:bg-secondary" aria-label="取消"><IconX className="size-5" /></button>
          <h2 id="moment-title" className="font-semibold">记录这一刻</h2>
          <button type="button" onClick={() => inputRef.current?.click()} disabled={publishing} className="min-h-11 rounded-full px-3 text-sm font-medium text-primary">更换</button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-5 md:px-6">
          <img src={preview} alt="待发布照片" className="max-h-[52dvh] w-full rounded-2xl bg-secondary object-contain" />
          <label className="mt-5 block">
            <span className="sr-only">写下这一刻</span>
            <textarea value={text} onChange={(event) => setText(event.target.value)} maxLength={5000} rows={4} placeholder="写下这一刻…" className="w-full resize-none rounded-2xl bg-secondary/75 px-4 py-3 text-[15px] leading-6 outline-none placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring/35" />
          </label>
          <p className="mt-3 text-xs text-muted-foreground">原图中的拍摄时间和坐标会随记录保存。</p>
          {error ? <p role="alert" className="mt-3 rounded-xl bg-destructive/10 px-4 py-3 text-sm text-destructive">{error}</p> : null}
        </div>
        <footer className="shrink-0 border-t border-border/60 p-4 pb-[calc(1rem+env(safe-area-inset-bottom))] md:px-6 md:pb-5">
          <button type="button" onClick={() => void publish()} disabled={publishing} className="flex h-12 w-full items-center justify-center gap-2 rounded-2xl bg-primary text-sm font-semibold text-primary-foreground disabled:opacity-60">
            {publishing ? "正在记录…" : <><IconPhoto className="size-5" aria-hidden />发布这一刻</>}
          </button>
        </footer>
      </section>
    </div>, document.body) : null}

    {notice ? <div className="fixed left-1/2 top-5 z-[90] flex -translate-x-1/2 items-center gap-2 rounded-full bg-foreground px-4 py-2.5 text-sm text-background shadow-xl"><IconCheck className="size-4" />已记录，正在整理</div> : null}
  </>;
}
