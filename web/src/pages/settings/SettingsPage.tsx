import "@/styles/combined/index.css";
import { useEffect, useState, type ComponentType, type ReactNode } from "react";
import {
  IconChevronRight,
  IconDownload,
  IconFileText,
  IconKey,
  IconLogout,
  IconMessageCircle,
  IconRefresh,
  IconShieldLock,
  IconTrash,
  IconUser,
} from "@tabler/icons-react";
import { useNavigate } from "react-router-dom";
import { calculateAgeFromBirthMonth, changePassword, deleteAccount, logout, parseBirthMonth, saveProfile, toBirthMonthValue } from "@/api/auth";
import { getRunLogs, retryRunLog, type RunLogItem } from "@/api/run-logs";
import {
  createIMessageBindingCode,
  disconnectIMessage,
  getIMessageBinding,
  type IMessageBinding,
  type IMessageBindingCode,
} from "@/api/imessage";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { useUser } from "@/components/layout/user-context";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { MonthPicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { backwards } from "@/lib/navigation";
import { SafetyNotice } from "@/pages/legal/LegalPages";

interface InstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

type Panel = "profile" | "password" | "imessage" | "runs" | null;

/** 0.4 设置只保留用户能实际控制的账户、运行和 PWA 功能。 */
export default function SettingsPage() {
  const user = useUser();
  const navigate = useNavigate();
  const [panel, setPanel] = useState<Panel>(null);
  const [installPrompt, setInstallPrompt] = useState<InstallPromptEvent | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [deleteConfirmation, setDeleteConfirmation] = useState("");
  const [deleteError, setDeleteError] = useState("");
  const [imessage, setIMessage] = useState<IMessageBinding>();
  const { scrolled, onScroll } = useSecondaryHeaderScroll();

  useEffect(() => {
    const listener = (event: Event) => {
      event.preventDefault();
      setInstallPrompt(event as InstallPromptEvent);
    };
    window.addEventListener("beforeinstallprompt", listener);
    return () => window.removeEventListener("beforeinstallprompt", listener);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getIMessageBinding(controller.signal).then(setIMessage).catch(() => undefined);
    return () => controller.abort();
  }, []);

  async function installApp() {
    if (installPrompt) {
      await installPrompt.prompt();
      await installPrompt.userChoice;
      setInstallPrompt(null);
      return;
    }
    setMessage(/iphone|ipad|ipod/i.test(navigator.userAgent)
      ? "在 Safari 中点“分享”，再选择“添加到主屏幕”。"
      : "请使用浏览器菜单中的“安装应用”或“添加到主屏幕”。");
  }

  async function signOut() {
    setBusy(true);
    try {
      await logout();
      navigate("/access", { replace: true });
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : "退出失败。");
      setBusy(false);
    }
  }

  async function removeAccount() {
    if (deleteConfirmation.trim() !== user.username) {
      setDeleteError("用户名不匹配。");
      return;
    }
    setBusy(true);
    setDeleteError("");
    try {
      await deleteAccount();
      navigate("/access", { replace: true });
    } catch (reason) {
      setDeleteError(reason instanceof Error ? reason.message : "账号删除失败。");
      setBusy(false);
    }
  }

  return (
    <section className="yuji-settings flex h-full min-h-0 flex-col bg-background" aria-labelledby="settings-title">
      <SecondaryPageHeader title="设置" titleId="settings-title" onBack={() => backwards(navigate, "/app/home")} backLabel="返回首页" scrolled={scrolled} />
      <main className="min-h-0 flex-1 overflow-y-auto" onScroll={onScroll}>
        <div className="yuji-settings-grid mx-auto grid w-full max-w-[920px] gap-8 px-5 pb-10 pt-7 md:grid-cols-2 md:px-9">
          <div className="space-y-7">
            <SettingsSection title="账户">
              <SettingsRow icon={IconUser} label="个人资料" value={`${user.displayName} · ${user.gender === "Male" ? "男" : "女"} · ${user.age ?? "—"} 岁`} onClick={() => setPanel("profile")} />
              <SettingsRow icon={IconKey} label="修改密码" onClick={() => setPanel("password")} />
            </SettingsSection>
            <SettingsSection title="应用">
              <SettingsRow icon={IconDownload} label="安装到设备" value="Android / iOS" onClick={() => void installApp()} />
              <SettingsRow
                icon={IconMessageCircle}
                label="连接 iMessage"
                value={!imessage?.enabled ? "暂未开放" : imessage.isBound ? "已连接" : "未连接"}
                onClick={() => setPanel("imessage")}
              />
            </SettingsSection>
          </div>
          <div className="space-y-7">
            <SettingsSection title="整理">
              <SettingsRow icon={IconRefresh} label="运行记录" value="查看失败并重试" onClick={() => setPanel("runs")} />
            </SettingsSection>
            <SettingsSection title="条款与安全">
              <SettingsRow icon={IconShieldLock} label="隐私政策" onClick={() => navigate("/privacy")} />
              <SettingsRow icon={IconFileText} label="用户条款" onClick={() => navigate("/terms")} />
            </SettingsSection>
            <SafetyNotice />
            <SettingsSection title="登录">
              <SettingsRow icon={IconLogout} label={busy ? "正在退出…" : "退出登录"} disabled={busy} onClick={() => void signOut()} />
              <SettingsRow icon={IconTrash} label="删除账号" danger disabled={busy} onClick={() => {
                setDeleteConfirmation("");
                setDeleteError("");
                setDeleteDialogOpen(true);
              }} />
            </SettingsSection>
            {message && <p className="text-sm leading-6 text-muted-foreground" role="status">{message}</p>}
          </div>
        </div>
      </main>

      <ProfilePanel open={panel === "profile"} onOpenChange={(open) => setPanel(open ? "profile" : null)} />
      <PasswordPanel open={panel === "password"} onOpenChange={(open) => setPanel(open ? "password" : null)} />
      <IMessagePanel
        open={panel === "imessage"}
        binding={imessage}
        onBindingChange={setIMessage}
        onOpenChange={(open) => setPanel(open ? "imessage" : null)}
      />
      <RunsPanel open={panel === "runs"} onOpenChange={(open) => setPanel(open ? "runs" : null)} />
      <ConfirmDialog
        open={deleteDialogOpen}
        title="永久删除账号？"
        description="你的全部对话、消息、事件、片段、情绪、认识和图片都会被永久删除。此操作无法撤销。"
        detail={(
          <div className="mt-4">
            <label htmlFor="delete-account-confirmation" className="text-xs font-medium text-foreground">
              输入用户名 <strong className="text-destructive">{user.username}</strong> 以确认
            </label>
            <Input
              id="delete-account-confirmation"
              value={deleteConfirmation}
              onChange={(event) => {
                setDeleteConfirmation(event.target.value);
                if (deleteError) setDeleteError("");
              }}
              autoComplete="off"
              spellCheck={false}
              className="mt-2 h-11 rounded-xl"
              aria-invalid={Boolean(deleteError)}
            />
            {deleteError ? <p className="mt-2 text-xs text-destructive" role="alert">{deleteError}</p> : null}
          </div>
        )}
        confirmLabel="永久删除"
        busyLabel="正在删除…"
        busy={busy}
        confirmDisabled={deleteConfirmation.trim() !== user.username}
        onOpenChange={(open) => {
          setDeleteDialogOpen(open);
          if (!open) {
            setDeleteConfirmation("");
            setDeleteError("");
          }
        }}
        onConfirm={() => void removeAccount()}
      />
    </section>
  );
}

function IMessagePanel({
  open,
  binding,
  onBindingChange,
  onOpenChange,
}: {
  open: boolean;
  binding?: IMessageBinding;
  onBindingChange: (binding: IMessageBinding) => void;
  onOpenChange: (open: boolean) => void;
}) {
  const [code, setCode] = useState<IMessageBindingCode>();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function reload() {
    const current = await getIMessageBinding();
    onBindingChange(current);
    return current;
  }

  useEffect(() => {
    if (!open) return;
    void reload().catch((reason: unknown) => {
      setError(reason instanceof Error ? reason.message : "连接状态读取失败。");
    });
  }, [open]);

  async function createCode() {
    setBusy(true);
    setError("");
    try {
      setCode(await createIMessageBindingCode());
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "绑定码生成失败。");
    } finally {
      setBusy(false);
    }
  }

  async function disconnect() {
    if (!window.confirm("解除后，这个 iMessage 将不能继续访问当前遇己账号。确定解除吗？")) return;
    setBusy(true);
    setError("");
    try {
      await disconnectIMessage();
      setCode(undefined);
      await reload();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "解除连接失败。");
    } finally {
      setBusy(false);
    }
  }

  const activePhone = code?.publicPhone ?? binding?.publicPhone;
  const composeUrl = activePhone && code
    ? `sms:${activePhone}&body=${encodeURIComponent(`绑定 ${code.code}`)}`
    : undefined;

  return <SettingsSheet
    open={open}
    onOpenChange={onOpenChange}
    title="连接 iMessage"
    description="绑定自己的发件身份后，可以直接在信息 App 中和遇己对话。"
  >
    {!binding ? <p className="text-sm text-muted-foreground">正在读取…</p> : !binding.enabled ? (
      <p className="rounded-[12px] bg-secondary px-4 py-3 text-sm text-muted-foreground">iMessage 连接暂未开放。</p>
    ) : binding.isBound ? (
      <div className="space-y-4">
        <div className="rounded-[14px] border border-border/70 p-4">
          <p className="text-sm font-medium text-foreground">已连接</p>
          <p className="mt-1 text-xs text-muted-foreground">{binding.maskedSender}</p>
          {binding.boundAt && <p className="mt-1 text-xs text-muted-foreground">{new Date(binding.boundAt).toLocaleString("zh-CN")}</p>}
        </div>
        <p className="text-sm leading-6 text-muted-foreground">直接向 {binding.publicPhone} 发送文字即可。网页中的 iMessage 历史记录只读。</p>
        <Button variant="outline" className="h-11 w-full text-destructive" disabled={busy} onClick={() => void disconnect()}>
          {busy ? "正在解除…" : "解除连接"}
        </Button>
      </div>
    ) : (
      <div className="space-y-4">
        <p className="text-sm leading-6 text-muted-foreground">生成一次性绑定码，再用你自己的 iMessage 发送指定文字。遇己不会要求 Apple ID 或密码。</p>
        {code && (
          <div className="rounded-[14px] border border-border/70 p-4">
            <p className="text-xs text-muted-foreground">联系管理员获取号码</p>
            <p className="mt-2 select-all text-2xl font-semibold tracking-[0.12em] text-foreground">绑定 {code.code}</p>
            <p className="mt-2 text-xs text-muted-foreground">有效至 {new Date(code.expiresAt).toLocaleTimeString("zh-CN")}</p>
          </div>
        )}
        <Button className="h-11 w-full" disabled={busy} onClick={() => void createCode()}>
          {busy ? "正在生成…" : code ? "重新生成绑定码" : "生成绑定码"}
        </Button>
        {composeUrl && <Button asChild variant="outline" className="h-11 w-full"><a href={composeUrl}>在信息中打开</a></Button>}
      </div>
    )}
    {error && <p className="mt-4 text-sm text-destructive" role="alert">{error}</p>}
  </SettingsSheet>;
}

function ProfilePanel({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const user = useUser();
  const [displayName, setDisplayName] = useState(user.displayName);
  const [gender, setGender] = useState<"Male" | "Female">(user.gender || "Male");
  const [birthMonth, setBirthMonth] = useState(toBirthMonthValue(user.birthYear, user.birthMonth));
  const [aiName, setAiName] = useState(user.aiName);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    setError("");
    try {
      await saveProfile({ displayName, gender, ...parseBirthMonth(birthMonth), aiName });
      window.location.reload();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "资料保存失败。");
      setBusy(false);
    }
  }

  return <SettingsSheet open={open} onOpenChange={onOpenChange} title="个人资料" description="这些信息帮助 AI 正确称呼和理解你。">
    <div className="space-y-4">
      <Field label="称呼"><Input value={displayName} maxLength={30} onChange={(event) => setDisplayName(event.target.value)} /></Field>
      <Field label="性别"><select value={gender} onChange={(event) => setGender(event.target.value as "Male" | "Female")} className="h-10 w-full rounded-[10px] border border-border bg-background px-3 text-sm"><option value="Male">男</option><option value="Female">女</option></select></Field>
      <Field label="出生年月">
        <MonthPicker min={`${new Date().getFullYear() - 120}-01`} max={new Date().toISOString().slice(0, 7)} value={birthMonth} onChange={setBirthMonth} aria-label="选择出生年月" />
        {birthMonth && <p className="mt-1.5 text-xs text-muted-foreground">当前 {calculateAgeFromBirthMonth(birthMonth)} 岁，之后会自动更新</p>}
      </Field>
      <Field label="AI 名字"><Input value={aiName} maxLength={30} onChange={(event) => setAiName(event.target.value)} /></Field>
      {error && <p className="text-sm text-destructive" role="alert">{error}</p>}
      <Button className="h-11 w-full" disabled={busy} onClick={() => void save()}>{busy ? "正在保存…" : "保存资料"}</Button>
    </div>
  </SettingsSheet>;
}

function PasswordPanel({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    setError("");
    try {
      await changePassword(currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      onOpenChange(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "密码修改失败。");
    } finally {
      setBusy(false);
    }
  }

  return <SettingsSheet open={open} onOpenChange={onOpenChange} title="修改密码" description="新密码为 6～18 位，不限制字符组合。">
    <div className="space-y-4">
      <Field label="当前密码"><Input type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} /></Field>
      <Field label="新密码"><Input type="password" autoComplete="new-password" minLength={6} maxLength={18} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} /></Field>
      {error && <p className="text-sm text-destructive" role="alert">{error}</p>}
      <Button className="h-11 w-full" disabled={busy || newPassword.length < 8 || newPassword.length > 16} onClick={() => void save()}>{busy ? "正在保存…" : "保存新密码"}</Button>
    </div>
  </SettingsSheet>;
}

function RunsPanel({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [runs, setRuns] = useState<RunLogItem[]>();
  const [error, setError] = useState("");
  const [retrying, setRetrying] = useState("");

  async function load() {
    try {
      setRuns(await getRunLogs());
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "运行记录读取失败。");
    }
  }

  useEffect(() => { if (open) void load(); }, [open]);

  async function retry(item: RunLogItem) {
    const key = `${item.kind}:${item.id}:${item.stage}`;
    setRetrying(key);
    try {
      await retryRunLog(item);
      await load();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "重试失败。");
    } finally {
      setRetrying("");
    }
  }

  return <SettingsSheet open={open} onOpenChange={onOpenChange} title="运行记录" description="后台整理失败不会影响聊天，可在这里查看并重试。">
    {error && <p className="mb-4 text-sm text-destructive" role="alert">{error}</p>}
    {!runs ? <p className="text-sm text-muted-foreground">正在读取…</p> : runs.length === 0 ? <p className="text-sm text-muted-foreground">还没有整理任务。</p> : (
      <div className="space-y-2">
        {runs.map((run) => {
          const key = `${run.kind}:${run.id}:${run.stage}`;
          return <article key={key} className="rounded-[12px] border border-border/70 p-4">
            <div className="flex items-center justify-between gap-3"><p className="text-sm font-medium">{run.stage}</p><span className="text-xs text-muted-foreground">{run.state === "Failed" ? "失败" : run.state}</span></div>
            <p className="mt-1 text-xs text-muted-foreground">{new Date(run.occurredAt).toLocaleString("zh-CN")} · 尝试 {run.attemptCount} 次</p>
            <p className="mt-2 text-xs leading-5 text-destructive">{run.summary}</p>
            {run.canRetry && <Button variant="outline" size="sm" className="mt-3" disabled={retrying === key} onClick={() => void retry(run)}><IconRefresh className="size-4" aria-hidden />{retrying === key ? "正在重试…" : "重试"}</Button>}
          </article>
        })}
      </div>
    )}
  </SettingsSheet>;
}

function SettingsSheet({ open, onOpenChange, title, description, children }: { open: boolean; onOpenChange: (open: boolean) => void; title: string; description: string; children: ReactNode }) {
  return <Sheet open={open} onOpenChange={onOpenChange}><SheetContent side="right" className="yuji-settings-sheet w-full gap-0 border-border bg-background p-0 sm:max-w-[440px]"><SheetHeader className="px-6 pb-4 pt-7"><SheetTitle className="text-xl">{title}</SheetTitle><SheetDescription>{description}</SheetDescription></SheetHeader><div className="min-h-0 flex-1 overflow-y-auto px-6 pb-7 pt-3">{children}</div></SheetContent></Sheet>;
}

function SettingsSection({ title, children }: { title: string; children: ReactNode }) {
  return <section><h2 className="mb-2 px-1 text-xs font-semibold text-muted-foreground">{title}</h2><div className="yuji-settings-group overflow-hidden rounded-[14px] border border-border/65 bg-background divide-y divide-border/60">{children}</div></section>;
}

function SettingsRow({ icon: Icon, label, value, onClick, disabled, danger }: { icon: ComponentType<{ className?: string; "aria-hidden"?: boolean }>; label: string; value?: string; onClick?: () => void; disabled?: boolean; danger?: boolean }) {
  return <button type="button" disabled={disabled || !onClick} onClick={onClick} className={`yuji-settings-row flex min-h-14 w-full items-center gap-3 px-4 text-left text-sm transition-colors enabled:hover:bg-secondary/55 disabled:cursor-default ${danger ? "text-destructive" : "text-foreground"}`}><Icon className="size-[18px] shrink-0 text-muted-foreground" aria-hidden /><span className="font-medium">{label}</span>{value && <span className="ml-auto min-w-0 truncate text-xs text-muted-foreground">{value}</span>}{onClick && <IconChevronRight className="ml-1 size-4 shrink-0 text-muted-foreground" aria-hidden />}</button>;
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label className="block text-xs font-medium text-muted-foreground">{label}<span className="mt-1.5 block">{children}</span></label>;
}
