import { useNavigate } from "react-router-dom";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { PendingListBody, usePendingOverview } from "./pending-ui";
import { backwards } from "@/lib/navigation";

/** 「收件箱」整页列表：移动端从首页跳转进入，逐条确认人物或地点。 */
export default function InboxPage() {
  const navigate = useNavigate();
  const { items, people, places, loading, error, refresh } = usePendingOverview();
  const candidates = { people, places };
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  return <section className="yuji-inbox flex h-full min-h-0 flex-col bg-background" aria-label="收件箱">
    <SecondaryPageHeader title="待确认" subtitle={`${items.length} 条待处理`} onBack={() => backwards(navigate, "/app/home")} backLabel="返回首页" scrolled={scrolled} />
    <div className="mx-auto min-h-0 w-full max-w-3xl flex-1 overflow-y-auto px-4 pb-8 pt-4 md:px-7" onScroll={onScroll}>
      <p className="mb-4 text-sm leading-6 text-muted-foreground">这些人物或地点需要你确认，帮助遇己更准确地记录。</p>
      <PendingListBody items={items} loading={loading} error={error} candidates={candidates} onChanged={refresh} />
    </div>
  </section>;
}
