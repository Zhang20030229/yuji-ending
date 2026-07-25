import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AssistantRuntimeProvider, useComposerRuntime } from "@assistant-ui/react";
import { useAgUiRuntime } from "@assistant-ui/react-ag-ui";
import { IconMenu2, IconPlus } from "@tabler/icons-react";
import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { useUser } from "@/components/layout/user-context";
import ConversationThread from "./ConversationThread";
import {
  createConversationHistory,
  createConversationAgent,
  mergeLatestConversationPage,
} from "./conversation-runtime";
import {
  createConversationSession,
  deleteConversationSession,
  getConversationDeletionImpact,
  getConversationSessions,
  getSessionConversationPage,
  renameConversationSession,
  type ConversationDeletionImpact,
  type ConversationSession,
} from "@/api/conversation";
import { EchoraImageAttachmentAdapter } from "./image-attachment-adapter";
import ConversationSessionList from "./ConversationSessionList";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ConversationToolUIs } from "@/hooks/assistant-ui/tool-status";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  mergeCreatedConversationSession,
  pickActiveConversationSession,
} from "./conversation-session-state";

const LAUNCH_SESSION_KEY = "echora.launch-session-id";

/** 动态 AI 伙伴下的多会话聊天入口。 */
export default function CompanionPage() {
  const user = useUser();
  const location = useLocation();
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const [sessions, setSessions] = useState<ConversationSession[]>([]);
  const [sessionsChecked, setSessionsChecked] = useState(false);
  const [sessionBusy, setSessionBusy] = useState(false);
  const [mobileHistoryOpen, setMobileHistoryOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<ConversationSession>();
  const [deleteImpact, setDeleteImpact] = useState<ConversationDeletionImpact>();
  const [deleteImpactLoading, setDeleteImpactLoading] = useState(false);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const deleteImpactRequest = useRef(0);
  const sessionListRequest = useRef(0);
  const requestedSessionId = new URLSearchParams(location.search).get("session");
  const requestedSessionIdRef = useRef(requestedSessionId);
  requestedSessionIdRef.current = requestedSessionId;
  const activeSession = pickActiveConversationSession(sessions, requestedSessionId);
  const routeState = location.state as { composerDraft?: unknown } | null;
  const composerDraft = typeof routeState?.composerDraft === "string" ? routeState.composerDraft : undefined;
  const consumeDraft = useCallback(() => {
    navigate(`${location.pathname}${location.search}`, { replace: true, state: null });
  }, [location.pathname, location.search, navigate]);

  const loadSessions = useCallback(async () => {
    const requestId = ++sessionListRequest.current;
    try {
      let items = await getConversationSessions();
      let launchSessionId = window.sessionStorage.getItem(LAUNCH_SESSION_KEY);
      if (!launchSessionId) {
        const created = await createConversationSession();
        launchSessionId = created.id;
        window.sessionStorage.setItem(LAUNCH_SESSION_KEY, created.id);
        items = [created, ...items.filter((item) => item.id !== created.id)];
        navigate(`${location.pathname}?session=${created.id}`, { replace: true });
      }
      if (requestId !== sessionListRequest.current) return;
      setSessions(items);
      const requestedId = requestedSessionIdRef.current;
      if (items.length > 0 && !items.some((item) => item.id === requestedId)) {
        const preferred = items.find((item) => item.id === launchSessionId && item.status === "Current")
          ?? items.find((item) => item.status === "Current")
          ?? items[0];
        window.sessionStorage.setItem(LAUNCH_SESSION_KEY, preferred.id);
        navigate(`${location.pathname}?session=${preferred.id}`, { replace: true });
      }
    } catch (reason) {
      if (requestId !== sessionListRequest.current) return;
      setError(reason instanceof Error ? reason.message : "会话列表读取失败。");
    } finally {
      if (requestId === sessionListRequest.current) setSessionsChecked(true);
    }
  }, [location.pathname, navigate]);

  useEffect(() => { void loadSessions(); }, [loadSessions]);

  const selectSession = useCallback((id: string) => {
    setMobileHistoryOpen(false);
    navigate(`${location.pathname}?session=${id}`);
  }, [location.pathname, navigate]);

  const createSession = useCallback(async (continuedFromConversationId?: string) => {
    sessionListRequest.current += 1;
    setSessionBusy(true);
    try {
      const created = await createConversationSession(continuedFromConversationId);
      window.sessionStorage.setItem(LAUNCH_SESSION_KEY, created.id);
      setSessions((current) => mergeCreatedConversationSession(current, created));
      selectSession(created.id);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "新对话创建失败。");
    } finally {
      setSessionBusy(false);
    }
  }, [selectSession]);

  const renameSession = useCallback(async (id: string, title: string) => {
    try {
      const changed = await renameConversationSession(id, title);
      setSessions((current) => current.map((item) => item.id === id ? changed : item));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "会话重命名失败。");
      throw reason;
    }
  }, []);

  const requestDeleteSession = useCallback(async (id: string) => {
    const target = sessions.find((item) => item.id === id);
    if (!target) return;
    setDeleteTarget(target);
    setDeleteImpact(undefined);
    setDeleteImpactLoading(true);
    const requestId = ++deleteImpactRequest.current;
    try {
      const impact = await getConversationDeletionImpact(id);
      if (requestId !== deleteImpactRequest.current) return;
      setDeleteImpact(impact);
    } catch (reason) {
      if (requestId !== deleteImpactRequest.current) return;
      setDeleteTarget(undefined);
      setError(reason instanceof Error ? reason.message : "无法读取会话删除影响。");
    } finally {
      if (requestId === deleteImpactRequest.current) setDeleteImpactLoading(false);
    }
  }, [sessions]);

  const confirmDeleteSession = useCallback(async () => {
    if (!deleteTarget || !deleteImpact) return;
    setDeleteBusy(true);
    try {
      await deleteConversationSession(deleteTarget.id);
      const remaining = sessions.filter((item) => item.id !== deleteTarget.id);
      setSessions(remaining);
      setDeleteTarget(undefined);
      setDeleteImpact(undefined);
      if (activeSession?.id === deleteTarget.id) {
        if (remaining[0]) selectSession(remaining[0].id);
        else await createSession();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "会话删除失败。");
    } finally {
      setDeleteBusy(false);
    }
  }, [activeSession?.id, createSession, deleteImpact, deleteTarget, selectSession, sessions]);

  const sessionList = activeSession ? (
    <ConversationSessionList
      sessions={sessions}
      activeId={activeSession.id}
      onSelect={selectSession}
      onRename={renameSession}
      onDelete={requestDeleteSession}
      onCreate={() => createSession()}
    />
  ) : null;

  return (
    <section className="yuji-chat flex h-full min-h-0 bg-background" aria-labelledby="conversation-title">
      <aside className="yuji-session-rail hidden w-[260px] shrink-0 border-r md:block">{sessionList}</aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="yuji-chat-header relative mx-auto flex h-[58px] w-full max-w-[920px] shrink-0 items-center justify-center px-4 md:h-[68px] md:justify-start md:px-7">
          <Button type="button" variant="ghost" size="icon" className="absolute left-2 size-10 md:hidden" onClick={() => setMobileHistoryOpen(true)} aria-label="打开历史会话">
            <IconMenu2 className="size-5" aria-hidden />
          </Button>
          <h1 id="conversation-title" className="max-w-[65%] truncate text-lg font-semibold tracking-[-0.02em] text-foreground md:max-w-none md:text-xl">
            {user.aiName}
          </h1>
          <Button type="button" variant="ghost" size="icon" className="absolute right-2 size-10 md:hidden" disabled={sessionBusy} onClick={() => void createSession()} aria-label="新对话">
            <IconPlus className="size-5" aria-hidden />
          </Button>
        </header>

        {error && (
          <div className="yuji-inline-error mx-auto flex w-[calc(100%-32px)] max-w-[788px] items-center justify-between gap-3 rounded-xl bg-destructive/8 px-3 py-2 text-sm text-destructive" role="alert">
            <span>{error}</span>
            <Button variant="ghost" size="sm" className="shrink-0 text-destructive" onClick={() => setError("")}>关闭</Button>
          </div>
        )}

        {sessionsChecked && activeSession ? (
          <ConversationRuntime
            key={activeSession.id}
            sessionId={activeSession.id}
            readOnly={!activeSession.isWritable}
            channel={activeSession.channel}
            draft={composerDraft}
            onDraftConsumed={consumeDraft}
            onError={setError}
            onSessionUpdated={loadSessions}
            onContinue={() => createSession(activeSession.id)}
          />
        ) : <div className="flex-1" aria-label="正在读取会话" />}
      </div>

      <Sheet open={mobileHistoryOpen} onOpenChange={setMobileHistoryOpen}>
        <SheetContent side="left" className="w-[86%] max-w-[340px] gap-0 border-r-0 p-0" showCloseButton={false}>
          <SheetHeader className="sr-only"><SheetTitle>历史会话</SheetTitle></SheetHeader>
          {sessionList}
        </SheetContent>
      </Sheet>

      <ConfirmDialog
        open={Boolean(deleteTarget)}
        title={`删除“${deleteTarget?.title ?? "这段会话"}”？`}
        description={deleteImpactLoading ? (
          <span className="flex items-center gap-2"><span className="size-4 animate-spin rounded-full border-2 border-primary/20 border-t-primary" />正在计算会一并删除的内容…</span>
        ) : deleteImpact ? (
          <>
            <span className="block">将同时删除以下内容：</span>
            <span className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 rounded-xl bg-secondary/65 p-3 text-xs text-foreground sm:grid-cols-3">
              <span><strong>{deleteImpact.messageCount}</strong> 条消息</span>
              <span><strong>{deleteImpact.fragmentCount}</strong> 个片段</span>
              <span><strong>{deleteImpact.eventCount}</strong> 个事件</span>
              <span><strong>{deleteImpact.recognitionCount}</strong> 条认识</span>
              <span><strong>{deleteImpact.emotionCount}</strong> 条情绪</span>
              <span><strong>{deleteImpact.personRecordCount + deleteImpact.placeRecordCount}</strong> 条人物/地点记录</span>
            </span>
            <span className="mt-3 block font-medium text-destructive">此操作无法撤销。</span>
          </>
        ) : "暂时无法读取删除影响。"}
        busy={deleteBusy}
        confirmDisabled={deleteImpactLoading || !deleteImpact}
        onOpenChange={(open) => {
          if (!open) {
            deleteImpactRequest.current += 1;
            setDeleteTarget(undefined);
            setDeleteImpact(undefined);
            setDeleteImpactLoading(false);
          }
        }}
        onConfirm={() => void confirmDeleteSession()}
      />
    </section>
  );
}

interface ConversationRuntimeProps {
  /** 当前产品会话。 */
  sessionId: string;
  /** 历史会话只能阅读，继续聊天必须新建会话。 */
  readOnly: boolean;
  /** iMessage 会话在网页中只读。 */
  channel: ConversationSession["channel"];
  /** 从遇己“补充”动作带来的输入草稿。 */
  draft?: string;
  /** 草稿写入 assistant-ui 后清理路由状态。 */
  onDraftConsumed: () => void;
  /** 将 Agent 运行错误提升到页面级可恢复提示。 */
  onError: (message: string) => void;
  /** 回答落库后刷新会话标题和活动顺序。 */
  onSessionUpdated: () => Promise<void>;
  /** 从当前历史会话创建一条新的可写会话。 */
  onContinue: () => Promise<void>;
}

/** 将 ECHORA AG-UI Agent 交给 assistant-ui 管理消息、流式状态和输入。 */
function ConversationRuntime({ sessionId, readOnly, channel, draft, onDraftConsumed, onError, onSessionUpdated, onContinue }: ConversationRuntimeProps) {
  const [responseCommitVersion, setResponseCommitVersion] = useState(0);
  const responseCommitted = useCallback(() => {
    setResponseCommitVersion((version) => version + 1);
    void onSessionUpdated();
  }, [onSessionUpdated]);
  const agent = useMemo(
    () => createConversationAgent(sessionId, responseCommitted),
    [responseCommitted, sessionId],
  );
  const history = useMemo(() => createConversationHistory(sessionId), [sessionId]);
  const attachments = useMemo(() => new EchoraImageAttachmentAdapter(), []);
  const runtime = useAgUiRuntime({
    agent,
    // ECHORA 明确展示模型通过 Chat Completions 返回的可见思考内容。
    showThinking: true,
    adapters: { history, attachments },
    onError: (reason) => onError(reason.message),
  });

  useEffect(() => {
    if (responseCommitVersion === 0) return;
    const controller = new AbortController();
    getSessionConversationPage(sessionId, 10, undefined, controller.signal)
      .then((page) => runtime.thread.import(
        mergeLatestConversationPage(runtime.thread.export(), page),
      ))
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          onError(reason instanceof Error ? reason.message : "回复已经保存，但来源暂时无法显示。");
        }
      });
    return () => controller.abort();
  }, [onError, responseCommitVersion, runtime, sessionId]);

  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <ConversationToolUIs />
      {!readOnly && <ComposerDraftBridge draft={draft} onConsumed={onDraftConsumed} />}
      <ConversationThread sessionId={sessionId} readOnly={readOnly} channel={channel} onContinue={onContinue} />
    </AssistantRuntimeProvider>
  );
}

/** 使用 assistant-ui 的 ComposerRuntime 写入草稿，不维护第二份输入状态。 */
function ComposerDraftBridge({ draft, onConsumed }: { draft?: string; onConsumed: () => void }) {
  const composer = useComposerRuntime();
  useEffect(() => {
    if (!draft) return;
    composer.setText(draft);
    onConsumed();
    window.requestAnimationFrame(() => {
      document.querySelector<HTMLTextAreaElement>('textarea[aria-label="消息"]')?.focus();
    });
  }, [composer, draft, onConsumed]);
  return null;
}
