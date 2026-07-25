import { makeAssistantToolUI, type ToolCallMessagePartProps } from "@assistant-ui/react";
import { IconAlertCircle, IconCheck, IconLoader2, IconSearch } from "@tabler/icons-react";

const labels = {
  get_current_time: ["正在查询当前时间…", "已完成当前时间的查询", "查询当前时间失败"],
  GetCurrentTime: ["正在查询当前时间…", "已完成当前时间的查询", "查询当前时间失败"],
  search_life_records: ["正在查询相关记录…", "已完成相关记录的查询", "查询相关记录失败"],
  search_self_records: ["正在查询相关认识…", "已完成相关认识的查询", "查询相关认识失败"],
} as const;

type KnownToolName = keyof typeof labels;

/** 呈现一行简洁的中文 Tool 执行状态，不暴露参数和私有查询结果。 */
function ToolStatusRow({ status, toolName }: ToolCallMessagePartProps) {
  const copy = labels[toolName as KnownToolName] ?? ["正在使用工具…", "工具已完成", "工具执行失败"];
  const failed = status.type === "incomplete";
  const running = status.type === "running" || status.type === "requires-action";
  const Icon = failed ? IconAlertCircle : running ? IconLoader2 : IconCheck;
  return (
    <div
      className={`yuji-tool-status my-2 flex min-h-10 items-center gap-2 rounded-xl border px-3 py-2 text-xs ${failed ? "border-destructive/20 bg-destructive/5 text-destructive" : "border-border/70 bg-secondary/45 text-muted-foreground"}`}
      role={failed ? "alert" : "status"}
    >
      {running ? <Icon className="size-4 animate-spin" aria-hidden /> : <Icon className="size-4" aria-hidden />}
      <span>{copy[failed ? 2 : running ? 0 : 1]}</span>
      {!failed && <IconSearch className="ml-auto size-3.5 opacity-50" aria-hidden />}
    </div>
  );
}

const TimeToolUI = makeAssistantToolUI({ toolName: "get_current_time", render: ToolStatusRow });
const LegacyTimeToolUI = makeAssistantToolUI({ toolName: "GetCurrentTime", render: ToolStatusRow });
const LifeRecordSearchToolUI = makeAssistantToolUI({ toolName: "search_life_records", render: ToolStatusRow });
const SelfRecordSearchToolUI = makeAssistantToolUI({ toolName: "search_self_records", render: ToolStatusRow });

/** 注册 ConversationAgent 当前允许调用的三个中文 Tool UI。 */
export function ConversationToolUIs() {
  return <><TimeToolUI /><LegacyTimeToolUI /><LifeRecordSearchToolUI /><SelfRecordSearchToolUI /></>;
}
