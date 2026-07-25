import { useEffect, useState, type PropsWithChildren } from "react";
import { useAuiState } from "@assistant-ui/react";
import { IconBrain, IconChevronDown } from "@tabler/icons-react";

/**
 * 把 assistant-ui 的连续 reasoning part 收进一个可访问的原生折叠区。
 * 流式思考自动展开，最终回答开始后自动收起；此后完全尊重用户的手动选择。
 */
export function ReasoningDisclosure({ children }: PropsWithChildren) {
  const running = useAuiState((state) => state.message.status?.type === "running");
  const hasAnswer = useAuiState((state) =>
    state.message.parts.some((part) => part.type === "text" && part.text.length > 0),
  );
  const [open, setOpen] = useState(running && !hasAnswer);
  const [answerSeen, setAnswerSeen] = useState(hasAnswer);

  useEffect(() => {
    if (running && !hasAnswer) setOpen(true);
    if (!answerSeen && hasAnswer) {
      setOpen(false);
      setAnswerSeen(true);
    }
  }, [answerSeen, hasAnswer, running]);

  return (
    <details
      open={open}
      onToggle={(event) => setOpen(event.currentTarget.open)}
      className="yuji-reasoning group/reasoning mb-4 text-muted-foreground"
    >
      <summary className="flex min-h-7 cursor-pointer list-none items-center gap-2 text-xs font-medium select-none marker:content-none">
        <IconBrain className="size-4 text-primary" stroke={1.8} aria-hidden="true" />
        <span>{running && !hasAnswer ? "正在思考" : "思考过程"}</span>
        <IconChevronDown
          className="ml-auto size-4 transition-transform group-open/reasoning:rotate-180"
          stroke={1.8}
          aria-hidden="true"
        />
      </summary>
      <div className="mt-2 whitespace-pre-wrap border-l border-primary/20 pl-3 text-xs leading-6 text-muted-foreground">
        {children}
      </div>
    </details>
  );
}

/** 在当前 assistant-ui reasoning part 上下文中呈现模型返回的原文。 */
export function ReasoningText() {
  const text = useAuiState((state) => state.part.type === "reasoning" ? state.part.text : "");
  return text || <span className="animate-pulse">正在整理思路…</span>;
}
