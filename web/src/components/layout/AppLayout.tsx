import { useEffect, useState, type ComponentType } from "react";
import {
  IconChartDots,
  IconMapPin,
  IconMessageCircle,
  IconUser,
} from "@tabler/icons-react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { ApiError, hasAccessToken } from "@/api/client";
import { getMe, type UserIdentity } from "@/api/auth";
import { UserContext } from "./user-context";
import MomentCapture from "@/components/moments/MomentCapture";
import SpriteIcon from "@/components/companion/SpriteIcon";
import { DataUpdates } from "@/components/realtime/DataUpdates";
import { tapFeedback } from "@/native/shell";

interface AppLayoutProps {
  /** 在侧边栏的位置插入小精灵按钮 */
  companionButtonPosition?: 'before-nav' | 'after-nav';
  /** 打开小精灵的回调 */
  onOpenSprite?: () => void;
}

interface Destination {
  /** 目标路由。 */
  path: string;
  /** 用户可见的目标名称。 */
  label: string;
  /** Tabler 线性图标组件。 */
  icon: ComponentType<{ className?: string; "aria-hidden"?: boolean }>;
}

/**
 * 陪伴小精灵侧边栏按钮（桌面端专用）
 */
function CompanionSidebarButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="companion-sidebar-button group flex h-12 items-center justify-center gap-0 px-0 text-sm font-medium text-muted-foreground transition-colors rounded-lg hover:bg-primary-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 xl:justify-start xl:gap-[11px] xl:px-3"
      aria-label="打开陪伴小精灵"
    >
      <SpriteIcon className="size-[21px] shrink-0" />
      <span className="hidden truncate xl:inline">陪伴小精灵</span>
    </button>
  );
}

/** 桌面稳定侧栏与移动悬浮胶囊共享同一份 0.3 信息架构。 */

/** 桌面稳定侧栏与移动悬浮胶囊共享同一份 0.3 信息架构。 */
export default function AppLayout({
  companionButtonPosition = 'after-nav',
  onOpenSprite
}: AppLayoutProps) {
  const navigate = useNavigate();
  const [user, setUser] = useState<UserIdentity | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);

  useEffect(() => {
    if (!hasAccessToken()) {
      navigate("/access", { replace: true });
      return;
    }
    getMe().then((identity) => {
      // 已登录但尚未完成首次设置时，不能绕过 Setup Assistant 进入主界面。
      if (!identity.profileCompleted || !identity.aiName) {
        navigate("/access", { replace: true });
        return;
      }
      setUser(identity);
    }).catch((error: unknown) => {
      if (error instanceof ApiError && error.status === 401) {
        navigate("/access", { replace: true });
        return;
      }
      setLoadFailed(true);
    });
  }, [navigate]);

  if (loadFailed) {
    return <main className="grid h-dvh place-items-center bg-canvas text-sm text-muted-foreground">暂时无法载入遇己。</main>;
  }
  if (!user) return <div className="grid h-dvh place-items-center bg-canvas" aria-label="正在载入" />;

  const destinations: Destination[] = [
    { path: "/app/home", label: "首页", icon: IconUser },
    { path: "/app/companion", label: user.aiName, icon: IconMessageCircle },
    { path: "/app/map", label: "星图", icon: IconMapPin },
    { path: "/app/insights", label: "洞察", icon: IconChartDots },
  ];

  return (
    <UserContext.Provider value={user}>
      <DataUpdates>
      <div className="yuji-shell h-dvh lg:grid lg:grid-cols-[88px_minmax(0,1fr)] xl:grid-cols-[232px_minmax(0,1fr)]">
        <aside className="yuji-sidebar hidden h-dvh flex-col justify-between border-r px-3 py-[22px] lg:flex xl:px-4" aria-label="主导航">
          <div className="flex flex-col gap-1">
            <NavLink
              to="/app/home"
              className="mb-3 flex h-12 items-center justify-center gap-0 rounded-2xl px-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 xl:justify-start xl:gap-2.5 xl:px-2"
              aria-label="遇己首页"
            >
              <img src="/app-icon.svg" alt="" className="size-9 shrink-0 rounded-xl" />
              <strong className="hidden text-lg tracking-[-0.04em] text-foreground xl:block">遇己</strong>
            </NavLink>
            {onOpenSprite && companionButtonPosition === 'before-nav' && (
              <CompanionSidebarButton onClick={onOpenSprite} />
            )}
            <nav className="flex flex-col gap-1">
              {destinations.map(({ path, label, icon: Icon }) => (
                <NavLink
                  key={path}
                  to={path}
                  className={({ isActive }) => [
                    "yuji-nav-item flex h-12 items-center justify-center gap-0 px-0 text-sm font-medium text-muted-foreground transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 xl:justify-start xl:gap-[11px] xl:px-3",
                    isActive ? "bg-primary-soft text-primary" : "",
                  ].join(" ")}
                >
                  <Icon className="size-[21px] shrink-0" aria-hidden />
                  <span className="hidden truncate xl:inline">{label}</span>
                </NavLink>
              ))}
              {onOpenSprite && companionButtonPosition === 'after-nav' && (
                <CompanionSidebarButton onClick={onOpenSprite} />
              )}
            </nav>
            <div className="mt-3"><MomentCapture /></div>
          </div>
          <button
            type="button"
            onClick={() => navigate("/app/settings")}
            className="yuji-account flex h-14 items-center justify-center gap-0 border-t px-0 pt-2 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 xl:justify-start xl:gap-2.5 xl:px-2"
          >
            <span className="grid size-9 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-semibold text-primary">
              {user.username.slice(0, 1).toUpperCase()}
            </span>
            <span className="hidden min-w-0 xl:block">
              <span className="block truncate text-sm font-medium text-foreground">{user.username}</span>
              <span className="block text-[11px] text-muted-foreground">遇己账号</span>
            </span>
          </button>
        </aside>

        <main className="h-full min-h-0 min-w-0 overflow-hidden"><Outlet /></main>

        <nav className="yuji-mobile-nav fixed inset-x-2 z-40 flex h-16 items-center justify-center lg:hidden" aria-label="主导航">
          <div className="yuji-mobile-nav__surface flex h-16 w-full max-w-[430px] items-center justify-between gap-0.5 rounded-[24px] border p-1.5">
            {destinations.slice(0, 2).map(({ path, label, icon: Icon }) => (
              <NavLink
                key={path}
                to={path}
                className={({ isActive }) => [
                  "yuji-mobile-tab flex h-[52px] min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-[10px] font-medium text-muted-foreground transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40",
                  isActive ? "bg-primary-soft font-semibold text-primary" : "",
                ].join(" ")}
                onClick={tapFeedback}
              >
                <Icon className="size-[21px]" aria-hidden />
                <span className="max-w-full truncate px-1">{label}</span>
              </NavLink>
            ))}
            <MomentCapture compact />
            {destinations.slice(2).map(({ path, label, icon: Icon }) => (
              <NavLink
                key={path}
                to={path}
                className={({ isActive }) => [
                  "yuji-mobile-tab flex h-[52px] min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-[10px] font-medium text-muted-foreground transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40",
                  isActive ? "bg-primary-soft font-semibold text-primary" : "",
                ].join(" ")}
                onClick={tapFeedback}
              >
                <Icon className="size-[21px]" aria-hidden />
                <span className="max-w-full truncate px-1">{label}</span>
              </NavLink>
            ))}
          </div>
        </nav>
      </div>
      </DataUpdates>
    </UserContext.Provider>
  );
}
