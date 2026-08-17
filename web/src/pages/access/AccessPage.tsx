import { useEffect, useState, type FormEvent } from "react";
import { IconArrowRight, IconEye, IconEyeOff } from "@tabler/icons-react";
import { useNavigate, Link } from "react-router-dom";
import { calculateAgeFromBirthMonth, getMe, hasPendingOnboarding, login, parseBirthMonth, register, saveProfile } from "@/api/auth";
import { hasAccessToken } from "@/api/client";
import { Button } from "@/components/ui/button";
import { MonthPicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type View = "login" | "register" | "profile";

/** 统一访问入口：登录、注册和首次资料。 */
export default function AccessPage() {
  const navigate = useNavigate();
  const [view, setView] = useState<View>("login");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!hasAccessToken()) {
      setLoading(false);
      return;
    }
    getMe().then((user) => {
      if (user.profileCompleted) navigate(hasPendingOnboarding() ? "/onboarding" : "/app/home", { replace: true });
      else setView("profile");
    }).catch(() => undefined).finally(() => setLoading(false));
  }, [navigate]);

  if (loading) {
    return <main className="grid min-h-dvh place-items-center bg-background" aria-label="正在载入遇己" />;
  }

  return (
    <main className="yuji-access min-h-dvh bg-background md:grid md:grid-cols-2">
      <section className="yuji-access__decor relative hidden overflow-hidden px-12 py-10 md:flex md:flex-col" aria-label="遇己">
        <Brand />
        <div className="my-auto max-w-[480px]">
          <div className="relative mb-10 size-44" aria-hidden>
            <span className="absolute inset-0 rounded-full border border-primary/15" />
            <span className="absolute inset-6 rounded-full border border-primary/25" />
            <span className="absolute inset-[52px] rounded-full bg-primary-soft" />
            <span className="absolute inset-[78px] rounded-full bg-primary" />
          </div>
          <p className="max-w-[420px] text-[34px] font-semibold leading-[1.18] tracking-[-0.045em] text-foreground">
            把每天说过的话，慢慢整理成人生。
          </p>
          <p className="mt-5 max-w-sm text-[15px] leading-7 text-muted-foreground">
            遇己会记下事件与情绪，等你需要时，再陪你找回来。
          </p>
        </div>
        <p className="text-xs text-muted-foreground">让每天的经历，慢慢长成你。</p>
      </section>

      <section className="yuji-access__panel flex min-h-dvh items-center px-6 py-8 sm:px-10 md:px-[clamp(56px,8vw,120px)]">
        <div className="yuji-access__form mx-auto w-full max-w-[420px]">
          <div className="mb-12 md:hidden"><Brand /></div>
          {view === "login" && <LoginForm onRegister={() => { setError(""); setView("register"); }} onProfile={() => setView("profile")} error={error} setError={setError} onDone={() => navigate(hasPendingOnboarding() ? "/onboarding" : "/app/home", { replace: true })} />}
          {view === "register" && <RegisterForm onLogin={() => { setError(""); setView("login"); }} onCreated={() => setView("profile")} error={error} setError={setError} />}
          {view === "profile" && <ProfileForm error={error} setError={setError} onDone={() => navigate(hasPendingOnboarding() ? "/onboarding" : "/app/home", { replace: true })} />}
        </div>
      </section>
    </main>
  );
}

function LoginForm({ onRegister, onProfile, onDone, error, setError }: {
  onRegister: () => void;
  onProfile: () => void;
  onDone: () => void;
  error: string;
  setError: (value: string) => void;
}) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true); setError("");
    try {
      await login(username.trim(), password);
      const user = await getMe();
      if (user.profileCompleted) onDone(); else onProfile();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "登录失败。");
    } finally { setBusy(false); }
  }

  return <form onSubmit={submit}>
    <AccessHeading title="欢迎回来" description="继续和你的记忆伙伴说说今天。" />
    <div className="mt-9 space-y-5">
      <Field label="账号" id="login-username"><Input id="login-username" value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" autoFocus required className={inputClass} /></Field>
      <PasswordField id="login-password" value={password} setValue={setPassword} visible={visible} setVisible={setVisible} autoComplete="current-password" />
    </div>
    <AccessError message={error} />
    <aside className="mt-6 rounded-xl border border-border/70 bg-secondary/45 px-4 py-3" aria-label="演示账号">
      <p className="text-xs text-muted-foreground">演示账号</p>
      <p className="mt-1 text-sm font-medium text-foreground">账号：demo　密码：888888</p>
    </aside>
    <Button disabled={busy} className={`mt-4 ${actionClass}`}>{busy ? "正在登录…" : <>登录<IconArrowRight /></>}</Button>
    <button type="button" onClick={onRegister} className="mt-5 min-h-11 w-full text-sm text-muted-foreground hover:text-foreground">没有账号？创建账号</button>
  </form>;
}

function RegisterForm({ onLogin, onCreated, error, setError }: {
  onLogin: () => void;
  onCreated: () => void;
  error: string;
  setError: (value: string) => void;
}) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true); setError("");
    try {
      await register(username.trim(), password);
      onCreated();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "注册失败。");
    } finally { setBusy(false); }
  }

  return <form onSubmit={submit}>
    <AccessHeading title="创建账号" description="注册后再补充最少的个人资料。" />
    <div className="mt-9 space-y-5">
      <Field label="账号" id="register-username"><Input id="register-username" minLength={3} maxLength={20} value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" placeholder="中文、字母、数字或下划线" autoFocus required className={inputClass} /></Field>
      <PasswordField id="register-password" value={password} setValue={setPassword} visible={visible} setVisible={setVisible} autoComplete="new-password" />
    </div>
    <AccessError message={error} />
    <Button disabled={busy} className={`mt-8 ${actionClass}`}>{busy ? "正在创建…" : <>继续<IconArrowRight /></>}</Button>
    <button type="button" onClick={onLogin} className="mt-5 min-h-11 w-full text-sm text-muted-foreground hover:text-foreground">已有账号？返回登录</button>
    <p className="mt-6 text-center text-[11px] leading-5 text-muted-foreground">
      创建账号即表示你已阅读并同意
      <Link to="/terms" className="underline">《用户条款》</Link>
      与
      <Link to="/privacy" className="underline">《隐私政策》</Link>
      。遇己不提供医疗或心理诊断服务。
    </p>
  </form>;
}

function ProfileForm({ onDone, error, setError }: {
  onDone: () => void;
  error: string;
  setError: (value: string) => void;
}) {
  const [displayName, setDisplayName] = useState("");
  const [gender, setGender] = useState<"Male" | "Female">("Male");
  const [birthMonth, setBirthMonth] = useState("");
  const [aiName, setAiName] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!birthMonth) {
      setError("请选择出生年月。");
      return;
    }
    setBusy(true); setError("");
    try {
      await saveProfile({
        displayName: displayName.trim(),
        gender,
        ...parseBirthMonth(birthMonth),
        aiName: aiName.trim(),
      });
      onDone();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "资料保存失败。");
    } finally { setBusy(false); }
  }

  return <form onSubmit={submit}>
    <AccessHeading title="先认识一下" description="这些信息只用于称呼你和理解对话。" />
    <div className="mt-9 space-y-5">
      <Field label="希望怎样称呼你" id="display-name"><Input id="display-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} maxLength={50} autoFocus required className={inputClass} placeholder="例如：阿澈" /></Field>
      <fieldset>
        <legend className="mb-2.5 text-sm font-medium">性别</legend>
        <div className="grid grid-cols-2 gap-3">
          {([["Male", "男"], ["Female", "女"]] as const).map(([value, label]) => <label key={value} className={`grid h-12 cursor-pointer place-items-center rounded-xl border text-sm font-medium ${gender === value ? "border-primary bg-primary-soft text-primary" : "border-input"}`}><input className="sr-only" type="radio" checked={gender === value} onChange={() => setGender(value)} />{label}</label>)}
        </div>
      </fieldset>
      <Field label="出生年月" id="birth-month">
        <MonthPicker id="birth-month" min={`${new Date().getFullYear() - 120}-01`} max={new Date().toISOString().slice(0, 7)} value={birthMonth} onChange={setBirthMonth} aria-label="选择出生年月" className="h-12" />
        {birthMonth && <p className="text-xs text-muted-foreground">当前 {calculateAgeFromBirthMonth(birthMonth)} 岁，之后会自动更新</p>}
      </Field>
      <Field label="给 AI 伙伴起个名字" id="ai-name"><Input id="ai-name" value={aiName} onChange={(event) => setAiName(event.target.value)} maxLength={50} required className={inputClass} placeholder="以后会用这个名字和你说话" /></Field>
    </div>
    <AccessError message={error} />
    <Button disabled={busy || !birthMonth} className={`mt-8 ${actionClass}`}>{busy ? "正在保存…" : <>开始对话<IconArrowRight /></>}</Button>
  </form>;
}

function Brand() {
  return <div className="yuji-access__brand flex items-center gap-2.5"><img src="/app-icon.svg" alt="" className="yuji-brand-mark size-9 rounded-[13px]" /><span className="text-sm font-semibold tracking-[0.06em]">遇己</span></div>;
}

function AccessHeading({ title, description }: { title: string; description: string }) {
  return <header className="yuji-access__heading"><h1 className="text-[34px] font-semibold tracking-[-0.045em]">{title}</h1><p className="mt-3 text-[15px] leading-6 text-muted-foreground">{description}</p></header>;
}

function Field({ label, id, children }: { label: string; id: string; children: React.ReactNode }) {
  return <div className="grid gap-2.5"><Label htmlFor={id}>{label}</Label>{children}</div>;
}

function PasswordField({ id, value, setValue, visible, setVisible, autoComplete }: {
  id: string;
  value: string;
  setValue: (value: string) => void;
  visible: boolean;
  setVisible: (value: boolean) => void;
  autoComplete: string;
}) {
  return <div className="grid gap-2.5"><Label htmlFor={id}>密码</Label><div className="relative"><Input id={id} type={visible ? "text" : "password"} minLength={6} maxLength={18} value={value} onChange={(event) => setValue(event.target.value)} autoComplete={autoComplete} placeholder="6～18 位" required className={`${inputClass} pr-12`} /><button type="button" onClick={() => setVisible(!visible)} className="absolute inset-y-0 right-0 grid w-12 place-items-center text-muted-foreground" aria-label={visible ? "隐藏密码" : "显示密码"}>{visible ? <IconEyeOff className="size-5" /> : <IconEye className="size-5" />}</button></div></div>;
}

function AccessError({ message }: { message: string }) {
  return message ? <p className="mt-5 rounded-xl bg-destructive/8 px-4 py-3 text-sm text-destructive" role="alert">{message}</p> : null;
}

const inputClass = "h-12 rounded-xl border-input bg-background px-3.5 shadow-none focus-visible:ring-primary/25";
const actionClass = "h-12 w-full rounded-xl text-sm font-semibold";
