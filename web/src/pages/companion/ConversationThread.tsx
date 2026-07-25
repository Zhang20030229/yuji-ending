import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type TouchEvent,
  type UIEvent,
  type WheelEvent,
} from "react";
import {
  AttachmentPrimitive,
  AuiIf,
  ComposerPrimitive,
  ErrorPrimitive,
  MessagePrimitive,
  ThreadPrimitive,
  groupPartByType,
  useAui,
  useAuiState,
  useThreadRuntime,
} from "@assistant-ui/react";
import {
  IconArrowDown,
  IconArrowUp,
  IconMessagePlus,
  IconPaperclip,
  IconPlayerStopFilled,
  IconX,
} from "@tabler/icons-react";
import { getSessionConversationPage } from "@/api/conversation";
import type { ConversationSession } from "@/api/conversation";
import { Button } from "@/components/ui/button";
import { ReasoningDisclosure, ReasoningText } from "@/hooks/assistant-ui/reasoning";
import ImagePreviewDialog from "./ImagePreviewDialog";
import MarkdownText from "./MarkdownText";
import { prependConversationPage } from "./conversation-runtime";

/** 按 Pencil 对话画板呈现当前产品会话的消息流。 */
export default function ConversationThread({
  sessionId,
  readOnly,
  channel,
  onContinue,
}: {
  sessionId: string;
  readOnly: boolean;
  channel: ConversationSession["channel"];
  onContinue: () => Promise<void>;
}) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const historyIntent = useRef(false);
  const thread = useThreadRuntime();
  const firstSequence = useAuiState((state) => {
    const value = state.thread.messages[0]?.metadata.custom?.sequence;
    return typeof value === "number" ? value : null;
  });
  const hasEarlier = useAuiState((state) => state.thread.messages[0]?.metadata.custom?.hasEarlier === true);
  const isRunning = useAuiState((state) => state.thread.isRunning);
  const [loadingEarlier, setLoadingEarlier] = useState(false);
  const [historyError, setHistoryError] = useState("");

  const loadEarlier = useCallback(async () => {
    if (!hasEarlier || firstSequence === null || loadingEarlier || isRunning) return;
    const viewport = viewportRef.current;
    const previousHeight = viewport?.scrollHeight ?? 0;
    const previousTop = viewport?.scrollTop ?? 0;
    const anchorId = thread.getState().messages[0]?.id;
    const anchorTop = viewport && anchorId
      ? [...viewport.querySelectorAll<HTMLElement>("[data-message-id]")]
          .find((element) => element.dataset.messageId === anchorId)
          ?.getBoundingClientRect().top
      : undefined;
    setLoadingEarlier(true);
    setHistoryError("");
    try {
      const page = await getSessionConversationPage(sessionId, 10, firstSequence);
      thread.import(prependConversationPage(thread.export(), page));
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          if (viewport) {
            const anchor = anchorId
              ? [...viewport.querySelectorAll<HTMLElement>("[data-message-id]")]
                  .find((element) => element.dataset.messageId === anchorId)
              : undefined;
            if (anchorTop !== undefined && anchor) {
              viewport.scrollTop += anchor.getBoundingClientRect().top - anchorTop;
            } else {
              viewport.scrollTop = previousTop + viewport.scrollHeight - previousHeight;
            }
          }
          setLoadingEarlier(false);
        });
      });
    } catch (reason) {
      setHistoryError(reason instanceof Error ? reason.message : "更早的消息载入失败。");
      setLoadingEarlier(false);
    }
  }, [firstSequence, hasEarlier, isRunning, loadingEarlier, sessionId, thread]);

  function handleScroll(event: UIEvent<HTMLDivElement>) {
    const viewport = event.currentTarget;
    if (!historyIntent.current || viewport.scrollTop > 64) return;
    historyIntent.current = false;
    if (viewport.scrollHeight > viewport.clientHeight + 1) void loadEarlier();
  }

  function requestEarlier() {
    historyIntent.current = true;
    requestAnimationFrame(() => {
      if (!historyIntent.current || (viewportRef.current?.scrollTop ?? 65) > 64) return;
      historyIntent.current = false;
      void loadEarlier();
    });
  }

  function handleWheel(event: WheelEvent<HTMLDivElement>) {
    if (event.deltaY < 0) requestEarlier();
  }

  function handleTouchMove(_event: TouchEvent<HTMLDivElement>) {
    requestEarlier();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (["ArrowUp", "PageUp", "Home"].includes(event.key)) requestEarlier();
  }

  return (
    <ThreadPrimitive.Root className="yuji-thread flex min-h-0 flex-1 flex-col bg-background">
      <ThreadPrimitive.Viewport
        ref={viewportRef}
        turnAnchor="bottom"
        autoScroll={!loadingEarlier}
        onScroll={handleScroll}
        onWheel={handleWheel}
        onTouchMove={handleTouchMove}
        onKeyDown={handleKeyDown}
        className="yuji-thread-viewport flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain"
      >
        <div className="yuji-thread-content mx-auto flex w-full max-w-[820px] flex-1 flex-col px-4 py-3 md:px-12 md:py-8">
          <AuiIf condition={(state) => state.thread.isLoading}>
            <div className="my-auto flex justify-center" role="status" aria-label="正在载入对话">
              <span className="size-5 animate-spin rounded-full border-2 border-muted border-t-primary" />
            </div>
          </AuiIf>
          <AuiIf condition={(state) => !state.thread.isLoading && state.thread.messages.length === 0}>
            <p className="my-auto text-center text-sm text-muted-foreground">今天想说点什么？</p>
          </AuiIf>
          {(hasEarlier || historyError) && (
            <div className="mb-5 flex flex-col items-center gap-2 text-center">
              <button
                type="button"
                className="min-h-11 rounded-full px-4 text-xs font-medium text-muted-foreground hover:bg-secondary hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 disabled:cursor-wait disabled:opacity-60"
                onClick={() => void loadEarlier()}
                disabled={loadingEarlier || isRunning}
              >
                {loadingEarlier ? "正在载入…" : "查看更早的消息"}
              </button>
              {historyError && <p className="text-xs text-destructive" role="alert">{historyError}</p>}
            </div>
          )}
          <ThreadPrimitive.Messages>
            {() => <ConversationMessage />}
          </ThreadPrimitive.Messages>
        </div>

        <ThreadPrimitive.ViewportFooter className="yuji-thread-footer sticky bottom-0 mt-auto px-4 pt-2 md:px-9 md:pt-3">
          <ThreadPrimitive.ScrollToBottom asChild behavior="smooth">
            <Button
              type="button"
              variant="outline"
              className="absolute bottom-full left-1/2 mb-2 size-10 -translate-x-1/2 rounded-full bg-background shadow-sm disabled:hidden"
              aria-label="回到底部"
              title="回到底部"
            >
              <IconArrowDown className="size-4" aria-hidden />
            </Button>
          </ThreadPrimitive.ScrollToBottom>
          {readOnly ? (
            <div className="mx-auto flex w-full max-w-[788px] items-center justify-between gap-4 rounded-[16px] border border-border bg-background px-4 py-3 shadow-[0_2px_10px_rgba(0,0,0,0.04)]">
              <div className="min-w-0">
                <p className="text-sm font-medium text-foreground">{channel === "IMessage" ? "来自 iMessage 的对话" : "这是一段历史会话"}</p>
                <p className="mt-0.5 text-xs text-muted-foreground">{channel === "IMessage" ? "网页只展示记录，请回到 iMessage 继续对话。" : "历史内容保持不变；继续聊天会创建新会话。"}</p>
              </div>
              {channel === "Web" && <Button type="button" className="shrink-0 rounded-full" onClick={() => void onContinue()}>
                <IconMessagePlus className="size-4" aria-hidden />
                继续聊
              </Button>}
            </div>
          ) : (
            <Composer />
          )}
        </ThreadPrimitive.ViewportFooter>
      </ThreadPrimitive.Viewport>
    </ThreadPrimitive.Root>
  );
}

/** 根据 assistant-ui 当前消息角色选择唯一的视觉结构。 */
function ConversationMessage() {
  const role = useAuiState((state) => state.message.role);
  return role === "user" ? <OwnerMessage /> : <AssistantMessage />;
}

/** 用户消息使用右对齐品牌色气泡，并保留已有图片素材。 */
function OwnerMessage() {
  return (
    <MessagePrimitive.Root className="yuji-message yuji-message--owner mb-4 flex w-full flex-col items-end gap-2">
      <MessagePrimitive.Attachments components={{ Attachment: OwnerAttachment }} />
      <div className="yuji-owner-bubble max-w-[86%] whitespace-pre-wrap rounded-[18px_18px_6px_18px] bg-primary px-[14px] py-[11px] text-sm leading-[1.6] text-primary-foreground md:max-w-[72%]">
        <MessagePrimitive.Content />
      </div>
      <MessageTime />
    </MessagePrimitive.Root>
  );
}

/** AI 消息保持无气泡排版，让长文本像正文一样自然阅读。 */
function AssistantMessage() {
  return (
    <MessagePrimitive.Root className="yuji-message yuji-message--assistant mb-5 w-full text-sm leading-[1.65] text-foreground">
      <div className="yuji-assistant-bubble min-w-0 max-w-[760px]">
        <MessagePrimitive.GroupedParts
          groupBy={groupPartByType({ reasoning: ["group-reasoning"] })}
          indicator="empty"
        >
          {({ part, children }) => {
            if (part.type === "group-reasoning") return <ReasoningDisclosure>{children}</ReasoningDisclosure>;
            if (part.type === "reasoning") return <ReasoningText />;
            if (part.type === "text") return <MarkdownText />;
            if (part.type === "tool-call") return part.toolUI;
            if (part.type === "data") {
              if (part.dataRendererUI) return part.dataRendererUI;
              const fallback = part.data as { question?: unknown };
              return typeof fallback?.question === "string"
                ? <p className="whitespace-pre-wrap">{fallback.question}</p>
                : null;
            }
            if (part.type === "indicator") {
              return <span className="inline-block size-1.5 animate-pulse rounded-full bg-muted-foreground" aria-label="正在等待模型" />;
            }
            return null;
          }}
        </MessagePrimitive.GroupedParts>
      </div>
      <MessagePrimitive.Error>
        <ErrorPrimitive.Root className="mt-2 rounded-[10px] bg-destructive/8 px-3 py-2 text-sm text-destructive" role="alert">
          <ErrorPrimitive.Message />
        </ErrorPrimitive.Root>
      </MessagePrimitive.Error>
      <MessageTime />
    </MessagePrimitive.Root>
  );
}

/** 呈现历史消息的受保护图片缩略图。 */
function OwnerAttachment() {
  const source = useAuiState((state) => {
    const image = state.attachment.content?.find((part) => part.type === "image");
    return image?.type === "image" ? image.image : undefined;
  });
  if (!source) return null;
  return (
    <AttachmentPrimitive.Root className="block">
      <ImagePreviewDialog
        src={source}
        alt="用户发送的图片"
        thumbnailClassName="size-[132px] object-contain"
      />
    </AttachmentPrimitive.Root>
  );
}

/** 使用消息真实创建时间，避免伪造日期或技术状态文案。 */
function MessageTime() {
  const createdAt = useAuiState((state) => state.message.createdAt);
  if (!createdAt) return null;
  return (
    <time className="mt-1 block text-[10px] leading-none text-muted-foreground" dateTime={createdAt.toISOString()}>
      {createdAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
    </time>
  );
}

/** assistant-ui Composer 负责输入、发送、取消和键盘行为。 */
function Composer() {
  const attachmentsReady = useAuiState((state) => state.composer.attachments.every(
    (attachment) => attachment.status.type === "complete" || attachment.status.type === "requires-action",
  ));

  return (
    <ComposerPrimitive.Root
      onSubmit={(event) => {
        if (!attachmentsReady) event.preventDefault();
      }}
      className="yuji-composer mx-auto flex min-h-[68px] w-full max-w-[788px] flex-col rounded-[22px] border px-[10px] py-2 focus-within:border-ring focus-within:ring-2 focus-within:ring-ring/20"
    >
      <div className="mb-2 flex max-w-full flex-wrap gap-2 empty:hidden">
        <ComposerPrimitive.Attachments components={{ Image: ComposerImageAttachment }} />
      </div>
      <div className="flex items-end gap-2">
        <ComposerPrimitive.AddAttachment asChild>
          <Button type="button" variant="ghost" className="size-11 shrink-0 rounded-xl text-muted-foreground" aria-label="添加图片">
            <IconPaperclip className="size-5" aria-hidden="true" />
          </Button>
        </ComposerPrimitive.AddAttachment>
        <ComposerPrimitive.Input
          rows={1}
          placeholder="继续说点什么…"
          aria-label="消息"
          className="max-h-36 min-h-11 flex-1 resize-none bg-transparent px-1 py-3 text-base leading-5 outline-none placeholder:text-muted-foreground md:text-sm"
        />
        <AuiIf condition={(state) => !state.thread.isRunning}>
          <ComposerPrimitive.Send asChild>
            <Button className="size-11 shrink-0 rounded-full" aria-label="发送" disabled={!attachmentsReady}>
              <IconArrowUp className="size-5" stroke={2.2} aria-hidden="true" />
            </Button>
          </ComposerPrimitive.Send>
        </AuiIf>
        <AuiIf condition={(state) => state.thread.isRunning}>
          <ComposerPrimitive.Cancel asChild>
            <Button className="size-11 shrink-0 rounded-full" aria-label="停止回复">
              <IconPlayerStopFilled className="size-4" aria-hidden="true" />
            </Button>
          </ComposerPrimitive.Cancel>
        </AuiIf>
      </div>
    </ComposerPrimitive.Root>
  );
}

/** 待发送图片直接显示缩略图，上传仍由 assistant-ui 附件状态管理。 */
function ComposerImageAttachment() {
  const aui = useAui();
  const file = useAuiState((state) => state.attachment.file);
  const storedSource = useAuiState((state) => {
    const image = state.attachment.content?.find((part) => part.type === "image");
    return image?.type === "image" ? image.image : undefined;
  });
  const name = useAuiState((state) => state.attachment.name);
  const status = useAuiState((state) => state.attachment.status);
  const localSource = useObjectUrl(file);
  const source = storedSource ?? localSource;
  const uploading = status.type === "running";
  const failed = status.type === "incomplete";
  const [removing, setRemoving] = useState(false);
  const [removeError, setRemoveError] = useState(false);

  async function remove() {
    setRemoving(true);
    setRemoveError(false);
    try {
      await aui.attachment().remove();
    } catch {
      setRemoving(false);
      setRemoveError(true);
    }
  }

  return (
    <AttachmentPrimitive.Root className="relative block" aria-busy={uploading || removing}>
      {source ? (
        <ImagePreviewDialog src={source} alt={name} thumbnailClassName="size-28 object-contain" />
      ) : (
        <span className="grid size-28 place-items-center rounded-xl bg-secondary text-xs text-muted-foreground" role="status">正在准备…</span>
      )}
      {(uploading || failed || removing || removeError) && (
        <span
          className={`absolute inset-x-1 bottom-1 rounded-md px-1.5 py-1 text-center text-[10px] font-medium ${failed || removeError ? "bg-destructive text-destructive-foreground" : "bg-background/90 text-muted-foreground"}`}
          role={failed || removeError ? "alert" : "status"}
        >
          {removeError ? "移除失败，请重试" : failed ? "上传失败，请移除后重试" : removing ? "正在移除…" : "正在上传…"}
        </span>
      )}
      <button
        type="button"
        className="absolute -right-2 -top-2 grid size-10 place-items-center rounded-full bg-background text-muted-foreground shadow-sm hover:bg-secondary hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:cursor-wait disabled:opacity-60"
        aria-label={`移除 ${name}`}
        disabled={uploading || removing}
        onClick={() => void remove()}
      >
        <IconX className="size-4" aria-hidden />
      </button>
    </AttachmentPrimitive.Root>
  );
}

/** 为尚未上传的本地文件建立短生命周期预览地址。 */
function useObjectUrl(file?: File) {
  const [url, setUrl] = useState<string>();
  useEffect(() => {
    if (!file) {
      setUrl(undefined);
      return;
    }
    const next = URL.createObjectURL(file);
    setUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [file]);
  return url;
}
