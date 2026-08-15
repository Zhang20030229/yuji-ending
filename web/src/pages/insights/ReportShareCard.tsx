import { forwardRef } from "react";
import type { ReportContent, ReportEvidence } from "@/api/reports";

/**
 * 分享长图专用卡片：全部内联样式 + 系统字体，避免栅格化时丢失 Tailwind 样式。
 * 默认不渲染原话，也从不渲染任何图片，避免分享时外泄私密内容或污染 canvas。
 */
const ReportShareCard = forwardRef<HTMLDivElement, {
  content: ReportContent;
  evidence: ReportEvidence[];
  periodLabel: string;
  rangeText: string;
  includeQuotes: boolean;
}>(function ReportShareCard({ content, evidence, periodLabel, rangeText, includeQuotes }, ref) {
  return (
    <div ref={ref} style={card}>
      <div style={eyebrow}>{periodLabel} · {rangeText}</div>
      <h1 style={headline}>{content.headline}</h1>
      <p style={summary}>{content.summary}</p>

      <div style={{ marginTop: 28 }}>
        {content.findings.map((finding, index) => (
          <div key={`${finding.title}-${index}`} style={block}>
            <div style={blockTitle}>{finding.title}</div>
            <div style={blockBody}>{finding.observation}</div>
            {includeQuotes ? <Quotes refs={finding.evidenceRefs} evidence={evidence} /> : null}
          </div>
        ))}
      </div>

      {content.smallExperiment ? (
        <div style={experiment}>
          <div style={experimentLabel}>可以尝试的一小步</div>
          <div style={experimentTitle}>{content.smallExperiment.title}</div>
          <div style={blockBody}>{content.smallExperiment.action}</div>
          <div style={hint}>{content.smallExperiment.reflectionQuestion}</div>
        </div>
      ) : null}

      {content.reflectionQuestions.length > 0 ? (
        <div style={{ marginTop: 24 }}>
          <div style={sectionLabel}>可以留意</div>
          {content.reflectionQuestions.map((item) => <div key={item} style={blockBody}>· {item}</div>)}
        </div>
      ) : null}

      {content.uncertainties.length > 0 ? (
        <div style={footnote}>{content.uncertainties.join("；")}</div>
      ) : null}

      <div style={brand}>遇己 · 这份回顾只反映已经留下的记录，不是诊断</div>
    </div>
  );
});

function Quotes({ refs, evidence }: { refs: string[]; evidence: ReportEvidence[] }) {
  const items = evidence.filter((item) => refs.includes(item.ref));
  if (items.length === 0) return null;
  return (
    <div style={{ marginTop: 10 }}>
      {items.map((item) => (
        <div key={item.ref} style={quote}>“{item.text || "未填写文字"}”</div>
      ))}
    </div>
  );
}

const fontFamily = '-apple-system, BlinkMacSystemFont, "PingFang SC", "Hiragino Sans GB", "Microsoft YaHei", sans-serif';
const card: React.CSSProperties = {
  width: 720, padding: 48, boxSizing: "border-box",
  backgroundColor: "#f7f3ea", color: "#2c2a26", fontFamily,
};
const eyebrow: React.CSSProperties = { fontSize: 14, color: "#8a7f6d", letterSpacing: 1 };
const headline: React.CSSProperties = { margin: "16px 0 0", fontSize: 34, lineHeight: 1.35, fontWeight: 600 };
const summary: React.CSSProperties = { margin: "16px 0 0", fontSize: 17, lineHeight: 1.75, color: "#4a453d" };
const block: React.CSSProperties = {
  marginTop: 14, padding: 20, borderRadius: 16, backgroundColor: "#ffffff",
};
const blockTitle: React.CSSProperties = { fontSize: 17, fontWeight: 600 };
const blockBody: React.CSSProperties = { marginTop: 8, fontSize: 15, lineHeight: 1.8, color: "#4a453d" };
const sectionLabel: React.CSSProperties = { fontSize: 13, fontWeight: 600, color: "#8a7f6d", marginBottom: 6 };
const experiment: React.CSSProperties = {
  marginTop: 24, padding: 22, borderRadius: 16,
  backgroundColor: "#efe6d4", border: "1px solid #ddcfb4",
};
const experimentLabel: React.CSSProperties = { fontSize: 13, fontWeight: 600, color: "#8a6d3b" };
const experimentTitle: React.CSSProperties = { marginTop: 10, fontSize: 18, fontWeight: 600 };
const hint: React.CSSProperties = { marginTop: 10, fontSize: 13, lineHeight: 1.7, color: "#7a7365" };
const quote: React.CSSProperties = {
  marginTop: 8, padding: "10px 14px", borderRadius: 12,
  backgroundColor: "#f2ece0", fontSize: 14, lineHeight: 1.75, color: "#4a453d",
};
const footnote: React.CSSProperties = {
  marginTop: 24, paddingTop: 16, borderTop: "1px solid #e2d9c8",
  fontSize: 13, lineHeight: 1.7, color: "#8a7f6d",
};
const brand: React.CSSProperties = { marginTop: 28, fontSize: 12, color: "#a09585" };

export default ReportShareCard;
