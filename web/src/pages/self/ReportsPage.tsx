import { useNavigate, useParams } from "react-router-dom";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { backwards } from "@/lib/navigation";
import ReportsView from "./ReportsView";

/** 「心迹」独立入口页：首页「查看报告」与事件报告详情都指向这里。 */
export default function ReportsPage() {
  const { itemId } = useParams<{ itemId?: string }>();
  const navigate = useNavigate();
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  return <section className="yuji-self flex h-full min-h-0 flex-col bg-background" aria-label="报告">
    <SecondaryPageHeader title={itemId ? "报告详情" : "心迹"} onBack={() => backwards(navigate, itemId ? "/app/reports" : "/app/home")} backLabel={itemId ? "返回报告列表" : "返回首页"} scrolled={scrolled} />
    <div className="mx-auto min-h-0 w-full max-w-[1180px] flex-1 overflow-y-auto px-4 pb-8 pt-4 md:px-7 md:pt-5" onScroll={onScroll}>
      <ReportsView reportId={itemId} />
    </div>
  </section>;
}
