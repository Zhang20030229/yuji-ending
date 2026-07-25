import { useCallback, useEffect, useMemo, useState } from "react";
import {
  IconCalendar,
  IconChevronRight,
  IconFileAnalytics,
  IconHeart,
  IconLoader2,
  IconPlus,
  IconQuote,
  IconRefresh,
} from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "@/api/client";
import {
  generateReport,
  getReport,
  getReportsOverview,
  retryReport,
  saveWellbeingAssessment,
  type GenerateReportInput,
  type ReportContent,
  type ReportDetail,
  type ReportEvidence,
  type ReportListItem,
  type ReportSection,
  type ReportStatus,
  type WellbeingAssessment,
} from "@/api/reports";
import ImagePreviewDialog from "@/pages/companion/ImagePreviewDialog";
import { useDataRevision } from "@/components/realtime/DataUpdates";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";

/** 心迹报告首页与详情；同一组件覆盖 Web 和移动端。 */
export default function ReportsView({ reportId }: { reportId?: string }) {
  const id = Number(reportId);
  return Number.isSafeInteger(id) && id > 0
    ? <ReportDetailView id={id} />
    : <ReportHome />;
}

function ReportHome() {
  const navigate = useNavigate();
  const [reports, setReports] = useState<ReportListItem[]>();
  const [wellbeing, setWellbeing] = useState<WellbeingAssessment[]>();
  const [error, setError] = useState("");
  const [generateOpen, setGenerateOpen] = useState(false);
  const [wellbeingOpen, setWellbeingOpen] = useState(false);
  const revision = useDataRevision("reports");

  const load = useCallback(async () => {
    try {
      const overview = await getReportsOverview();
      setReports(overview.reports);
      setWellbeing(overview.wellbeing);
      setError("");
    } catch (reason) {
      setError(messageOf(reason, "暂时无法载入心迹报告。"));
    }
  }, []);

  useEffect(() => { void load(); }, [load, revision]);

  const latest = reports?.[0];
  return <div className="yuji-report-home mx-auto max-w-5xl pb-8">
    {error && <p className="rounded-xl bg-destructive/8 px-4 py-3 text-sm text-destructive" role="alert">{error}</p>}
    {!reports || !wellbeing ? <ReportSkeleton /> : (
      <>
        <WellbeingCard items={wellbeing} onStart={() => setWellbeingOpen(true)} />

        <div className="mt-6 flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold tracking-[-0.02em]">心迹报告</h2>
            <p className="mt-1 text-sm text-muted-foreground">从生活、情绪、人际和认识四个角度回看一段时间。</p>
          </div>
          <Button className="h-10 rounded-xl" onClick={() => setGenerateOpen(true)}>
            <IconPlus className="size-4" aria-hidden />生成报告
          </Button>
        </div>

        <div className="mt-5 space-y-4">
          {latest ? (
            <button type="button" onClick={() => navigate(`/app/reports/${latest.id}`)} className="yuji-report-card w-full rounded-2xl border border-border/70 bg-background p-5 text-left transition-colors hover:bg-secondary/35 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40">
              <div className="flex items-center justify-between gap-3">
                <span className="text-xs font-medium text-primary">{periodLabel(latest.periodType)}</span>
                <StatusBadge status={latest.status} />
              </div>
              <h3 className="mt-4 text-lg font-semibold leading-7">{latest.headline || statusHeadline(latest.status)}</h3>
              <p className="mt-2 text-sm text-muted-foreground">{formatRange(latest.startDate, latest.endDate)} · {latest.completedSectionCount} 个分区可查看</p>
              <span className="mt-5 flex items-center gap-1 text-xs font-medium text-primary">查看报告<IconChevronRight className="size-4" aria-hidden /></span>
            </button>
          ) : (
            <div className="grid min-h-64 place-items-center rounded-2xl border border-dashed border-border px-8 text-center">
              <div>
                <IconFileAnalytics className="mx-auto size-8 text-muted-foreground" aria-hidden />
                <h3 className="mt-4 text-base font-semibold">还没有心迹报告</h3>
                <p className="mt-2 max-w-sm text-sm leading-6 text-muted-foreground">先留下真实生活、感受和认识，再选择一个日期范围回看。</p>
              </div>
            </div>
          )}

          {reports.length > 1 && <section>
            <h3 className="mb-2 px-1 text-xs font-semibold text-muted-foreground">历史报告</h3>
            <div className="yuji-report-history divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/70">
              {reports.slice(1).map((item) => <button key={item.id} type="button" onClick={() => navigate(`/app/reports/${item.id}`)} className="flex min-h-16 w-full items-center gap-3 px-4 text-left transition-colors hover:bg-secondary/45">
                <IconCalendar className="size-5 shrink-0 text-muted-foreground" aria-hidden />
                <span className="min-w-0 flex-1"><strong className="block truncate text-sm">{item.headline || statusHeadline(item.status)}</strong><small className="mt-1 block text-xs text-muted-foreground">{formatRange(item.startDate, item.endDate)}</small></span>
                <StatusBadge status={item.status} compact />
                <IconChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              </button>)}
            </div>
          </section>}
        </div>
      </>
    )}

    <GenerateReportSheet open={generateOpen} onOpenChange={setGenerateOpen} />
    <WellbeingSheet open={wellbeingOpen} onOpenChange={setWellbeingOpen} onSaved={load} />
  </div>;
}

function ReportDetailView({ id }: { id: number }) {
  const [report, setReport] = useState<ReportDetail>();
  const [error, setError] = useState("");
  const [retrying, setRetrying] = useState("");
  const revision = useDataRevision("reports");

  const load = useCallback(async () => {
    try {
      setReport(await getReport(id));
      setError("");
    } catch (reason) {
      setError(messageOf(reason, "暂时无法载入这份报告。"));
    }
  }, [id]);

  useEffect(() => { void load(); }, [load, revision]);

  async function retry(kind: string) {
    setRetrying(kind);
    setError("");
    try {
      await retryReport(id, kind);
      await load();
    } catch (reason) {
      setError(messageOf(reason, "重试失败。"));
    } finally {
      setRetrying("");
    }
  }

  return <div className="yuji-report-detail mx-auto max-w-5xl pb-8">
    {error && <p className="mt-3 rounded-xl bg-destructive/8 px-4 py-3 text-sm text-destructive" role="alert">{error}</p>}
    {!report ? <ReportSkeleton /> : <>
      <div className="mt-3 flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-xs font-medium text-primary">{periodLabel(report.periodType)} · {report.triggerType === "Automatic" ? "自动回望" : "主动生成"}</p>
          <h2 className="mt-2 text-2xl font-semibold tracking-[-0.03em]">心迹报告</h2>
          <p className="mt-2 text-sm text-muted-foreground">{formatRange(report.startDate, report.endDate)}</p>
        </div>
        <StatusBadge status={report.status} />
      </div>

      {(report.status === "Pending" || report.status === "Running") && <div className="mt-6 flex min-h-36 items-center justify-center gap-3 rounded-2xl bg-secondary/60 text-sm text-muted-foreground" role="status">
        <IconLoader2 className="size-5 animate-spin" aria-hidden />四个角度正在分别回看，完成后会自动更新
      </div>}

      {report.status === "NotEnoughData" && <div className="mt-6 rounded-2xl border border-dashed border-border p-8 text-center"><h3 className="font-semibold">这段时间的记录还不足</h3><p className="mt-2 text-sm leading-6 text-muted-foreground">继续记录真实经历、感受和认识后，再回来生成报告。</p></div>}

      {report.overall ? <div className="mt-6"><ReportContentCard title="综合心迹" content={report.overall} evidence={report.overallEvidence} featured /></div>
        : report.error && report.status !== "Running" && report.status !== "Pending" ? <FailureCard title="综合心迹" error={report.error} busy={retrying === "Overall"} onRetry={() => void retry("Overall")} /> : null}

      <div className="mt-5 grid gap-4 lg:grid-cols-2">
        {report.sections.map((section) => <ReportSectionCard key={section.kind} section={section} retrying={retrying === section.kind} onRetry={() => void retry(section.kind)} />)}
      </div>
    </>}
  </div>;
}

function ReportSectionCard({ section, retrying, onRetry }: { section: ReportSection; retrying: boolean; onRetry: () => void }) {
  const title = sectionLabel(section.kind);
  if (section.status === "Pending" || section.status === "Running")
    return <div className="flex min-h-48 items-center justify-center gap-2 rounded-2xl border border-border/70 text-sm text-muted-foreground"><IconLoader2 className="size-4 animate-spin" />{title}正在生成</div>;
  if (section.status === "NotEnoughData")
    return <div className="rounded-2xl border border-border/70 p-5"><p className="text-xs font-semibold text-muted-foreground">{title}</p><h3 className="mt-4 font-semibold">记录不足</h3><p className="mt-2 text-sm leading-6 text-muted-foreground">{notEnoughText(section.kind)}</p><Metrics kind={section.kind} metrics={section.metrics} /></div>;
  if (section.status === "Failed" || !section.content)
    return <FailureCard title={title} error={section.error || "这个分区生成失败。"} busy={retrying} onRetry={onRetry} />;
  return <ReportContentCard title={title} content={section.content} evidence={section.evidence} metrics={<Metrics kind={section.kind} metrics={section.metrics} />} />;
}

function ReportContentCard({ title, content, evidence, metrics, featured }: { title: string; content: ReportContent; evidence: ReportEvidence[]; metrics?: React.ReactNode; featured?: boolean }) {
  const [openFinding, setOpenFinding] = useState<number>();
  return <article className={`yuji-report-content rounded-2xl border p-5 md:p-6 ${featured ? "border-primary/20 bg-primary-soft/30" : "border-border/70 bg-background"}`}>
    <p className="text-xs font-semibold text-muted-foreground">{title}</p>
    <h3 className="mt-3 text-lg font-semibold leading-7">{content.headline}</h3>
    <p className="mt-2 text-sm leading-6 text-foreground/80">{content.summary}</p>
    {metrics}
    <div className="mt-5 space-y-3">
      {content.findings.map((finding, index) => {
        const sources = evidence.filter(item => finding.evidenceRefs.includes(item.ref));
        const open = openFinding === index;
        return <section key={`${finding.title}-${index}`} className="rounded-xl bg-background/80 p-4 ring-1 ring-border/60">
          <div className="flex items-start gap-3"><span className="mt-1.5 size-2 shrink-0 rounded-full bg-primary" /><div className="min-w-0 flex-1"><h4 className="text-sm font-semibold">{finding.title}</h4><p className="mt-1.5 text-sm leading-6 text-foreground/75">{finding.observation}</p></div></div>
          <button type="button" onClick={() => setOpenFinding(open ? undefined : index)} className="mt-3 flex min-h-9 items-center gap-1.5 rounded-lg text-xs font-medium text-primary">
            <IconQuote className="size-4" aria-hidden />{open ? "收起依据" : `查看依据（${sources.length}）`}
          </button>
          {open && <EvidenceList items={sources} />}
        </section>;
      })}
    </div>
    <CbtReportInsights content={content} evidence={evidence} />
    {content.reflectionQuestions.length > 0 && <div className="mt-5"><p className="text-xs font-semibold text-muted-foreground">可以留意</p><ul className="mt-2 space-y-1.5 text-sm leading-6">{content.reflectionQuestions.map((item) => <li key={item}>· {item}</li>)}</ul></div>}
    {content.uncertainties.length > 0 && <div className="mt-5 border-t border-border/60 pt-4"><p className="text-xs leading-5 text-muted-foreground">{content.uncertainties.join("；")}</p></div>}
  </article>;
}

function CbtReportInsights({ content, evidence }: { content: ReportContent; evidence: ReportEvidence[] }) {
  const cycles = content.cbtCycles ?? [];
  const helpful = content.helpfulResponses ?? [];
  if (cycles.length === 0 && helpful.length === 0 && !content.smallExperiment) return null;
  return <section className="mt-5 space-y-3 border-t border-border/60 pt-5" aria-label="情境与反应观察">
    {cycles.length > 0 && <div>
      <p className="text-xs font-semibold text-muted-foreground">可能的循环</p>
      <div className="mt-2 space-y-2">{cycles.map((cycle) => {
        const sources = evidence.filter((item) => cycle.evidenceRefs.includes(item.ref));
        return <details key={`${cycle.title}-${cycle.evidenceRefs.join("-")}`} className="rounded-xl bg-secondary/60 px-4 py-3">
          <summary className="cursor-pointer text-sm font-semibold">{cycle.title}</summary>
          <p className="mt-2 text-sm leading-6 text-foreground/75">{cycle.observation}</p>
          <EvidenceList items={sources} />
        </details>;
      })}</div>
    </div>}
    {helpful.length > 0 && <div>
      <p className="text-xs font-semibold text-muted-foreground">已经出现的有帮助应对</p>
      <div className="mt-2 space-y-2">{helpful.map((item, index) => {
        const sources = evidence.filter((source) => item.evidenceRefs.includes(source.ref));
        return <details key={`${item.observation}-${index}`} className="rounded-xl bg-secondary/60 px-4 py-3">
          <summary className="cursor-pointer text-sm leading-6">{item.observation}</summary>
          <EvidenceList items={sources} />
        </details>;
      })}</div>
    </div>}
    {content.smallExperiment && <div className="rounded-xl border border-primary/20 bg-primary-soft/35 p-4">
      <p className="text-xs font-semibold text-primary">可以尝试的一小步</p>
      <h4 className="mt-2 text-sm font-semibold">{content.smallExperiment.title}</h4>
      <p className="mt-1.5 text-sm leading-6 text-foreground/80">{content.smallExperiment.action}</p>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{content.smallExperiment.reflectionQuestion}</p>
    </div>}
  </section>;
}

function EvidenceList({ items }: { items: ReportEvidence[] }) {
  if (items.length === 0) return <p className="mt-3 text-xs text-muted-foreground">这条观察没有可展示的依据。</p>;
  return <div className="mt-3 space-y-2">{items.map((item) => <blockquote key={item.ref} className="rounded-xl bg-secondary/70 p-3">
    <p className="text-[11px] text-muted-foreground">{item.ref} · {item.sourceType === "Moment" ? "一刻" : "对话"} · {new Date(item.occurredAt).toLocaleString("zh-CN")}</p>
    <p className="mt-2 text-sm leading-6">“{item.text || "未填写文字"}”</p>
    {item.imageUrls.length > 0 && <div className="mt-3 flex flex-wrap gap-2">{item.imageUrls.map((url, index) => <ImagePreviewDialog key={url} src={url} alt={`依据图片 ${index + 1}`} thumbnailClassName="size-20 object-cover" />)}</div>}
    {item.imageDescriptions.length > 0 && <p className="mt-2 text-xs leading-5 text-muted-foreground">{item.imageDescriptions.join("；")}</p>}
  </blockquote>)}</div>;
}

function Metrics({ kind, metrics }: { kind: ReportSection["kind"]; metrics: Record<string, unknown> }) {
  const items = metricItems(kind, metrics);
  if (items.length === 0) return null;
  return <div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3">{items.map(([label, value]) => <div key={label} className="rounded-xl bg-secondary/60 px-3 py-2.5"><strong className="block text-sm">{value}</strong><span className="mt-0.5 block text-[11px] text-muted-foreground">{label}</span></div>)}</div>;
}

function FailureCard({ title, error, busy, onRetry }: { title: string; error: string; busy: boolean; onRetry: () => void }) {
  return <div className="mt-5 rounded-2xl border border-destructive/20 bg-destructive/5 p-5"><p className="text-xs font-semibold text-destructive">{title}</p><h3 className="mt-3 font-semibold">生成失败</h3><p className="mt-2 text-sm text-muted-foreground">{error}</p><Button variant="outline" size="sm" className="mt-4 rounded-lg" disabled={busy} onClick={onRetry}><IconRefresh className={busy ? "animate-spin" : ""} />{busy ? "正在重试…" : "重试"}</Button></div>;
}

function GenerateReportSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate();
  const [preset, setPreset] = useState<GenerateReportInput["preset"]>("Last7Days");
  const [startDate, setStartDate] = useState(todayInput());
  const [endDate, setEndDate] = useState(todayInput());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const rangeText = useMemo(() => selectedRangeText(preset, startDate, endDate), [endDate, preset, startDate]);

  async function submit() {
    setBusy(true);
    setError("");
    try {
      const report = await generateReport({
        preset,
        startDate: preset === "Custom" ? startDate : undefined,
        endDate: preset === "Custom" ? endDate : undefined,
      });
      onOpenChange(false);
      navigate(`/app/reports/${report.id}`);
    } catch (reason) {
      setError(messageOf(reason, "报告创建失败。"));
    } finally {
      setBusy(false);
    }
  }

  return <Sheet open={open} onOpenChange={onOpenChange}>
    <SheetContent side="bottom" className="max-h-[88dvh] overflow-y-auto rounded-t-[24px] border-border px-5 pb-[calc(24px+env(safe-area-inset-bottom))] pt-2 md:inset-auto md:left-1/2 md:top-1/2 md:w-[520px] md:-translate-x-1/2 md:-translate-y-1/2 md:rounded-2xl md:border">
      <SheetHeader className="px-0 pb-4 pt-5 text-left"><SheetTitle className="text-xl">生成心迹报告</SheetTitle><SheetDescription>选择要回看的日期范围，开始和结束日期都包含。</SheetDescription></SheetHeader>
      <div className="grid gap-2">
        {([
          ["Last7Days", "最近 7 天", "适合快速回看最近一周"],
          ["Last30Days", "最近 30 天", "适合查看较完整的近期变化"],
          ["Custom", "自定义日期", "最多 90 天"],
        ] as const).map(([value, label, detail]) => <label key={value} className={`flex min-h-16 cursor-pointer items-center gap-3 rounded-xl border px-4 ${preset === value ? "border-primary bg-primary-soft/50" : "border-border"}`}><input type="radio" name="report-preset" value={value} checked={preset === value} onChange={() => setPreset(value)} className="accent-primary" /><span><strong className="block text-sm">{label}</strong><small className="mt-1 block text-xs text-muted-foreground">{detail}</small></span></label>)}
      </div>
      {preset === "Custom" && <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2"><label className="grid gap-1.5 text-xs font-medium text-muted-foreground">开始日期<DatePicker value={startDate} max={todayInput()} onChange={setStartDate} aria-label="选择开始日期" /></label><label className="grid gap-1.5 text-xs font-medium text-muted-foreground">结束日期<DatePicker value={endDate} min={startDate} max={todayInput()} onChange={setEndDate} aria-label="选择结束日期" /></label></div>}
      <p className="mt-4 rounded-xl bg-secondary px-4 py-3 text-sm text-muted-foreground">将分析：{rangeText}</p>
      {error && <p className="mt-3 text-sm text-destructive" role="alert">{error}</p>}
      <Button className="mt-5 h-11 w-full rounded-xl" disabled={busy} onClick={() => void submit()}>{busy ? <><IconLoader2 className="animate-spin" />正在创建…</> : "开始生成"}</Button>
    </SheetContent>
  </Sheet>;
}

function WellbeingCard({ items, onStart }: { items: WellbeingAssessment[]; onStart: () => void }) {
  const latest = items[0];
  return <section className="yuji-report-card rounded-2xl border border-border/70 bg-background p-5">
    <div className="flex items-center gap-2"><IconHeart className="size-5 text-primary" aria-hidden /><h3 className="font-semibold">WHO-5 心理幸福感</h3></div>
    <p className="mt-2 text-sm leading-6 text-muted-foreground">五道自评题，回顾过去两周。它与智能体报告并列，不是疾病诊断。</p>
    {latest ? <div className="mt-5 rounded-xl bg-secondary/65 p-4"><div className="flex items-end gap-2"><strong className="text-3xl tracking-[-0.04em]">{latest.percentageScore}</strong><span className="pb-1 text-xs text-muted-foreground">/ 100</span></div><p className="mt-2 text-xs leading-5 text-muted-foreground">{latest.interpretation}</p><p className="mt-2 text-[11px] text-muted-foreground">{new Date(latest.assessedAt).toLocaleDateString("zh-CN")}</p></div> : <p className="mt-5 rounded-xl bg-secondary/65 p-4 text-sm text-muted-foreground">还没有完成过自评。</p>}
    {items.length > 1 && <div className="mt-4 space-y-2">{items.slice(0, 6).reverse().map((item) => <div key={item.id} className="flex items-center gap-2 text-[11px] text-muted-foreground"><span className="w-16">{new Date(item.assessedAt).toLocaleDateString("zh-CN", { month: "numeric", day: "numeric" })}</span><span className="h-1.5 flex-1 overflow-hidden rounded-full bg-secondary"><span className="block h-full rounded-full bg-primary" style={{ width: `${item.percentageScore}%` }} /></span><strong className="w-7 text-right text-foreground">{item.percentageScore}</strong></div>)}</div>}
    <Button variant="outline" className="mt-5 h-10 w-full rounded-xl" onClick={onStart}>{latest ? "再次自评" : "开始自评"}</Button>
    <a href="https://www.who.int/publications/m/item/WHO-UCN-MSD-MHE-2024.01" target="_blank" rel="noreferrer" className="mt-3 block text-center text-[11px] text-muted-foreground underline underline-offset-2">了解 WHO-5 官方说明</a>
  </section>;
}

function WellbeingSheet({ open, onOpenChange, onSaved }: { open: boolean; onOpenChange: (open: boolean) => void; onSaved: () => Promise<void> }) {
  const [answers, setAnswers] = useState<number[]>([-1, -1, -1, -1, -1]);
  const [result, setResult] = useState<WellbeingAssessment>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!open) return;
    setAnswers([-1, -1, -1, -1, -1]);
    setResult(undefined);
    setError("");
  }, [open]);

  async function submit() {
    setBusy(true);
    setError("");
    try {
      setResult(await saveWellbeingAssessment(answers));
      await onSaved();
    } catch (reason) {
      setError(messageOf(reason, "自评提交失败。"));
    } finally {
      setBusy(false);
    }
  }

  return <Sheet open={open} onOpenChange={onOpenChange}>
    <SheetContent side="bottom" className="max-h-[92dvh] overflow-y-auto rounded-t-[24px] border-border px-5 pb-[calc(24px+env(safe-area-inset-bottom))] pt-2 md:inset-auto md:left-1/2 md:top-1/2 md:w-[680px] md:-translate-x-1/2 md:-translate-y-1/2 md:rounded-2xl md:border">
      <SheetHeader className="px-0 pb-4 pt-5 text-left"><SheetTitle className="text-xl">WHO-5 心理幸福感</SheetTitle><SheetDescription>请根据过去两周的实际感受回答。结果用于自我观察，不是疾病诊断。</SheetDescription></SheetHeader>
      {result ? <div className="py-6 text-center"><p className="text-sm text-muted-foreground">本次得分</p><strong className="mt-2 block text-5xl tracking-[-0.05em]">{result.percentageScore}</strong><p className="mx-auto mt-4 max-w-md text-sm leading-6 text-muted-foreground">{result.interpretation}</p><Button className="mt-7 rounded-xl" onClick={() => onOpenChange(false)}>完成</Button></div> : <>
        <div className="space-y-5">{whoQuestions.map((question, index) => <fieldset key={question} className="rounded-xl border border-border/70 p-4"><legend className="px-1 text-sm font-medium">{index + 1}. {question}</legend><div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3">{frequencyOptions.map((option) => <label key={option.score} className={`flex min-h-11 cursor-pointer items-center gap-2 rounded-lg px-3 text-xs ${answers[index] === option.score ? "bg-primary-soft text-primary" : "bg-secondary/65 text-muted-foreground"}`}><input type="radio" name={`who-${index}`} checked={answers[index] === option.score} onChange={() => setAnswers((current) => current.map((value, itemIndex) => itemIndex === index ? option.score : value))} className="accent-primary" />{option.label}</label>)}</div></fieldset>)}</div>
        {error && <p className="mt-4 text-sm text-destructive" role="alert">{error}</p>}
        <Button className="mt-5 h-11 w-full rounded-xl" disabled={busy || answers.some((value) => value < 0)} onClick={() => void submit()}>{busy ? <><IconLoader2 className="animate-spin" />正在提交…</> : "查看结果"}</Button>
      </>}
    </SheetContent>
  </Sheet>;
}

function StatusBadge({ status, compact }: { status: ReportStatus; compact?: boolean }) {
  const label = statusLabel(status);
  const tone = status === "Complete" ? "bg-emerald-50 text-emerald-700" : status === "Partial" ? "bg-amber-50 text-amber-700" : status === "Failed" ? "bg-red-50 text-red-700" : status === "NotEnoughData" ? "bg-secondary text-muted-foreground" : "bg-primary-soft text-primary";
  return <span className={`${compact ? "px-2 py-1 text-[10px]" : "px-2.5 py-1 text-xs"} shrink-0 rounded-full font-medium ${tone}`}>{label}</span>;
}

function ReportSkeleton() {
  return <div className="mt-5 grid gap-4 md:grid-cols-2">{[1, 2, 3, 4].map((item) => <div key={item} className="h-44 animate-pulse rounded-2xl bg-secondary" />)}</div>;
}

function metricItems(kind: ReportSection["kind"], metrics: Record<string, unknown>): Array<[string, string]> {
  const number = (key: string) => String(typeof metrics[key] === "number" ? metrics[key] : 0);
  if (kind === "Life") return [["记录天数", number("recordedDays")], ["片段", number("fragmentCount")], ["事件", number("eventCount")], ["地点", number("placeCount")], ["图片", number("photoCount")]];
  if (kind === "Emotion") {
    const items: Array<[string, string]> = [["情绪记录", number("recordCount")], ["覆盖天数", number("coveredDays")], ["情绪家族", String(Array.isArray(metrics.familyDistribution) ? metrics.familyDistribution.length : 0)]];
    if (typeof metrics.cbtObservationCount === "number" && metrics.cbtObservationCount > 0) {
      items.push(["情境观察", String(metrics.cbtObservationCount)]);
      items.push(["观察天数", number("cbtCoveredDays")]);
    }
    return items;
  }
  if (kind === "Relationship") return [["涉及人物", number("peopleCount")], ["互动天数", number("interactionDays")], ["原话依据", number("evidenceCount")]];
  return [["新增认识", number("recordCount")], ["覆盖天数", number("coveredDays")], ["认识分类", String(Array.isArray(metrics.categoryDistribution) ? metrics.categoryDistribution.length : 0)]];
}

function sectionLabel(kind: ReportSection["kind"]) {
  return { Life: "生活回望", Emotion: "情绪脉络", Relationship: "人际往来", Recognition: "自我认识" }[kind];
}
function notEnoughText(kind: ReportSection["kind"]) {
  return { Life: "至少需要两条不同原话支持的生活记录。", Emotion: "至少需要三条原话，并覆盖两个不同日期。", Relationship: "至少需要两条涉及人物的不同原话。", Recognition: "至少需要两条有依据的认识。" }[kind];
}
function statusLabel(status: ReportStatus) {
  return { Pending: "等待生成", Running: "生成中", Complete: "已完成", Partial: "部分完成", NotEnoughData: "记录不足", Failed: "生成失败" }[status];
}
function statusHeadline(status: ReportStatus) {
  return status === "NotEnoughData" ? "这段时间的记录还不足" : status === "Failed" ? "报告生成失败" : status === "Partial" ? "部分回望已经完成" : status === "Complete" ? "一份新的心迹报告" : "正在生成心迹报告";
}
function periodLabel(type: ReportListItem["periodType"]) {
  return type === "Weekly" ? "一周心迹" : type === "Monthly" ? "一月心迹" : "自定义回望";
}
function formatRange(start: string, end: string) {
  const formatter = new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "short", day: "numeric" });
  return `${formatter.format(new Date(`${start}T00:00:00`))}－${formatter.format(new Date(`${end}T00:00:00`))}`;
}
function selectedRangeText(preset: GenerateReportInput["preset"], start: string, end: string) {
  if (preset === "Custom") return start && end ? formatRange(start, end) : "请选择完整日期";
  const today = new Date();
  const first = new Date(today);
  first.setDate(today.getDate() - (preset === "Last7Days" ? 6 : 29));
  return formatRange(toDateInput(first), toDateInput(today));
}
function messageOf(reason: unknown, fallback: string) {
  return reason instanceof ApiError || reason instanceof Error ? reason.message : fallback;
}
function todayInput() { return toDateInput(new Date()); }
function toDateInput(date: Date) { const offset = date.getTimezoneOffset(); return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 10); }

const whoQuestions = [
  "我感到心情愉快，情绪良好",
  "我感到平静和放松",
  "我感到充满活力",
  "我醒来时感到清新，休息充分",
  "我的日常生活中充满了有趣的事情",
];
const frequencyOptions = [
  { score: 5, label: "所有时间" },
  { score: 4, label: "大部分时间" },
  { score: 3, label: "超过一半时间" },
  { score: 2, label: "少于一半时间" },
  { score: 1, label: "有些时候" },
  { score: 0, label: "任何时候都没有" },
];
