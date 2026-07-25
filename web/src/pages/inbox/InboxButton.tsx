import { useEffect, useRef, useState } from "react";
import { IconInbox, IconMapPin, IconUser } from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { usePendingOverview } from "./pending-ui";

/** 首页收件箱入口：有待确认项时显示红点；桌面端下拉预览，移动端跳转整页。 */
export default function InboxButton() {
  const navigate = useNavigate();
  const { items } = usePendingOverview();
  const [isDesktop, setIsDesktop] = useState(() => window.matchMedia("(min-width: 1024px)").matches);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const query = window.matchMedia("(min-width: 1024px)");
    const onChange = () => setIsDesktop(query.matches);
    query.addEventListener("change", onChange);
    return () => query.removeEventListener("change", onChange);
  }, []);

  useEffect(() => {
    if (!isDesktop) setOpen(false);
  }, [isDesktop]);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  function handleClick() {
    if (isDesktop) {
      setOpen((value) => !value);
      return;
    }
    navigate("/app/inbox");
  }

  function openItem(id: number) {
    setOpen(false);
    navigate(`/app/inbox/${id}`);
  }

  return <div className="inbox-control relative shrink-0" ref={rootRef}>
    <button
      type="button"
      onClick={handleClick}
      aria-label="收件箱"
      aria-haspopup={isDesktop ? "dialog" : undefined}
      aria-expanded={isDesktop ? open : undefined}
      className="relative grid size-10 place-items-center rounded-full border border-primary-soft bg-background text-primary shadow-sm"
    >
      <IconInbox className="size-[18px]" aria-hidden />
      {items.length > 0 ? <span className="absolute right-2 top-2 size-2 rounded-full border border-background bg-destructive" aria-hidden /> : null}
    </button>

    {isDesktop && open ? <div role="dialog" aria-label="收件箱" className="absolute right-0 top-[calc(100%+10px)] z-20 w-[min(360px,calc(100vw-48px))] rounded-2xl border border-border/70 bg-background p-2.5 shadow-lg">
      <div className="flex items-baseline justify-between gap-3 px-2 pb-2.5 pt-1">
        <strong className="text-sm font-semibold">收件箱</strong>
        <span className="text-xs text-muted-foreground">{items.length} 条待确认</span>
      </div>
      {items.length === 0
        ? <p className="px-3 py-4 text-center text-sm text-muted-foreground">没有需要确认的人物或地点。</p>
        : <ul className="grid gap-1.5">{items.slice(0, 5).map((item) => (
          <li key={item.id}>
            <button type="button" onClick={() => openItem(item.id)} className="grid w-full grid-cols-[32px_1fr] items-center gap-2 rounded-xl px-2.5 py-2 text-left hover:bg-secondary/70">
              <span className="grid size-8 place-items-center rounded-lg bg-primary-soft text-primary">{item.kind === "Person" ? <IconUser className="size-4" /> : <IconMapPin className="size-4" />}</span>
              <span className="min-w-0"><strong className="block truncate text-xs font-semibold">{item.mention}</strong><small className="block truncate text-[11px] text-muted-foreground">{item.reason}</small></span>
            </button>
          </li>
        ))}</ul>}
      {items.length > 0 ? <button type="button" onClick={() => { setOpen(false); navigate("/app/inbox"); }} className="mt-1.5 flex min-h-9 w-full items-center justify-center rounded-xl text-xs font-medium text-primary hover:bg-primary-soft/60">查看全部</button> : null}
    </div> : null}
  </div>;
}
