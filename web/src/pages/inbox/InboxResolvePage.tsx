import { useNavigate, useParams } from "react-router-dom";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { EmptyState, PendingCard, usePendingOverview } from "./pending-ui";
import { backwards } from "@/lib/navigation";

/** 「收件箱详情」：桌面下拉与移动端列表都指向这一条待确认项。 */
export default function InboxResolvePage() {
  const { pendingId } = useParams<{ pendingId: string }>();
  const navigate = useNavigate();
  const { items, people, places, loading, error, refresh } = usePendingOverview();
  const candidates = { people, places };
  const id = Number(pendingId);
  const item = items.find((entry) => entry.id === id);
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  function backToInbox() {
    navigate("/app/inbox");
  }

  return <section className="yuji-inbox flex h-full min-h-0 flex-col bg-background" aria-label="收件箱详情">
    <SecondaryPageHeader title="确认详情" subtitle={item?.kind === "Person" ? "人物" : item?.kind === "Place" ? "地点" : "待确认"} onBack={() => backwards(navigate, "/app/inbox")} backLabel="返回待确认列表" scrolled={scrolled} />
    <div className="mx-auto min-h-0 w-full max-w-3xl flex-1 overflow-y-auto px-4 pb-8 pt-4 md:px-7" onScroll={onScroll}>
      {loading ? <EmptyState text="正在载入…" /> : error ? <EmptyState text={error} /> : !item ? <EmptyState text="这条待确认项已经处理过了。" /> : (
        <PendingCard item={item} candidates={item.kind === "Person" ? candidates.people : candidates.places} onChanged={() => { refresh(); backToInbox(); }} />
      )}
    </div>
  </section>;
}
