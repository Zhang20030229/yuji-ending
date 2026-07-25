import { apiFetch, apiJson } from "./client";

/** 报告中一条带真实原话引用的观察。 */
export interface ReportFinding {
  title: string;
  observation: string;
  evidenceRefs: string[];
  confidence: "High" | "Medium";
}

/** 至少由两个不同日期依据支持的一种可能循环。 */
export interface CbtCycle {
  title: string;
  observation: string;
  evidenceRefs: string[];
}

/** 记录中已经发生且可能有帮助的一种应对。 */
export interface HelpfulResponse {
  observation: string;
  evidenceRefs: string[];
}

/** 基于已有记录提出的一个低风险小实验。 */
export interface SmallExperiment {
  title: string;
  action: string;
  reflectionQuestion: string;
}

/** 子报告与综合心迹共用的内容。 */
export interface ReportContent {
  headline: string;
  summary: string;
  findings: ReportFinding[];
  reflectionQuestions: string[];
  uncertainties: string[];
  cbtCycles: CbtCycle[];
  helpfulResponses: HelpfulResponse[];
  smallExperiment?: SmallExperiment;
}

/** 报告历史卡片。 */
export interface ReportListItem {
  id: number;
  periodType: "Weekly" | "Monthly" | "CustomRange";
  startDate: string;
  endDate: string;
  status: ReportStatus;
  headline?: string;
  completedSectionCount: number;
  createdAt: string;
  updatedAt: string;
}

export type ReportStatus =
  | "Pending"
  | "Running"
  | "Complete"
  | "Partial"
  | "NotEnoughData"
  | "Failed";

/** 页面可以展示的一条完整依据。 */
export interface ReportEvidence {
  ref: string;
  occurredAt: string;
  sourceType: "Conversation" | "Moment";
  text: string;
  imageUrls: string[];
  imageDescriptions: string[];
}

/** 一个独立报告分区。 */
export interface ReportSection {
  kind: "Life" | "Emotion" | "Relationship" | "Recognition";
  status: ReportStatus;
  metrics: Record<string, unknown>;
  content?: ReportContent;
  evidence: ReportEvidence[];
  error?: string;
  canRetry: boolean;
}

/** 完整报告包。 */
export interface ReportDetail {
  id: number;
  periodType: "Weekly" | "Monthly" | "CustomRange";
  startDate: string;
  endDate: string;
  triggerType: "Manual" | "Automatic";
  status: ReportStatus;
  overall?: ReportContent;
  overallEvidence: ReportEvidence[];
  sections: ReportSection[];
  error?: string;
  createdAt: string;
  updatedAt: string;
}

/** 一次 WHO-5 固定计分结果。 */
export interface WellbeingAssessment {
  id: number;
  answers: number[];
  rawScore: number;
  percentageScore: number;
  assessedAt: string;
  interpretation: string;
}

export interface GenerateReportInput {
  preset: "Last7Days" | "Last30Days" | "Custom";
  startDate?: string;
  endDate?: string;
}

export interface ReportsOverview {
  reports: ReportListItem[];
  wellbeing: WellbeingAssessment[];
}

/** 读取报告历史。 */
export function getReports(signal?: AbortSignal) {
  return apiJson<ReportListItem[]>("/reports", { signal });
}

/** 一次读取报告和 WHO-5 历史。 */
export function getReportsOverview(signal?: AbortSignal) {
  return apiJson<ReportsOverview>("/reports/overview", { signal });
}

/** 读取一个报告详情。 */
export function getReport(id: number, signal?: AbortSignal) {
  return apiJson<ReportDetail>(`/reports/${id}`, { signal });
}

/** 创建或覆盖同一日期范围的报告。 */
export function generateReport(input: GenerateReportInput) {
  return apiJson<ReportListItem>("/reports", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

/** 重试一个失败分区或综合心迹。 */
export async function retryReport(id: number, kind: string) {
  await apiFetch(`/reports/${id}/retry?kind=${encodeURIComponent(kind)}`, { method: "POST" });
}

/** 读取 WHO-5 历史。 */
export function getWellbeingAssessments(signal?: AbortSignal) {
  return apiJson<WellbeingAssessment[]>("/reports/wellbeing", { signal });
}

/** 保存完整五题并由服务端固定计分。 */
export function saveWellbeingAssessment(answers: number[]) {
  return apiJson<WellbeingAssessment>("/reports/wellbeing", {
    method: "POST",
    body: JSON.stringify({ answers }),
  });
}
