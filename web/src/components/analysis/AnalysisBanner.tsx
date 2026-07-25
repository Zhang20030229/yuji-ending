import { useEffect, useState } from "react";
import { IconSparkles } from "@tabler/icons-react";
import { getAnalysisStatus, type AnalysisStatus } from "@/api/analysis";
import { useDataRevision } from "@/components/realtime/DataUpdates";

/** 在拾光或遇己顶部显示真实后台状态，并在整理完成后自动消失。 */
export default function AnalysisBanner({ scope }: { scope: "archive" | "self" }) {
  const [status, setStatus] = useState<AnalysisStatus>();
  const revision = useDataRevision("analysis");
  useEffect(() => {
    let active = true;
    void getAnalysisStatus()
      .then((next) => { if (active) setStatus(next); })
      .catch(() => undefined);
    return () => { active = false; };
  }, [revision]);
  const count = scope === "archive" ? status?.lifeRecordCount ?? 0 : (status?.recognitionCount ?? 0) + (status?.emotionCount ?? 0);
  if (!count) return null;
  return <div className="yuji-analysis-banner mx-auto mt-3 flex w-[calc(100%-2rem)] max-w-[1124px] items-center gap-2 rounded-xl bg-primary-soft px-4 py-3 text-sm text-primary" role="status">
    <IconSparkles className="size-4 animate-pulse" aria-hidden />
    <span>{scope === "archive" ? "拾光" : "遇己"}有内容正在整理</span>
  </div>;
}
