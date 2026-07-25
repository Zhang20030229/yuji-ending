import { useEffect, useState } from "react";
import {
  IconCheck,
  IconEdit,
  IconMessageCircle,
  IconPlus,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import type { ConversationSession } from "@/api/conversation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

interface ConversationSessionListProps {
  sessions: ConversationSession[];
  activeId: string;
  onSelect: (id: string) => void;
  onRename: (id: string, title: string) => Promise<void>;
  onDelete: (id: string) => Promise<void>;
  onCreate: () => Promise<void>;
}

/** 桌面侧栏和移动历史面板共用的产品会话列表。 */
export default function ConversationSessionList({
  sessions,
  activeId,
  onSelect,
  onRename,
  onDelete,
  onCreate,
}: ConversationSessionListProps) {
  const [editingId, setEditingId] = useState<string>();
  const [draft, setDraft] = useState("");

  useEffect(() => {
    setEditingId(undefined);
    setDraft("");
  }, [activeId]);

  function beginRename(session: ConversationSession) {
    setEditingId(session.id);
    setDraft(session.title);
  }

  async function saveRename() {
    if (!editingId || !draft.trim()) return;
    try {
      await onRename(editingId, draft.trim());
      setEditingId(undefined);
    } catch {
      // 页面级错误条已经解释失败原因；保留输入框，方便用户修改后重试。
    }
  }

  return (
    <div className="yuji-session-list flex h-full min-h-0 flex-col">
      <div className="shrink-0 p-3">
        <Button type="button" className="h-10 w-full rounded-xl" onClick={() => void onCreate()}>
          <IconPlus className="size-4" aria-hidden />新对话
        </Button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-4 pt-3" aria-label="历史会话">
        {sessions.map((session) => {
          const active = session.id === activeId;
          if (editingId === session.id) {
            return (
              <div key={session.id} className="mb-1 flex items-center gap-1 rounded-[10px] bg-white p-1.5 shadow-sm">
                <Input
                  autoFocus
                  value={draft}
                  maxLength={120}
                  className="h-8 border-0 bg-transparent px-2 shadow-none focus-visible:ring-0"
                  onChange={(event) => setDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") void saveRename();
                    if (event.key === "Escape") setEditingId(undefined);
                  }}
                />
                <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => void saveRename()} aria-label="保存标题">
                  <IconCheck className="size-4" aria-hidden />
                </Button>
                <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => setEditingId(undefined)} aria-label="取消重命名">
                  <IconX className="size-4" aria-hidden />
                </Button>
              </div>
            );
          }

          return (
            <div
              key={session.id}
              className={[
                "yuji-session-row mb-1 flex min-h-11 items-center rounded-[14px] transition-colors",
                active ? "bg-primary-soft text-primary" : "hover:bg-primary-soft/55",
              ].join(" ")}
            >
              <button
                type="button"
                className="flex min-w-0 flex-1 items-center gap-2.5 px-2.5 py-2 text-left text-sm focus-visible:outline-none"
                onClick={() => onSelect(session.id)}
              >
                <IconMessageCircle className="size-[17px] shrink-0 text-muted-foreground" aria-hidden />
                <span className="min-w-0 truncate">{session.title}</span>
                {session.channel === "IMessage" && <span className="shrink-0 rounded-full bg-secondary px-1.5 py-0.5 text-[9px] text-muted-foreground">iMessage</span>}
              </button>

              {active && (
                <div className="flex shrink-0 items-center pr-1">
                  <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => beginRename(session)} aria-label={`重命名${session.title}`}>
                    <IconEdit className="size-[15px]" aria-hidden />
                  </Button>
                  <Button type="button" variant="ghost" size="icon" className="size-8 text-muted-foreground hover:text-destructive" onClick={() => void onDelete(session.id)} aria-label={`删除${session.title}`}>
                    <IconTrash className="size-[15px]" aria-hidden />
                  </Button>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
